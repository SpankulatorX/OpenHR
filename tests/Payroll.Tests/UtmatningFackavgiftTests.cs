using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Payroll.Tests;

/// <summary>
/// Domäntester för löneutmätning (Kronofogden) och fackavgift: validering samt att
/// förbehållsbeloppet aldrig underskrids och att avgifter beräknas korrekt.
/// </summary>
public sealed class UtmatningFackavgiftTests
{
    private static readonly EmployeeId Anstalld = EmployeeId.New();
    private static readonly DateOnly Start = new(2026, 1, 1);

    // === Löneutmätning: validering ===

    [Fact]
    public void SkapaFastBelopp_SetsAllFields()
    {
        var u = Loneutmatning.SkapaFastBelopp(
            Anstalld, "U-12345-26", Money.SEK(3000m), Money.SEK(12000m), Start,
            mottagare: "Kronofogdemyndigheten", registreradAv: "HR");

        Assert.Equal(UtmatningTyp.FastBelopp, u.Typ);
        Assert.Equal(3000m, u.Belopp.Amount);
        Assert.Equal(12000m, u.Forbehallsbelopp.Amount);
        Assert.Equal("U-12345-26", u.Malnummer);
        Assert.Equal("Kronofogdemyndigheten", u.Mottagare);
        Assert.Null(u.Slutdatum);
    }

    [Fact]
    public void SkapaFastBelopp_ZeroAmount_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.Zero, Money.SEK(10000m), Start));

    [Fact]
    public void SkapaFastBelopp_EmptyMalnummer_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Loneutmatning.SkapaFastBelopp(Anstalld, "  ", Money.SEK(1000m), Money.SEK(10000m), Start));

    [Fact]
    public void Skapa_NegativeForbehallsbelopp_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.SEK(1000m), Money.SEK(-1m), Start));

    [Fact]
    public void SkapaAndel_Zero_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Loneutmatning.SkapaAndel(Anstalld, "U-1", 0m, Money.SEK(10000m), Start));

    [Fact]
    public void SkapaAndel_Negative_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Loneutmatning.SkapaAndel(Anstalld, "U-1", -0.1m, Money.SEK(10000m), Start));

    [Fact]
    public void SkapaAndel_AboveOne_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Loneutmatning.SkapaAndel(Anstalld, "U-1", 1.5m, Money.SEK(10000m), Start));

    // === Löneutmätning: förbehållsbeloppslogik ===

    [Fact]
    public void BeraknaAvdrag_FixedWithinAvailable_ReturnsFullAmount()
    {
        var u = Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.SEK(3000m), Money.SEK(12000m), Start);
        // Netto 20000, förbehåll 12000 → 8000 tillgängligt → hela 3000 dras.
        Assert.Equal(3000m, u.BeraknaAvdrag(Money.SEK(20000m)).Amount);
    }

    [Fact]
    public void BeraknaAvdrag_FixedExceedsAvailable_CapsToForbehall()
    {
        var u = Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.SEK(9000m), Money.SEK(15000m), Start);
        // Netto 20000, förbehåll 15000 → endast 5000 finns över → kapas till 5000.
        Assert.Equal(5000m, u.BeraknaAvdrag(Money.SEK(20000m)).Amount);
    }

    [Fact]
    public void BeraknaAvdrag_NetBelowForbehall_ReturnsZero()
    {
        var u = Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.SEK(3000m), Money.SEK(18000m), Start);
        Assert.Equal(Money.Zero, u.BeraknaAvdrag(Money.SEK(17000m)));
    }

    [Fact]
    public void BeraknaAvdrag_Share_ReturnsNetTimesShare_CappedByForbehall()
    {
        var u = Loneutmatning.SkapaAndel(Anstalld, "U-1", 0.20m, Money.SEK(10000m), Start);
        // 20 % av 20000 = 4000, tillgängligt 10000 → 4000.
        Assert.Equal(4000m, u.BeraknaAvdrag(Money.SEK(20000m)).Amount);
    }

    [Fact]
    public void ArAktivUnder_RespectsPeriod()
    {
        var u = Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.SEK(1000m), Money.SEK(9000m),
            new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 31));

        Assert.True(u.ArAktivUnder(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)));
        Assert.False(u.ArAktivUnder(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        Assert.False(u.ArAktivUnder(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public void Avsluta_SetsSlutdatum_AndRejectsBeforeStart()
    {
        var u = Loneutmatning.SkapaFastBelopp(Anstalld, "U-1", Money.SEK(1000m), Money.SEK(9000m), Start);
        u.Avsluta(new DateOnly(2026, 6, 30), "Skulden betald");
        Assert.Equal(new DateOnly(2026, 6, 30), u.Slutdatum);
        Assert.Equal("Skulden betald", u.Avslutsorsak);

        Assert.Throws<ArgumentException>(() => u.Avsluta(new DateOnly(2025, 12, 1)));
    }

    // === Fackavgift ===

    [Fact]
    public void Fackavgift_FastBelopp_ReturnsAmountRegardlessOfSalary()
    {
        var f = Fackavgift.SkapaFastBelopp(Anstalld, "Kommunal", Money.SEK(320m), Start);
        Assert.Equal(320m, f.BeraknaAvgift(Money.SEK(35000m)).Amount);
        Assert.Equal(320m, f.BeraknaAvgift(Money.SEK(10000m)).Amount);
    }

    [Fact]
    public void Fackavgift_Procent_ReturnsSalaryTimesPercent()
    {
        var f = Fackavgift.SkapaProcent(Anstalld, "Kommunal", 0.01m, Start);
        Assert.Equal(350m, f.BeraknaAvgift(Money.SEK(35000m)).Amount);
    }

    [Fact]
    public void Fackavgift_EmptyForbund_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Fackavgift.SkapaFastBelopp(Anstalld, " ", Money.SEK(100m), Start));

    [Fact]
    public void Fackavgift_ProcentZero_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Fackavgift.SkapaProcent(Anstalld, "Vision", 0m, Start));

    [Fact]
    public void Fackavgift_ProcentAboveOne_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            Fackavgift.SkapaProcent(Anstalld, "Vision", 1.2m, Start));

    [Fact]
    public void Fackavgift_ArAktivUnder_RespectsPeriod()
    {
        var f = Fackavgift.SkapaFastBelopp(Anstalld, "Vision", Money.SEK(200m),
            new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        Assert.True(f.ArAktivUnder(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        Assert.False(f.ArAktivUnder(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
    }
}
