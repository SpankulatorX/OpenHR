using RegionHR.Core.Domain;
using RegionHR.LAS.Domain;
using RegionHR.Leave.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Positions.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Analytics;

/// <summary>
/// Beräknar realtids-beslutsstöds-KPI:er per organisationsenhet (samt en regionövergripande
/// översikt) direkt ur domändata. Avsett för chefs-/HR-dashboarden /rapporter/beslutsstod.
///
/// KPI:er:
///  • Personalomsättning % — avslutade anställningar senaste 12 mån / aktivt headcount.
///  • Sjukfrånvaro %       — sjukfrånvarodagar senaste 12 mån / (headcount × arbetsdagar).
///  • Bemanningsgrad %     — tillsatta positioner / bemanningsbara positioner.
///  • LAS-riskantal        — aktiva tidsbegränsade anställningar nära LAS-omvandlingsgräns.
///  • Lönekostnad/enhet    — arbetskraftskostnad (brutto + arbetsgivaravgift) per månad och per FTE.
///
/// Tjänsten är ren (statisk, ingen DB, inga sidoeffekter) — anroparen laddar listorna via
/// <c>IDbContextFactory</c> och skickar in dem. Det gör KPI-beräkningarna enhetstestbara
/// utan databas, i linje med <c>FlightRiskService</c>.
///
/// Approximationer (dokumenterade, v1):
///  • Sjukfrånvaro räknar ~21 arbetsdagar/månad över ett 12-månadersfönster.
///  • Personalomsättningens nämnare är nuvarande headcount (ej rullande medel), i linje
///    med den befintliga <c>KPICalculationService</c>.
///  • LAS-risk baseras på anställningens sammanhängande längd från startdatum, inte på
///    summerade perioder inom referensfönstret (det görs av LAS-modulen).
/// </summary>
public static class BeslutsstodKpiService
{
    /// <summary>Antal arbetsdagar per månad (approximation för frånvaronämnaren).</summary>
    private const decimal ArbetsdagarPerManad = 21m;

    /// <summary>Längd på det rullande fönstret för omsättning och sjukfrånvaro.</summary>
    private const int FonsterManader = 12;

    /// <summary>
    /// Vikariat: varningsgräns i dagar. Sätts under LAS 2-årsgräns
    /// (<see cref="LASAccumulation.VIKARIAT_MAX_DAGAR_5AR"/> = 730) för att flagga i tid.
    /// </summary>
    private const int VikariatVarningDagar = 640;

    /// <summary>Nyckel för den regionövergripande översiktsraden.</summary>
    public const string OversiktEnhetId = "ALLA";

    /// <summary>
    /// Beräknar KPI:er per enhet + en regionövergripande översikt.
    /// </summary>
    public static BeslutsstodResultat Berakna(
        IReadOnlyCollection<Employee> employees,
        IReadOnlyCollection<PayrollResult> payrollResults,
        IReadOnlyCollection<LeaveRequest> leaveRequests,
        IReadOnlyCollection<Position> positions,
        IReadOnlyCollection<OrganizationUnit> enheter,
        DateOnly snapshotDatum)
    {
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(payrollResults);
        ArgumentNullException.ThrowIfNull(leaveRequests);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(enheter);

        var fonsterStart = snapshotDatum.AddMonths(-FonsterManader);

        // Uppslag: anställning → anställning (för att härleda enhet på löneresultat).
        var employmentById = new Dictionary<Guid, Employment>();
        var empById = new Dictionary<Guid, Employee>();
        foreach (var e in employees)
        {
            empById[e.Id.Value] = e;
            foreach (var a in e.Anstallningar)
                employmentById[a.Id.Value] = a;
        }

        // Aktiva anställningar och avslut, med resolverad enhet.
        var aktiva = employees
            .SelectMany(e => e.AktivaAnstallningar(snapshotDatum))
            .ToList();

        var avslutade12 = employees
            .SelectMany(e => e.Anstallningar)
            .Where(a => a.Giltighetsperiod.End is { } slut
                        && slut >= fonsterStart && slut <= snapshotDatum)
            .ToList();

        // Sjukfrånvarodagar per enhet i fönstret.
        var sjukPerEnhet = new Dictionary<Guid, int>();
        var sjukTotalt = 0;
        foreach (var lr in leaveRequests)
        {
            if (lr.Typ != LeaveType.Sjukfranvaro) continue;
            if (lr.FranDatum < fonsterStart || lr.FranDatum > snapshotDatum) continue;
            if (!empById.TryGetValue(lr.AnstallId, out var emp)) continue;

            var anst = emp.AktivAnstallning(lr.FranDatum) ?? emp.Anstallningar.FirstOrDefault();
            if (anst is null) continue;

            var enhetGuid = anst.EnhetId.Value;
            sjukPerEnhet[enhetGuid] = sjukPerEnhet.GetValueOrDefault(enhetGuid) + lr.AntalDagar;
            sjukTotalt += lr.AntalDagar;
        }

        // Löneresultat grupperade per resolverad enhet.
        var lonerPerEnhet = new Dictionary<Guid, List<PayrollResult>>();
        foreach (var r in payrollResults)
        {
            if (!employmentById.TryGetValue(r.AnstallningsId.Value, out var anst)) continue;
            var enhetGuid = anst.EnhetId.Value;
            if (!lonerPerEnhet.TryGetValue(enhetGuid, out var lista))
                lonerPerEnhet[enhetGuid] = lista = [];
            lista.Add(r);
        }

        var aktivaPerEnhet = aktiva.ToLookup(a => a.EnhetId.Value);
        var avslutadePerEnhet = avslutade12.ToLookup(a => a.EnhetId.Value);
        var positionerPerEnhet = positions.ToLookup(p => p.EnhetId);

        var perEnhet = new List<BeslutsstodKpi>();
        foreach (var enhet in enheter.OrderBy(e => e.Namn, StringComparer.Ordinal))
        {
            var g = enhet.Id.Value;
            perEnhet.Add(Bygg(
                enhetId: g.ToString(),
                namn: enhet.Namn,
                kostnadsstalle: enhet.Kostnadsstalle,
                aktiva: aktivaPerEnhet[g].ToList(),
                avslutade12: avslutadePerEnhet[g].Count(),
                sjukdagar: sjukPerEnhet.GetValueOrDefault(g),
                positioner: positionerPerEnhet[g].ToList(),
                loner: lonerPerEnhet.GetValueOrDefault(g) ?? [],
                snapshot: snapshotDatum));
        }

        var oversikt = Bygg(
            enhetId: OversiktEnhetId,
            namn: "Hela regionen",
            kostnadsstalle: "",
            aktiva: aktiva,
            avslutade12: avslutade12.Count,
            sjukdagar: sjukTotalt,
            positioner: positions.ToList(),
            loner: payrollResults.Where(r => employmentById.ContainsKey(r.AnstallningsId.Value)).ToList(),
            snapshot: snapshotDatum);

        return new BeslutsstodResultat(oversikt, perEnhet, snapshotDatum);
    }

