using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Leave.Domain;
using RegionHR.Leave.Services;
using RegionHR.Notifications.Domain;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Orkestrerar ledighetsgodkännanden mot databasen. Domänlogiken (statusövergång,
/// saldodragning, val av berörda pass) ligger i <see cref="LedighetGodkannandeService"/>;
/// denna tjänst kopplar den mot EF Core, notifierar den anställde och persisterar allt
/// atomiskt i en enda <c>SaveChanges</c>.
/// </summary>
public sealed class LedighetService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly LedighetGodkannandeService _godkannande = new();

    public LedighetService(IDbContextFactory<RegionHRDbContext> dbFactory)
        => _dbFactory = dbFactory;

    /// <summary>
    /// Godkänner en ledighetsansökan: drar semestersaldo (endast semester), avbokar
    /// överlappande schemapass och notifierar den anställde. Allt sker i en transaktion —
    /// om saldot inte räcker eller statusen är fel sparas ingenting.
    /// </summary>
    public async Task<LedighetGodkannandeResultat> GodkannAsync(
        Guid requestId, Guid godkannare, string? kommentar = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var request = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Ledighetsansökan hittades inte.");

        // Semestersaldo för det år ledigheten börjar (endast relevant för semester).
        VacationBalance? saldo = null;
        if (request.Typ == LeaveType.Semester)
        {
            var ar = request.FranDatum.Year;
            saldo = await db.VacationBalances
                .FirstOrDefaultAsync(b => b.AnstallId == request.AnstallId && b.Ar == ar, ct);
        }

        // Läs in schemapassen i perioden och adaptera dem till domänens abstraktion.
        var anstallId = new EmployeeId(request.AnstallId);
        var shifts = await db.ScheduledShifts
            .Where(s => s.AnstallId == anstallId
                        && s.Datum >= request.FranDatum
                        && s.Datum <= request.TillDatum)
            .ToListAsync(ct);

        var pass = shifts
            .Select(s => (IPaverkbartPass)new ScheduledShiftPass(s))
            .ToList();

        var resultat = _godkannande.Godkann(request, godkannare, kommentar, saldo, pass);

        // Notifiera den anställde om godkännandet.
        db.Notifications.Add(Notification.Create(
            request.AnstallId,
            "Ledighetsansökan godkänd",
            $"Din ansökan om {TypText(request.Typ)} {request.FranDatum:yyyy-MM-dd}–{request.TillDatum:yyyy-MM-dd} " +
            $"({request.AntalDagar} dagar) har godkänts.",
            NotificationType.Info,
            actionUrl: "/minsida/ledighet"));

        await db.SaveChangesAsync(ct);
        return resultat;
    }

    /// <summary>
    /// Avslår en ledighetsansökan och notifierar den anställde.
    /// Påverkar varken semestersaldo eller schema.
    /// </summary>
    public async Task AvslaAsync(
        Guid requestId, Guid godkannare, string kommentar, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var request = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Ledighetsansökan hittades inte.");

        request.Avvisa(godkannare, kommentar);

        db.Notifications.Add(Notification.Create(
            request.AnstallId,
            "Ledighetsansökan avslagen",
            $"Din ansökan om {TypText(request.Typ)} {request.FranDatum:yyyy-MM-dd}–{request.TillDatum:yyyy-MM-dd} " +
            $"har avslagits. {kommentar}",
            NotificationType.Warning,
            actionUrl: "/minsida/ledighet"));

        await db.SaveChangesAsync(ct);
    }

    private static string TypText(LeaveType typ) => typ switch
    {
        LeaveType.Semester => "semester",
        LeaveType.Sjukfranvaro => "sjukfrånvaro",
        LeaveType.Foraldraledighet => "föräldraledighet",
        LeaveType.VAB => "VAB",
        LeaveType.Tjanstledighet => "tjänstledighet",
        LeaveType.Komptid => "komptid",
        LeaveType.Utbildning => "utbildning",
        _ => typ.ToString()
    };

    /// <summary>
    /// Adapter som exponerar ett <see cref="ScheduledShift"/> som <see cref="IPaverkbartPass"/>.
    /// Endast planerade pass kan påverkas; pågående, avslutade, redan avbokade eller bytta
    /// pass lämnas orörda. Att markera som frånvaro sätter status <see cref="ShiftStatus.Avbokad"/>,
    /// vilket lönekörningen redan exkluderar från arbetade timmar/OB.
    /// </summary>
    private sealed class ScheduledShiftPass : IPaverkbartPass
    {
        private readonly ScheduledShift _shift;
        public ScheduledShiftPass(ScheduledShift shift) => _shift = shift;

        public DateOnly Datum => _shift.Datum;
        public bool KanPaverkas => _shift.Status == ShiftStatus.Planerad;
        public void MarkeraSomFranvaro() => _shift.Status = ShiftStatus.Avbokad;
    }
}
