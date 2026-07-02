using Xunit;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Tester för svenska arbetsgivaravgifter (domänen <see cref="Arbetsgivaravgift"/>).
/// Alla värden verifierade mot Skatteverket 2026:
///  - Full avgift 31,42 %.
///  - Född 1937 eller tidigare: 0 %.
///  - Äldre (född 1958 eller tidigare för 2025/2026): endast ålderspensionsavgift 10,21 %.
///  - Temporär ungdomsnedsättning 2026-04-01 – 2027-09-30: 20,81 % på lön ≤ 25 000 kr/mån
///    för den som vid årets ingång fyllt 18 men inte 23 år (född 2003–2007 för 2026).
/// </summary>
public class ArbetsgivaravgiftTests
{
    // --- Fulla satser ---

    [Theory]
    [InlineData(2025, 0.3142)]
    [InlineData(2026, 0.3142)]
    public void FullSats_PerAr(int ar, decimal forvantad)
    {
        Assert.Equal(forvantad, Arbetsgivaravgift.FullSats(ar));
    }

    [Fact]
    public void StandardArbetstagare_FullAvgift()
    {
        // Född 1985 → full avgift oavsett år (utanför äldre/ungdom)
        Assert.Equal(0.3142m, Arbetsgivaravgift.Sats(1985, 2026, 6));
        Assert.Equal(0.3142m, Arbetsgivaravgift.Sats(1985, 2025, 6));
    }

    // --- Född 1937 eller tidigare: inga avgifter ---

    [Theory]
    [InlineData(1937, 2026)]
    [InlineData(1936, 2026)]
    [InlineData(1900, 2026)]
    [InlineData(1937, 2025)]
    public void Fodd1937EllerTidigare_IngenAvgift(int fodelseAr, int inkomstAr)
    {
        Assert.Equal(0m, Arbetsgivaravgift.Sats(fodelseAr, inkomstAr, 6));
        Assert.Equal(Money.Zero, Arbetsgivaravgift.Belopp(Money.SEK(30000m), fodelseAr, inkomstAr, 6));
    }

    // --- Äldre: endast ålderspensionsavgift 10,21 % ---

    [Theory]
    [InlineData(1938, 2026, 0.1021)]  // lägsta årskull som får reducerad äldreavgift (efter 1937)
    [InlineData(1958, 2026, 0.1021)]  // fyllt 67 vid ingången av 2026
    [InlineData(1958, 2025, 0.1021)]  // fyllt 66 vid ingången av 2025
    [InlineData(1959, 2026, 0.3142)]  // fyller 67 UNDER 2026 → ej reducerad, full avgift
    [InlineData(1959, 2025, 0.3142)]  // fyller 66 UNDER 2025 → ej reducerad
    public void Aldre_ReduceradEllerFull(int fodelseAr, int inkomstAr, decimal forvantad)
    {
        Assert.Equal(forvantad, Arbetsgivaravgift.Sats(fodelseAr, inkomstAr, 6));
    }

    [Fact]
    public void AldreFodelseArTom_1958_For2025Och2026()
    {
        Assert.Equal(1958, Arbetsgivaravgift.AldreFodelseArTom(2025));
        Assert.Equal(1958, Arbetsgivaravgift.AldreFodelseArTom(2026));
    }

    [Fact]
    public void Aldre_Belopp_EndastAlderspensionsavgift()
    {
        var belopp = Arbetsgivaravgift.Belopp(Money.SEK(40000m), 1950, 2026, 6);
        Assert.Equal(40000m * 0.1021m, belopp.Amount);
    }

    // --- Ungdomsnedsättning (temporär, 2026-04 – 2027-09) ---

    [Theory]
    [InlineData(2003, 2026, 6, 0.2081)]  // 23 vid årets ingång → övre gräns
    [InlineData(2007, 2026, 6, 0.2081)]  // 19 vid årets ingång → nedre gräns
    [InlineData(2005, 2026, 4, 0.2081)]  // april 2026 → fönstret öppnar
    [InlineData(2004, 2027, 9, 0.2081)]  // september 2027 → fönstret stänger
    public void Ungdom_InomFonster_Reducerad(int fodelseAr, int inkomstAr, int manad, decimal forvantad)
    {
        Assert.Equal(forvantad, Arbetsgivaravgift.Sats(fodelseAr, inkomstAr, manad));
    }

    [Theory]
    [InlineData(2005, 2026, 3)]   // mars 2026 → före fönstret → full avgift
    [InlineData(2005, 2027, 10)]  // oktober 2027 → efter fönstret
    [InlineData(2005, 2025, 6)]   // 2025 fanns ingen ungdomsnedsättning
    [InlineData(2008, 2026, 6)]   // 18 vid årets ingång → för ung (kräver fyllt 18, dvs ≥19)
    [InlineData(2002, 2026, 6)]   // 24 vid årets ingång → för gammal för ungdomsrabatten
    public void Ungdom_UtanforVillkor_FullAvgift(int fodelseAr, int inkomstAr, int manad)
    {
        Assert.Equal(0.3142m, Arbetsgivaravgift.Sats(fodelseAr, inkomstAr, manad));
    }

    [Fact]
    public void Ungdom_Belopp_ReduceradUppTillTak_SedanFull()
    {
        // Lön 35 000: 25 000 * 20,81 % + 10 000 * 31,42 %
        var belopp = Arbetsgivaravgift.Belopp(Money.SEK(35000m), 2005, 2026, 6);
        var forvantad = 25000m * 0.2081m + 10000m * 0.3142m;
        Assert.Equal(forvantad, belopp.Amount);
    }

    [Fact]
    public void Ungdom_Belopp_UnderTak_EndastReducerad()
    {
        var belopp = Arbetsgivaravgift.Belopp(Money.SEK(20000m), 2005, 2026, 6);
        Assert.Equal(20000m * 0.2081m, belopp.Amount);
    }

    [Fact]
    public void Ungdom_UtanforFonster_Belopp_FullAvgift()
    {
        // Samma person i mars 2026 → full avgift på hela lönen
        var belopp = Arbetsgivaravgift.Belopp(Money.SEK(35000m), 2005, 2026, 3);
        Assert.Equal(35000m * 0.3142m, belopp.Amount);
    }
}
