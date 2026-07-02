using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RegionHR.Infrastructure.Persistence;
using RegionHR.LAS.Services;
using RegionHR.Notifications.Domain;

namespace RegionHR.Infrastructure.BackgroundJobs;

/// <summary>
/// Bakgrundsjobb som var 12:e timme bevakar LAS-ackumuleringar mot konverteringsgränsen
/// (SAVA 365 / vikariat 730 dagar) och skapar notifieringar till HR och den anställdes chef
/// när trösklarna 300/330/350/360 dagar (SAVA) passeras.
///
/// Larmet går ALDRIG till den anställde själv — det är HR/chef som ska agera på en
/// annalkande konvertering (formbyte till tillsvidare). Notifieringar avdupliceras per
/// nivå+tröskel inom ett dygnsfönster så samma larm inte upprepas varje körning.
/// </summary>
public class LASAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LASAlertService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(12);

    public LASAlertService(IServiceScopeFactory scopeFactory, ILogger<LASAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("LASAlertService: kontrollerar LAS-trösklar");
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RegionHRDbContext>();
                var skapade = await KontrolleraTrosklarAsync(db, stoppingToken);
                _logger.LogInformation("LASAlertService: {Count} LAS-notifieringar skapade", skapade);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fel vid kontroll av LAS-trösklar");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Kör en bevakningsomgång mot en given DbContext. Intern för testbarhet.
    /// Returnerar antalet skapade notifieringar.
    /// </summary>
    internal async Task<int> KontrolleraTrosklarAsync(RegionHRDbContext db, CancellationToken ct)
    {
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var cutoff = DateTime.UtcNow.AddHours(-20);

        var ackumuleringar = await db.LASAccumulations.AsNoTracking().ToListAsync(ct);

        // HR-mottagare: anställda vars befattning anger HR-funktion.
        var hrMottagareIds = await db.Employees
            .Where(e => e.Anstallningar.Any(a => a.Befattningstitel != null && a.Befattningstitel.Contains("HR")))
            .Select(e => e.Id)
            .ToListAsync(ct);
        var hrMottagare = hrMottagareIds.Select(id => id.Value).ToList();

        var skapade = 0;

        foreach (var acc in ackumuleringar)
        {
            var bedomning = LASAlertRegler.Bedom(acc.Anstallningsform, acc.AckumuleradeDagar);
            if (bedomning.Niva == LASAlertNiva.Ingen)
                continue;

            var anstalld = await db.Employees
                .Include(e => e.Anstallningar)
                .FirstOrDefaultAsync(e => e.Id == acc.AnstallId, ct);

            Guid? chefId = null;
            var aktivAnstallning = anstalld?.Anstallningar.FirstOrDefault(a => a.Giltighetsperiod.IsActiveOn(idag))
                ?? anstalld?.Anstallningar.OrderByDescending(a => a.Giltighetsperiod.Start).FirstOrDefault();
            if (aktivAnstallning is not null)
            {
                var enhet = await db.OrganizationUnits
                    .FirstOrDefaultAsync(u => u.Id == aktivAnstallning.EnhetId, ct);
                if (enhet?.ChefId is { } chef)
                    chefId = chef.Value;
            }

            var mottagare = LASAlertRegler.ValjMottagare(hrMottagare, chefId, acc.AnstallId.Value);
            if (mottagare.Count == 0)
            {
                _logger.LogWarning(
                    "LASAlertService: inga HR/chef-mottagare kunde hittas för anställd {AnstallId} — larm hoppas över",
                    acc.AnstallId.Value);
                continue;
            }

            var namn = anstalld is null ? "okänd anställd" : $"{anstalld.Fornamn} {anstalld.Efternamn}";
            var relType = $"LAS-HRAlert-{bedomning.Niva}-{bedomning.TroskelDagar}";
            var relId = acc.Id.ToString();
            var (titel, meddelande, typ) = ByggMeddelande(bedomning, namn, acc.AckumuleradeDagar);

            foreach (var userId in mottagare)
            {
                var redanNotifierad = await db.Notifications.AnyAsync(n =>
                    n.UserId == userId &&
                    n.RelatedEntityType == relType &&
                    n.RelatedEntityId == relId &&
                    n.CreatedAt > cutoff, ct);
                if (redanNotifierad)
                    continue;

                db.Notifications.Add(Notification.Create(
                    userId,
                    titel,
                    meddelande,
                    typ,
                    NotificationChannel.InApp,
                    actionUrl: "/las",
                    relatedEntityType: relType,
                    relatedEntityId: relId));
                skapade++;
            }
        }

        if (skapade > 0)
            await db.SaveChangesAsync(ct);

        return skapade;
    }

    private static (string Titel, string Meddelande, NotificationType Typ) ByggMeddelande(
        LASAlertBedomning b, string namn, int dagar) => b.Niva switch
    {
        LASAlertNiva.Konvertering => (
            $"LAS: {namn} ska konverteras till tillsvidare",
            $"{namn} har {dagar} LAS-dagar (gräns {b.GransDagar}). Konvertering till tillsvidareanställning krävs — HR/chef måste fatta beslut.",
            NotificationType.Action),
        LASAlertNiva.MycketKritisk => (
            $"LAS: {namn} mycket nära konverteringsgräns",
            $"{namn} har {dagar} LAS-dagar, {b.DagarKvar} dagar kvar till gräns {b.GransDagar}. Förbered beslut om formbyte.",
            NotificationType.Warning),
        LASAlertNiva.Kritisk => (
            $"LAS: {namn} närmar sig konverteringsgräns",
            $"{namn} har {dagar} LAS-dagar, {b.DagarKvar} dagar kvar till gräns {b.GransDagar}.",
            NotificationType.Warning),
        _ => (
            $"LAS-varning: {namn}",
            $"{namn} har {dagar} LAS-dagar, {b.DagarKvar} dagar kvar till gräns {b.GransDagar}.",
            NotificationType.Warning)
    };
}
