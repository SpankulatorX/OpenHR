using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Domäntester för ersättning till förtroendevalda/fritidspolitiker: arvode, förlorad
/// arbetsinkomst, skattefri/skattepliktig reseersättning enligt Skatteverkets schablon,
/// skatteberäkning och utbetalningsunderlaget.
/// </summary>
public sealed class FortroendevaldErsattningTests
{
    private static readonly DateOnly Datum = new(2026, 3, 12);
    private static readonly string Pnr = (string)Personnummer.CreateValidated("196001011234");

    private static FortroendevaldErsattning Standard(decimal kmErsattning = 2.50m) =>
        FortroendevaldErsattning.Skapa(
            "Karin Politiker", Pnr, "Ledamot regionfullmäktige", Datum,
            sammantradesarvode: Money.SEK(500m),
            forloradArbetsinkomst: Money.SEK(1000m),
            antalKm: 100m,
            kmErsattning: kmErsattning,
            organ: "Regionfullmäktige");

    [Fact]
    public void Reseersattning_AtSchablon_IsFullyTaxFree()
    {
        var e = Standard();
        Assert.Equal(250m, e.BeraknaReseersattning().Amount);
        Assert.Equal(250m, e.BeraknaSkattefriResa().Amount);
        Assert.Equal(0m, e.BeraknaSkattepliktigResa().Amount);
    }

    [Fact]
    public void Reseersattning_AboveSchablon_TaxesExcessOnly()
    {
        var e = Standard(kmErsattning: 4.00m);
        Assert.Equal(400m, e.BeraknaReseersattning().Amount);
        Assert.Equal(250m, e.BeraknaSkattefriResa().Amount);       // kapas vid 2,50 kr/km
        Assert.Equal(150m, e.BeraknaSkattepliktigResa().Amount);   // 400 - 250
    }

    [Fact]
    public void Skattepliktigt_IsArvodePlusForloradPlusTaxableTravel()
    {
        var e = Standard();
        // 500 + 1000 + 0 = 1500
        Assert.Equal(1500m, e.BeraknaSkattepliktigt().Amount);
    }

    [Fact]
    public void Skatt_IsThirtyPercentOfSkattepliktigt()
    {
        var e = Standard();
        Assert.Equal(450m, e.BeraknaSkatt().Amount);   // 30 % av 1500
    }

    [Fact]
    public void BruttoNetto_Computed()
    {
        var e = Standard();
        // Brutto = 500 + 1000 + 250 = 1750; netto = 1750 - 450 = 1300
        Assert.Equal(1750m, e.BeraknaBrutto().Amount);
        Assert.Equal(1300m, e.BeraknaNetto().Amount);
    }

    [Fact]
    public void BruttoNetto_WithTaxableTravel()
    {
        var e = Standard(kmErsattning: 4.00m);
        // Skattepliktigt = 1650, skatt = 495, brutto = 1900, netto = 1405
        Assert.Equal(1650m, e.BeraknaSkattepliktigt().Amount);
        Assert.Equal(495m, e.BeraknaSkatt().Amount);
        Assert.Equal(1900m, e.BeraknaBrutto().Amount);
        Assert.Equal(1405m, e.BeraknaNetto().Amount);
    }

    [Fact]
    public void Skapa_EmptyName_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            FortroendevaldErsattning.Skapa("  ", Pnr, "Ledamot", Datum));

    [Fact]
    public void Skapa_EmptyPersonnummer_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            FortroendevaldErsattning.Skapa("Karin", " ", "Ledamot", Datum));

    [Fact]
    public void Skapa_EmptyUppdrag_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            FortroendevaldErsattning.Skapa("Karin", Pnr, "  ", Datum));

    [Fact]
    public void Skapa_NegativeKm_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            FortroendevaldErsattning.Skapa("Karin", Pnr, "Ledamot", Datum, antalKm: -5m));

    [Fact]
    public void Skapa_InvalidTaxRate_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            FortroendevaldErsattning.Skapa("Karin", Pnr, "Ledamot", Datum, skattesats: 2m));

    [Fact]
    public void Skapa_NoTravel_ZeroReseersattning()
    {
        var e = FortroendevaldErsattning.Skapa("Karin", Pnr, "Ledamot", Datum,
            sammantradesarvode: Money.SEK(400m));
        Assert.Equal(0m, e.BeraknaReseersattning().Amount);
        Assert.Equal(400m, e.BeraknaSkattepliktigt().Amount);
    }

    [Fact]
    public void StatusFlow_RegisteredToApprovedToPaid()
    {
        var e = Standard();
        Assert.Equal(FortroendevaldStatus.Registrerad, e.Status);
        e.Godkann();
        Assert.Equal(FortroendevaldStatus.Godkand, e.Status);
        e.MarkeraUtbetald();
        Assert.Equal(FortroendevaldStatus.Utbetald, e.Status);
    }

    [Fact]
    public void MarkeraUtbetald_BeforeApproval_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Standard().MarkeraUtbetald());

    [Fact]
    public void UnderlagGenerator_TotalsMatchSumOfPosts()
    {
        var poster = new List<FortroendevaldErsattning> { Standard(), Standard() };
        var input = new FortroendevaldUnderlagInput(
            "232100000164", "Region Örebro län", "2026-03", new DateOnly(2026, 3, 25), poster);

        var content = new FortroendevaldUnderlagGenerator().ByggInnehall(input);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header + kolumnrubrik + 2 poster + summeringsrad = 5
        Assert.Equal(5, lines.Length);
        // Brutto 1750*2=3500, skatt 450*2=900, netto 1300*2=2600
        Assert.Equal("#S;2;3500.00;900.00;2600.00", lines[^1]);
    }

    [Fact]
    public void UnderlagGenerator_ProducesCsvFileName()
    {
        var input = new FortroendevaldUnderlagInput(
            "232100000164", "Region Örebro län", "2026-03", new DateOnly(2026, 3, 25),
            new List<FortroendevaldErsattning> { Standard() });

        var fil = new FortroendevaldUnderlagGenerator().Generera(input);
        Assert.Equal("FORTROENDEVALDA_232100000164_202603.csv", fil.FileName);
        Assert.Equal("text/csv", fil.MimeType);
    }
}
