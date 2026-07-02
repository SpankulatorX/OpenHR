using Xunit;
using RegionHR.HalsoSAM.Domain;
using RegionHR.HalsoSAM.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.HalsoSAM.Tests;

public class FKAnmalanBedomningTests
{
    private static readonly DateOnly Dag1 = new(2026, 1, 1);

    [Fact]
    public void For_Dag14_ArInteFKAnmalanAktuell()
    {
        // 14 dagar in (dag 1 = 1 jan, referens = 14 jan) → fortfarande sjuklöneperiod
        var b = FKAnmalanBedomning.For(Dag1, sistaSjukdag: null, idag: new DateOnly(2026, 1, 14));

        Assert.Equal(14, b.AntalKalenderdagar);
        Assert.False(b.ArFKAnmalanAktuell);
    }

    [Fact]
    public void For_Dag15_ArFKAnmalanAktuell()
    {
        var b = FKAnmalanBedomning.For(Dag1, sistaSjukdag: null, idag: new DateOnly(2026, 1, 15));

        Assert.Equal(15, b.AntalKalenderdagar);
        Assert.True(b.ArFKAnmalanAktuell);
    }

    [Fact]
    public void For_SjuklonePeriodOchFKDatum_ForankrasFranDag1()
    {
        var b = FKAnmalanBedomning.For(Dag1, sistaSjukdag: null, idag: new DateOnly(2026, 1, 20));

        Assert.Equal(Dag1, b.SjuklonePeriodFran);
        Assert.Equal(Dag1.AddDays(13), b.SjuklonePeriodTill);          // dag 14
        Assert.Equal(Dag1.AddDays(14), b.ForsakringskassanFranDatum);  // dag 15
        Assert.Equal(Dag1.AddDays(7), b.LakarintygKravsFranDatum);     // dag 8
    }

    [Fact]
    public void For_Dag8_KraverLakarintyg()
    {
        var foreDag8 = FKAnmalanBedomning.For(Dag1, null, idag: new DateOnly(2026, 1, 7));
        var pADag8 = FKAnmalanBedomning.For(Dag1, null, idag: new DateOnly(2026, 1, 8));

        Assert.False(foreDag8.LakarintygKravs);
        Assert.True(pADag8.LakarintygKravs);
    }

    [Fact]
    public void For_LakarintygKravsMenSaknas_FlaggasVarning()
    {
        var utan = FKAnmalanBedomning.For(Dag1, null, new DateOnly(2026, 1, 20), lakarintygFinns: false);
        var med = FKAnmalanBedomning.For(Dag1, null, new DateOnly(2026, 1, 20), lakarintygFinns: true);

        Assert.True(utan.LakarintygSaknasVarning);
        Assert.False(med.LakarintygSaknasVarning);
    }

    [Fact]
    public void For_AvslutatSjukfall_AnvanderSistaSjukdagSomReferens()
    {
        var b = FKAnmalanBedomning.For(Dag1, sistaSjukdag: new DateOnly(2026, 1, 10),
            idag: new DateOnly(2026, 2, 1));

        Assert.Equal(new DateOnly(2026, 1, 10), b.ReferensDatum);
        Assert.Equal(10, b.AntalKalenderdagar);
    }

    [Fact]
    public void ForRehabCase_MedDag1_BeraknarFranDag1()
    {
        var anstall = EmployeeId.New();
        var dag1 = new DateOnly(2026, 3, 1);
        var rehab = RehabCase.Skapa(anstall, RehabTrigger.FjortonSammanhangandeDagar, dag1);

        var b = FKAnmalanBedomning.ForRehabCase(rehab, idag: new DateOnly(2026, 3, 20));

        Assert.NotNull(b);
        Assert.Equal(dag1, b!.ForstaSjukdag);
        Assert.Equal(20, b.AntalKalenderdagar);
        Assert.True(b.ArFKAnmalanAktuell);
    }
}
