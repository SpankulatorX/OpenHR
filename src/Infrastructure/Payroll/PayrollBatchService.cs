using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Payroll.Domain;
using RegionHR.Payroll.Engine;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// Orkestrerar en fullständig lönekörning end-to-end mot den RIKTIGA löneberäkningsmotorn.
///
/// Denna Infrastructure-variant ersätter den modulinterna orkestreraren
/// (RegionHR.Payroll.Services.PayrollBatchService) vars audit-fynd var att den bara hade
/// ICoreHRModule och därför (a) inte kunde räkna upp några anställda
/// (GetRootOrganizationUnitsAsync gav tom lista → 0 bearbetade) och (b) inte kunde bygga
/// något underlag (BuildPayrollInputAsync gav tomt). Här finns full databasåtkomst, så
/// anställda räknas upp på riktigt och underlaget byggs ur schema + frånvaro via
/// <see cref="PayrollInputBuilder"/>.
/// </summary>
public sealed class PayrollBatchService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly PayrollCalculationEngine _calculationEngine;
    private readonly RetroactiveRecalculationEngine _retroEngine;
    private readonly PayrollInputBuilder _inputBuilder;

    public PayrollBatchService(
        IDbContextFactory<RegionHRDbContext> dbFactory,
        PayrollCalculationEngine calculationEngine,
        RetroactiveRecalculationEngine retroEngine,
        PayrollInputBuilder inputBuilder)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _calculationEngine = calculationEngine ?? throw new ArgumentNullException(nameof(calculationEngine));
        _retroEngine = retroEngine ?? throw new ArgumentNullException(nameof(retroEngine));
        _inputBuilder = inputBuilder ?? throw new ArgumentNullException(nameof(inputBuilder));
    }

    /// <summary>
    /// Kör en ordinarie lönekörning för given period. Räknar upp varje anställd med en aktiv
    /// anställning, bygger riktigt underlag och kör den fullständiga brutto-till-netto-motorn.
    /// </summary>
    public async Task<PayrollRunResult> ExecutePayrollRunAsync(
        int year, int month, string startadAv, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var periodStart = new DateOnly(year, month, 1);

        var run = PayrollRun.Skapa(year, month, startadAv);
        run.Paborja();

        var employments = await HamtaAktivaAnstallningarAsync(db, periodStart, ct);
        var fel = new List<PayrollRunError>();

        foreach (var employment in employments)
        {
            try
            {
                var input = await _inputBuilder.BuildAsync(db, employment, year, month, ct);
                var result = await _calculationEngine.CalculateAsync(
                    run.Id, employment.AnstallId, employment.Id, year, month, input, ct);
                run.LaggTillResultat(result);
            }
            catch (Exception ex)
            {
                fel.Add(new PayrollRunError(employment.AnstallId, ex.Message));
            }
        }

        run.MarkeraSomBeraknad();
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return new PayrollRunResult(run, fel);
    }

    /// <summary>
    /// Kör en retroaktiv lönekörning. Beräknar om en tidigare period med aktuella uppgifter och
    /// skapar differensrader (via <see cref="RetroactiveRecalculationEngine"/>) som betalas ut
    /// eller återkrävs i den innevarande perioden.
    /// </summary>
    public async Task<PayrollRunResult> ExecuteRetroactiveRunAsync(
        int year, int month, string retroPeriod, string startadAv, CancellationToken ct = default)
    {
        var (retroYear, retroMonth) = ParsePeriod(retroPeriod);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var originalRun = await db.PayrollRuns
            .Include(r => r.Resultat)
            .Where(r => r.Year == retroYear && r.Month == retroMonth && !r.ArRetroaktiv
                        && (r.Status == PayrollRunStatus.Beraknad
                            || r.Status == PayrollRunStatus.Granskad
                            || r.Status == PayrollRunStatus.Godkand
                            || r.Status == PayrollRunStatus.Utbetald))
            .OrderByDescending(r => r.StartadVid)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"Ingen tidigare lönekörning att räkna om för period {retroPeriod}.");

        var run = PayrollRun.Skapa(year, month, startadAv, retroaktiv: true, retroPeriod: retroPeriod);
        run.Paborja();

        var retroStart = new DateOnly(retroYear, retroMonth, 1);
        // Vid årsövergång ska den retroaktiva delen beskattas enligt utbetalningsårets regler.
        var taxTableYear = retroYear != year ? year : (int?)null;
        var fel = new List<PayrollRunError>();

        foreach (var original in originalRun.Resultat)
        {
            try
            {
                var employment = await HamtaAktivAnstallningAsync(db, original.AnstallId, retroStart, ct);
                if (employment is null)
                    continue;

                var input = await _inputBuilder.BuildAsync(db, employment, retroYear, retroMonth, ct);
                var recalculated = await _calculationEngine.CalculateAsync(
                    run.Id, original.AnstallId, employment.Id, retroYear, retroMonth, input, ct);

                var retro = await _retroEngine.RecalculateAsync(original, recalculated, taxTableYear, ct);
                if (retro.NettoDifferens == Money.Zero && retro.BruttoDifferens == Money.Zero)
                    continue;

                var diffResult = PayrollResult.Skapa(
                    run.Id, original.AnstallId, employment.Id, year, month,
                    recalculated.Manadslon, recalculated.Sysselsattningsgrad, recalculated.Kollektivavtal);

                foreach (var line in retro.DifferenceLines)
                {
                    diffResult.LaggTillRad(new PayrollResultLine
                    {
                        LoneartKod = line.LoneartKod,
                        Benamning = $"{line.Benamning} ({retroPeriod})",
                        Antal = 1,
                        Sats = line.Differens,
                        Belopp = line.Differens,
                        Skattekategori = TaxCategory.Skattepliktig,
                        ArAvdrag = line.ArAvdrag
                    });
                }

                diffResult.Brutto = retro.BruttoDifferens;
                diffResult.Skatt = retro.SkatteDifferens;
                diffResult.Netto = retro.NettoDifferens;
                diffResult.Arbetsgivaravgifter = retro.ArbetsgivaravgiftDifferens;
                diffResult.Pensionsavgift = retro.PensionDifferens;

                run.LaggTillResultat(diffResult);
            }
            catch (Exception ex)
            {
                fel.Add(new PayrollRunError(original.AnstallId, ex.Message));
            }
        }

        run.MarkeraSomBeraknad();
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return new PayrollRunResult(run, fel);
    }

    /// <summary>
    /// Hämtar en aktiv anställning per anställd för perioden (första aktiva, samma val som motorn
    /// gör internt), så att varje anställd får exakt ett löneresultat.
    /// </summary>
    private static async Task<IReadOnlyList<Employment>> HamtaAktivaAnstallningarAsync(
        RegionHRDbContext db, DateOnly periodStart, CancellationToken ct)
    {
        var aktiva = await db.Employments
            .Where(e => e.Giltighetsperiod.Start <= periodStart
                        && (e.Giltighetsperiod.End == null || e.Giltighetsperiod.End >= periodStart))
            .ToListAsync(ct);

        return aktiva
            .GroupBy(e => e.AnstallId)
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<Employment?> HamtaAktivAnstallningAsync(
        RegionHRDbContext db, EmployeeId anstallId, DateOnly datum, CancellationToken ct)
    {
        return await db.Employments
            .FirstOrDefaultAsync(e => e.AnstallId == anstallId
                                      && e.Giltighetsperiod.Start <= datum
                                      && (e.Giltighetsperiod.End == null || e.Giltighetsperiod.End >= datum), ct);
    }

    private static (int Year, int Month) ParsePeriod(string period)
    {
        var parts = (period ?? string.Empty).Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var y)
            || !int.TryParse(parts[1], out var m)
            || m is < 1 or > 12)
        {
            throw new ArgumentException($"Ogiltigt periodformat: '{period}'. Förväntat: YYYY-MM.", nameof(period));
        }

        return (y, m);
    }
}

/// <summary>Resultatet av en lönekörning: körningen plus ev. per-anställd-fel.</summary>
public sealed record PayrollRunResult(PayrollRun Run, IReadOnlyList<PayrollRunError> Fel);

/// <summary>Ett fel som uppstod vid beräkning för en enskild anställd.</summary>
public sealed record PayrollRunError(EmployeeId AnstallId, string Meddelande);
