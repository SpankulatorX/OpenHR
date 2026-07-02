using RegionHR.IntegrationHub.Adapters.SKR;
using Xunit;

namespace RegionHR.IntegrationHub.Tests;

public class SKRNovemberstatistikGeneratorTests
{
    private static SKRNovemberstatistikInput Bygg(int ar, params SKRNovemberIndivid[] individer) => new()
    {
        Ar = ar,
        Organisationsnamn = "Region Örebro län",
        Organisationsnummer = "232100-0016",
        Individer = individer.ToList()
    };

    private static SKRNovemberIndivid Ind(
        string grupp, string kon, bool tillsvidare, decimal sysgrad, int alder, decimal? franvaro = null) =>
        new()
        {
            Personalgrupp = grupp,
            Kon = kon,
            ArTillsvidare = tillsvidare,
            Sysselsattningsgrad = sysgrad,
            Alder = alder,
            Franvaroprocent = franvaro
        };

    [Fact]
    public void Mattidpunkt_ar_forsta_november()
    {
        var input = Bygg(2025, Ind("Sjuksköterska", "Man", true, 100m, 40));
        var result = new SKRNovemberstatistikGenerator().Generera(input);

        Assert.Equal(new DateOnly(2025, 11, 1), result.Mattidpunkt);
        Assert.Contains("#MATTIDPUNKT=2025-11-01", result.Innehall);
        Assert.Contains("#TYP=SKR-NOVEMBERSTATISTIK", result.Innehall);
    }

    [Fact]
    public void Arsarbetare_ar_summan_av_sysselsattningsgrader()
    {
        var input = Bygg(2025,
            Ind("Sjuksköterska", "Man", true, 100m, 40),
            Ind("Sjuksköterska", "Kvinna", false, 50m, 30, 5m),
            Ind("Undersköterska", "Kvinna", true, 80m, 50, 10m));

        var result = new SKRNovemberstatistikGenerator().Generera(input);

        // 1.0 + 0.5 + 0.8 = 2.3 årsarbetare
        Assert.Equal(2.3m, result.Totalt.Arsarbetare);
        Assert.Equal(3, result.Totalt.AntalAnstallda);
        Assert.Equal(2, result.Totalt.AntalTillsvidare);
        Assert.Equal(1, result.Totalt.AntalVisstid);
        // (100 + 50 + 80) / 3 = 76.7
        Assert.Equal(76.7m, result.Totalt.MedelSysselsattningsgrad);
        // (40 + 30 + 50) / 3 = 40.0
        Assert.Equal(40.0m, result.Totalt.Medelalder);
        // medel av angivna frånvaroprocent [5, 10] = 7.5
        Assert.Equal(7.5m, result.Totalt.Franvaroprocent);
    }

    [Fact]
    public void Grupperar_per_personalgrupp_och_kon()
    {
        var input = Bygg(2025,
            Ind("Sjuksköterska", "Man", true, 100m, 40),
            Ind("Sjuksköterska", "Kvinna", false, 50m, 30));

        var result = new SKRNovemberstatistikGenerator().Generera(input);

        Assert.Equal(2, result.Grupper.Count);
        var man = result.Grupper.Single(g => g.Kon == "M");
        Assert.Equal(1, man.AntalTillsvidare);
        Assert.Equal(0, man.AntalVisstid);
        Assert.Equal(1.0m, man.Arsarbetare);
        Assert.Null(man.Franvaroprocent); // ingen frånvaro angiven

        var kvinna = result.Grupper.Single(g => g.Kon == "K");
        Assert.Equal(1, kvinna.AntalVisstid);
        Assert.Equal(0.5m, kvinna.Arsarbetare);
    }

    [Fact]
    public void Filen_har_status_och_totalrad()
    {
        var input = Bygg(2025, Ind("Sjuksköterska", "Man", true, 100m, 40));
        var result = new SKRNovemberstatistikGenerator().Generera(input);

        Assert.Contains("#STATUS=EJ_INLAMNAD_KRAVER_SKR_INLOGGNING", result.Innehall);
        Assert.Contains("Personalgrupp;Kon;AntalAnstallda;AntalTillsvidare;AntalVisstid;Arsarbetare", result.Innehall);
        Assert.Contains("TOTALT;A;", result.Innehall);
        Assert.EndsWith(".csv", result.Filnamn);
    }

    [Fact]
    public void Saknad_personalgrupp_blir_ospecificerad()
    {
        var input = Bygg(2025, Ind("", "Kvinna", true, 100m, 45));
        var g = Assert.Single(new SKRNovemberstatistikGenerator().Generera(input).Grupper);
        Assert.Equal("Ospecificerad", g.Personalgrupp);
    }
}