    private static BeslutsstodKpi Bygg(
        string enhetId,
        string namn,
        string kostnadsstalle,
        IReadOnlyList<Employment> aktiva,
        int avslutade12,
        int sjukdagar,
        IReadOnlyList<Position> positioner,
        IReadOnlyList<PayrollResult> loner,
        DateOnly snapshot)
    {
        var headcount = aktiva.Count;
        var fte = aktiva.Sum(a => a.Sysselsattningsgrad.Value) / 100m;

        var personalomsattning = headcount > 0
            ? (decimal)avslutade12 / headcount * 100m
            : 0m;

        var mojligaArbetsdagar = headcount * FonsterManader * ArbetsdagarPerManad;
        var sjukfranvaroProcent = mojligaArbetsdagar > 0
            ? sjukdagar / mojligaArbetsdagar * 100m
            : 0m;

        var bemanningsbara = positioner.Count(p => p.Status != PositionStatus.Avvecklad);
        var tillsatta = positioner.Count(p => p.Status == PositionStatus.Aktiv);
        var bemanningsgrad = bemanningsbara > 0
            ? (decimal)tillsatta / bemanningsbara * 100m
            : 0m;

        var lasRisk = aktiva.Count(a => ArLasRisk(a, snapshot));

        var totalKostnad = loner.Sum(r => r.Brutto.Amount + r.Arbetsgivaravgifter.Amount);
        var distinktaPerioder = loner
            .Select(r => (r.Year, r.Month))
            .Distinct()
            .Count();
        var lonekostnadPerManad = distinktaPerioder > 0
            ? totalKostnad / distinktaPerioder
            : 0m;
        var lonekostnadPerFte = fte > 0
            ? lonekostnadPerManad / fte
            : 0m;

        return new BeslutsstodKpi(
            EnhetId: enhetId,
            EnhetNamn: namn,
            Kostnadsstalle: kostnadsstalle,
            Headcount: headcount,
            Fte: decimal.Round(fte, 2),
            PersonalomsattningProcent: decimal.Round(personalomsattning, 1),
            SjukfranvaroProcent: decimal.Round(sjukfranvaroProcent, 1),
            Bemanningsgrad: decimal.Round(bemanningsgrad, 1),
            AntalPositioner: bemanningsbara,
            TillsattaPositioner: tillsatta,
            LasRiskAntal: lasRisk,
            LonekostnadPerManadSEK: decimal.Round(lonekostnadPerManad, 0),
            LonekostnadPerFteSEK: decimal.Round(lonekostnadPerFte, 0));
    }

    /// <summary>
    /// True om anställningen är tidsbegränsad och dess sammanhängande längd närmar sig
    /// LAS omvandlingsgräns. SAVA/säsong flaggas vid ~10 mån
    /// (<see cref="LASAccumulation.SAVA_ALARM_10_MANADER"/>), vikariat vid
    /// <see cref="VikariatVarningDagar"/> dagar (under 2-årsgränsen).
    /// </summary>
    internal static bool ArLasRisk(Employment anst, DateOnly snapshot)
    {
        var tenureDagar = snapshot.DayNumber - anst.Giltighetsperiod.Start.DayNumber + 1;
        return anst.Anstallningsform switch
        {
            EmploymentType.SAVA or EmploymentType.Sasongsanstallning
                => tenureDagar >= LASAccumulation.SAVA_ALARM_10_MANADER,
            EmploymentType.Vikariat
                => tenureDagar >= VikariatVarningDagar,
            _ => false
        };
    }
}

/// <summary>KPI-rad för en enhet (eller regionöversikten) i beslutsstödet.</summary>
public sealed record BeslutsstodKpi(
    string EnhetId,
    string EnhetNamn,
    string Kostnadsstalle,
    int Headcount,
    decimal Fte,
    decimal PersonalomsattningProcent,
    decimal SjukfranvaroProcent,
    decimal Bemanningsgrad,
    int AntalPositioner,
    int TillsattaPositioner,
    int LasRiskAntal,
    decimal LonekostnadPerManadSEK,
    decimal LonekostnadPerFteSEK);

/// <summary>Resultatet av en beslutsstöds-beräkning: översikt + rader per enhet.</summary>
public sealed record BeslutsstodResultat(
    BeslutsstodKpi Oversikt,
    IReadOnlyList<BeslutsstodKpi> PerEnhet,
    DateOnly SnapshotDatum);
