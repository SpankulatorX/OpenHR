using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

public class FlexCalculatorTests
{
    private readonly EmployeeId _emp = EmployeeId.New();

    private FlexInstallning Installning(
        bool aktiverad = true,
        decimal maxPlus = 100m,
        decimal maxMinus = -100m,
        decimal dagligGrans = 0m)
        => new()
        {
            AnstallId = _emp,
            FlexAktiverad = aktiverad,
            MaxPlusTimmar = maxPlus,
            MaxMinusTimmar = maxMinus,
            DagligFlexgransTimmar = dagligGrans
        };

    private static FlexDagsunderlag Dag(int dag, decimal planerat, decimal? faktiskt)
        => new(new DateOnly(2026, 3, dag), planerat, faktiskt);

    [Fact]
    public void Berakna_ArbetatMer_GerPositivtFlex()
    {
        var dagar = new[] { Dag(1, 8m, 9m) }; // +1h

        var res = FlexCalculator.Berakna(0m, dagar, Installning());

        Assert.Equal(1.0m, res.RaFlexforandring);
        Assert.Equal(1.0m, res.UtgaendeSaldo);
        Assert.Single(res.Dagsposter);
        Assert.Equal(1.0m, res.Dagsposter[0].FlexDelta);
    }

    [Fact]
    public void Berakna_ArbetatMindre_GerNegativtFlex()
    {
        var dagar = new[] { Dag(1, 8m, 6.5m) }; // -1.5h

        var res = FlexCalculator.Berakna(0m, dagar, Installning());

        Assert.Equal(-1.5m, res.UtgaendeSaldo);
    }

    [Fact]
    public void Berakna_FleraDagar_Summeras()
    {
        var dagar = new[]
        {
            Dag(1, 8m, 9m),    // +1
            Dag(2, 8m, 8m),    //  0
            Dag(3, 8m, 6m),    // -2
            Dag(4, 8m, 8.5m),  // +0.5
        };

        var res = FlexCalculator.Berakna(0m, dagar, Installning());

        Assert.Equal(-0.5m, res.UtgaendeSaldo);
        Assert.Equal(4, res.Dagsposter.Count);
    }

    [Fact]
    public void Berakna_IngaendeSaldo_RaknasMed()
    {
        var dagar = new[] { Dag(1, 8m, 10m) }; // +2

        var res = FlexCalculator.Berakna(5m, dagar, Installning());

        Assert.Equal(5m, res.IngaendeSaldo);
        Assert.Equal(2m, res.RaFlexforandring);
        Assert.Equal(7m, res.UtgaendeSaldo);
    }

    [Fact]
    public void Berakna_DagUtanFaktiskTid_Hoppas()
    {
        var dagar = new[]
        {
            Dag(1, 8m, 9m),    // +1
            Dag(2, 8m, null),  // ej stämplad — ska ej påverka
        };

        var res = FlexCalculator.Berakna(0m, dagar, Installning());

        Assert.Equal(1m, res.UtgaendeSaldo);
        Assert.Single(res.Dagsposter);
    }

    [Fact]
    public void Berakna_DagligGrans_KaparStorPositivDag()
    {
        var dagar = new[] { Dag(1, 8m, 14m) }; // +6, men daglig gräns 3
        var inst = Installning(dagligGrans: 3m);

        var res = FlexCalculator.Berakna(0m, dagar, inst);

        Assert.Equal(3m, res.UtgaendeSaldo);
        Assert.True(res.Dagsposter[0].Kapad);
    }

    [Fact]
    public void Berakna_DagligGrans_KaparStorNegativDag()
    {
        var dagar = new[] { Dag(1, 8m, 2m) }; // -6, daglig gräns 3
        var inst = Installning(dagligGrans: 3m);

        var res = FlexCalculator.Berakna(0m, dagar, inst);

        Assert.Equal(-3m, res.UtgaendeSaldo);
        Assert.True(res.Dagsposter[0].Kapad);
    }

    [Fact]
    public void Berakna_OverTak_KlampasOchFlaggas()
    {
        var dagar = new[] { Dag(1, 8m, 16m) }; // +8
        var inst = Installning(maxPlus: 5m);

        var res = FlexCalculator.Berakna(0m, dagar, inst);

        Assert.Equal(5m, res.UtgaendeSaldo);
        Assert.True(res.NaddeOvreGrans);
        Assert.False(res.NaddeUndreGrans);
    }

    [Fact]
    public void Berakna_UnderGolv_KlampasOchFlaggas()
    {
        var dagar = new[] { Dag(1, 8m, 0m) }; // -8
        var inst = Installning(maxMinus: -5m);

        var res = FlexCalculator.Berakna(0m, dagar, inst);

        Assert.Equal(-5m, res.UtgaendeSaldo);
        Assert.True(res.NaddeUndreGrans);
        Assert.False(res.NaddeOvreGrans);
    }

    [Fact]
    public void Berakna_MaxPlusNoll_BetyderObegransatTak()
    {
        var dagar = new[] { Dag(1, 8m, 108m) }; // +100
        var inst = Installning(maxPlus: 0m, maxMinus: -100m);

        var res = FlexCalculator.Berakna(0m, dagar, inst);

        Assert.Equal(100m, res.UtgaendeSaldo);
        Assert.False(res.NaddeOvreGrans);
    }

    [Fact]
    public void Berakna_FlexInaktiverad_PaverkarInteSaldot()
    {
        var dagar = new[] { Dag(1, 8m, 12m) }; // +4 skulle annars läggas till
        var inst = Installning(aktiverad: false);

        var res = FlexCalculator.Berakna(3m, dagar, inst);

        Assert.Equal(0m, res.RaFlexforandring);
        Assert.Equal(3m, res.UtgaendeSaldo);
        Assert.Empty(res.Dagsposter);
    }

    [Fact]
    public void Berakna_TomtUnderlag_GerIngaendeSaldo()
    {
        var res = FlexCalculator.Berakna(4m, Array.Empty<FlexDagsunderlag>(), Installning());

        Assert.Equal(4m, res.UtgaendeSaldo);
        Assert.Equal(0m, res.RaFlexforandring);
        Assert.Empty(res.Dagsposter);
    }

    [Fact]
    public void Berakna_Nattpass_HanterasSomVanligDelta()
    {
        // Nattpass 21-07 = 10h - 0.75 rast = 9.25 planerat; faktiskt 9.25 → 0 flex
        var dagar = new[] { Dag(1, 9.25m, 9.25m) };

        var res = FlexCalculator.Berakna(0m, dagar, Installning());

        Assert.Equal(0m, res.UtgaendeSaldo);
    }
}
