using Microsoft.EntityFrameworkCore;
using RegionHR.HalsoSAM.Domain;
using RegionHR.HalsoSAM.Services;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.HalsoSAM;

/// <summary>
/// Läser sjukfrånvaro ur <c>SickLeaveNotifications</c> (Leave-modulen) och exponerar
/// dels statistik-rådata (<see cref="ISickLeaveDataProvider"/>), dels sjukfrånvaro-
/// perioder per anställd för den automatiska rehab-triggningen.
/// Pågående sjukfall (utan slutdatum) räknas t.o.m. dagens datum.
/// </summary>
public sealed class SickLeaveNotificationDataProvider : ISickLeaveDataProvider
{
    private readonly RegionHRDbContext _db;

    public SickLeaveNotificationDataProvider(RegionHRDbContext db) => _db = db;

    public async Task<IReadOnlyList<SjukfranvaroRad>> HamtaSjukfranvaroAsync(
        OrganizationId enhetId, DateOnly from, DateOnly till, CancellationToken ct)
    {
        // Anställda på enheten under perioden.
        var anstalldaPaEnhet = await _db.Employees
            .Where(e => e.Anstallningar.Any(a => a.EnhetId == enhetId))
            .Select(e => e.Id)
            .ToListAsync(ct);

        var anstalldSet = anstalldaPaEnhet.Select(id => id.Value).ToHashSet();

        var notiser = await _db.SickLeaveNotifications
            .Where(s => s.StartDatum <= till)
            .ToListAsync(ct);

        return notiser
            .Where(s => anstalldSet.Contains(s.AnstallId))
            .Where(s => (s.SlutDatum ?? DateOnly.FromDateTime(DateTime.Today)) >= from)
            .Select(s => new SjukfranvaroRad
            {
                AnstallId = EmployeeId.From(s.AnstallId),
                StartDatum = s.StartDatum,
                SlutDatum = s.SlutDatum ?? DateOnly.FromDateTime(DateTime.Today)
            })
            .ToList();
    }

    public async Task<int> HamtaAntalAktivaRehabArendenAsync(OrganizationId enhetId, CancellationToken ct)
    {
        // RehabCase saknar enhetskoppling — returnerar totalt antal aktiva ärenden.
        return await _db.RehabCases.CountAsync(r => r.Status != RehabStatus.Avslutad, ct);
    }

    public async Task<int> HamtaAntalAnstallda(OrganizationId enhetId, DateOnly datum, CancellationToken ct)
    {
        return await _db.Employees
            .CountAsync(e => e.Anstallningar.Any(a =>
                a.EnhetId == enhetId &&
                a.Giltighetsperiod.Start <= datum &&
                (a.Giltighetsperiod.End == null || a.Giltighetsperiod.End >= datum)), ct);
    }

    /// <summary>
    /// Sjukfrånvaroperioder grupperade per anställd — indata till
    /// <see cref="SickLeaveMonitor"/> för automatisk rehab-triggning.
    /// </summary>
    public async Task<IReadOnlyDictionary<EmployeeId, List<SjukfranvaroPeriod>>>
        HamtaPerioderPerAnstalldAsync(CancellationToken ct)
    {
        var idag = DateOnly.FromDateTime(DateTime.Today);

        var notiser = await _db.SickLeaveNotifications.ToListAsync(ct);

        return notiser
            .GroupBy(s => EmployeeId.From(s.AnstallId))
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => new SjukfranvaroPeriod
                {
                    StartDatum = s.StartDatum,
                    SlutDatum = s.SlutDatum ?? idag
                }).ToList());
    }
}
