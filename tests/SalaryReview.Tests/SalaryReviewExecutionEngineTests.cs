using Xunit;
using RegionHR.Core.Domain;
using RegionHR.SalaryReview.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.SalaryReview.Tests;

/// <summary>
/// Tester för genomförandeflödet: en fackligt godkänd runda ska applicera ny lön
/// på rätt anställning och beräkna retroaktiv efterbetalning.
/// </summary>
public class SalaryReviewExecutionEngineTests
{
    private static readonly DateOnly Ikraft = new(2026, 4, 1);
    private static readonly DateOnly Genomforande = new(2026, 6, 15); // 2 månaders retro

    private static Employee EmployeeMed(
        Money lon, out EmploymentId anstId,
        CollectiveAgreementType avtal = CollectiveAgreementType.AB)
    {
        var emp = Employee.Skapa(new Personnummer("198112289874"), "Test", "Person");
        var anst = emp.LaggTillAnstallning(
            OrganizationId.New(), EmploymentType.Tillsvidare, avtal,
            lon, new Percentage(100m), new DateOnly(2025, 1, 1));
        anstId = anst.Id;
        return emp;
    }

    private static SalaryReviewRound GodkandRundaMed(
        EmployeeId anstallId, Money nuvarande, Money foreslagen,
        EmploymentId? anstallningId = null)
    {
        var runda = SalaryReviewRound.Skapa(
            "Test", 2026, CollectiveAgreementType.AB, Money.SEK(1_000_000m), Ikraft);
        var f = runda.LaggTillForslag(anstallId, nuvarande, foreslagen, "Motiv", anstallningId);
        runda.GodkannForslag(f.Id);
        runda.SkickaFackligAvstemning();
        runda.GodkannFacklig("Facklig motpart");
        return runda;
    }

