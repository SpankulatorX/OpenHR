using Microsoft.EntityFrameworkCore;
using RegionHR.Analytics.Domain;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Leave.Domain;
using RegionHR.Positions.Domain;
using RegionHR.Recruitment.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Services;

/// <summary>
/// Beräkningstjänst för KPI:er. Aggregerar verklig data från databasen
/// och skapar/uppdaterar KPISnapshot-poster med tröskelvärdering och trendanalys.
/// </summary>
public class KPICalculationService
{
    private readonly RegionHRDbContext _db;

    public KPICalculationService(RegionHRDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Beräkna alla aktiva KPI:er för angiven period och spara snapshots.
    /// </summary>
    /// <param name="period">Periodangivelse, t.ex. "2026-Q1"</param>
    /// <returns>Lista med beräknade snapshots</returns>
    public async Task<List<KPISnapshot>> CalculateAllAsync(string period, CancellationToken ct = default)
    {
        var definitions = await _db.KPIDefinitions
            .Where(k => k.ArAktiv)
            .ToListAsync(ct);

        var snapshots = new List<KPISnapshot>();

        foreach (var def in definitions)
        {
            var value = await CalculateKPIValueAsync(def.Namn, period, ct);
            var previousSnapshot = await GetPreviousSnapshotAsync(def.Id, period, ct);
            var previousValue = previousSnapshot?.Varde;
            var trend = CalculateTrend(value, previousValue);

            var snapshot = KPISnapshot.Skapa(
                def.Id,
                period,
                value,
                previousValue,
                trend);

            _db.KPISnapshots.Add(snapshot);
            snapshots.Add(snapshot);
        }

        await _db.SaveChangesAsync(ct);
        return snapshots;
    }

    /// <summary>
    /// Beräkna värdet för en specifik KPI baserat på dess namn.
    /// </summary>
    internal async Task<decimal> CalculateKPIValueAsync(string kpiNamn, string period, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        return kpiNamn switch
        {
            "Headcount" => await CalculateHeadcountAsync(today, ct),
            "FTE (heltidsekvivalenter)" => await CalculateFTEAsync(today, ct),
            "Vakansgrad" => await CalculateVacancyRateAsync(ct),
            "Personalomsattning" => await CalculateTurnoverAsync(today, ct),
            "Sjukfranvaro %" => await CalculateSickLeavePercentAsync(period, today, ct),
            "Lonekostnad per FTE" => await CalculateSalaryCostPerFTEAsync(period, today, ct),
            "Certifieringstackning" => await CalculateCertificationCoverageAsync(today, ct),
            "Time to fill (dagar)" => await CalculateTimeToFillAsync(ct),
            "eNPS" => await CalculateENPSAsync(ct),
            "LAS-riskantal" => await CalculateLASRiskCountAsync(ct),
            _ => 0m
        };
    }

    /// <summary>
    /// Anställningar som är aktiva på angivet datum: påbörjade (Start &lt;= datum) och
    /// med öppet slutdatum eller slutdatum på/efter datumet. Framtida (ej påbörjade)
    /// anställningar räknas inte, i linje med BeslutsstodKpiService/DateRange.IsActiveOn.
    /// </summary>
    private IQueryable<Employment> AktivaAnstallningar(DateOnly datum) =>
        _db.Employments.Where(e =>
            e.Giltighetsperiod.Start <= datum &&
            (e.Giltighetsperiod.End == null || e.Giltighetsperiod.End >= datum));

    /// <summary>
    /// Headcount: antal individer (distinkta anställda) med minst en aktiv anställning.
    /// En person med flera parallella anställningar räknas en gång.
    /// </summary>
    private async Task<decimal> CalculateHeadcountAsync(DateOnly today, CancellationToken ct)
    {
        return await AktivaAnstallningar(today)
            .Select(e => e.AnstallId)
            .Distinct()
            .CountAsync(ct);
    }

    /// <summary>
    /// FTE (heltidsekvivalenter): summa sysselsättningsgrad / 100 för aktiva anställningar.
    /// </summary>
    private async Task<decimal> CalculateFTEAsync(DateOnly today, CancellationToken ct)
    {
        var activeEmployments = await AktivaAnstallningar(today).ToListAsync(ct);

        if (activeEmployments.Count == 0) return 0m;

        var totalPercent = activeEmployments.Sum(e => (decimal)e.Sysselsattningsgrad);
        return totalPercent / 100m;
    }

    /// <summary>
    /// Vakansgrad: antal vakanta positioner / totalt antal positioner * 100.
    /// </summary>
    private async Task<decimal> CalculateVacancyRateAsync(CancellationToken ct)
    {
        var totalPositions = await _db.Positions_Table.CountAsync(ct);
        if (totalPositions == 0) return 0m;

        var vacantPositions = await _db.Positions_Table
            .CountAsync(p => p.Status == PositionStatus.Vakant, ct);

        return (decimal)vacantPositions / totalPositions * 100m;
    }

    /// <summary>
    /// Personalomsättning: antal avslutade anställningar senaste 12 mån / genomsnittligt headcount * 100.
    /// Om data saknas returneras 0.
    /// </summary>
    private async Task<decimal> CalculateTurnoverAsync(DateOnly today, CancellationToken ct)
    {
        var twelveMonthsAgo = today.AddMonths(-12);

        var terminatedCount = await _db.Employments
            .CountAsync(e =>
                e.Giltighetsperiod.End != null &&
                e.Giltighetsperiod.End >= twelveMonthsAgo &&
                e.Giltighetsperiod.End <= today, ct);

        var currentHeadcount = await CalculateHeadcountAsync(today, ct);

        if (currentHeadcount == 0) return 0m;

        return terminatedCount / currentHeadcount * 100m;
    }

    /// <summary>
    /// Sjukfrånvaro %: sjukdagar under perioden / (headcount * arbetsdagar i perioden) * 100.
    /// Endast godkänd (eller inskickad, ännu ej behandlad) frånvaro räknas —
    /// Utkast/Avslagen/Återkallad exkluderas. Periodsformat: "YYYY-QN" (t.ex. "2026-Q1").
    /// </summary>
    private async Task<decimal> CalculateSickLeavePercentAsync(string period, DateOnly today, CancellationToken ct)
    {
        var (periodStart, periodEnd) = ParsePeriodDates(period, today);

        var totalSickDays = await _db.LeaveRequests
            .Where(r => r.Typ == LeaveType.Sjukfranvaro &&
                        (r.Status == LeaveRequestStatus.Godkand || r.Status == LeaveRequestStatus.Inskickad) &&
                        r.FranDatum >= periodStart &&
                        r.FranDatum <= periodEnd)
            .SumAsync(r => r.AntalDagar, ct);

        var headcount = await CalculateHeadcountAsync(today, ct);

        if (headcount == 0) return 0m;

        // Approximate workdays in period (roughly 21 workdays per month)
        var monthsInPeriod = EstimateMonthsInPeriod(periodStart, periodEnd);
        var workdaysInPeriod = monthsInPeriod * 21m;

        var totalPossibleWorkdays = headcount * workdaysInPeriod;
        if (totalPossibleWorkdays == 0) return 0m;

        return totalSickDays / totalPossibleWorkdays * 100m;
    }

    /// <summary>
    /// Lönekostnad per FTE: (brutto + arbetsgivaravgifter) / FTE.
    /// Matchas mot PayrollResult via Year/Month inom perioden.
    /// </summary>
    private async Task<decimal> CalculateSalaryCostPerFTEAsync(string period, DateOnly today, CancellationToken ct)
    {
        var (periodStart, periodEnd) = ParsePeriodDates(period, today);

        var payrollResults = await _db.PayrollResults
            .Where(r => (r.Year > periodStart.Year || (r.Year == periodStart.Year && r.Month >= periodStart.Month)) &&
                        (r.Year < periodEnd.Year || (r.Year == periodEnd.Year && r.Month <= periodEnd.Month)))
            .ToListAsync(ct);

        if (payrollResults.Count == 0) return 0m;

        var totalCost = payrollResults.Sum(r => r.Brutto.Amount + r.Arbetsgivaravgifter.Amount);

        var fte = await CalculateFTEAsync(today, ct);
        if (fte == 0) return 0m;

        // Return average monthly cost per FTE
        var monthsInPeriod = EstimateMonthsInPeriod(periodStart, periodEnd);
        if (monthsInPeriod == 0) monthsInPeriod = 1;

        return totalCost / fte / monthsInPeriod;
    }

    /// <summary>
    /// Certifieringstäckning: giltiga certifieringar / antal obligatoriska utbildningskrav * 100.
    /// </summary>
    private async Task<decimal> CalculateCertificationCoverageAsync(DateOnly today, CancellationToken ct)
    {
        var totalRequired = await _db.MandatoryTrainings.CountAsync(ct);
        if (totalRequired == 0) return 100m; // If no requirements, coverage is 100%

        var totalValidCertifications = await _db.Certifications
            .CountAsync(c => c.GiltigTill == null || c.GiltigTill >= today, ct);

        return (decimal)totalValidCertifications / totalRequired * 100m;
    }

    /// <summary>
    /// Time to fill: genomsnittligt antal dagar från SistaAnsokningsDag till tillsättning.
    /// Vacancy saknar PubliceradDatum/TillsattDatum, så vi approximerar utifrån
    /// SistaAnsokningsDag och CreatedAt för tillsatta vakanser.
    /// </summary>
    private async Task<decimal> CalculateTimeToFillAsync(CancellationToken ct)
    {
        var filledVacancies = await _db.Vacancies
            .Where(v => v.Status == VacancyStatus.Tillsatt)
            .ToListAsync(ct);

        if (filledVacancies.Count == 0) return 0m;

        // Approximate: days from creation to SistaAnsokningsDag as a proxy for time to fill
        var totalDays = filledVacancies.Sum(v =>
        {
            var createdDate = DateOnly.FromDateTime(v.CreatedAt);
            var endDate = v.SistaAnsokningsDag;
            var days = endDate.DayNumber - createdDate.DayNumber;
            return days > 0 ? days : 0;
        });

        return (decimal)totalDays / filledVacancies.Count;
    }

    /// <summary>
    /// eNPS: beräknas från pulsundersökningssvar.
    /// Promoters (4-5) minus Detractors (1-2) i procent av totalt antal svar.
    /// Returnerar 0 om inga svar finns.
    /// </summary>
    private async Task<decimal> CalculateENPSAsync(CancellationToken ct)
    {
        var responses = await _db.PulseSurveyResponses
            .Include(r => r.Svar)
            .ToListAsync(ct);

        var allAnswers = responses.SelectMany(r => r.Svar).ToList();
        if (allAnswers.Count == 0) return 0m;

        var promoters = allAnswers.Count(a => a.Varde >= 4);
        var detractors = allAnswers.Count(a => a.Varde <= 2);
        var total = allAnswers.Count;

        return ((decimal)promoters / total - (decimal)detractors / total) * 100m;
    }

    /// <summary>
    /// LAS-riskantal: antal LAS-ackumuleringar med >= 305 dagar (10 månader SAVA-alarm).
    /// </summary>
    private async Task<decimal> CalculateLASRiskCountAsync(CancellationToken ct)
    {
        return await _db.LASAccumulations
            .CountAsync(a => a.AckumuleradeDagar >= 305, ct);
    }

    /// <summary>
    /// Beräkna trend genom att jämföra nuvarande värde med föregående periods värde.
    /// </summary>
    internal static string CalculateTrend(decimal currentValue, decimal? previousValue)
    {
        if (!previousValue.HasValue || previousValue.Value == 0)
            return "Stable";

        var prev = previousValue.Value;

        if (currentValue > prev * 1.05m)
            return "Up";
        if (currentValue < prev * 0.95m)
            return "Down";
        return "Stable";
    }

    /// <summary>
    /// Hämta föregående periods snapshot för en KPI-definition.
    /// Returnerar den senaste snapshot som har en annan period än den angivna.
    /// </summary>
    private async Task<KPISnapshot?> GetPreviousSnapshotAsync(Guid kpiDefinitionId, string currentPeriod, CancellationToken ct)
    {
        return await _db.KPISnapshots
            .Where(s => s.KPIDefinitionId == kpiDefinitionId && s.Period != currentPeriod)
            .OrderByDescending(s => s.BeraknadVid)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Tolkar periodangivelse (t.ex. "2026-Q1") till start- och slutdatum.
    /// Stödjer format: "YYYY-QN" (kvartal), "YYYY-MM" (månad), "YYYY" (år).
    /// </summary>
    internal static (DateOnly Start, DateOnly End) ParsePeriodDates(string period, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(period))
            return (today.AddMonths(-3), today);

        // Quarterly format: "2026-Q1"
        if (period.Contains("-Q", StringComparison.OrdinalIgnoreCase))
        {
            var parts = period.Split("-Q", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var quarter))
            {
                var startMonth = (quarter - 1) * 3 + 1;
                var start = new DateOnly(year, startMonth, 1);
                var end = start.AddMonths(3).AddDays(-1);
                return (start, end);
            }
        }

        // Monthly format: "2026-03"
        if (period.Length == 7 && period[4] == '-')
        {
            var parts = period.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month))
            {
                var start = new DateOnly(year, month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                return (start, end);
            }
        }

        // Year format: "2026"
        if (period.Length == 4 && int.TryParse(period, out var yearOnly))
        {
            return (new DateOnly(yearOnly, 1, 1), new DateOnly(yearOnly, 12, 31));
        }

        // Fallback: last 3 months
        return (today.AddMonths(-3), today);
    }

    private static decimal EstimateMonthsInPeriod(DateOnly start, DateOnly end)
    {
        var months = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
        return months > 0 ? months : 1;
    }
}
