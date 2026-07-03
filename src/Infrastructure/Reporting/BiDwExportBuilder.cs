using System.Globalization;
using RegionHR.Analytics.Domain.BiExport;
using RegionHR.Core.Domain;
using RegionHR.Leave.Domain;
using RegionHR.Payroll.Domain;

namespace RegionHR.Infrastructure.Reporting;

/// <summary>
/// Bygger ett dimensionsmodellerat <see cref="BiStjarnschema"/> ur domänentiteter
/// (anställda, löneresultat, frånvaro, organisationsenheter) för BI/DW-export.
///
/// Byggaren är ren (inga sidoeffekter, ingen DB-åtkomst) — anroparen laddar listorna
/// via <c>IDbContextFactory</c> och skickar in dem, precis som
/// <c>FlightRiskService</c> tar en färdig lista med anställda. Det gör den enkel att
/// enhetstesta utan databas.
/// </summary>
public static class BiDwExportBuilder
{
    private const string OkandNyckel = "OKÄND";

    private static readonly string[] ManadNamn =
    [
        "", "Januari", "Februari", "Mars", "April", "Maj", "Juni",
        "Juli", "Augusti", "September", "Oktober", "November", "December"
    ];

    /// <summary>
    /// Bygger stjärnschemat. <paramref name="snapshotDatum"/> avgör vilka anställningar
    /// som räknas som aktiva i faktatabellen för anställning samt anställdas ålder.
    /// </summary>
    public static BiStjarnschema Bygg(
        IReadOnlyCollection<Employee> employees,
        IReadOnlyCollection<PayrollResult> payrollResults,
        IReadOnlyCollection<LeaveRequest> leaveRequests,
        IReadOnlyCollection<OrganizationUnit> enheter,
        DateOnly snapshotDatum)
    {
        ArgumentNullException.ThrowIfNull(employees);
        ArgumentNullException.ThrowIfNull(payrollResults);
        ArgumentNullException.ThrowIfNull(leaveRequests);
        ArgumentNullException.ThrowIfNull(enheter);

        var empById = new Dictionary<Guid, Employee>();
        foreach (var e in employees)
            empById[e.Id.Value] = e;

        var employmentById = new Dictionary<Guid, Employment>();
        foreach (var e in employees)
            foreach (var a in e.Anstallningar)
                employmentById[a.Id.Value] = a;

        // ── Dimensionsackumulatorer ──────────────────────────────────────────
        var dimEnhet = new Dictionary<string, BiDimEnhet>(StringComparer.Ordinal);
        foreach (var enhet in enheter)
        {
            var key = enhet.Id.Value.ToString();
            dimEnhet[key] = new BiDimEnhet(
                EnhetId: key,
                Namn: enhet.Namn,
                Kostnadsstalle: enhet.Kostnadsstalle,
                Typ: enhet.Typ.ToString(),
                OverordnadEnhetId: enhet.OverordnadEnhetId?.Value.ToString(),
                HsaId: enhet.HsaId);
        }

        var dimBefattning = new Dictionary<string, BiDimBefattning>(StringComparer.Ordinal);
        var dimKon = new Dictionary<string, BiDimKon>(StringComparer.Ordinal);
        var dimAlder = new Dictionary<string, BiDimAlder>(StringComparer.Ordinal);
        var tidIds = new HashSet<string>(StringComparer.Ordinal);

        // ── Fakta: anställning ────────────────────────────────────────────────
        var faktaAnstallning = new List<BiFaktaAnstallning>();
        var snapshotTid = TidId(snapshotDatum.Year, snapshotDatum.Month);

        foreach (var emp in employees)
        {
            var konId = RegistreraKon(dimKon, emp);
            var alderId = RegistreraAlder(dimAlder, emp, snapshotDatum);

            foreach (var anst in emp.AktivaAnstallningar(snapshotDatum))
            {
                var enhetId = RegistreraEnhet(dimEnhet, anst.EnhetId.Value);
                var befattningId = RegistreraBefattning(dimBefattning, anst);
                tidIds.Add(snapshotTid);

                var grad = anst.Sysselsattningsgrad.Value;
                faktaAnstallning.Add(new BiFaktaAnstallning(
                    TidId: snapshotTid,
                    EnhetId: enhetId,
                    BefattningId: befattningId,
                    KonId: konId,
                    AlderId: alderId,
                    AntalAnstallningar: 1,
                    Sysselsattningsgrad: grad,
                    Fte: grad / 100m,
                    ManadslonSEK: anst.Manadslon.Amount,
                    ArTillsvidare: anst.ArTillsvidareanstallning ? 1 : 0,
                    ArTidsbegransad: anst.ArTidsbegransad ? 1 : 0));
            }
        }

        // ── Fakta: lön ────────────────────────────────────────────────────────
        var faktaLon = new List<BiFaktaLon>();
        foreach (var r in payrollResults)
        {
            var tid = TidId(r.Year, r.Month);
            tidIds.Add(tid);

            var enhetId = employmentById.TryGetValue(r.AnstallningsId.Value, out var anst)
                ? RegistreraEnhet(dimEnhet, anst.EnhetId.Value)
                : RegistreraOkandEnhet(dimEnhet);

            var konId = empById.TryGetValue(r.AnstallId.Value, out var emp)
                ? RegistreraKon(dimKon, emp)
                : RegistreraOkantKon(dimKon);

            faktaLon.Add(new BiFaktaLon(
                TidId: tid,
                EnhetId: enhetId,
                KonId: konId,
                BruttoSEK: r.Brutto.Amount,
                SkattSEK: r.Skatt.Amount,
                NettoSEK: r.Netto.Amount,
                ArbetsgivaravgifterSEK: r.Arbetsgivaravgifter.Amount,
                PensionsavgiftSEK: r.Pensionsavgift.Amount,
                TotalArbetskraftskostnadSEK: r.Brutto.Amount + r.Arbetsgivaravgifter.Amount));
        }

        // ── Fakta: frånvaro ───────────────────────────────────────────────────
        // Endast godkänd (eller inskickad, ännu ej behandlad) frånvaro är faktagrundande —
        // Utkast/Avslagen/Återkallad ska inte in i DW:t.
        var faktaFranvaro = new List<BiFaktaFranvaro>();
        foreach (var lr in leaveRequests)
        {
            if (lr.Status is not (LeaveRequestStatus.Godkand or LeaveRequestStatus.Inskickad))
                continue;

            var tid = TidId(lr.FranDatum.Year, lr.FranDatum.Month);
            tidIds.Add(tid);

            string enhetId;
            string konId;
            if (empById.TryGetValue(lr.AnstallId, out var emp))
            {
                konId = RegistreraKon(dimKon, emp);
                var anst = emp.AktivAnstallning(lr.FranDatum) ?? emp.Anstallningar.FirstOrDefault();
                enhetId = anst is not null
                    ? RegistreraEnhet(dimEnhet, anst.EnhetId.Value)
                    : RegistreraOkandEnhet(dimEnhet);
            }
            else
            {
                konId = RegistreraOkantKon(dimKon);
                enhetId = RegistreraOkandEnhet(dimEnhet);
            }

            faktaFranvaro.Add(new BiFaktaFranvaro(
                TidId: tid,
                EnhetId: enhetId,
                KonId: konId,
                FranvaroTyp: lr.Typ.ToString(),
                AntalDagar: lr.AntalDagar,
                AntalFall: 1));
        }

        // ── Tidsdimension ur samtliga refererade perioder ─────────────────────
        var dimTid = tidIds
            .Select(id =>
            {
                var ar = int.Parse(id[..4], CultureInfo.InvariantCulture);
                var manad = int.Parse(id[5..], CultureInfo.InvariantCulture);
                var kvartal = (manad - 1) / 3 + 1;
                return new BiDimTid(id, ar, kvartal, manad, ManadNamn[manad]);
            })
            .OrderBy(d => d.TidId, StringComparer.Ordinal)
            .ToList();

        return new BiStjarnschema(
            DimTid: dimTid,
            DimEnhet: dimEnhet.Values.OrderBy(d => d.Namn, StringComparer.Ordinal).ToList(),
            DimBefattning: dimBefattning.Values.OrderBy(d => d.Titel, StringComparer.Ordinal).ToList(),
            DimKon: dimKon.Values.OrderBy(d => d.KonId, StringComparer.Ordinal).ToList(),
            DimAlder: dimAlder.Values.OrderBy(d => d.MinAlder).ToList(),
            FaktaAnstallning: faktaAnstallning,
            FaktaLon: faktaLon,
            FaktaFranvaro: faktaFranvaro,
            SnapshotDatum: snapshotDatum,
            GenereradVid: DateTime.UtcNow);
    }

