using Xunit;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Tester för skattetabell-uppslag samt de årsversionerade kommunala skattesatserna.
/// Skattevärdena nedan är riktiga rader ur Skatteverkets tabell 34, kolumn 1, 2026.
/// </summary>
public class TaxTableTests
{
    private static TaxTable Tabell34Kolumn1_2026()
    {
        var t = new TaxTable { Id = 1, Ar = 2026, Tabellnummer = 34, Kolumn = 1 };
        // Riktiga kolumn-1-värden (nedre bracketgräns → skatt), Skatteverket tabell 34, 2026.
        t.LaggTillRad(new TaxTableRow { InkomstFran = 0m, InkomstTill = 2000m, Skattebelopp = 0m });
        t.LaggTillRad(new TaxTableRow { InkomstFran = 20001m, InkomstTill = 22000m, Skattebelopp = 3541m });
        t.LaggTillRad(new TaxTableRow { InkomstFran = 30001m, InkomstTill = 32000m, Skattebelopp = 6105m });
        t.LaggTillRad(new TaxTableRow { InkomstFran = 35001m, InkomstTill = 36000m, Skattebelopp = 7399m });
        t.LaggTillRad(new TaxTableRow { InkomstFran = 50001m, InkomstTill = 52000m, Skattebelopp = 12120m });
        return t;
    }

    [Theory]
    [InlineData(21000, 3541)]   // faller i intervallet 20001–22000
    [InlineData(31000, 6105)]
    [InlineData(35500, 7399)]
    [InlineData(51000, 12120)]
    public void BeraknaManadenSkatt_ValjerRattIntervall(decimal manadslon, decimal forvantadSkatt)
    {
        var skatt = Tabell34Kolumn1_2026().BeraknaManadenSkatt(Money.SEK(manadslon));
        Assert.Equal(forvantadSkatt, skatt.Amount);
    }

    [Fact]
    public void BeraknaManadenSkatt_LagInkomst_NollSkatt()
    {
        var skatt = Tabell34Kolumn1_2026().BeraknaManadenSkatt(Money.SEK(1500m));
        Assert.Equal(0m, skatt.Amount);
    }

    [Fact]
    public void BeraknaManadenSkatt_TomTabell_NollSkatt()
    {
        var tom = new TaxTable { Id = 2, Ar = 2026, Tabellnummer = 34, Kolumn = 1 };
        Assert.Equal(0m, tom.BeraknaManadenSkatt(Money.SEK(35000m)).Amount);
    }

    // --- KommunSkattesatser (Region Örebro län, 2026) ---

    [Theory]
    [InlineData("Örebro", 0.3365)]
    [InlineData("Kumla", 0.3384)]
    [InlineData("Hallsberg", 0.3385)]
    [InlineData("Askersund", 0.3415)]
    [InlineData("Karlskoga", 0.3430)]
    [InlineData("Lindesberg", 0.3460)]
    public void KommunSkattesatser_ForKommun_2026(string kommun, decimal forvantad)
    {
        Assert.Equal(forvantad, KommunSkattesatser.ForKommun(kommun, 2026));
    }

    [Fact]
    public void KommunSkattesatser_OkandKommun_FallerTillbakaPaOrebro()
    {
        Assert.Equal(KommunSkattesatser.OrebroTotal2026, KommunSkattesatser.ForKommun("Stockholm", 2026));
        Assert.Equal(KommunSkattesatser.OrebroTotal2026, KommunSkattesatser.ForKommun(null, 2026));
    }

    [Fact]
    public void KommunSkattesatser_OkantAr_FallerTillbakaPaSenasteKanda()
    {
        // 2030 saknas i tabellen → använd senaste kända året (2026)
        Assert.Equal(KommunSkattesatser.OrebroTotal2026, KommunSkattesatser.ForKommun("Örebro", 2030));
    }

    [Theory]
    [InlineData("Örebro", 34)]      // 33,65 → 34
    [InlineData("Askersund", 34)]   // 34,15 → 34
    [InlineData("Lindesberg", 35)]  // 34,60 → 35
    public void KommunSkattesatser_Tabellnummer_AvrundasTillHelProcent(string kommun, int forvantat)
    {
        Assert.Equal(forvantat, KommunSkattesatser.Tabellnummer(kommun, 2026));
    }
}
