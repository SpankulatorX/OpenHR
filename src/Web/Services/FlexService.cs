using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Beräknar och persisterar flexsaldo ur stämplingar (faktisk tid) jämfört med schemalagd tid,
/// samt hanterar flexinställningar (gränser). Själva räkningen görs av den rena
/// <see cref="FlexCalculator"/>; denna tjänst hämtar underlag och sparar resultat.
/// </summary>
public class FlexService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public FlexService(IDbContextFactory<RegionHRDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>Hämta sparad flexinställning eller standard om ingen finns.</summary>
    public async Task<FlexInstallning> HamtaInstallningAsync(Guid anstallGuid, CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var inst = await db.Set<FlexInstallning>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct);
        return inst ?? FlexInstallning.Standard(empId);
    }

    /// <summary>
    /// Spara flexinställning (endast chef/HR). Validerar gränserna: MaxPlus ≥ 0, MaxMinus ≤ 0,
    /// daglig gräns ≥ 0. Kastar <see cref="ArgumentException"/> vid ogiltiga värden.
    /// </summary>
    public async Task SparaInstallningAsync(
        Guid anstallGuid,
        bool flexAktiverad,
        decimal maxPlusTimmar,
        decimal maxMinusTimmar,
        decimal dagligFlexgransTimmar,
        Guid andradAv,
        CancellationToken ct = default)
    {
        if (maxPlusTimmar < 0m)
            throw new ArgumentException("Övre gräns kan inte vara negativ.", nameof(maxPlusTimmar));
        if (maxMinusTimmar > 0m)
            throw new ArgumentException("Undre gräns kan inte vara positiv.", nameof(maxMinusTimmar));
        if (dagligFlexgransTimmar < 0m)
            throw new ArgumentException("Daglig flexgräns kan inte vara negativ.", nameof(dagligFlexgransTimmar));

        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var inst = await db.Set<FlexInstallning>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct);

        if (inst is null)
        {
            inst = FlexInstallning.Standard(empId);
            db.Set<FlexInstallning>().Add(inst);
        }

        inst.FlexAktiverad = flexAktiverad;
        inst.MaxPlusTimmar = maxPlusTimmar;
        inst.MaxMinusTimmar = maxMinusTimmar;
        inst.DagligFlexgransTimmar = dagligFlexgransTimmar;
        inst.SenastAndradAv = andradAv;
        inst.UppdateradVid = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Beräkna flexöversikt (läsning, inget sparas) för perioden. Underlaget är alla schemalagda
    /// pass i intervallet; endast pass med både in- och utstämpling räknas som faktisk tid.
    /// Kompsaldo = summan av registrerad övertid i perioden.
    /// </summary>
    public async Task<FlexOversikt> BeraknaOversiktAsync(
        Guid anstallGuid,
        DateOnly fran,
        DateOnly tom,
        CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var installning = await db.Set<FlexInstallning>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct)
            ?? FlexInstallning.Standard(empId);

        var pass = await db.ScheduledShifts
            .Where(s => s.AnstallId == empId && s.Datum >= fran && s.Datum <= tom
                        && s.Status != ShiftStatus.Avbokad)
            .OrderBy(s => s.Datum)
            .ToListAsync(ct);

        var underlag = pass.Select(s => new FlexDagsunderlag(s.Datum, s.PlaneradeTimmar, s.FaktiskaTimmar)).ToList();
        var resultat = FlexCalculator.Berakna(0m, underlag, installning);

        var kompsaldo = pass.Where(s => s.OvertidTimmar.HasValue).Sum(s => s.OvertidTimmar!.Value);

        return new FlexOversikt(
            resultat.UtgaendeSaldo,
            Math.Round(kompsaldo, 2),
            installning.FlexAktiverad,
            installning.MaxPlusTimmar,
            installning.MaxMinusTimmar,
            resultat.NaddeOvreGrans,
            resultat.NaddeUndreGrans,
            resultat.Dagsposter);
    }

    /// <summary>
    /// Räkna om flexsaldot ur underlaget och spara ögonblicksbilden i <see cref="FlexBalance"/>.
    /// Returnerar den sparade balansen.
    /// </summary>
    public async Task<FlexBalance> RaknaOmOchSparaAsync(
        Guid anstallGuid,
        DateOnly fran,
        DateOnly tom,
        CancellationToken ct = default)
    {
        var empId = EmployeeId.From(anstallGuid);
        var oversikt = await BeraknaOversiktAsync(anstallGuid, fran, tom, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var balance = await db.Set<FlexBalance>()
            .FirstOrDefaultAsync(x => x.AnstallId == empId, ct);

        if (balance is null)
        {
            balance = new FlexBalance { AnstallId = empId };
            db.Set<FlexBalance>().Add(balance);
        }

        balance.SaldoTimmar = oversikt.Flexsaldo;
        balance.KompsaldoTimmar = oversikt.Kompsaldo;
        balance.BeraknadTom = tom;
        balance.UppdateradVid = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return balance;
    }
}

/// <summary>Aggregerad flexöversikt för presentation.</summary>
public sealed record FlexOversikt(
    decimal Flexsaldo,
    decimal Kompsaldo,
    bool FlexAktiverad,
    decimal MaxPlus,
    decimal MaxMinus,
    bool NaddeOvreGrans,
    bool NaddeUndreGrans,
    IReadOnlyList<FlexDagspost> Dagsposter);
