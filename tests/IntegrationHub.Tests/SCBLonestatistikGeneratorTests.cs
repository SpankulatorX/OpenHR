using System.Text;
using RegionHR.IntegrationHub.Adapters.SCB;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.IntegrationHub.Tests;

public class SCBLonestatistikGeneratorTests
{
    private static SCBLonestatistikInput Bygg(params SCBLoneIndivid[] individer) => new()
    {
        Ar = 2025,
        Manad = 11,
        Organisationsnamn = "Region Örebro län",
        Organisationsnummer = "232100-0016",
        Individer = individer.ToList()
    };

    private static SCBLoneIndivid Ind(string yrkeskod, string yrke, string kon, decimal lon, decimal sysgrad) =>
        new() { Yrkeskod = yrkeskod, Yrkesbenamning = yrke, Kon = kon, Manadslon = lon, Sysselsattningsgrad = sysgrad };

    [Fact]
    public void Aggregerar_per_yrke_och_kon()
    {
        var input = Bygg(
            Ind("204012", "Sjuksköterska", "Man", 40000m, 100m),
            Ind("204012", "Sjuksköterska", "Man", 44000m, 100m),
            Ind("204012", "Sjuksköterska", "Kvinna", 38000m, 100m),
            Ind("204012", "Sjuksköterska", "Kvinna", 40000m, 100m));

        var result = new SCBLonestatistikGenerator().Generera(input);

        Assert.Equal(2, result.Grupper.Count); // en cell per kön
        var man = result.Grupper.Single(g => g.Kon == "M");
        Assert.Equal(2, man.Antal);
        Assert.Equal(42000m, man.MedelHeltidslon);
        Assert.Equal(42000m, man.Median);

        var kvinna = result.Grupper.Single(g => g.Kon == "K");
        Assert.Equal(39000m, kvinna.MedelHeltidslon);
    }

    [Fact]
    public void Raknar_upp_deltid_till_heltid_innan_aggregering()
    {
        // 20000 kr vid 50 % → heltidslön 40000 kr; 40000 kr vid 100 % → 40000 kr.
        var input = Bygg(
            Ind("101010", "Assistent", "Man", 20000m, 50m),
            Ind("101010", "Assistent", "Man", 40000m, 100m));

        var g = Assert.Single(new SCBLonestatistikGenerator().Generera(input).Grupper);
        Assert.Equal(40000m, g.MedelHeltidslon);
        Assert.Equal(75m, g.MedelSysselsattningsgrad); // (50 + 100) / 2
    }

    [Fact]
    public void Kvinnors_loneandel_i_procent_av_mans()
    {
        var input = Bygg(
            Ind("204012", "Sjuksköterska", "Man", 40000m, 100m),
            Ind("204012", "Sjuksköterska", "Man", 44000m, 100m),
            Ind("204012", "Sjuksköterska", "Kvinna", 38000m, 100m),
            Ind("204012", "Sjuksköterska", "Kvinna", 40000m, 100m));

        var result = new SCBLonestatistikGenerator().Generera(input);

        // 39000 / 42000 = 92.857 % → 92.9 %
        Assert.Equal(92.9m, result.KvinnorLoneandelProcent);
    }

    [Fact]
    public void Loneandel_ar_null_nar_ett_kon_saknas()
    {
        var input = Bygg(Ind("204012", "Sjuksköterska", "Man", 40000m, 100m));
        Assert.Null(new SCBLonestatistikGenerator().Generera(input).KvinnorLoneandelProcent);
    }

    [Fact]
    public void Filen_har_metadata_status_och_semikolonseparerad_header()
    {
        var input = Bygg(Ind("204012", "Sjuksköterska", "Kvinna", 38000m, 100m));
        var result = new SCBLonestatistikGenerator().Generera(input);

        Assert.Contains("#TYP=SCB-LONESTRUKTURSTATISTIK-REGION", result.Innehall);
        Assert.Contains("#STATUS=EJ_INLAMNAD_KRAVER_SCB_INLOGGNING", result.Innehall);
        Assert.Contains("#MATTIDPUNKT=2025-11-01", result.Innehall);
        Assert.Contains("Yrkeskod;Yrkesbenamning;Kon;Antal;MedelHeltidslon", result.Innehall);
        Assert.EndsWith(".csv", result.Filnamn);
    }

    [Fact]
    public void Anvander_invariant_decimaltecken_och_crlf()
    {
        // 40000 kr vid 80 % → heltidslön exakt 50000 kr.
        var input = Bygg(Ind("204012", "Sjuksköterska", "Kvinna", 40000m, 80m));
        var result = new SCBLonestatistikGenerator().Generera(input);

        Assert.Contains("50000", result.Innehall);    // heltidsuppräknad lön
        Assert.Contains("80.0", result.Innehall);      // punkt, inte komma
        Assert.DoesNotContain("80,0", result.Innehall);
        Assert.Contains("\r\n", result.Innehall);
    }

    [Fact]
    public void Bytes_ar_latin1_kodade()
    {
        var input = Bygg(Ind("204012", "Sjuksköterska", "Kvinna", 38000m, 100m));
        var result = new SCBLonestatistikGenerator().Generera(input);

        // Latin-1: 'ö' = 0xF6 (ett byte), inte UTF-8:s 0xC3 0xB6.
        Assert.Contains((byte)0xF6, result.Bytes);
        Assert.Equal(result.Innehall, Encoding.Latin1.GetString(result.Bytes));
    }

    [Fact]
    public void Kon_normaliseras_fran_personnummer()
    {
        // Näst sista siffran jämn = kvinna, udda = man (Personnummer-value objectet).
        var kvinna = Personnummer.CreateValidated("199001011224");
        var man = Personnummer.CreateValidated("199001011234");

        var input = Bygg(
            Ind("204012", "Sjuksköterska", kvinna.LegalGender, 38000m, 100m),
            Ind("204012", "Sjuksköterska", man.LegalGender, 40000m, 100m));

        var result = new SCBLonestatistikGenerator().Generera(input);
        Assert.Contains(result.Grupper, g => g.Kon == "K");
        Assert.Contains(result.Grupper, g => g.Kon == "M");
    }

    [Fact]
    public void Tom_indata_ger_inga_grupper_men_giltig_fil()
    {
        var result = new SCBLonestatistikGenerator().Generera(Bygg());
        Assert.Empty(result.Grupper);
        Assert.Equal(0, result.AntalIndivider);
        Assert.Contains("#ANTAL_GRUPPER=0", result.Innehall);
    }

    [Theory]
    [InlineData(50, 25)]   // median av [10,20,30,40] = 25
    [InlineData(0, 10)]    // P0 = minsta
    [InlineData(100, 40)]  // P100 = största
    public void Percentil_interpolerar_linjart(int p, int forvantat)
    {
        var sorterade = new decimal[] { 10, 20, 30, 40 };
        Assert.Equal(forvantat, SCBLonestatistikGenerator.Percentil(sorterade, p));
    }

    [Fact]
    public void Percentil_enstaka_och_tomt()
    {
        Assert.Equal(100m, SCBLonestatistikGenerator.Percentil(new decimal[] { 100 }, 50));
        Assert.Equal(0m, SCBLonestatistikGenerator.Percentil(System.Array.Empty<decimal>(), 50));
    }
}
