using Xunit;
using RegionHR.SalaryReview.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.SalaryReview.Tests;

public class SalaryReviewRoundTests
{
    private static SalaryReviewRound SkapaTestRunda(Money? budget = null)
    {
        return SalaryReviewRound.Skapa(
            "Löneöversyn 2026",
            2026,
            CollectiveAgreementType.AB,
            budget ?? Money.SEK(500_000m),
            new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void Skapa_runda_med_budget_satter_korrekta_varden()
    {
        var runda = SkapaTestRunda(Money.SEK(1_000_000m));

        Assert.Equal("Löneöversyn 2026", runda.Namn);
        Assert.Equal(2026, runda.Ar);
        Assert.Equal(CollectiveAgreementType.AB, runda.Avtalsomrade);
        Assert.Equal(Money.SEK(1_000_000m), runda.TotalBudget);
        Assert.Equal(Money.Zero, runda.FordeladBudget);
        Assert.Equal(Money.SEK(1_000_000m), runda.AterstaendeBudget);
        Assert.Equal(SalaryReviewStatus.Planering, runda.Status);
        Assert.Equal(new DateOnly(2026, 4, 1), runda.IkrafttradandeDatum);
    }

    [Fact]
    public void LaggTillForslag_inom_budget_lyckas()
    {
        var runda = SkapaTestRunda(Money.SEK(10_000m));
        var anstallId = EmployeeId.New();

        var forslag = runda.LaggTillForslag(
            anstallId,
            Money.SEK(30_000m),
            Money.SEK(32_000m),
            "Bra prestation");

        Assert.Single(runda.Forslag);
        Assert.Equal(Money.SEK(2_000m), forslag.Okning);
        Assert.Equal(SalaryProposalStatus.Forslag, forslag.Status);
        Assert.Equal(Money.SEK(2_000m), runda.FordeladBudget);
    }

    [Fact]
    public void LaggTillForslag_overskrider_budget_kastar_exception()
    {
        var runda = SkapaTestRunda(Money.SEK(1_000m));
        var anstallId = EmployeeId.New();

        Assert.Throws<InvalidOperationException>(() =>
            runda.LaggTillForslag(
                anstallId,
                Money.SEK(30_000m),
                Money.SEK(32_000m),
                "Överskrider budget"));
    }

    [Fact]
    public void LaggTillForslag_med_lonesankning_kastar_exception_och_ror_inte_budgeten()
    {
        var runda = SkapaTestRunda(Money.SEK(10_000m));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            runda.LaggTillForslag(
                EmployeeId.New(),
                Money.SEK(30_000m),
                Money.SEK(28_000m),
                "Oavsiktlig sänkning"));

        Assert.Contains("lägre än nuvarande", ex.Message);
        Assert.Empty(runda.Forslag);
        // En negativ ökning får inte tyst frigöra budget.
        Assert.Equal(Money.Zero, runda.FordeladBudget);
    }

    [Fact]
    public void LaggTillForslag_med_oforandrad_lon_tillats()
    {
        var runda = SkapaTestRunda(Money.SEK(10_000m));

        var forslag = runda.LaggTillForslag(
            EmployeeId.New(), Money.SEK(30_000m), Money.SEK(30_000m), "Ingen förändring");

        Assert.Equal(Money.Zero, forslag.Okning);
        Assert.Equal(Money.Zero, runda.FordeladBudget);
    }

    [Fact]
    public void Facklig_avstamning_flow_Planering_till_Genomford()
    {
        var runda = SkapaTestRunda(Money.SEK(50_000m));
        var anstallId = EmployeeId.New();

        // Lägg till förslag
        runda.LaggTillForslag(anstallId, Money.SEK(30_000m), Money.SEK(31_000m), "Motivering");

        // Planering → FackligAvstemning
        runda.SkickaFackligAvstemning();
        Assert.Equal(SalaryReviewStatus.FackligAvstemning, runda.Status);

        // FackligAvstemning → Godkand
        runda.GodkannFacklig("Anna Svensson, Kommunal");
        Assert.Equal(SalaryReviewStatus.Godkand, runda.Status);
        Assert.Equal("Anna Svensson, Kommunal", runda.FackligRepresentant);

        // Godkand → Genomford
        runda.Genomfor();
        Assert.Equal(SalaryReviewStatus.Genomford, runda.Status);
    }

    [Fact]
    public void SkickaFackligAvstemning_utan_forslag_kastar_exception()
    {
        var runda = SkapaTestRunda();

        Assert.Throws<InvalidOperationException>(() => runda.SkickaFackligAvstemning());
    }

    [Fact]
    public void GodkannForslag_andrar_status_till_Godkand()
    {
        var runda = SkapaTestRunda(Money.SEK(50_000m));
        var anstallId = EmployeeId.New();

        var forslag = runda.LaggTillForslag(
            anstallId, Money.SEK(30_000m), Money.SEK(31_000m), "Motivering");

        runda.GodkannForslag(forslag.Id);

        Assert.Equal(SalaryProposalStatus.Godkand, forslag.Status);
    }

    [Fact]
    public void AvvisaForslag_andrar_status_och_aterstaller_budget()
    {
        var runda = SkapaTestRunda(Money.SEK(50_000m));
        var anstallId = EmployeeId.New();

        var forslag = runda.LaggTillForslag(
            anstallId, Money.SEK(30_000m), Money.SEK(31_500m), "Motivering");

        Assert.Equal(Money.SEK(1_500m), runda.FordeladBudget);

        runda.AvvisaForslag(forslag.Id, "Ej motiverat");

        Assert.Equal(SalaryProposalStatus.Avslagen, forslag.Status);
        Assert.Equal("Ej motiverat", forslag.AvvisningsAnledning);
        Assert.Equal(Money.Zero, runda.FordeladBudget);
    }

    [Fact]
    public void GenomsnittligOkningProcent_beraknas_korrekt()
    {
        var runda = SkapaTestRunda(Money.SEK(100_000m));

        // Anställd 1: 30 000 → 31 000 = 3.33%
        runda.LaggTillForslag(EmployeeId.New(), Money.SEK(30_000m), Money.SEK(31_000m), "M1");

        // Anställd 2: 40 000 → 41 200 = 3.00%
        runda.LaggTillForslag(EmployeeId.New(), Money.SEK(40_000m), Money.SEK(41_200m), "M2");

        // Förväntad: (3.333... + 3.0) / 2 ≈ 3.1666...
        var genomsnitt = runda.GenomsnittligOkningProcent;
        Assert.True(genomsnitt > 3.16m && genomsnitt < 3.17m,
            $"Genomsnittlig ökning borde vara ~3.17%, var {genomsnitt}%");
    }

    [Fact]
    public void GenomsnittligOkningProcent_exkluderar_avslagna()
    {
        var runda = SkapaTestRunda(Money.SEK(100_000m));

        var forslag1 = runda.LaggTillForslag(EmployeeId.New(), Money.SEK(30_000m), Money.SEK(31_000m), "M1");
        var forslag2 = runda.LaggTillForslag(EmployeeId.New(), Money.SEK(40_000m), Money.SEK(44_000m), "M2");

        runda.AvvisaForslag(forslag2.Id, "Ej godkänd");

        // Bara förslag 1 kvar: 1000/30000 * 100 = 3.333...%
        var genomsnitt = runda.GenomsnittligOkningProcent;
        Assert.True(genomsnitt > 3.33m && genomsnitt < 3.34m,
            $"Genomsnittlig ökning borde vara ~3.33%, var {genomsnitt}%");
    }

    [Fact]
    public void Genomfor_fran_Planering_kastar_exception()
    {
        var runda = SkapaTestRunda();
        runda.LaggTillForslag(EmployeeId.New(), Money.SEK(30_000m), Money.SEK(31_000m), "M1");

        Assert.Throws<InvalidOperationException>(() => runda.Genomfor());
    }

    [Fact]
    public void Genomfor_fran_FackligAvstemning_kastar_exception()
    {
        var runda = SkapaTestRunda();
        runda.LaggTillForslag(EmployeeId.New(), Money.SEK(30_000m), Money.SEK(31_000m), "M1");
        runda.SkickaFackligAvstemning();

        Assert.Throws<InvalidOperationException>(() => runda.Genomfor());
    }
}
