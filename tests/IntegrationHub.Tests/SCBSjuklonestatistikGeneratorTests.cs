using RegionHR.IntegrationHub.Adapters.SCB;
using Xunit;

namespace RegionHR.IntegrationHub.Tests;

public class SCBSjuklonestatistikGeneratorTests
{
    private static SCBSjuklonestatistikInput Bygg(params SCBSjuklonIndivid[] individer) => new()
    {
        Ar = 2025,
        Kvartal = 2,
        Organisationsnamn = "Region Örebro län",
        Organisationsnummer = "232100-0016",
        Individer = individer.ToList()
    };

    private static SCBSjuklonIndivid Ind(string kon, int sjukdagar, decimal sjuklon, decimal mojliga) =>
        new() { Kon = kon, SjukdagarISjuklonePeriod = sjukdagar, UtbetaldSjuklon = sjuklon, MojligaArbetsdagar = mojliga };

    [Fact]
    public void Aggregerar_per_kon_och_total()
    {
        var input = Bygg(
            Ind("Man", 5, 4000m, 60m),
            Ind("Man", 0, 0m, 60m),
            Ind("Kvinna", 10, 8000m, 60m));

        var result = new SCBSjuklonestatistikGenerator().Generera(input);

        var man = result.Grupper.Single(g => g.Kon == "M");
        Assert.Equal(2, man.AntalAnstallda);
        Assert.Equal(1, man.AntalMedSjuklon);
        Assert.Equal(5, man.SummaSjukdagar);
        Assert.Equal(4000m, man.SummaUtbetaldSjuklon);
        // 5 / 120 * 100 = 4.17 %
        Assert.Equal(4.17m, man.SjukfranvaroProcent);

        Assert.Equal(3, result.Totalt.AntalAnstallda);
        Assert.Equal(2, result.Totalt.AntalMedSjuklon);
        Assert.Equal(15, result.Totalt.SummaSjukdagar);
        Assert.Equal(12000m, result.Totalt.SummaUtbetaldSjuklon);
        // 15 / 180 * 100 = 8.33 %
        Assert.Equal(8.33m, result.Totalt.SjukfranvaroProcent);
    }

    [Fact]
    public void Sjukdagar_kappas_till_sjukloneperioden_14_dagar()
    {
        var input = Bygg(Ind("Kvinna", 20, 10000m, 63m));
        var g = Assert.Single(new SCBSjuklonestatistikGenerator().Generera(input).Grupper);
        Assert.Equal(14, g.SummaSjukdagar); // 20 → kappat till 14
    }

    [Fact]
    public void Nollstalld_period_ger_noll_procent()
    {
        var input = Bygg(Ind("Man", 0, 0m, 0m));
        Assert.Equal(0m, new SCBSjuklonestatistikGenerator().Generera(input).Totalt.SjukfranvaroProcent);
    }

    [Fact]
    public void Filen_har_typ_status_och_totalrad()
    {
        var input = Bygg(Ind("Man", 5, 4000m, 60m), Ind("Kvinna", 10, 8000m, 60m));
        var result = new SCBSjuklonestatistikGenerator().Generera(input);

        Assert.Contains("#TYP=SCB-SJUKLONESTATISTIK-KSJU", result.Innehall);
        Assert.Contains("#STATUS=EJ_INLAMNAD_KRAVER_SCB_INLOGGNING", result.Innehall);
        Assert.Contains("Kon;AntalAnstallda;AntalMedSjuklon;SummaSjukdagar", result.Innehall);
        Assert.Contains("\r\nA;", result.Innehall); // totalraden (kön "A")
        Assert.Contains("K2", result.Filnamn);      // kvartal 2 i filnamnet
    }
}