    private static string TidId(int ar, int manad) =>
        $"{ar:D4}-{manad:D2}";

    private static string RegistreraEnhet(Dictionary<string, BiDimEnhet> dim, Guid enhetGuid)
    {
        var key = enhetGuid.ToString();
        if (!dim.ContainsKey(key))
        {
            // Anställning pekar på en enhet som inte fanns i enhetslistan — lägg till platshållare.
            dim[key] = new BiDimEnhet(key, OkandNyckel, "", "", null, null);
        }
        return key;
    }

    private static string RegistreraOkandEnhet(Dictionary<string, BiDimEnhet> dim)
    {
        if (!dim.ContainsKey(OkandNyckel))
            dim[OkandNyckel] = new BiDimEnhet(OkandNyckel, OkandNyckel, "", "", null, null);
        return OkandNyckel;
    }

    private static string RegistreraBefattning(Dictionary<string, BiDimBefattning> dim, Employment anst)
    {
        var titel = string.IsNullOrWhiteSpace(anst.Befattningstitel)
            ? "(okänd befattning)"
            : anst.Befattningstitel.Trim();

        if (!dim.ContainsKey(titel))
            dim[titel] = new BiDimBefattning(titel, titel, anst.BESTAKod, anst.AIDKod);

        return titel;
    }

