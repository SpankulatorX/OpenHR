using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Domäntester för tolkersättning: arvodes-/skatte-/nettoberäkning, F-skattelogik,
/// statusövergångar och utbetalningsunderlaget.
/// </summary>
public sealed class TolkersattningTests
{
    private static readonly DateOnly Datum = new(2026, 3, 10);

    private static Tolkersattning Standard(bool fskatt = false) =>
        Tolkersattning.Skapa(
            "Amina Tolk", "Arabiska", TolkuppdragTyp.Kontakttolkning, Datum,
            antalTimmar: 2m, timarvode: Money.SEK(400m),
            forberedelsearvode: Money.SEK(100m), reseersattning: Money.SEK(50m),
            harFSkatt: fskatt);

    [Fact]
    public void BeraknaArvode_TimmarTimesRate_PlusPrep()
    {
        var t = Standard();
        // 2 * 400 + 100 = 900
        Assert.Equal(900m, t.BeraknaArvode().Amount);
    }

    [Fact]
    public void BeraknaSkatt_NoFSkatt_ThirtyPercentOfArvode()
    {
        var t = Standard();
        Assert.Equal(270m, t.BeraknaSkatt().Amount);   // 30 % av 900
    }

    [Fact]
    public void BeraknaBruttoNetto_WithoutFSkatt()
    {
        var t = Standard();
        // Brutto = arvode 900 + resa 50 = 950; netto = 950 - 270 = 680
        Assert.Equal(950m, t.BeraknaBrutto().Amount);
        Assert.Equal(680m, t.BeraknaNetto().Amount);
    }

    [Fact]
    public void BeraknaSkatt_WithFSkatt_IsZero()
    {
        var t = Standard(fskatt: true);
        Assert.Equal(0m, t.BeraknaSkatt().Amount);
        Assert.Equal(950m, t.BeraknaNetto().Amount);   // hela bruttot betalas ut
    }

    [Fact]
    public void ReseersattningIsTaxFree_NotPartOfArvode()
    {
        var t = Standard();
        // Reseersättningen ingår i brutto men beskattas aldrig (skatt baseras bara på arvodet).
        Assert.Equal(t.BeraknaArvode().Amount * 0.30m, t.BeraknaSkatt().Amount);
    }

    [Fact]
    public void Skapa_EmptyName_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Tolkersattning.Skapa("  ", "Arabiska", TolkuppdragTyp.Telefontolkning, Datum, 1m, Money.SEK(300m)));

    [Fact]
    public void Skapa_EmptyLanguage_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Tolkersattning.Skapa("Tolk", " ", TolkuppdragTyp.Telefontolkning, Datum, 1m, Money.SEK(300m)));

    [Fact]
    public void Skapa_NegativeHours_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Tolkersattning.Skapa("Tolk", "Arabiska", TolkuppdragTyp.Telefontolkning, Datum, -1m, Money.SEK(300m)));

    [Fact]
    public void Skapa_InvalidTaxRate_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Tolkersattning.Skapa("Tolk", "Arabiska", TolkuppdragTyp.Telefontolkning, Datum, 1m, Money.SEK(300m),
                skattesats: 1.5m));

    [Fact]
    public void StatusFlow_RegisteredToApprovedToPaid()
    {
        var t = Standard();
        Assert.Equal(TolkersattningStatus.Registrerad, t.Status);
        t.Godkann();
        Assert.Equal(TolkersattningStatus.Godkand, t.Status);
        t.MarkeraUtbetald();
        Assert.Equal(TolkersattningStatus.Utbetald, t.Status);
    }

    [Fact]
    public void MarkeraUtbetald_BeforeApproval_Throws()
    {
        var t = Standard();
        Assert.Throws<InvalidOperationException>(() => t.MarkeraUtbetald());
    }

    [Fact]
    public void Godkann_AfterPaid_Throws()
    {
        var t = Standard();
        t.Godkann();
        t.MarkeraUtbetald();
        Assert.Throws<InvalidOperationException>(() => t.Godkann());
    }

    [Fact]
    public void UnderlagGenerator_TotalsMatchSumOfPosts()
    {
        var poster = new List<Tolkersattning> { Standard(), Standard(fskatt: true) };
        var input = new TolkersattningUnderlagInput(
            "232100000164", "Region Örebro län", "2026-03", new DateOnly(2026, 3, 25), poster);

        var content = new TolkersattningUnderlagGenerator().ByggInnehall(input);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + kolumnrubrik + 2 poster + summeringsrad = 5
        Assert.Equal(5, lines.Length);
        // Brutto 950+950=1900, skatt 270+0=270, netto 680+950=1630
        Assert.Equal("#S;2;1900.00;270.00;1630.00", lines[^1]);
    }

    [Fact]
    public void UnderlagGenerator_ProducesCsvFileName()
    {
        var input = new TolkersattningUnderlagInput(
            "232100000164", "Region Örebro län", "2026-03", new DateOnly(2026, 3, 25),
            new List<Tolkersattning> { Standard() });

        var fil = new TolkersattningUnderlagGenerator().Generera(input);
        Assert.Equal("TOLKERSATTNING_232100000164_202603.csv", fil.FileName);
        Assert.Equal("text/csv", fil.MimeType);
    }
}
