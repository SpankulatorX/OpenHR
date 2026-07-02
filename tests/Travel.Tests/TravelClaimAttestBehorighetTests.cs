using Xunit;
using RegionHR.Travel.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Travel.Tests;

/// <summary>
/// Behörighetsregler för attest av resekrav: självattest förbjuden,
/// beloppsgräns kräver HR, och klar-för-utbetalning-signalen.
/// </summary>
public class TravelClaimAttestBehorighetTests
{
    private static TravelClaim InskickatKrav(EmployeeId inlamnare, decimal utlaggBelopp = 0m)
    {
        var claim = TravelClaim.Skapa(inlamnare, "Tjänsteresa", new DateOnly(2026, 5, 12));
        if (utlaggBelopp > 0m)
            claim.LaggTillUtlagg("Utlägg", Money.SEK(utlaggBelopp));
        claim.SkickaIn();
        return claim;
    }

    [Fact]
    public void Sjalvattest_forbjuden_nar_attestant_ar_inlamnaren()
    {
        var inlamnare = EmployeeId.New();
        var claim = InskickatKrav(inlamnare);

        var ex = Assert.Throws<InvalidOperationException>(
            () => claim.Attestera(inlamnare, "Egen Anställd", attestantArHR: false));

        Assert.Contains("självattest", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Status oförändrad — inget godkänns.
        Assert.Equal(TravelClaimStatus.Inskickad, claim.Status);
    }

    [Fact]
    public void Attest_tillaten_nar_attestant_ar_annan_person()
    {
        var inlamnare = EmployeeId.New();
        var chef = EmployeeId.New();
        var claim = InskickatKrav(inlamnare);

        claim.Attestera(chef, "Eva Nilsson", attestantArHR: false);

        Assert.Equal(TravelClaimStatus.Godkand, claim.Status);
        Assert.Equal("Eva Nilsson", claim.AttesteradAv);
    }

    [Fact]
    public void Attestant_utan_anstallningskoppling_far_alltid_attestera()
    {
        // Admin utan EmployeeId (null) kan aldrig vara inlämnaren → tillåts.
        var claim = InskickatKrav(EmployeeId.New());

        claim.Attestera(null, "Systemadministratör", attestantArHR: true);

        Assert.Equal(TravelClaimStatus.Godkand, claim.Status);
    }

    [Fact]
    public void Krav_over_belopsgrans_kraver_HR_behorighet()
    {
        var claim = InskickatKrav(EmployeeId.New(), utlaggBelopp: 30_000m);
        Assert.True(claim.KraverHRAttest);

        // Chef utan HR-behörighet nekas.
        var ex = Assert.Throws<InvalidOperationException>(
            () => claim.Attestera(EmployeeId.New(), "Chef", attestantArHR: false));
        Assert.Contains("HR", ex.Message);
        Assert.Equal(TravelClaimStatus.Inskickad, claim.Status);

        // HR-behörig attestant tillåts.
        claim.Attestera(EmployeeId.New(), "Karl Berg", attestantArHR: true);
        Assert.Equal(TravelClaimStatus.Godkand, claim.Status);
    }

    [Fact]
    public void Krav_under_belopsgrans_kraver_ej_HR()
    {
        var claim = InskickatKrav(EmployeeId.New(), utlaggBelopp: 5_000m);
        Assert.False(claim.KraverHRAttest);

        claim.Attestera(EmployeeId.New(), "Chef", attestantArHR: false);

        Assert.Equal(TravelClaimStatus.Godkand, claim.Status);
    }

    [Fact]
    public void Avvisa_sjalvattest_forbjuden()
    {
        var inlamnare = EmployeeId.New();
        var claim = InskickatKrav(inlamnare);

        Assert.Throws<InvalidOperationException>(
            () => claim.Avvisa(inlamnare, "Egen Anställd", "Vill inte betala"));
        Assert.Equal(TravelClaimStatus.Inskickad, claim.Status);
    }

    [Fact]
    public void Avvisa_med_annan_attestant_satter_avslagen_och_anledning()
    {
        var claim = InskickatKrav(EmployeeId.New());

        claim.Avvisa(EmployeeId.New(), "Eva Nilsson", "Kvitto saknas");

        Assert.Equal(TravelClaimStatus.Avslagen, claim.Status);
        Assert.Equal("Kvitto saknas", claim.AvvisningsAnledning);
        Assert.Equal("Eva Nilsson", claim.AttesteradAv);
    }

    [Fact]
    public void ArKlarForUtbetalning_endast_i_godkant_tillstand()
    {
        var claim = InskickatKrav(EmployeeId.New());
        Assert.False(claim.ArKlarForUtbetalning); // Inskickad

        claim.Attestera(EmployeeId.New(), "Chef", attestantArHR: false);
        Assert.True(claim.ArKlarForUtbetalning);  // Godkand → lönekörningen plockar upp

        claim.MarkeraSomUtbetald();
        Assert.False(claim.ArKlarForUtbetalning); // Utbetald → redan hanterad
    }
}
