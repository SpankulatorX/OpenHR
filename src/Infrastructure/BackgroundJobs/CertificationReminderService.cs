using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RegionHR.Competence.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Notifications.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.BackgroundJobs;

/// <summary>
/// Bakgrundsjobb som en gång per dygn bevakar certifieringar/legitimationer vars
/// giltighet löper ut inom 90/60/30 dagar och skapar påminnelsenotiser till den
/// anställde samt dennes chef (via enhetens ChefId), i linje med <see cref="LASAlertService"/>.
///
/// Notiser avdupliceras per certifiering och tröskel via RelatedEntityType/RelatedEntityId,
/// så varje tröskel (90/60/30) larmar exakt en gång per certifiering och mottagare.
/// </summary>
public class CertificationReminderService : BackgroundService
{
    /// <summary>Påminnelsetrösklar i dagar före utgång, i stigande ordning.</summary>
    internal static readonly int[] TroskelDagar = [30, 60, 90];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CertificationReminderService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public CertificationReminderService(IServiceScopeFactory scopeFactory, ILogger<CertificationReminderService> logger)
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
                _logger.LogInformation("CertificationReminderService: Checking expiring certifications");
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RegionHRDbContext>();
                var skapade = await KontrolleraCertifieringarAsync(db, stoppingToken);
                _logger.LogInformation(
                    "CertificationReminderService: {Count} certifieringsnotiser skapade", skapade);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking certifications");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Kör en bevakningsomgång mot en given DbContext. Intern för testbarhet.
    /// Returnerar antalet skapade notifieringar.
    /// </summary>
    internal async Task<int> KontrolleraCertifieringarAsync(RegionHRDbContext db, CancellationToken ct)
    {
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var maxDatum = idag.AddDays(TroskelDagar.Max());

        var utgaende = await db.Certifications.AsNoTracking()
            .Where(c => c.GiltigTill != null && c.GiltigTill >= idag && c.GiltigTill <= maxDatum)
            .ToListAsync(ct);

        var skapade = 0;

        foreach (var cert in utgaende)
        {
            var dagarKvar = cert.GiltigTill!.Value.DayNumber - idag.DayNumber;
            var troskel = TroskelDagar.First(t => dagarKvar <= t); // 30, 60 eller 90

            var anstalldId = EmployeeId.From(cert.AnstallId);
            var anstalld = await db.Employees
                .Include(e => e.Anstallningar)
                .FirstOrDefaultAsync(e => e.Id == anstalldId, ct);

            // Mottagare: den anställde själv + dennes chef (om en kan härledas via enheten).
            var mottagare = new List<Guid> { cert.AnstallId };
            var aktivAnstallning = anstalld?.Anstallningar.FirstOrDefault(a => a.Giltighetsperiod.IsActiveOn(idag))
                ?? anstalld?.Anstallningar.OrderByDescending(a => a.Giltighetsperiod.Start).FirstOrDefault();
            if (aktivAnstallning is not null)
            {
                var enhet = await db.OrganizationUnits
                    .FirstOrDefaultAsync(u => u.Id == aktivAnstallning.EnhetId, ct);
                if (enhet?.ChefId is { } chef && chef.Value != cert.AnstallId)
                    mottagare.Add(chef.Value);
            }

            var namn = anstalld is null ? "okänd anställd" : $"{anstalld.Fornamn} {anstalld.Efternamn}";
            var relType = $"CertReminder-{troskel}";
            var relId = cert.Id.ToString();
            var (titel, meddelande, typ) = ByggMeddelande(cert, namn, dagarKvar, troskel);

            foreach (var userId in mottagare)
            {
                var redanNotifierad = await db.Notifications.AnyAsync(n =>
                    n.UserId == userId &&
                    n.RelatedEntityType == relType &&
                    n.RelatedEntityId == relId, ct);
                if (redanNotifierad)
                    continue;

                db.Notifications.Add(Notification.Create(
                    userId,
                    titel,
                    meddelande,
                    typ,
                    NotificationChannel.InApp,
                    actionUrl: "/kompetens",
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
        Certification cert, string namn, int dagarKvar, int troskel) => troskel switch
    {
        30 => (
            $"Certifiering löper ut snart: {cert.Namn}",
            $"{namn}: certifieringen \"{cert.Namn}\" löper ut {cert.GiltigTill:yyyy-MM-dd} ({dagarKvar} dagar kvar). Boka förnyelse omgående.",
            NotificationType.Warning),
        60 => (
            $"Certifiering löper ut inom 60 dagar: {cert.Namn}",
            $"{namn}: certifieringen \"{cert.Namn}\" löper ut {cert.GiltigTill:yyyy-MM-dd} ({dagarKvar} dagar kvar). Planera förnyelse.",
            NotificationType.Reminder),
        _ => (
            $"Certifiering löper ut inom 90 dagar: {cert.Namn}",
            $"{namn}: certifieringen \"{cert.Namn}\" löper ut {cert.GiltigTill:yyyy-MM-dd} ({dagarKvar} dagar kvar).",
            NotificationType.Reminder)
    };
}
