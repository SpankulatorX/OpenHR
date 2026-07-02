using RegionHR.Infrastructure.Payroll;
using Xunit;

namespace RegionHR.Travel.Tests;

/// <summary>
/// Låser Skatteverkets verifierade satser för inkomstår 2026 (SKV 354 utgåva 36, dec 2025).
/// Om ett värde ändras här ska källa (år/tabell) uppdateras, inte konstanten "tyst".
/// </summary>
public class TraktamentsCalculatorTests
{
    private readonly TraktamentsCalculator _calc = new();

    // ---------- Årsversionerade satser (2026 verifierad) ----------

    [Fact]
    public void Satser_2026_helt_halvt_natt_ar_300_150_150()
    {
        var s = TraktamentsCalculator.SatserForAr(2026);
        Assert.Equal(300m, s.HeltMaximibeloppInrikes);
        Assert.Equal(150m, s.HalvtMaximibeloppInrikes);
        Assert.Equal(150m, s.NattraktamenteInrikes);
    }

    [Fact]
    public void Satser_2023_helt_ar_260_gammalt_varde()
    {
        Assert.Equal(260m, TraktamentsCalculator.SatserForAr(2023).HeltMaximibeloppInrikes);
    }

    [Fact]
    public void Satser_2024_och_2025_helt_ar_290()
    {
        Assert.Equal(290m, TraktamentsCalculator.SatserForAr(2024).HeltMaximibeloppInrikes);
        Assert.Equal(290m, TraktamentsCalculator.SatserForAr(2025).HeltMaximibeloppInrikes);
    }

    [Fact]
    public void Satser_framtida_ar_faller_tillbaka_till_senast_kanda()
    {
        var framtid = TraktamentsCalculator.SatserForAr(2030);
        Assert.Equal(TraktamentsCalculator.SenastKandaAr, framtid.InkomstAr);
        Assert.Equal(300m, framtid.HeltMaximibeloppInrikes);
    }

    [Fact]
    public void Satser_ar_fore_tabellen_faller_tillbaka_till_aldsta()
    {
        Assert.Equal(2023, TraktamentsCalculator.SatserForAr(2019).InkomstAr);
    }

    // ---------- Inrikes traktamente ----------

