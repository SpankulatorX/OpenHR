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

    public async Task<List<OrganizationUnit>> HamtaOrganisationAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.OrganizationUnits
            .Include(o => o.Underenheter)
            .Where(o => o.OverordnadEnhetId == null)
            .ToListAsync(ct);
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
