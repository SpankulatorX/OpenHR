using Microsoft.EntityFrameworkCore;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Reporting.Domain;
using RegionHR.Reporting.Engine;

namespace RegionHR.Infrastructure.Reporting;

/// <summary>
/// Exekverar sparade rapportdefinitioner (datakälla + kolumner + filter + gruppering)
/// mot databasen och returnerar faktiska rader. Bryggar EF-lagret till den rena
/// <see cref="ReportQueryEngine"/> genom att materialisera varje datakälla till
/// ordböcker (kolumnnamn → värde) som motorn kan filtrera, gruppera och projicera.
///
/// Kolumnnycklarna matchar exakt de namn som rapportbyggaren
/// (ReportBuilder.razor, GetColumnsForDataSource) erbjuder, så att en sparad
/// kolumnvalslista kan köras oförändrad.
/// </summary>
public sealed class ReportExecutionService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly ReportQueryEngine _engine = new();

    /// <summary>Datakällor som motorn kan exekvera (samma nycklar som byggarens dropdown).</summary>
    public static readonly IReadOnlyList<string> Datakallor =
        ["Anstallda", "Lonekorngar", "Franvaro", "Schema", "Certifieringar", "LAS-ackumuleringar", "Rekrytering"];

    public ReportExecutionService(IDbContextFactory<RegionHRDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public Task<ReportResult> ExecuteAsync(ReportDefinition def, CancellationToken ct = default)
        => ExecuteAsync(def.Datakalla, def.Kolumner, def.Filter, def.Gruppering, def.VisualiseringsTyp, ct);

    public async Task<ReportResult> ExecuteAsync(
        string? datakalla, string? kolumnerJson, string? filterJson,
        string? gruppering, string? visualiseringsTyp, CancellationToken ct = default)
    {
        var spec = ReportQuerySpec.FranDefinition(datakalla, kolumnerJson, filterJson, gruppering, visualiseringsTyp);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rader = await LaddaRaderAsync(db, spec.Datakalla, ct);
        return _engine.Execute(spec, rader);
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaRaderAsync(
        RegionHRDbContext db, string datakalla, CancellationToken ct) => datakalla switch
        {
            "Anstallda" => await LaddaAnstallda(db, ct),
            "Lonekorngar" => await LaddaLonekorningar(db, ct),
            "Franvaro" => await LaddaFranvaro(db, ct),
            "Schema" => await LaddaSchema(db, ct),
            "Certifieringar" => await LaddaCertifieringar(db, ct),
            "LAS-ackumuleringar" => await LaddaLAS(db, ct),
            "Rekrytering" => await LaddaRekrytering(db, ct),
            _ => new List<IReadOnlyDictionary<string, object?>>()
        };

    private static Dictionary<string, object?> Rad() => new(StringComparer.OrdinalIgnoreCase);

    private static async Task<Dictionary<Guid, string>> EnhetsNamnAsync(RegionHRDbContext db, CancellationToken ct)
    {
        var enheter = await db.OrganizationUnits.AsNoTracking().ToListAsync(ct);
        return enheter.ToDictionary(o => o.Id.Value, o => o.Namn);
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaAnstallda(
        RegionHRDbContext db, CancellationToken ct)
    {
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var employees = await db.Employees.AsNoTracking().Include(e => e.Anstallningar).ToListAsync(ct);
        var enhetNamn = await EnhetsNamnAsync(db, ct);

        var rows = new List<IReadOnlyDictionary<string, object?>>(employees.Count);
        foreach (var e in employees)
        {
            var aktiv = e.AktivAnstallning(idag);
            var anst = aktiv ?? e.Anstallningar.LastOrDefault();
            var enhetId = anst?.EnhetId.Value;

            var r = Rad();
            r["Fornamn"] = e.Fornamn;
            r["Efternamn"] = e.Efternamn;
            r["Personnummer"] = e.Personnummer.ToMaskedString();
            r["Epost"] = e.Epost;
            r["Telefon"] = e.Telefon;
            r["Enhet"] = enhetId is Guid gid && enhetNamn.TryGetValue(gid, out var n) ? n : null;
            r["Befattning"] = anst?.Befattningstitel;
            r["Anstallningsdatum"] = anst?.Giltighetsperiod.Start;
            r["Sysselsattningsgrad"] = anst?.Sysselsattningsgrad.Value;
            r["Anstallningstyp"] = anst?.Anstallningsform.ToString();
            r["Anstallningsstatus"] = aktiv is not null ? "Aktiv" : "Avslutad";
            rows.Add(r);
        }
        return rows;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaLonekorningar(
        RegionHRDbContext db, CancellationToken ct)
    {
        var results = await db.PayrollResults.AsNoTracking().ToListAsync(ct);
        var employments = await db.Employments.AsNoTracking().ToListAsync(ct);
        var emplEnhet = employments.ToDictionary(x => x.Id.Value, x => x.EnhetId.Value);
        var enhetNamn = await EnhetsNamnAsync(db, ct);

        var rows = new List<IReadOnlyDictionary<string, object?>>(results.Count);
        foreach (var pr in results.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month))
        {
            string? enhet = null;
            if (emplEnhet.TryGetValue(pr.AnstallningsId.Value, out var eid) && enhetNamn.TryGetValue(eid, out var n))
                enhet = n;

            var r = Rad();
            r["Period"] = $"{pr.Year}-{pr.Month:D2}";
            r["Brutto"] = pr.Brutto.Amount;
            r["Skatt"] = pr.Skatt.Amount;
            r["Netto"] = pr.Netto.Amount;
            r["OB-tillagg"] = pr.OBTillagg.Amount;
            r["Overtid"] = pr.Overtidstillagg.Amount;
            r["Arbetsgivaravgift"] = pr.Arbetsgivaravgifter.Amount;
            r["Enhet"] = enhet;
            rows.Add(r);
        }
        return rows;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaFranvaro(
        RegionHRDbContext db, CancellationToken ct)
    {
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var leaves = await db.LeaveRequests.AsNoTracking().ToListAsync(ct);
        var employees = await db.Employees.AsNoTracking().Include(e => e.Anstallningar).ToListAsync(ct);
        var empById = employees.ToDictionary(e => e.Id.Value);
        var enhetNamn = await EnhetsNamnAsync(db, ct);

        var rows = new List<IReadOnlyDictionary<string, object?>>(leaves.Count);
        foreach (var l in leaves.OrderByDescending(x => x.FranDatum))
        {
            string namn = "Okänd";
            string? enhet = null;
            if (empById.TryGetValue(l.AnstallId, out var emp))
            {
                namn = $"{emp.Fornamn} {emp.Efternamn}";
                var anst = emp.AktivAnstallning(idag) ?? emp.Anstallningar.LastOrDefault();
                if (anst is not null && enhetNamn.TryGetValue(anst.EnhetId.Value, out var n)) enhet = n;
            }

            var r = Rad();
            r["Antalld"] = namn;
            r["FranvarotypTyp"] = l.Typ.ToString();
            r["FranDatum"] = l.FranDatum;
            r["TillDatum"] = l.TillDatum;
            r["AntalDagar"] = l.AntalDagar;
            r["Enhet"] = enhet;
            rows.Add(r);
        }
        return rows;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaSchema(
        RegionHRDbContext db, CancellationToken ct)
    {
        var shifts = await db.ScheduledShifts.AsNoTracking().OrderByDescending(s => s.Datum).ToListAsync(ct);
        var rows = new List<IReadOnlyDictionary<string, object?>>(shifts.Count);
        foreach (var s in shifts)
        {
            var r = Rad();
            r["Datum"] = s.Datum;
            r["Pass"] = s.PassTyp.ToString();
            r["Start"] = s.PlaneradStart.ToString("HH:mm");
            r["Slut"] = s.PlaneradSlut.ToString("HH:mm");
            r["Rast"] = (int)s.Rast.TotalMinutes;
            r["OB-kategori"] = s.OBKategori.ToString();
            r["Status"] = s.Status.ToString();
            rows.Add(r);
        }
        return rows;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaCertifieringar(
        RegionHRDbContext db, CancellationToken ct)
    {
        var certs = await db.Certifications.AsNoTracking().ToListAsync(ct);
        var rows = new List<IReadOnlyDictionary<string, object?>>(certs.Count);
        foreach (var c in certs)
        {
            var r = Rad();
            r["Certifiering"] = c.Namn;
            r["Typ"] = c.Typ.ToString();
            r["Utfardare"] = c.Utfardare;
            r["GiltigFran"] = c.GiltigFran;
            r["GiltigTill"] = c.GiltigTill;
            r["Obligatorisk"] = c.ArObligatorisk;
            rows.Add(r);
        }
        return rows;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaLAS(
        RegionHRDbContext db, CancellationToken ct)
    {
        var accs = await db.LASAccumulations.AsNoTracking().OrderByDescending(a => a.AckumuleradeDagar).ToListAsync(ct);
        var employees = await db.Employees.AsNoTracking().ToListAsync(ct);
        var namnById = employees.ToDictionary(e => e.Id.Value, e => $"{e.Fornamn} {e.Efternamn}");

        var rows = new List<IReadOnlyDictionary<string, object?>>(accs.Count);
        foreach (var a in accs)
        {
            var r = Rad();
            r["Antalld"] = namnById.TryGetValue(a.AnstallId.Value, out var n) ? n : "Okänd";
            r["AckumuleradeDagar"] = a.AckumuleradeDagar;
            r["Varningsniva"] = a.Status.ToString();
            r["SenastUppdaterad"] = a.UpdatedAt ?? a.CreatedAt;
            rows.Add(r);
        }
        return rows;
    }

    private static async Task<List<IReadOnlyDictionary<string, object?>>> LaddaRekrytering(
        RegionHRDbContext db, CancellationToken ct)
    {
        // Ansokngar är en ägd samling som EF auto-inkluderar med aggregatet (samma mönster
        // som Rekrytering/Statistik.razor använder).
        var vacancies = await db.Vacancies.AsNoTracking().ToListAsync(ct);
        var enhetNamn = await EnhetsNamnAsync(db, ct);

        var rows = new List<IReadOnlyDictionary<string, object?>>(vacancies.Count);
        foreach (var v in vacancies)
        {
            int? timeToFill = null;
            if (v.TillsattAnsokanId is not null)
            {
                var slut = v.UpdatedAt ?? v.CreatedAt;
                timeToFill = Math.Max(0, (int)(slut - v.CreatedAt).TotalDays);
            }

            var r = Rad();
            r["Vakans"] = v.Titel;
            r["Status"] = v.Status.ToString();
            r["Antalnsokande"] = v.Ansokngar.Count;
            r["PubliceradDatum"] = v.CreatedAt;
            r["TimeToFill"] = timeToFill;
            r["Enhet"] = enhetNamn.TryGetValue(v.EnhetId.Value, out var n) ? n : null;
            rows.Add(r);
        }
        return rows;
    }
}
