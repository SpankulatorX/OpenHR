using System;
using System.Linq;
using RegionHR.GDPR.Klassa;
using Xunit;

namespace RegionHR.GDPR.Tests;

public class KlassaTests
{
    // ── Konsekvensregler ──────────────────────────────────────────────

    [Fact]
    public void RekommenderatKrav_Halsodata_KraverHogstaKonfidentialitet()
    {
        // Hälsodata (GDPR art. 9) → hög konfidentialitet.
        var krav = KlassaRegler.RekommenderatKrav(InformationsKategori.Halsouppgift);
        Assert.Equal(KonsekvensNiva.Allvarlig, krav.MinKonfidentialitet);
    }

    [Fact]
    public void RekommenderatKrav_Loneuppgift_KraverHogRiktighet()
    {
        // Fel i lön ger direkt ekonomisk skada → riktighet högst.
        var krav = KlassaRegler.RekommenderatKrav(InformationsKategori.Loneuppgift);
        Assert.Equal(KonsekvensNiva.Allvarlig, krav.MinRiktighet);
    }

    [Theory]
    [InlineData(InformationsKategori.Halsouppgift, true)]
    [InlineData(InformationsKategori.FackligTillhorighet, true)]
    [InlineData(InformationsKategori.Grunddata, false)]
    [InlineData(InformationsKategori.Loneuppgift, false)]
    [InlineData(InformationsKategori.SkyddadIdentitet, false)]
    public void ArKansligPersonuppgift_FoljerArtikel9(InformationsKategori kategori, bool forvantat)
    {
        Assert.Equal(forvantat, KlassaRegler.ArKansligPersonuppgift(kategori));
    }

    [Fact]
    public void UppfyllerKrav_UnderMiniminiva_ReturnerarFalse()
    {
        // Hälsodata med Måttlig konfidentialitet understiger kravet (Allvarlig).
        var uppfyller = KlassaRegler.UppfyllerKrav(
            InformationsKategori.Halsouppgift,
            KonsekvensNiva.Mattlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig);
        Assert.False(uppfyller);
    }

    [Fact]
    public void UppfyllerKrav_PaEllerOverMiniminiva_ReturnerarTrue()
    {
        var uppfyller = KlassaRegler.UppfyllerKrav(
            InformationsKategori.Halsouppgift,
            KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig);
        Assert.True(uppfyller);
    }

    [Fact]
    public void HogstaNiva_ValjerStorsta()
    {
        Assert.Equal(
            KonsekvensNiva.Allvarlig,
            KlassaRegler.HogstaNiva(KonsekvensNiva.Mattlig, KonsekvensNiva.Allvarlig, KonsekvensNiva.Forsumbar));
    }

    // ── Entitet ───────────────────────────────────────────────────────

