using RegionHR.Infrastructure.Payroll;
using Xunit;

namespace RegionHR.Travel.Tests;

/// <summary>
/// Låser den utökade utlands-normalbeloppslistan för inkomstår 2026. Beloppen är avstämda mot
/// Skatteverkets normalbelopp 2026 via två oberoende sammanställningar (Björn Lundén samt
/// foretagande.se, hämtade 2026-07). Om ett värde ändras här ska källan uppdateras — inte
/// konstanten "tyst".
/// </summary>
public class UtlandsNormalbeloppTests
{
    private readonly TraktamentsCalculator _calc = new();

    // ---------- Urval av landsbelopp (dubbelkällverifierade) ----------

    [Theory]
    [InlineData("Norge", 1054)]
    [InlineData("Danmark", 1226)]
    [InlineData("USA", 1049)]
    [InlineData("Schweiz", 1332)]
    [InlineData("Belgien", 897)]
    [InlineData("Nederländerna", 745)]
    [InlineData("Luxemburg", 953)]
    [InlineData("Island", 1125)]
    [InlineData("Irland", 1038)]
    [InlineData("Österrike", 730)]
    [InlineData("Polen", 597)]
    [InlineData("Estland", 683)]
    [InlineData("Lettland", 763)]
    [InlineData("Litauen", 635)]
    [InlineData("Kanada", 895)]
    [InlineData("Australien", 727)]
    [InlineData("Singapore", 845)]
    [InlineData("Japan", 386)]
    [InlineData("Kina", 581)]
    [InlineData("Israel", 956)]
    [InlineData("Förenade Arabemiraten", 883)]
    public void Normalbelopp_2026_for_land_ar_som_faststallt(string land, int forvantat)
    {
        Assert.Equal((decimal)forvantat, TraktamentsCalculator.GetUtrikesNormalbelopp(land, 2026));
    }

    // ---------- Skiftläges- och whitespace-tolerans ----------

    [Theory]
    [InlineData("schweiz")]
    [InlineData("SCHWEIZ")]
    [InlineData("Schweiz")]
    [InlineData("  Schweiz  ")]
    public void Uppslag_ar_skiftlages_och_whitespace_okansligt(string input)
    {
        Assert.Equal(1332m, TraktamentsCalculator.GetUtrikesNormalbelopp(input, 2026));
    }

    // ---------- Default-fallback bevaras ----------

    [Fact]
    public void Okant_land_faller_fortfarande_tillbaka_till_default()
    {
        Assert.Equal(TraktamentsCalculator.UtrikesDefaultNormalbelopp,
            TraktamentsCalculator.GetUtrikesNormalbelopp("Atlantis", 2026));
        Assert.Equal(493m, TraktamentsCalculator.UtrikesDefaultNormalbelopp);
    }

    [Fact]
    public void Null_land_faller_tillbaka_till_default_utan_krasch()
    {
        Assert.Equal(TraktamentsCalculator.UtrikesDefaultNormalbelopp,
            TraktamentsCalculator.GetUtrikesNormalbelopp(null!, 2026));
    }

    // ---------- Listan för UI (UtrikesLander) ----------

    [Fact]
    public void UtrikesLander_ar_en_vasentlig_lista_och_sorterad()
    {
        var lander = TraktamentsCalculator.UtrikesLander(2026);

        // Listan har utökats väsentligt från den ursprungliga delmängden (9 länder).
        Assert.True(lander.Count >= 40, $"Förväntade minst 40 länder, fick {lander.Count}.");

        // Sorterad enligt svensk kultur.
        var sorterad = lander.OrderBy(x => x, StringComparer.Create(new System.Globalization.CultureInfo("sv-SE"), false)).ToList();
        Assert.Equal(sorterad, lander);

        // Inga dubbletter.
        Assert.Equal(lander.Count, lander.Distinct().Count());
    }

    [Fact]
    public void UtrikesLander_innehaller_kanda_lander()
    {
        var lander = TraktamentsCalculator.UtrikesLander(2026);
        foreach (var land in new[] { "Norge", "Tyskland", "USA", "Japan", "Schweiz", "Australien", "Kina" })
        {
            Assert.Contains(land, lander);
        }
    }

    [Fact]
    public void Varje_listat_land_har_ett_eget_belopp_over_noll()
    {
        foreach (var land in TraktamentsCalculator.UtrikesLander(2026))
        {
            var belopp = TraktamentsCalculator.GetUtrikesNormalbelopp(land, 2026);
            Assert.True(belopp > 0m, $"{land} saknar giltigt normalbelopp.");
        }
    }

    // ---------- Beräkning använder de nya beloppen ----------

    [Fact]
    public void BeraknaUtrikes_med_nytt_land_multiplicerar_normalbelopp_med_dagar()
    {
        // Schweiz 1332 kr/dag; 2026-03-02 08:00 -> 2026-03-03 18:00 = 34 h -> 2 dagar.
        var r = _calc.BeraknaUtrikes("Schweiz", new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 3, 18, 0, 0));
        Assert.Equal(2, r.AntalDagar);
        Assert.Equal(1332m * 2, r.Dagtraktamente);
        Assert.Equal(1332m * 2, r.Totalt);
    }

    [Fact]
    public void Framtida_ar_faller_tillbaka_till_2026_belopp()
    {
        Assert.Equal(1332m, TraktamentsCalculator.GetUtrikesNormalbelopp("Schweiz", 2030));
        Assert.Contains("Schweiz", TraktamentsCalculator.UtrikesLander(2030));
    }
}
