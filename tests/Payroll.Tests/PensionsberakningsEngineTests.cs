using Xunit;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Tester för avgiftsbestämd tjänstepension AKAP-KR (<see cref="PensionsberakningsEngine"/>).
/// Värden verifierade mot Pensionsmyndigheten/SKR för inkomståret 2026:
///  - IBB 2026 = 83 400 kr.
///  - 7,5 IBB = 625 500 kr/år (52 125 kr/mån).
///  - 30 IBB = 2 502 000 kr/år (208 500 kr/mån).
///  - Premie: 6,0 % upp till 7,5 IBB, 31,5 % däröver, 0 % över 30 IBB.
/// </summary>
public class PensionsberakningsEngineTests
{
    private readonly PensionsberakningsEngine _engine = new();

    // --- Inkomstbasbelopp per år ---

    [Theory]
    [InlineData(2024, 76_200)]
    [InlineData(2025, 80_600)]
    [InlineData(2026, 83_400)]
    [InlineData(2027, 83_400)]   // framtida år → senast kända
    [InlineData(2020, 76_200)]   // tidigare år → äldsta kända
    public void Inkomstbasbelopp_PerAr(int ar, decimal forvantat)
    {
        Assert.Equal(forvantat, PensionsberakningsEngine.Inkomstbasbelopp(ar));
    }

    // --- Konstanter enligt AKAP-KR ---

    [Fact]
    public void Premiesatser_OchGranser_ArKorrekta()
    {
        Assert.Equal(0.06m, PensionsberakningsEngine.PremiesatsUnderGrans);
        Assert.Equal(0.315m, PensionsberakningsEngine.PremiesatsOverGrans);
        Assert.Equal(7.5m, PensionsberakningsEngine.GransIBB);
        Assert.Equal(30m, PensionsberakningsEngine.TakIBB);
    }

    // --- Månadspremie under brytpunkten (endast 6 %) ---

    [Fact]
    public void Manad_UnderGrans_Endast6Procent()
    {
        var p = _engine.BeraknaManadspremie(Money.SEK(30_000m), 2026);

        Assert.Equal(1_800m, p.PremieUnderGrans.Amount);   // 30 000 * 6 %
        Assert.Equal(0m, p.PremieOverGrans.Amount);
        Assert.Equal(1_800m, p.TotalPremie.Amount);
        Assert.False(p.OverstigerTak);
    }

    [Fact]
    public void Manad_ExaktPaBrytpunkten_Endast6Procent()
    {
        // 52 125 kr = 7,5 IBB / 12 för 2026 → allt beskattas med 6 %
        var p = _engine.BeraknaManadspremie(Money.SEK(52_125m), 2026);

        Assert.Equal(3_127.5m, p.PremieUnderGrans.Amount);
        Assert.Equal(0m, p.PremieOverGrans.Amount);
        Assert.Equal(3_127.5m, p.TotalPremie.Amount);
    }

    // --- Månadspremie över brytpunkten (6 % + 31,5 %) ---

    [Fact]
    public void Manad_OverGrans_DeladPremie()
    {
        // 62 125 kr/mån 2026: 52 125 * 6 % + 10 000 * 31,5 %
        var p = _engine.BeraknaManadspremie(Money.SEK(62_125m), 2026);

        Assert.Equal(3_127.5m, p.PremieUnderGrans.Amount);   // 52 125 * 6 %
        Assert.Equal(3_150m, p.PremieOverGrans.Amount);      // 10 000 * 31,5 %
        Assert.Equal(6_277.5m, p.TotalPremie.Amount);
        Assert.False(p.OverstigerTak);
    }

    // --- Årspremie ---

    [Fact]
    public void Ars_OverGrans_DeladPremie()
    {
        // 745 500 kr/år 2026: 625 500 * 6 % + 120 000 * 31,5 %
        var p = _engine.BeraknaArspremie(Money.SEK(745_500m), 2026);

        Assert.Equal(37_530m, p.PremieUnderGrans.Amount);    // 625 500 * 6 %
        Assert.Equal(37_800m, p.PremieOverGrans.Amount);     // 120 000 * 31,5 %
        Assert.Equal(75_330m, p.TotalPremie.Amount);
        Assert.Equal(625_500m, p.GransBelopp.Amount);
        Assert.Equal(2_502_000m, p.TakBelopp.Amount);
    }

    // --- Taket vid 30 IBB ---

    [Fact]
    public void Ars_OverTak_PremieBaraUppTill30IBB()
    {
        // 3 000 000 kr/år 2026: premiegrundande kapas vid 2 502 000 (30 IBB)
        var p = _engine.BeraknaArspremie(Money.SEK(3_000_000m), 2026);

        Assert.True(p.OverstigerTak);
        Assert.Equal(2_502_000m, p.PremiegrundandeBelopp.Amount);
        Assert.Equal(37_530m, p.PremieUnderGrans.Amount);      // 625 500 * 6 %
        Assert.Equal(591_097.5m, p.PremieOverGrans.Amount);    // (2 502 000 - 625 500) * 31,5 %
        Assert.Equal(628_627.5m, p.TotalPremie.Amount);
    }

    // --- Konsistens: 12 månadspremier == årspremie för jämn lön ---

    [Theory]
    [InlineData(30_000)]   // helt under brytpunkten
    [InlineData(62_125)]   // över brytpunkten
    public void Manad_Gånger12_LikaMed_Ars_ForJamnLon(decimal manadslon)
    {
        var manad = _engine.BeraknaManadspremie(Money.SEK(manadslon), 2026);
        var ars = _engine.BeraknaArspremie(Money.SEK(manadslon * 12m), 2026);

        Assert.Equal(ars.TotalPremie.Amount, manad.TotalPremie.Amount * 12m);
    }

    // --- Randfall: noll och negativt underlag ---

    [Fact]
    public void NollUnderlag_GerNollPremie()
    {
        var p = _engine.BeraknaManadspremie(Money.Zero, 2026);
        Assert.Equal(0m, p.TotalPremie.Amount);
    }

    [Fact]
    public void NegativtUnderlag_KapasTillNoll()
    {
        var p = _engine.BeraknaManadspremie(Money.SEK(-5_000m), 2026);
        Assert.Equal(0m, p.PremiegrundandeBelopp.Amount);
        Assert.Equal(0m, p.TotalPremie.Amount);
        Assert.False(p.OverstigerTak);
    }

    // --- Invariant: total == under + över, valuta bevaras ---

    [Theory]
    [InlineData(0)]
    [InlineData(25_000)]
    [InlineData(52_125)]
    [InlineData(90_000)]
    [InlineData(250_000)]
    public void Total_ArAlltid_SummanAv_UnderOchOver(decimal manadslon)
    {
        var p = _engine.BeraknaManadspremie(Money.SEK(manadslon), 2026);
        Assert.Equal(p.PremieUnderGrans.Amount + p.PremieOverGrans.Amount, p.TotalPremie.Amount);
        Assert.Equal("SEK", p.TotalPremie.Currency);
    }
}