    private static string RegistreraKon(Dictionary<string, BiDimKon> dim, Employee emp)
    {
        var beteckning = emp.Personnummer.LegalGender; // "Man" / "Kvinna"
        var key = beteckning switch
        {
            "Man" => "M",
            "Kvinna" => "K",
            _ => "U"
        };
        if (!dim.ContainsKey(key))
            dim[key] = new BiDimKon(key, beteckning);
        return key;
    }

    private static string RegistreraOkantKon(Dictionary<string, BiDimKon> dim)
    {
        if (!dim.ContainsKey("U"))
            dim["U"] = new BiDimKon("U", "Okänt");
        return "U";
    }

    private static string RegistreraAlder(Dictionary<string, BiDimAlder> dim, Employee emp, DateOnly datum)
    {
        var alder = BeraknaAlder(emp.Personnummer.BirthDate, datum);
        var (id, intervall, min, max) = alder switch
        {
            < 30 => ("-29", "Under 30", 0, 29),
            < 40 => ("30-39", "30-39", 30, 39),
            < 50 => ("40-49", "40-49", 40, 49),
            < 60 => ("50-59", "50-59", 50, 59),
            _ => ("60+", "60 och äldre", 60, 200)
        };
        if (!dim.ContainsKey(id))
            dim[id] = new BiDimAlder(id, intervall, min, max);
        return id;
    }

    private static int BeraknaAlder(DateOnly fodelse, DateOnly datum)
    {
        var alder = datum.Year - fodelse.Year;
        if (datum < fodelse.AddYears(alder))
            alder--;
        return Math.Max(alder, 0);
    }
}
