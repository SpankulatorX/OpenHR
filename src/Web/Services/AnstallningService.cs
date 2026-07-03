using RegionHR.Core.Contracts;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace RegionHR.Web.Services;

public class AnstallningService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public AnstallningService(IDbContextFactory<RegionHRDbContext> dbFactory) => _dbFactory = dbFactory;

    /// <summary>
    /// Bakåtkompatibel "hämta upp till 100"-lista (används av bl.a. mallgeneratorns
    /// medarbetarväljare). För den paginerade listvyn: använd <see cref="HamtaSidaAsync"/>.
    /// </summary>
    public async Task<List<EmployeeListItem>> HamtaAllaAsync(string? sokterm = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var idag = DateOnly.FromDateTime(DateTime.Today);

        var employees = await ByggFiltreradFraga(db, idag, sokterm, null)
            .OrderBy(e => e.Efternamn).ThenBy(e => e.Fornamn)
            .Take(100)
            .Include(e => e.Anstallningar)
            .ToListAsync(ct);

        var enhetNamn = await HamtaEnhetNamnAsync(db, ct);
        return employees.Select(e => MapItem(e, idag, enhetNamn)).ToList();
    }

    /// <summary>
    /// Serversidig paginering för anställd-listan. Laddar BARA en sida (typiskt 25–50)
    /// ur databasen plus totalantal — skalar till tiotusentals anställda utan att frysa
    /// Blazor-circuiten. Filtrerar valfritt på fritext (namn, befattning, e-post) och/eller
    /// en exakt enhet (<paramref name="enhetId"/>, används både för enhetsfilter i UI och
    /// för chefs-scoping). Aktiv anställning bestäms i SQL via giltighetsperioden.
    /// </summary>
    /// <param name="hoppaOver">Antal rader att hoppa över (sida × sidstorlek).</param>
    /// <param name="taAntal">Sidstorlek (klampas till 1–200).</param>
    public async Task<PagedResult<EmployeeListItem>> HamtaSidaAsync(
        int hoppaOver, int taAntal, string? sokterm = null, Guid? enhetId = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var query = ByggFiltreradFraga(db, idag, sokterm, enhetId);

        var total = await query.CountAsync(ct);

        var employees = await query
            .OrderBy(e => e.Efternamn).ThenBy(e => e.Fornamn)
            .Skip(Math.Max(0, hoppaOver))
            .Take(Math.Clamp(taAntal, 1, 200))
            .Include(e => e.Anstallningar)
            .ToListAsync(ct);

        var enhetNamn = await HamtaEnhetNamnAsync(db, ct);
        var items = employees.Select(e => MapItem(e, idag, enhetNamn)).ToList();
        return new PagedResult<EmployeeListItem>(items, total);
    }

    /// <summary>
    /// Hämtar hela det filtrerade urvalet (utan paginering) för CSV/Excel-export.
    /// Har ett tak (<paramref name="max"/>) som skyddsnät mot en oavsiktlig helexport
    /// av hela registret; höj vid behov.
    /// </summary>
    public async Task<List<EmployeeListItem>> HamtaForExportAsync(
        string? sokterm = null, Guid? enhetId = null, int max = 20000, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var idag = DateOnly.FromDateTime(DateTime.Today);

        var employees = await ByggFiltreradFraga(db, idag, sokterm, enhetId)
            .OrderBy(e => e.Efternamn).ThenBy(e => e.Fornamn)
            .Take(Math.Clamp(max, 1, 100000))
            .Include(e => e.Anstallningar)
            .ToListAsync(ct);

        var enhetNamn = await HamtaEnhetNamnAsync(db, ct);
        return employees.Select(e => MapItem(e, idag, enhetNamn)).ToList();
    }

    /// <summary>
    /// Bygger den filtrerade grundfrågan (utan paginering/materialisering). All filtrering
    /// sker i SQL: aktiv-anställnings-scoping via giltighetsperioden och fritext via LIKE.
    /// </summary>
    private static IQueryable<Employee> ByggFiltreradFraga(
        RegionHRDbContext db, DateOnly idag, string? sokterm, Guid? enhetId)
    {
        var query = db.Employees.AsQueryable();

        if (enhetId is Guid eid)
        {
            var oid = OrganizationId.From(eid);
            query = query.Where(e => e.Anstallningar.Any(a =>
                a.EnhetId == oid &&
                a.Giltighetsperiod.Start <= idag &&
                (a.Giltighetsperiod.End == null || a.Giltighetsperiod.End >= idag)));
        }

        if (!string.IsNullOrWhiteSpace(sokterm))
        {
            var t = sokterm.Trim().ToLower();
            query = query.Where(e =>
                e.Fornamn.ToLower().Contains(t) ||
                e.Efternamn.ToLower().Contains(t) ||
                (e.Fornamn + " " + e.Efternamn).ToLower().Contains(t) ||
                (e.Epost != null && e.Epost.ToLower().Contains(t)) ||
                e.Anstallningar.Any(a =>
                    a.Befattningstitel != null && a.Befattningstitel.ToLower().Contains(t)));
        }

        return query;
    }

    /// <summary>Uppslag enhets-id → namn. Organisationsenheter är en liten tabell (≪ anställda).</summary>
    private static async Task<Dictionary<Guid, string>> HamtaEnhetNamnAsync(
        RegionHRDbContext db, CancellationToken ct)
    {
        var enheter = await db.OrganizationUnits
            .Select(o => new { o.Id, o.Namn })
            .ToListAsync(ct);
        return enheter.ToDictionary(x => x.Id.Value, x => x.Namn);
    }

    private static EmployeeListItem MapItem(Employee e, DateOnly idag, IReadOnlyDictionary<Guid, string> enhetNamn)
    {
        var aktiv = e.AktivAnstallning(idag);
        var enhet = aktiv != null && enhetNamn.TryGetValue(aktiv.EnhetId.Value, out var namn) ? namn : "-";
        return new EmployeeListItem(
            e.Id,
            e.Fornamn,
            e.Efternamn,
            e.Personnummer.ToMaskedString(),
            e.Epost,
            aktiv?.Befattningstitel ?? "-",
            aktiv?.Anstallningsform.ToString() ?? "-",
            aktiv?.Sysselsattningsgrad.Value ?? 0,
            enhet);
    }

    public async Task<Employee?> HamtaAsync(EmployeeId id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Employees
            .Include(e => e.Anstallningar)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<EmployeeId> SkapaAsync(
        string personnummer, string fornamn, string efternamn,
        string? epost = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var pnr = new Personnummer(personnummer);
        var employee = Employee.Skapa(pnr, fornamn, efternamn);
        if (epost is not null)
            employee.UppdateraKontaktuppgifter(epost, null, null);

        await db.Employees.AddAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return employee.Id;
    }

    public async Task UppdateraKontaktuppgifterAsync(
        EmployeeId id, string? epost, string? telefon, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return;
        employee.UppdateraKontaktuppgifter(epost, telefon, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task UppdateraKontaktuppgifterMedAdressAsync(
        EmployeeId id, string? epost, string? telefon, Address? adress, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return;
        employee.UppdateraKontaktuppgifter(epost, telefon, adress);
        await db.SaveChangesAsync(ct);
    }

    public async Task UppdateraSkatteuppgifterAsync(
        EmployeeId id, int skattetabell, int skattekolumn, string kommun,
        decimal kommunalSkattesats, bool harKyrkoavgift, decimal? kyrkoavgiftssats,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return;
        employee.UppdateraSkatteuppgifter(skattetabell, skattekolumn, kommun, kommunalSkattesats, harKyrkoavgift, kyrkoavgiftssats);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Hämtar HELA organisationsträdet i EN platt fråga och bygger parent→barn-hierarkin
    /// i minnet (via <see cref="OrganizationUnit.OverordnadEnhetId"/>). Den gamla varianten
    /// laddade bara roten + en nivå barn med <c>.Include(o =&gt; o.Underenheter)</c>, så nivå
    /// 3–4 (kliniker/vårdcentraler/underenheter) blev osynliga. Nu returneras rot-noderna
    /// med fullt påfyllda barn i alla nivåer — alla enheter är nåbara. Varje nod bär även
    /// antal aktiva anställda (direkt + rullat upp över subträdet) för att kunna visas i UI.
    /// </summary>
    public async Task<List<OrgTreeNode>> HamtaOrganisationAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // En platt fråga över hela (lilla) enhetstabellen, ordnad på namn.
        var enheter = await db.OrganizationUnits
            .AsNoTracking()
            .OrderBy(o => o.Namn)
            .ToListAsync(ct);

        var antalPerEnhet = await HamtaAntalAnstalldaPerEnhetAsync(db, ct);

        var platta = enheter.Select(o => new OrgTreeNode(
            o.Id,
            o.Namn,
            o.Typ,
            o.Kostnadsstalle,
            o.OverordnadEnhetId is { } p ? p.Value : (Guid?)null,
            antalPerEnhet.TryGetValue(o.Id.Value, out var n) ? n : 0));

        return ByggHierarki(platta);
    }

    /// <summary>
    /// Rent in-minne-trädbygge: kopplar en platt lista av noder till en parent→barn-hierarki
    /// via <see cref="OrgTreeNode.OverordnadId"/> och returnerar rot-noderna. Barnens ordning
    /// bevaras från inlistan (kalla med namnsorterad lista för namnsorterat träd). En nod utan
    /// förälder — ELLER med en förälder som inte finns i urvalet (dinglande FK) — behandlas som
    /// rot, så att ingen enhet någonsin blir onåbar. Antal anställda rullas upp per subträd.
    /// Ren funktion utan DB-beroende → enhetstestbar.
    /// </summary>
    public static List<OrgTreeNode> ByggHierarki(IEnumerable<OrgTreeNode> platta)
    {
        var noder = platta as IList<OrgTreeNode> ?? platta.ToList();

        var perId = new Dictionary<Guid, OrgTreeNode>(noder.Count);
        foreach (var n in noder)
        {
            n.Barn.Clear();
            perId[n.Id.Value] = n; // vid ev. dubbletter vinner den sista
        }

        var rotter = new List<OrgTreeNode>();
        foreach (var n in noder)
        {
            if (n.OverordnadId is { } pid
                && perId.TryGetValue(pid, out var parent)
                && !ReferenceEquals(parent, n))
            {
                parent.Barn.Add(n);
            }
            else
            {
                rotter.Add(n);
            }
        }

        foreach (var rot in rotter)
            BeraknaTotaltAntal(rot);

        return rotter;
    }

    private static int BeraknaTotaltAntal(OrgTreeNode nod)
    {
        var summa = nod.AntalAnstalldaDirekt;
        foreach (var barn in nod.Barn)
            summa += BeraknaTotaltAntal(barn);
        nod.AntalAnstalldaTotalt = summa;
        return summa;
    }

    /// <summary>
    /// Antal AKTIVA anställda per enhet (nyckel = enhetens Guid). Hämtar bara enhets-id-kolumnen
    /// för aktiva anställningar i en fråga och grupperar i minnet — undviker att materialisera
    /// hela anställningsraderna och är oberoende av GroupBy-översättning för värde-konverterade
    /// nycklar. Enhetsurvalet är litet; anställningarna filtreras på giltighetsperioden i SQL.
    /// </summary>
    /// <param name="begransaTill">
    /// Om satt: räkna bara dessa enheter (IN-filter i SQL) i stället för hela registret. Används
    /// av enhetsdetaljen som bara behöver enheten själv + dess direkta barn — då transporteras
    /// bara de matchande radernas enhets-id, inte alla ~11 000. Trädvyn kallar utan filter.
    /// </param>
    private static async Task<Dictionary<Guid, int>> HamtaAntalAnstalldaPerEnhetAsync(
        RegionHRDbContext db, CancellationToken ct, IReadOnlyCollection<OrganizationId>? begransaTill = null)
    {
        var idag = DateOnly.FromDateTime(DateTime.Today);
        var query = db.Employments
            .Where(a => a.Giltighetsperiod.Start <= idag
                        && (a.Giltighetsperiod.End == null || a.Giltighetsperiod.End >= idag));

        if (begransaTill is { Count: > 0 })
            query = query.Where(a => begransaTill.Contains(a.EnhetId));

        var enhetsIds = await query
            .Select(a => a.EnhetId)
            .ToListAsync(ct);

        return enhetsIds
            .GroupBy(id => id.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Detaljdata för EN organisationsenhet: enhetens egna fält, dess överordnade enhet
    /// (för länk tillbaka) och dess DIREKTA underenheter (med antal aktiva anställda per barn).
    /// Antal anställda i själva enheten räknas i DB. Laddar aldrig anställningsrader hit — den
    /// paginerade anställdlistan hämtas separat via <see cref="HamtaSidaAsync"/>.
    /// </summary>
    public async Task<OrgEnhetDetalj?> HamtaEnhetDetaljAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var oid = OrganizationId.From(id);

        var enhet = await db.OrganizationUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == oid, ct);
        if (enhet is null)
            return null;

        string? overordnadNamn = null;
        if (enhet.OverordnadEnhetId is { } pid)
        {
            overordnadNamn = await db.OrganizationUnits
                .AsNoTracking()
                .Where(o => o.Id == pid)
                .Select(o => o.Namn)
                .FirstOrDefaultAsync(ct);
        }

        var barn = await db.OrganizationUnits
            .AsNoTracking()
            .Where(o => o.OverordnadEnhetId == oid)
            .OrderBy(o => o.Namn)
            .ToListAsync(ct);

        // Räkna anställda bara för enheten själv + dess direkta barn (inte hela registret).
        var relevanta = new List<OrganizationId>(barn.Count + 1) { enhet.Id };
        relevanta.AddRange(barn.Select(b => b.Id));
        var antalPerEnhet = await HamtaAntalAnstalldaPerEnhetAsync(db, ct, relevanta);
        int Antal(OrganizationId x) => antalPerEnhet.TryGetValue(x.Value, out var n) ? n : 0;

        var barnDto = barn
            .Select(b => new OrgEnhetBarn(b.Id, b.Namn, b.Typ, Antal(b.Id)))
            .ToList();

        return new OrgEnhetDetalj(
            enhet.Id,
            enhet.Namn,
            enhet.Typ,
            enhet.Kostnadsstalle,
            enhet.CFARKod,
            enhet.HsaId,
            enhet.Giltighet.Start,
            enhet.Giltighet.End,
            enhet.OverordnadEnhetId is { } op ? op : (OrganizationId?)null,
            overordnadNamn,
            barnDto,
            Antal(enhet.Id));
    }

    /// <summary>
    /// Sammanfattning "antal aktiva anställda per befattning" för en enhet. Aggregeras i DB
    /// (bara befattning + antal transporteras) och filtreras på enhet + aktiv giltighetsperiod.
    /// </summary>
    public async Task<List<BefattningsAntal>> HamtaBefattningsfordelningAsync(
        Guid enhetId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var oid = OrganizationId.From(enhetId);
        var idag = DateOnly.FromDateTime(DateTime.Today);

        // Hämtar bara befattningskolumnen för enhetens aktiva anställningar; grupperar i minnet.
        var titlar = await db.Employments
            .Where(a => a.EnhetId == oid
                        && a.Giltighetsperiod.Start <= idag
                        && (a.Giltighetsperiod.End == null || a.Giltighetsperiod.End >= idag))
            .Select(a => a.Befattningstitel)
            .ToListAsync(ct);

        return titlar
            .GroupBy(t => string.IsNullOrWhiteSpace(t) ? "Ej angiven" : t!.Trim())
            .Select(g => new BefattningsAntal(g.Key, g.Count()))
            .OrderByDescending(r => r.Antal)
            .ThenBy(r => r.Befattning, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Platt lista över alla enheter för val i anställningsformulär.</summary>
    public async Task<List<EnhetVal>> HamtaEnheterAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var enheter = await db.OrganizationUnits.OrderBy(o => o.Namn).ToListAsync(ct);
        return enheter.Select(o => new EnhetVal(o.Id, o.Namn, o.Kostnadsstalle)).ToList();
    }

    /// <summary>
    /// Skapar en ny organisationsenhet. Anropar domänfabriken direkt och persisterar via DbContext.
    /// Sätts <paramref name="overordnadEnhetId"/> blir enheten en underenhet till den valda enheten.
    /// </summary>
    public async Task<OrganizationId> SkapaEnhetAsync(
        string namn, OrganizationUnitType typ, string kostnadsstalle,
        OrganizationId? overordnadEnhetId = null, string? cfarKod = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var enhet = OrganizationUnit.Skapa(
            namn.Trim(), typ, kostnadsstalle.Trim(),
            DateOnly.FromDateTime(DateTime.Today),
            overordnadEnhetId,
            string.IsNullOrWhiteSpace(cfarKod) ? null : cfarKod.Trim());
        await db.OrganizationUnits.AddAsync(enhet, ct);
        await db.SaveChangesAsync(ct);
        return enhet.Id;
    }

    /// <summary>Kollektivavtal att koppla en anställning till (DB-backade avtal).</summary>
    public async Task<List<AvtalVal>> HamtaAvtalAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var avtal = await db.CollectiveAgreements.OrderBy(a => a.Namn).ToListAsync(ct);
        return avtal.Select(a => new AvtalVal(a.Id, a.Namn)).ToList();
    }

    /// <summary>
    /// Anställ en ny person: skapar Employee-aggregatet OCH den första anställningen i ett svep.
    /// All validering (LAS-regler, slutdatum, lön) sker i domänen.
    /// </summary>
    public async Task<EmployeeId> SkapaMedAnstallningAsync(
        string personnummer, string fornamn, string efternamn,
        string? epost, string? telefon,
        OrganizationId enhetId, EmploymentType anstallningsform, CollectiveAgreementType kollektivavtal,
        decimal manadslon, decimal sysselsattningsgrad, DateOnly startdatum, DateOnly? slutdatum,
        string? bestaKod, string? aidKod, string? befattning, CollectiveAgreementId? avtalsId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var pnr = new Personnummer(personnummer);
        var employee = Employee.Skapa(pnr, fornamn, efternamn);
        if (!string.IsNullOrWhiteSpace(epost) || !string.IsNullOrWhiteSpace(telefon))
            employee.UppdateraKontaktuppgifter(epost, telefon, null);

        employee.LaggTillAnstallning(
            enhetId, anstallningsform, kollektivavtal,
            Money.SEK(manadslon), new Percentage(sysselsattningsgrad),
            startdatum, slutdatum, bestaKod, aidKod, befattning, avtalsId);

        await db.Employees.AddAsync(employee, ct);
        await db.SaveChangesAsync(ct);
        return employee.Id;
    }

    /// <summary>Lägg till ytterligare en anställning på en befintlig anställd.</summary>
    public async Task<EmploymentId> LaggTillAnstallningAsync(
        EmployeeId anstalldId,
        OrganizationId enhetId, EmploymentType anstallningsform, CollectiveAgreementType kollektivavtal,
        decimal manadslon, decimal sysselsattningsgrad, DateOnly startdatum, DateOnly? slutdatum,
        string? bestaKod, string? aidKod, string? befattning, CollectiveAgreementId? avtalsId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await db.Employees.Include(e => e.Anstallningar)
            .FirstOrDefaultAsync(e => e.Id == anstalldId, ct)
            ?? throw new InvalidOperationException("Anställd hittades inte.");

        var employment = employee.LaggTillAnstallning(
            enhetId, anstallningsform, kollektivavtal,
            Money.SEK(manadslon), new Percentage(sysselsattningsgrad),
            startdatum, slutdatum, bestaKod, aidKod, befattning, avtalsId);

        await db.SaveChangesAsync(ct);
        return employment.Id;
    }

    public async Task AndraLonAsync(
        EmployeeId anstalldId, EmploymentId anstallningId, decimal nyLon, string andradAv, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await LaddaMedAnstallningarAsync(db, anstalldId, ct);
        employee.AndraAnstallningsLon(anstallningId, Money.SEK(nyLon), andradAv);
        await db.SaveChangesAsync(ct);
    }

    public async Task AndraSysselsattningsgradAsync(
        EmployeeId anstalldId, EmploymentId anstallningId, decimal nyGrad, string andradAv, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await LaddaMedAnstallningarAsync(db, anstalldId, ct);
        employee.AndraAnstallningsSysselsattningsgrad(anstallningId, new Percentage(nyGrad), andradAv);
        await db.SaveChangesAsync(ct);
    }

    public async Task SattBefattningAsync(
        EmployeeId anstalldId, EmploymentId anstallningId, string befattning, string andradAv, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await LaddaMedAnstallningarAsync(db, anstalldId, ct);
        employee.SattAnstallningsBefattning(anstallningId, befattning, andradAv);
        await db.SaveChangesAsync(ct);
    }

    public async Task AvslutaAnstallningAsync(
        EmployeeId anstalldId, EmploymentId anstallningId, DateOnly slutdatum, string andradAv, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var employee = await LaddaMedAnstallningarAsync(db, anstalldId, ct);
        employee.AvslutaAnstallning(anstallningId, slutdatum, andradAv);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Employee> LaddaMedAnstallningarAsync(
        RegionHRDbContext db, EmployeeId id, CancellationToken ct) =>
        await db.Employees.Include(e => e.Anstallningar).FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new InvalidOperationException("Anställd hittades inte.");
}

public record EmployeeListItem(
    EmployeeId Id,
    string Fornamn,
    string Efternamn,
    string PersonnummerMaskerat,
    string? Epost,
    string Befattning,
    string Anstallningsform,
    decimal Sysselsattningsgrad,
    string Enhet);

/// <summary>En sida ur ett större resultat: raderna + totalantalet matchande poster.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int TotalAntal);

public record EnhetVal(OrganizationId Id, string Namn, string Kostnadsstalle);
public record AvtalVal(CollectiveAgreementId Id, string Namn);

/// <summary>
/// En nod i organisationsträdet (vy-modell). Bär enhetens visningsfält, förälder-id (för
/// hierarkibygget), antal aktiva anställda direkt i enheten och — efter <see
/// cref="AnstallningService.ByggHierarki"/> — det totala antalet i hela subträdet samt
/// barnnoderna. Muterbar där det behövs (Barn/summor fylls under trädbygget); allt annat är
/// oföränderligt efter konstruktion.
/// </summary>
public sealed class OrgTreeNode
{
    public OrganizationId Id { get; }
    public string Namn { get; }
    public OrganizationUnitType Typ { get; }
    public string Kostnadsstalle { get; }
    public Guid? OverordnadId { get; }

    /// <summary>Antal aktiva anställda direkt i denna enhet (exkl. underenheter).</summary>
    public int AntalAnstalldaDirekt { get; set; }

    /// <summary>Antal aktiva anställda i hela subträdet (inkl. denna enhet). Sätts av trädbygget.</summary>
    public int AntalAnstalldaTotalt { get; set; }

    public List<OrgTreeNode> Barn { get; } = [];

    /// <summary>Antal direkta underenheter.</summary>
    public int AntalUnderenheter => Barn.Count;

    public OrgTreeNode(
        OrganizationId id,
        string namn,
        OrganizationUnitType typ,
        string kostnadsstalle,
        Guid? overordnadId,
        int antalAnstalldaDirekt = 0)
    {
        Id = id;
        Namn = namn;
        Typ = typ;
        Kostnadsstalle = kostnadsstalle;
        OverordnadId = overordnadId;
        AntalAnstalldaDirekt = antalAnstalldaDirekt;
        AntalAnstalldaTotalt = antalAnstalldaDirekt;
    }
}

/// <summary>Detaljvy för en organisationsenhet (enhetsfält + förälder + direkta barn).</summary>
public record OrgEnhetDetalj(
    OrganizationId Id,
    string Namn,
    OrganizationUnitType Typ,
    string Kostnadsstalle,
    string? CFARKod,
    string? HsaId,
    DateOnly GiltigFran,
    DateOnly? GiltigTill,
    OrganizationId? OverordnadId,
    string? OverordnadNamn,
    IReadOnlyList<OrgEnhetBarn> DirektaUnderenheter,
    int AntalAnstallda);

/// <summary>En direkt underenhet i detaljvyn (klickbar, med antal anställda).</summary>
public record OrgEnhetBarn(OrganizationId Id, string Namn, OrganizationUnitType Typ, int AntalAnstallda);

/// <summary>Rad i sammanfattningen "antal anställda per befattning".</summary>
public record BefattningsAntal(string Befattning, int Antal);
