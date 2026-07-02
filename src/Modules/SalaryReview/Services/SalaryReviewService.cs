using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;
using RegionHR.SalaryReview.Domain;

namespace RegionHR.SalaryReview.Services;

/// <summary>
/// Tjänst för att hantera löneöversynsrundor — budgetfördelning, facklig avstämning och genomförande.
/// </summary>
public sealed class SalaryReviewService
{
    private readonly IRepository<SalaryReviewRound, Guid> _repository;

    public SalaryReviewService(IRepository<SalaryReviewRound, Guid> repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Skapar en ny löneöversynsrunda med budget.
    /// </summary>
    public async Task<SalaryReviewRound> SkapaRundaAsync(
        string namn, int ar, CollectiveAgreementType avtal,
        Money budget, DateOnly ikrafttradande, CancellationToken ct)
    {
        var runda = SalaryReviewRound.Skapa(namn, ar, avtal, budget, ikrafttradande);
        await _repository.AddAsync(runda, ct);
        return runda;
    }

    /// <summary>
    /// Fördelar budget ned i organisationshierarkin (top -> förvaltning -> verksamhet -> enhet).
    /// </summary>
    public async Task FordelaBudgetAsync(
        Guid rundaId, IReadOnlyList<BudgetFordelning> fordelningar, CancellationToken ct)
    {
        var runda = await _repository.GetByIdAsync(rundaId, ct)
            ?? throw new InvalidOperationException($"Löneöversynsrunda {rundaId} hittades inte");

        var totalFordelad = Money.Zero;
        foreach (var fordelning in fordelningar)
        {
            totalFordelad = totalFordelad + fordelning.Budget;
        }

        if (totalFordelad > runda.AterstaendeBudget + runda.FordeladBudget)
            throw new InvalidOperationException("Total fördelning överskrider budget");

        await _repository.UpdateAsync(runda, ct);
    }

    /// <summary>
    /// Exporterar sammanställning för facklig granskning — aggregerad statistik per avtalsområde.
    /// </summary>
    public async Task<FackligSammanstallning> ExporteraFackligAsync(Guid rundaId, CancellationToken ct)
    {
        var runda = await _repository.GetByIdAsync(rundaId, ct)
            ?? throw new InvalidOperationException($"Löneöversynsrunda {rundaId} hittades inte");

        var aktiva = runda.Forslag
            .Where(f => f.Status != SalaryProposalStatus.Avslagen)
            .ToList();

        var totalOkning = aktiva.Count > 0
            ? Money.SEK(aktiva.Sum(f => f.Okning.Amount))
            : Money.Zero;

        var genomsnittligOkning = aktiva.Count > 0
            ? aktiva.Average(f => f.OkningProcent)
            : 0m;

        var perBefattning = new Dictionary<string, decimal>();

        return new FackligSammanstallning
        {
            AntalAnstallda = aktiva.Count,
            GenomsnittligOkningProcent = genomsnittligOkning,
            TotalOkning = totalOkning,
            PerBefattning = perBefattning
        };
    }

    /// <summary>
    /// Flyttar rundan till status Genomförd via aggregatet. Själva appliceringen av
    /// ny lön på anställningarna görs av <see cref="Domain.SalaryReviewExecutionEngine"/>
    /// i det lager som har tillgång till Employee-aggregaten (Web-tjänsten
    /// LoneoversynService), eftersom Employee ligger utanför denna repository.
    /// </summary>
    public async Task GenomforAsync(Guid rundaId, CancellationToken ct)
    {
        var runda = await _repository.GetByIdAsync(rundaId, ct)
            ?? throw new InvalidOperationException($"Löneöversynsrunda {rundaId} hittades inte");

        runda.Genomfor();
        await _repository.UpdateAsync(runda, ct);
    }
}

/// <summary>
/// Budgetfördelning per organisationsenhet.
/// </summary>
public sealed class BudgetFordelning
{
    public OrganizationId EnhetId { get; set; }
    public Money Budget { get; set; }
}

/// <summary>
/// Sammanställning för facklig avstämning.
/// </summary>
public sealed class FackligSammanstallning
{
    public int AntalAnstallda { get; set; }
    public decimal GenomsnittligOkningProcent { get; set; }
    public Money TotalOkning { get; set; }
    public Dictionary<string, decimal> PerBefattning { get; set; } = new();
}