    [Fact]
    public void Genomfor_applicerar_ny_lon_pa_anstallningen()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out var anstId);
        var runda = GodkandRundaMed(emp.Id, Money.SEK(30_000m), Money.SEK(32_000m), anstId);
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        var res = new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr");

        Assert.Equal(Money.SEK(32_000m), emp.Anstallningar.Single().Manadslon);
        Assert.Equal(1, res.AntalAnstallda);
        Assert.Equal(Money.SEK(2_000m), res.TotalOkning);
    }

    [Fact]
    public void Genomfor_flyttar_runda_till_Genomford_med_datum()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out var anstId);
        var runda = GodkandRundaMed(emp.Id, Money.SEK(30_000m), Money.SEK(31_000m), anstId);
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr");

        Assert.Equal(SalaryReviewStatus.Genomford, runda.Status);
        Assert.Equal(Genomforande, runda.GenomfordDatum);
    }

    [Fact]
    public void Genomfor_beraknar_retroaktivt_belopp_fran_ikrafttradande()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out var anstId);
        var runda = GodkandRundaMed(emp.Id, Money.SEK(30_000m), Money.SEK(32_000m), anstId);
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        var res = new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr");

        var andring = Assert.Single(res.Andringar);
        Assert.Equal(2, andring.RetroaktivaManader);            // april + maj
        Assert.Equal(Money.SEK(4_000m), andring.RetroaktivtBelopp); // 2 000 × 2
        Assert.Equal(Money.SEK(4_000m), res.TotalRetroaktivt);
        Assert.Equal(Money.SEK(4_000m), runda.Forslag.Single().RetroaktivtBelopp);
    }

    [Fact]
    public void Genomfor_applicerar_bara_godkanda_forslag()
    {
        var emp1 = EmployeeMed(Money.SEK(30_000m), out var anst1);
        var emp2 = EmployeeMed(Money.SEK(40_000m), out var anst2);

        var runda = SalaryReviewRound.Skapa(
            "Test", 2026, CollectiveAgreementType.AB, Money.SEK(1_000_000m), Ikraft);
        var f1 = runda.LaggTillForslag(emp1.Id, Money.SEK(30_000m), Money.SEK(31_000m), "m1", anst1);
        runda.LaggTillForslag(emp2.Id, Money.SEK(40_000m), Money.SEK(42_000m), "m2", anst2);
        runda.GodkannForslag(f1.Id); // f2 lämnas som Forslag
        runda.SkickaFackligAvstemning();
        runda.GodkannFacklig("rep");

        var dict = new Dictionary<EmployeeId, Employee> { [emp1.Id] = emp1, [emp2.Id] = emp2 };
        var res = new SalaryReviewExecutionEngine().Genomfor(runda, dict, new DateOnly(2026, 6, 1), "hr");

        Assert.Single(res.Andringar);
        Assert.Equal(Money.SEK(31_000m), emp1.Anstallningar.Single().Manadslon);
        Assert.Equal(Money.SEK(40_000m), emp2.Anstallningar.Single().Manadslon); // oförändrad
    }

    [Fact]
    public void Genomfor_fran_ej_godkand_status_kastar()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out var anstId);
        var runda = SalaryReviewRound.Skapa(
            "Test", 2026, CollectiveAgreementType.AB, Money.SEK(1_000_000m), Ikraft);
        var f = runda.LaggTillForslag(emp.Id, Money.SEK(30_000m), Money.SEK(31_000m), "m", anstId);
        runda.GodkannForslag(f.Id); // fortfarande Planering, ej fackligt godkänd
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        Assert.Throws<InvalidOperationException>(() =>
            new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr"));
    }

    [Fact]
    public void Genomfor_utan_godkanda_forslag_kastar()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out var anstId);
        var runda = SalaryReviewRound.Skapa(
            "Test", 2026, CollectiveAgreementType.AB, Money.SEK(1_000_000m), Ikraft);
        runda.LaggTillForslag(emp.Id, Money.SEK(30_000m), Money.SEK(31_000m), "m", anstId);
        runda.SkickaFackligAvstemning();
        runda.GodkannFacklig("rep"); // Godkand men förslaget är fortfarande Forslag
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        Assert.Throws<InvalidOperationException>(() =>
            new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr"));
    }

    [Fact]
    public void Genomfor_med_saknad_anstalld_kastar()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out var anstId);
        var runda = GodkandRundaMed(emp.Id, Money.SEK(30_000m), Money.SEK(31_000m), anstId);
        var tomt = new Dictionary<EmployeeId, Employee>(); // anställd saknas

        Assert.Throws<InvalidOperationException>(() =>
            new SalaryReviewExecutionEngine().Genomfor(runda, tomt, Genomforande, "hr"));
    }

    [Fact]
    public void Genomfor_med_explicit_anstallningId_traffar_ratt_anstallning()
    {
        var emp = Employee.Skapa(new Personnummer("198112289874"), "Fler", "Anstallningar");
        var a1 = emp.LaggTillAnstallning(OrganizationId.New(), EmploymentType.Tillsvidare,
            CollectiveAgreementType.AB, Money.SEK(30_000m), new Percentage(100m), new DateOnly(2025, 1, 1));
        var a2 = emp.LaggTillAnstallning(OrganizationId.New(), EmploymentType.Tillsvidare,
            CollectiveAgreementType.AB, Money.SEK(20_000m), new Percentage(50m), new DateOnly(2025, 1, 1));

        var runda = GodkandRundaMed(emp.Id, Money.SEK(20_000m), Money.SEK(22_000m), a2.Id);
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr");

        Assert.Equal(Money.SEK(22_000m), emp.Anstallningar.First(a => a.Id == a2.Id).Manadslon);
        Assert.Equal(Money.SEK(30_000m), emp.Anstallningar.First(a => a.Id == a1.Id).Manadslon); // oförändrad
    }

    [Fact]
    public void Genomfor_med_ett_aktivt_anstallning_harleds_utan_explicit_id()
    {
        var emp = EmployeeMed(Money.SEK(30_000m), out _);
        var runda = GodkandRundaMed(emp.Id, Money.SEK(30_000m), Money.SEK(31_500m)); // ingen explicit id
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr");

        Assert.Equal(Money.SEK(31_500m), emp.Anstallningar.Single().Manadslon);
    }

    [Fact]
    public void Genomfor_med_tvetydig_anstallning_kastar()
    {
        var emp = Employee.Skapa(new Personnummer("198112289874"), "Tve", "Tydig");
        emp.LaggTillAnstallning(OrganizationId.New(), EmploymentType.Tillsvidare,
            CollectiveAgreementType.AB, Money.SEK(30_000m), new Percentage(100m), new DateOnly(2025, 1, 1));
        emp.LaggTillAnstallning(OrganizationId.New(), EmploymentType.Tillsvidare,
            CollectiveAgreementType.AB, Money.SEK(25_000m), new Percentage(50m), new DateOnly(2025, 1, 1));

        // Utgångslön matchar ingen av anställningarna och inget explicit id anges → tvetydigt.
        var runda = GodkandRundaMed(emp.Id, Money.SEK(99_999m), Money.SEK(100_000m));
        var dict = new Dictionary<EmployeeId, Employee> { [emp.Id] = emp };

        Assert.Throws<InvalidOperationException>(() =>
            new SalaryReviewExecutionEngine().Genomfor(runda, dict, Genomforande, "hr"));
    }

    [Theory]
    [InlineData(2026, 4, 1, 2026, 6, 15, 2)]   // 2 månader retro
    [InlineData(2025, 4, 1, 2026, 6, 1, 14)]   // årsövergång, 14 månader
    [InlineData(2026, 6, 1, 2026, 6, 20, 0)]   // samma månad → ingen retro
    [InlineData(2026, 8, 1, 2026, 6, 15, 0)]   // ikraft i framtiden → ingen retro
    public void RetroaktivaManader_beraknas_korrekt(
        int iy, int im, int idd, int gy, int gm, int gd, int forvantat)
    {
        var manader = SalaryReviewExecutionEngine.RetroaktivaManader(
            new DateOnly(iy, im, idd), new DateOnly(gy, gm, gd));
        Assert.Equal(forvantat, manader);
    }
}
