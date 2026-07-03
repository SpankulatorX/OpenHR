using Xunit;
using RegionHR.Agreements.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Agreements.Tests;

/// <summary>
/// Verifierar de årsversionerade O-tilläggssatserna mot AB § 21 mom. 1.
/// KÄLLA: SKR "Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01", § 21.
/// Satser fr.o.m. 2025-04-01: A=126,90 (natt 152,30), B=66,10 (natt 76,00), C=56,70, D=25,60.
/// Satser fr.o.m. 2026-04-01: A=130,70 (natt 156,90), B=68,10 (natt 101,80 per anm. 1: +30 %), C=58,40, D=26,40.
/// </summary>
public class ABOTillaggSatserTests
{
    private static readonly DateOnly Ar2025 = new(2025, 6, 1);   // gäller 2025-04-01-tabellen
    private static readonly DateOnly Ar2026 = new(2026, 6, 1);   // gäller 2026-04-01-tabellen

    [Fact]
    public void Grundsats_2025_MatcharAB21()
    {
        Assert.Equal(25.60m, ABOTillaggSatser.Grundsats(OBCategory.VardagKvall, Ar2025));
        Assert.Equal(56.70m, ABOTillaggSatser.Grundsats(OBCategory.VardagNatt, Ar2025));
        Assert.Equal(66.10m, ABOTillaggSatser.Grundsats(OBCategory.Helg, Ar2025));
        Assert.Equal(126.90m, ABOTillaggSatser.Grundsats(OBCategory.Storhelg, Ar2025));
    }

    [Fact]
    public void Grundsats_2026_MatcharAB21()
    {
        Assert.Equal(26.40m, ABOTillaggSatser.Grundsats(OBCategory.VardagKvall, Ar2026));
        Assert.Equal(58.40m, ABOTillaggSatser.Grundsats(OBCategory.VardagNatt, Ar2026));
        Assert.Equal(68.10m, ABOTillaggSatser.Grundsats(OBCategory.Helg, Ar2026));
        Assert.Equal(130.70m, ABOTillaggSatser.Grundsats(OBCategory.Storhelg, Ar2026));
    }

    [Fact]
    public void Nattsats_HöjsForStorhelgOchHelg()
    {
        // A och B höjs natt mot helg/helgdag kl. 22–06.
        Assert.Equal(152.30m, ABOTillaggSatser.Nattsats(OBCategory.Storhelg, Ar2025));
        Assert.Equal(76.00m, ABOTillaggSatser.Nattsats(OBCategory.Helg, Ar2025));
        Assert.Equal(156.90m, ABOTillaggSatser.Nattsats(OBCategory.Storhelg, Ar2026));
        // AB 25 § 21 anm. 1: B-satsen höjd med 30 % nattetid → 78,30 × 1,30 = 101,80.
        Assert.Equal(101.80m, ABOTillaggSatser.Nattsats(OBCategory.Helg, Ar2026));
    }

    [Fact]
    public void Nattsats_HöjsInteForVardagNattEllerKvall()
    {
        // C och D har ingen natthöjning — nattsatsen är samma som grundsatsen.
        Assert.Equal(56.70m, ABOTillaggSatser.Nattsats(OBCategory.VardagNatt, Ar2025));
        Assert.Equal(25.60m, ABOTillaggSatser.Nattsats(OBCategory.VardagKvall, Ar2025));
    }

    [Fact]
    public void Ingen_GerNoll()
    {
        Assert.Equal(0m, ABOTillaggSatser.Grundsats(OBCategory.Ingen, Ar2025));
        Assert.Equal(0m, ABOTillaggSatser.Nattsats(OBCategory.Ingen, Ar2025));
    }

    [Fact]
    public void ForDatum_ValjerRattAvtalsversion_VidArsgrans()
    {
        // Dagen före 2026-04-01 ska fortfarande ge 2025-satser.
        Assert.Equal(126.90m, ABOTillaggSatser.Grundsats(OBCategory.Storhelg, new DateOnly(2026, 3, 31)));
        // 2026-04-01 exakt ska ge 2026-satser.
        Assert.Equal(130.70m, ABOTillaggSatser.Grundsats(OBCategory.Storhelg, new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void ForDatum_ForeForstaVersion_AnvanderAldstaKandaTabell()
    {
        // Datum före 2025-04-01: golvet (äldsta kända tabellen = 2025-04-01).
        Assert.Equal(ABOTillaggSatser.ArligtGolvdatum, new DateOnly(2025, 4, 1));
        Assert.Equal(126.90m, ABOTillaggSatser.Grundsats(OBCategory.Storhelg, new DateOnly(2024, 1, 1)));
    }

    [Fact]
    public void SatsForTimme_AnvanderNattsats_ForHelgKl22Till06()
    {
        // Helg (B) kl 23:00 → natthöjd sats.
        Assert.Equal(76.00m, ABOTillaggSatser.SatsForTimme(OBCategory.Helg, Ar2025, new TimeOnly(23, 0)));
        Assert.Equal(76.00m, ABOTillaggSatser.SatsForTimme(OBCategory.Helg, Ar2025, new TimeOnly(2, 0)));
        // Helg (B) kl 14:00 → grundsats.
        Assert.Equal(66.10m, ABOTillaggSatser.SatsForTimme(OBCategory.Helg, Ar2025, new TimeOnly(14, 0)));
        // Storhelg (A) kl 02:00 → natthöjd sats.
        Assert.Equal(152.30m, ABOTillaggSatser.SatsForTimme(OBCategory.Storhelg, Ar2025, new TimeOnly(2, 0)));
    }

    [Fact]
    public void SatsForTimme_IngenNatthojning_ForVardagNatt()
    {
        // Vardagnatt (C) kl 23:00 → oförändrad grundsats (ingen höjning).
        Assert.Equal(56.70m, ABOTillaggSatser.SatsForTimme(OBCategory.VardagNatt, Ar2025, new TimeOnly(23, 0)));
    }

    [Fact]
    public void Storhelg_ArHogreAnHelg_HigreAnNatt_HogreAnKvall()
    {
        // Sanity: storleksordningen A > B > C > D enligt AB.
        var a = ABOTillaggSatser.Grundsats(OBCategory.Storhelg, Ar2025);
        var b = ABOTillaggSatser.Grundsats(OBCategory.Helg, Ar2025);
        var c = ABOTillaggSatser.Grundsats(OBCategory.VardagNatt, Ar2025);
        var d = ABOTillaggSatser.Grundsats(OBCategory.VardagKvall, Ar2025);
        Assert.True(a > b && b > c && c > d);
    }
}
