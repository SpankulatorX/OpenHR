using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SalaryReview.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Orkestrerar löneöversynens genomförandeflöde för Blazor-sidorna:
/// förslag → facklig avstämning → godkännande → genomförande (applicerar ny lön + retro).
///
/// Använder <see cref="IDbContextFactory{TContext}"/> direkt (samma mönster som
/// övriga Web-tjänster) och kör den rena <see cref="SalaryReviewExecutionEngine"/>
/// för själva löneappliceringen. Kan registreras i DI eller instansieras av sidan.
/// </summary>
public sealed class LoneoversynService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly SalaryReviewExecutionEngine _engine = new();

    public LoneoversynService(IDbContextFactory<RegionHRDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<List<SalaryReviewRound>> HamtaRundorAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SalaryReviewRounds
            .Include(r => r.Forslag)
            .OrderByDescending(r => r.Ar)
            .ToListAsync(ct);
    }

    public async Task<SalaryReviewRound?> HamtaRundaAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SalaryReviewRounds
            .Include(r => r.Forslag)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Anställda med en aktiv anställning inom rundans avtalsområde, med nuvarande lön
    /// och rätt anställnings-id — underlag för att lägga till löneförslag.
    /// </summary>
    public async Task<List<LoneKandidat>> HamtaKandidaterAsync(
        CollectiveAgreementType avtal, DateOnly peildatum, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var anstallda = await db.Employees
            .Include(e => e.Anstallningar)
            .OrderBy(e => e.Efternamn)
            .ToListAsync(ct);

        var kandidater = new List<LoneKandidat>();
        foreach (var e in anstallda)
        {
            var anstallning = e.AktivaAnstallningar(peildatum)
                .FirstOrDefault(a => a.Kollektivavtal == avtal)
                ?? e.AktivaAnstallningar(peildatum).FirstOrDefault();
            if (anstallning is null) continue;

            kandidater.Add(new LoneKandidat(
                e.Id,
                anstallning.Id,
                e.FulltNamn,
                anstallning.Befattningstitel ?? "-",
                anstallning.Manadslon));
        }
        return kandidater;
    }

    public async Task LaggTillForslagAsync(
        Guid rundaId, EmployeeId anstallId, EmploymentId anstallningId,
        decimal foreslagenLon, string motivering, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);

        var employee = await db.Employees
            .Include(e => e.Anstallningar)
            .FirstOrDefaultAsync(e => e.Id == anstallId, ct)
            ?? throw new InvalidOperationException("Anställd hittades inte.");
        var anstallning = employee.Anstallningar.FirstOrDefault(a => a.Id == anstallningId)
            ?? throw new InvalidOperationException("Anställning hittades inte på den anställde.");

        runda.LaggTillForslag(anstallId, anstallning.Manadslon, Money.SEK(foreslagenLon), motivering, anstallningId);
        await db.SaveChangesAsync(ct);
    }

    public async Task GodkannForslagAsync(Guid rundaId, Guid forslagId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.GodkannForslag(forslagId);
        await db.SaveChangesAsync(ct);
    }

    public async Task AvvisaForslagAsync(Guid rundaId, Guid forslagId, string anledning, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.AvvisaForslag(forslagId, anledning);
        await db.SaveChangesAsync(ct);
    }

    public async Task SkickaFackligAvstemningAsync(Guid rundaId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.SkickaFackligAvstemning();
        await db.SaveChangesAsync(ct);
    }

    public async Task GodkannFackligAsync(Guid rundaId, string fackligRepresentant, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.GodkannFacklig(fackligRepresentant);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Genomför rundan: applicerar varje godkänt förslag som ny lön på anställningen,
    /// beräknar retroaktivt belopp och sparar allt i samma transaktion.
    /// </summary>
    public async Task<SalaryReviewExecutionResult> GenomforAsync(
        Guid rundaId, string genomfordAv, DateOnly? genomforandeDatum = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);

        var anstalldIds = runda.Forslag
            .Where(f => f.Status == SalaryProposalStatus.Godkand)
            .Select(f => f.AnstallId)
            .Distinct()
            .ToList();

        var anstallda = new Dictionary<EmployeeId, Employee>();
        foreach (var id in anstalldIds)
        {
            var employee = await db.Employees
                .Include(e => e.Anstallningar)
                .FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new InvalidOperationException($"Anställd {id} hittades inte.");
            anstallda[id] = employee;
        }

        var datum = genomforandeDatum ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resultat = _engine.Genomfor(runda, anstallda, datum, genomfordAv);

        // En SaveChanges persisterar både rundans status/retro och de nya lönerna
        // (alla aggregat spåras av samma DbContext).
        await db.SaveChangesAsync(ct);
        return resultat;
    }

    /// <summary>Namnuppslag EmployeeId → fullständigt namn för att visa förslag/ändringar.</summary>
    public async Task<Dictionary<EmployeeId, string>> HamtaAnstalldNamnAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var anstallda = await db.Employees.ToListAsync(ct);
        return anstallda.ToDictionary(e => e.Id, e => e.FulltNamn);
    }

    private static async Task<SalaryReviewRound> LaddaRundaAsync(
        RegionHRDbContext db, Guid rundaId, CancellationToken ct) =>
        await db.SalaryReviewRounds.Include(r => r.Forslag).FirstOrDefaultAsync(r => r.Id == rundaId, ct)
        ?? throw new InvalidOperationException($"Löneöversynsrunda {rundaId} hittades inte.");
}

/// <summary>En anställd som kan få ett löneförslag i en runda.</summary>
public sealed record LoneKandidat(
    EmployeeId AnstallId,
    EmploymentId AnstallningId,
    string Namn,
    string Befattning,
    Money NuvarandeLon);
