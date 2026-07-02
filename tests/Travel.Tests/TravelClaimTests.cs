using Xunit;
using RegionHR.Travel.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Travel.Tests;

public class TravelClaimTests
{
    private static TravelClaim SkapaTestResekrav()
    {
        return TravelClaim.Skapa(
            EmployeeId.New(),
            "Tjänsteresa Stockholm",
            new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void Skapa_resekrav_satter_korrekta_varden()
    {
        var anstallId = EmployeeId.New();
        var claim = TravelClaim.Skapa(anstallId, "Konferens Göteborg", new DateOnly(2026, 4, 1));

        Assert.Equal(anstallId, claim.AnstallId);
        Assert.Equal("Konferens Göteborg", claim.Beskrivning);
        Assert.Equal(new DateOnly(2026, 4, 1), claim.ReseDatum);
        Assert.Equal(TravelClaimStatus.Utkast, claim.Status);
        Assert.Equal(Money.Zero, claim.TotalBelopp);
    }

    [Fact]
    public void Traktamente_berakning_hela_och_halva_dagar()
    {
        var claim = SkapaTestResekrav();

        // 2 hela dagar (300 kr/dag) + 1 halv dag (150 kr/dag) = 750 kr (Skatteverket 2026)
        claim.SattTraktamente(2, 1);

        Assert.Equal(2, claim.HelaDagar);
        Assert.Equal(1, claim.HalvaDagar);
        Assert.Equal(Money.SEK(750m), claim.Traktamente);
        Assert.Equal(Money.SEK(750m), claim.TotalBelopp);
    }

    [Fact]
    public void Milersattning_berakning()
    {
        var claim = SkapaTestResekrav();

        // 15 mil * 25 kr/mil = 375 kr
        claim.SattMilersattning(15m);

        Assert.Equal(15m, claim.KordaMil);
        Assert.Equal(Money.SEK(375m), claim.Milersattning);
        Assert.Equal(Money.SEK(375m), claim.TotalBelopp);
    }

    [Fact]
    public void Utlagg_med_kvitto()
    {
        var claim = SkapaTestResekrav();

        claim.LaggTillUtlagg("Hotell", Money.SEK(1_200m), "kvitto-123");

        Assert.Single(claim.Utlagg);
        Assert.Equal("Hotell", claim.Utlagg[0].Beskrivning);
        Assert.Equal(Money.SEK(1_200m), claim.Utlagg[0].Belopp);
        Assert.Equal("kvitto-123", claim.Utlagg[0].KvittoBildId);
        Assert.Equal(Money.SEK(1_200m), claim.TotalBelopp);
    }

    [Fact]
    public void TotalBelopp_aggregerar_alla_delar_korrekt()
    {
        var claim = SkapaTestResekrav();

        // Traktamente: 1 hel (300) + 0 halva = 300 (Skatteverket 2026)
        claim.SattTraktamente(1, 0);

        // Milersättning: 10 mil * 25 = 250
        claim.SattMilersattning(10m);

        // Utlägg: 500 + 350 = 850
        claim.LaggTillUtlagg("Lunch", Money.SEK(500m));
        claim.LaggTillUtlagg("Taxi", Money.SEK(350m));

        // Total: 300 + 250 + 850 = 1400
        Assert.Equal(Money.SEK(1_400m), claim.TotalBelopp);
    }

    [Fact]
    public void Attestera_flow_satter_status_och_attestant()
    {
        var claim = SkapaTestResekrav();
        claim.LaggTillUtlagg("Parkering", Money.SEK(100m));
        claim.SkickaIn();

        claim.Attestera("Karin Lindberg");

        Assert.Equal(TravelClaimStatus.Godkand, claim.Status);
        Assert.Equal("Karin Lindberg", claim.AttesteradAv);
        Assert.NotNull(claim.AttesteradVid);
    }

    [Fact]
    public void Avvisa_flow_satter_status_och_anledning()
    {
        var claim = SkapaTestResekrav();
        claim.LaggTillUtlagg("Parkering", Money.SEK(100m));
        claim.SkickaIn();

        claim.Avvisa("Karin Lindberg", "Kvitto saknas");

        Assert.Equal(TravelClaimStatus.Avslagen, claim.Status);
        Assert.Equal("Karin Lindberg", claim.AttesteradAv);
        Assert.Equal("Kvitto saknas", claim.AvvisningsAnledning);
    }

    [Fact]
    public void SkickaIn_satter_status_Inskickad()
    {
        var claim = SkapaTestResekrav();
        claim.LaggTillUtlagg("Mat", Money.SEK(200m));

        claim.SkickaIn();

        Assert.Equal(TravelClaimStatus.Inskickad, claim.Status);
    }

    [Fact]
    public void Attestera_utanfor_Inskickad_status_kastar_exception()
    {
        var claim = SkapaTestResekrav();

        Assert.Throws<InvalidOperationException>(() => claim.Attestera("Test"));
    }

    [Fact]
    public void MarkeraSomUtbetald_fran_Godkand_lyckas()
    {
        var claim = SkapaTestResekrav();
        claim.LaggTillUtlagg("Biljett", Money.SEK(500m));
        claim.SkickaIn();
        claim.Attestera("Chef");

        claim.MarkeraSomUtbetald();

        Assert.Equal(TravelClaimStatus.Utbetald, claim.Status);
    }

    [Fact]
    public void MarkeraSomUtbetald_fran_Inskickad_kastar_exception()
    {
        var claim = SkapaTestResekrav();
        claim.LaggTillUtlagg("Biljett", Money.SEK(500m));
        claim.SkickaIn();

        Assert.Throws<InvalidOperationException>(() => claim.MarkeraSomUtbetald());
    }
}