    [Fact]
    public void Inrikes_heldag_2026_ger_300_kr()
    {
        var r = _calc.BeraknaInrikes(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 20, 0, 0), hotell: true);
        Assert.Equal(300m, r.Dagtraktamente);
        Assert.Equal(0m, r.Natttillagg);
        Assert.Equal(300m, r.Totalt);
        Assert.Equal(1, r.AntalDagar);
    }

    [Fact]
    public void Inrikes_halvdag_2026_ger_150_kr()
    {
        var r = _calc.BeraknaInrikes(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 14, 0, 0), hotell: true);
        Assert.Equal(150m, r.Dagtraktamente);
        Assert.Equal(150m, r.Totalt);
    }

    [Fact]
    public void Inrikes_under_4_timmar_ger_inget_traktamente()
    {
        var r = _calc.BeraknaInrikes(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 11, 0, 0), hotell: true);
        Assert.Equal(0m, r.Dagtraktamente);
        Assert.Equal(0m, r.Totalt);
    }

    [Fact]
    public void Inrikes_flera_dagar_utan_hotell_ger_nattraktamente_150_per_natt()
    {
        // 2026-03-02 08:00 -> 2026-03-04 16:00 = 56 h -> 3 heldagar, 2 nätter
        var r = _calc.BeraknaInrikes(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 4, 16, 0, 0), hotell: false);
        Assert.Equal(3, r.AntalDagar);
        Assert.Equal(900m, r.Dagtraktamente);   // 3 x 300
        Assert.Equal(300m, r.Natttillagg);      // 2 x 150
        Assert.Equal(1200m, r.Totalt);
    }

    [Fact]
    public void Inrikes_med_hotell_ger_inget_nattraktamente()
    {
        var r = _calc.BeraknaInrikes(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 4, 16, 0, 0), hotell: true);
        Assert.Equal(0m, r.Natttillagg);
    }

    [Fact]
    public void Inrikes_heldag_2023_anvander_gammal_sats_260()
    {
        var r = _calc.BeraknaInrikes(new DateTime(2023, 3, 2, 8, 0, 0), new DateTime(2023, 3, 2, 20, 0, 0), hotell: true);
        Assert.Equal(260m, r.Dagtraktamente);
    }

    // ---------- Måltidsavdrag inrikes (SKV 354: frukost 20 %, lunch/middag 35 %) ----------

    [Fact]
    public void Maltidsavdrag_inrikes_2026_per_maltid_ar_60_105_105()
    {
        Assert.Equal(60m, _calc.BeraknaMaltidsavdragInrikes(1, 0, 0, 2026));  // frukost 20 % av 300
        Assert.Equal(105m, _calc.BeraknaMaltidsavdragInrikes(0, 1, 0, 2026)); // lunch 35 % av 300
        Assert.Equal(105m, _calc.BeraknaMaltidsavdragInrikes(0, 0, 1, 2026)); // middag 35 % av 300
    }

    [Fact]
    public void Maltidsavdrag_inrikes_helt_fri_kost_ar_270_dvs_90_procent()
    {
        // Frukost + lunch + middag = 90 % av 300 -> 270 kr avdrag, 30 kr kvar för småutgifter.
        Assert.Equal(270m, _calc.BeraknaMaltidsavdragInrikes(1, 1, 1, 2026));
    }

    [Fact]
    public void Inrikes_heldag_med_helt_fri_kost_ger_30_kr_netto()
    {
        var r = _calc.BeraknaInrikes(
            new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 20, 0, 0),
            hotell: true, friaFrukostar: 1, friaLuncher: 1, friaMiddagar: 1);
        Assert.Equal(300m, r.Dagtraktamente);
        Assert.Equal(270m, r.Maltidsavdrag);
        Assert.Equal(30m, r.Totalt);
    }

    [Fact]
    public void Inrikes_utan_fria_maltider_har_noll_maltidsavdrag()
    {
        var r = _calc.BeraknaInrikes(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 20, 0, 0), hotell: true);
        Assert.Equal(0m, r.Maltidsavdrag);
    }

    // ---------- Måltidsavdrag utrikes (SKV 354: frukost 15 %, lunch/middag 35 %, allt 85 %) ----------

    [Fact]
    public void Maltidsavdrag_utrikes_procentandelar_15_35_35_av_normalbelopp()
    {
        // Testinput normalbelopp = 1000 (ej ett påstått landsvärde) för att låsa procentsatserna.
        Assert.Equal(150m, _calc.BeraknaMaltidsavdragUtrikes(1000m, 1, 0, 0, 2026)); // frukost 15 %
        Assert.Equal(350m, _calc.BeraknaMaltidsavdragUtrikes(1000m, 0, 1, 0, 2026)); // lunch 35 %
        Assert.Equal(350m, _calc.BeraknaMaltidsavdragUtrikes(1000m, 0, 0, 1, 2026)); // middag 35 %
        Assert.Equal(850m, _calc.BeraknaMaltidsavdragUtrikes(1000m, 1, 1, 1, 2026)); // helt fri kost 85 %
    }

    // ---------- Utrikes traktamente / normalbelopp ----------

    [Fact]
    public void Utrikes_okant_land_faller_tillbaka_till_default()
    {
        Assert.Equal(TraktamentsCalculator.UtrikesDefaultNormalbelopp,
            TraktamentsCalculator.GetUtrikesNormalbelopp("Narnia", 2026));
    }

    [Fact]
    public void Utrikes_berakning_multiplicerar_normalbelopp_med_antal_dagar()
    {
        var normalbelopp = TraktamentsCalculator.GetUtrikesNormalbelopp("Norge", 2026);
        var r = _calc.BeraknaUtrikes("Norge", new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 18, 0, 0));
        Assert.Equal(1, r.AntalDagar);
        Assert.Equal(normalbelopp, r.Dagtraktamente);
        Assert.Equal(normalbelopp, r.Totalt);
    }

    [Fact]
    public void Utrikes_med_fri_lunch_drar_35_procent_av_normalbelopp()
    {
        var normalbelopp = TraktamentsCalculator.GetUtrikesNormalbelopp("Tyskland", 2026);
        var forvantatAvdrag = Math.Round(normalbelopp * 0.35m, 0, MidpointRounding.AwayFromZero);
        var r = _calc.BeraknaUtrikes("Tyskland", new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 18, 0, 0), friaLuncher: 1);
        Assert.Equal(forvantatAvdrag, r.Maltidsavdrag);
        Assert.Equal(normalbelopp - forvantatAvdrag, r.Totalt);
    }

    // ---------- Bilersättning (milersättning) ----------

    [Fact]
    public void Milersattning_egen_bil_2026_ar_25_kr_per_mil()
    {
        Assert.Equal(25m, TraktamentsCalculator.SatserForAr(2026).MilersattningEgenBil);
        Assert.Equal(375m, _calc.BeraknaMilersattning(15m, 2026));                 // 15 mil x 25
        Assert.Equal(500m, _calc.BeraknaMilersattning(20m, 2026, BilTyp.EgenBil)); // 20 mil x 25
    }

    [Fact]
    public void Milersattning_formansbil_2026_ar_12_kr_och_el_9_50()
    {
        Assert.Equal(120m, _calc.BeraknaMilersattning(10m, 2026, BilTyp.Formansbil));    // 10 x 12
        Assert.Equal(95m, _calc.BeraknaMilersattning(10m, 2026, BilTyp.FormansbilEl));   // 10 x 9,50
    }

    [Fact]
    public void Milersattning_negativa_mil_ger_noll()
    {
        Assert.Equal(0m, _calc.BeraknaMilersattning(-5m, 2026));
    }
}