    [Fact]
    public void Skapa_TomtNamn_KastarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => InformationsklassPost.Skapa(
            "  ", "b", InformationsKategori.Grunddata, "sys",
            KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig,
            "", "", "", "", ""));
    }

    [Fact]
    public void Skapa_SatterVardenOchGranskningstidpunkt()
    {
        var innan = DateTime.UtcNow;
        var post = InformationsklassPost.Skapa(
            "Lönedata", "beskr", InformationsKategori.Loneuppgift, "Lön",
            KonsekvensNiva.Betydande, KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande,
            "k", "r", "t", "skydd", "lagrum", arFordefinierad: true);

        Assert.NotEqual(Guid.Empty, post.Id);
        Assert.Equal("Lönedata", post.Informationsmangd);
        Assert.True(post.ArFordefinierad);
        Assert.True(post.SenastGranskad >= innan);
        Assert.Null(post.GranskadAv);
    }

    [Fact]
    public void Klassningsprofil_FormaterasKRT()
    {
        var post = InformationsklassPost.Skapa(
            "X", "", InformationsKategori.Grunddata, "sys",
            KonsekvensNiva.Betydande, KonsekvensNiva.Allvarlig, KonsekvensNiva.Mattlig,
            "", "", "", "", "");
        Assert.Equal("K3 R4 T2", post.Klassningsprofil);
    }

    [Fact]
    public void HogstaKonsekvens_ArMaxAvKRT()
    {
        var post = InformationsklassPost.Skapa(
            "X", "", InformationsKategori.Grunddata, "sys",
            KonsekvensNiva.Forsumbar, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "", "", "", "", "");
        Assert.Equal(KonsekvensNiva.Betydande, post.HogstaKonsekvens);
    }

    [Fact]
    public void Uppdatera_AndrarFaltOchGranskning()
    {
        var post = InformationsklassPost.Skapa(
            "X", "gammal", InformationsKategori.Grunddata, "sys",
            KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig,
            "", "", "", "", "");

        var innanUppdatering = DateTime.UtcNow;
        post.Uppdatera("ny beskr", InformationsKategori.Halsouppgift, "HälsoSAM",
            KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "km", "rm", "tm", "skydd", "lag", "sign-1");

        Assert.Equal("ny beskr", post.Beskrivning);
        Assert.Equal(InformationsKategori.Halsouppgift, post.Kategori);
        Assert.Equal(KonsekvensNiva.Allvarlig, post.Konfidentialitet);
        Assert.Equal("sign-1", post.GranskadAv);
        Assert.True(post.SenastGranskad >= innanUppdatering);
        // Namnet ändras inte av Uppdatera.
        Assert.Equal("X", post.Informationsmangd);
    }

    [Fact]
    public void Post_ArKansligPersonuppgift_SpeglarRegel()
    {
        var halsa = InformationsklassPost.Skapa(
            "H", "", InformationsKategori.Halsouppgift, "sys",
            KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "", "", "", "", "");
        Assert.True(halsa.ArKansligPersonuppgift);

        var grund = InformationsklassPost.Skapa(
            "G", "", InformationsKategori.Grunddata, "sys",
            KonsekvensNiva.Mattlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "", "", "", "", "");
        Assert.False(grund.ArKansligPersonuppgift);
    }

    // ── Seed ──────────────────────────────────────────────────────────

    [Fact]
    public void Seed_ArIckeTomOchHarUnikaNamn()
    {
        var poster = KlassaSeed.Fordefinierade();
        Assert.NotEmpty(poster);
        var namn = poster.Select(p => p.Informationsmangd).ToList();
        Assert.Equal(namn.Count, namn.Distinct().Count());
    }

    [Fact]
    public void Seed_VarjePost_UppfyllerRekommenderatKrav()
    {
        // Skyddar mot inkonsekvent seed: alla standardklassningar ska ligga på/över sin miniminivå.
        foreach (var post in KlassaSeed.Fordefinierade())
        {
            Assert.True(post.UppfyllerRekommenderatKrav,
                $"Seed-posten '{post.Informationsmangd}' understiger rekommenderad KLASSA-nivå.");
        }
    }

    [Fact]
    public void Seed_InnehallerDeKansligaDatamangderna()
    {
        var poster = KlassaSeed.Fordefinierade();

        // Lön, personnummer, hälsa/rehab och facklig tillhörighet ska alltid finnas.
        Assert.Contains(poster, p => p.Kategori == InformationsKategori.Loneuppgift);
        Assert.Contains(poster, p => p.Informationsmangd.Contains("Personnummer", StringComparison.Ordinal));
        Assert.Contains(poster, p => p.Kategori == InformationsKategori.Halsouppgift);
        Assert.Contains(poster, p => p.Kategori == InformationsKategori.FackligTillhorighet);
    }

    [Fact]
    public void Seed_KansligaPersonuppgifter_HarHogstaKonfidentialitet()
    {
        // Alla art. 9-mängder (hälsa, facklig) ska ha konfidentialitet på högsta nivån.
        foreach (var post in KlassaSeed.Fordefinierade().Where(p => p.ArKansligPersonuppgift))
        {
            Assert.Equal(KonsekvensNiva.Allvarlig, post.Konfidentialitet);
        }
    }

    // ── Visningstext ──────────────────────────────────────────────────

    [Fact]
    public void KlassaText_Niva_InnehallerNivanummer()
    {
        Assert.Contains("4", KlassaText.Niva(KonsekvensNiva.Allvarlig));
        Assert.Contains("1", KlassaText.Niva(KonsekvensNiva.Forsumbar));
    }
}
