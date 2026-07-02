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

    public async Task<List<EmployeeListItem>> HamtaAllaAsync(string? sokterm = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Employees
            .Include(e => e.Anstallningar)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(sokterm))
        {
            var term = sokterm.ToLower();
            query = query.Where(e =>
                e.Fornamn.ToLower().Contains(term) ||
                e.Efternamn.ToLower().Contains(term));
        }

        var employees = await query.OrderBy(e => e.Efternamn).Take(100).ToListAsync(ct);

        return employees.Select(e =>
        {
            var aktiv = e.Anstallningar.FirstOrDefault(a =>
                a.Giltighetsperiod.Start <= DateOnly.FromDateTime(DateTime.Today) &&
                (a.Giltighetsperiod.End == null || a.Giltighetsperiod.End >= DateOnly.FromDateTime(DateTime.Today)));
            return new EmployeeListItem(
                e.Id,
                e.Fornamn,
                e.Efternamn,
                e.Personnummer.ToMaskedString(),
                e.Epost,
                aktiv?.Befattningstitel ?? "-",
                aktiv?.Anstallningsform.ToString() ?? "-",
                aktiv?.Sysselsattningsgrad.Value ?? 0);
        }).ToList();
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
    decimal Sysselsattningsgrad);

public record EnhetVal(OrganizationId Id, string Namn, string Kostnadsstalle);
public record AvtalVal(CollectiveAgreementId Id, string Namn);
