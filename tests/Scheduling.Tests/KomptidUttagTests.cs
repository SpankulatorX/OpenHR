using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

/// <summary>
/// Tester för arbetsflödet i <see cref="KomptidUttag"/>: skapande, validering av indata
/// och statusövergångarna Begärd → Godkänd/Avslagen/Återkallad.
/// </summary>
public class KomptidUttagTests
{
    private readonly EmployeeId _emp = EmployeeId.New();
    private readonly Guid _chef = Guid.NewGuid();
    private static readonly DateOnly _fran = new(2026, 5, 4);
    private static readonly DateOnly _till = new(2026, 5, 5);

    private KomptidUttag Ledigt(decimal timmar = 8m)
        => KomptidUttag.Skapa(_emp, timmar, KomputtagTyp.Ledighet, _fran, _till, "Kompledigt");

    private KomptidUttag Utbetalt(decimal timmar = 8m)
        => KomptidUttag.Skapa(_emp, timmar, KomputtagTyp.Utbetalning, null, null, null);

    [Fact]
    public void Skapa_Ledigt_GerBegardMedDatum()
    {
        var u = Ledigt(8m);

        Assert.Equal(KomputtagStatus.Begard, u.Status);
        Assert.Equal(KomputtagTyp.Ledighet, u.Typ);
        Assert.Equal(8m, u.Timmar);
        Assert.Equal(_fran, u.FranDatum);
        Assert.Equal(_till, u.TillDatum);
    }

    [Fact]
    public void Skapa_Utbetalning_NollstallerDatum()
    {
        var u = KomptidUttag.Skapa(_emp, 5m, KomputtagTyp.Utbetalning, _fran, _till, null);

        Assert.Null(u.FranDatum);
        Assert.Null(u.TillDatum);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Skapa_IckePositivaTimmar_Kastar(double timmar)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KomptidUttag.Skapa(_emp, (decimal)timmar, KomputtagTyp.Utbetalning, null, null, null));
    }

    [Fact]
    public void Skapa_LedigtUtanDatum_Kastar()
    {
        Assert.Throws<ArgumentException>(
            () => KomptidUttag.Skapa(_emp, 8m, KomputtagTyp.Ledighet, null, null, null));
    }

    [Fact]
    public void Skapa_LedigtMedSlutForeStart_Kastar()
    {
        Assert.Throws<ArgumentException>(
            () => KomptidUttag.Skapa(_emp, 8m, KomputtagTyp.Ledighet, _till, _fran, null));
    }

    [Fact]
    public void Godkann_FranBegard_GerGodkand()
    {
        var u = Ledigt();

        u.Godkann(_chef, "OK");

        Assert.Equal(KomputtagStatus.Godkand, u.Status);
        Assert.Equal(_chef, u.HandlagdAv);
        Assert.NotNull(u.HandlagdVid);
        Assert.Equal("OK", u.Kommentar);
    }

    [Fact]
    public void Godkann_TvaGanger_Kastar()
    {
        var u = Ledigt();
        u.Godkann(_chef, null);

        Assert.Throws<InvalidOperationException>(() => u.Godkann(_chef, null));
    }

    [Fact]
    public void Avsla_UtanSkal_Kastar()
    {
        var u = Utbetalt();

        Assert.Throws<ArgumentException>(() => u.Avsla(_chef, "  "));
        Assert.Equal(KomputtagStatus.Begard, u.Status);
    }

    [Fact]
    public void Avsla_MedSkal_GerAvslagen()
    {
        var u = Utbetalt();

        u.Avsla(_chef, "Bemanningsläget tillåter inte ledigt just nu.");

        Assert.Equal(KomputtagStatus.Avslagen, u.Status);
        Assert.Equal(_chef, u.HandlagdAv);
    }

    [Fact]
    public void Aterkalla_FranBegard_GerAterkallad()
    {
        var u = Ledigt();

        u.Aterkalla();

        Assert.Equal(KomputtagStatus.Aterkallad, u.Status);
    }

    [Fact]
    public void Godkann_EfterAterkallad_Kastar()
    {
        var u = Ledigt();
        u.Aterkalla();

        Assert.Throws<InvalidOperationException>(() => u.Godkann(_chef, null));
    }

    [Fact]
    public void KopplaLedighetspost_KraverGodkant()
    {
        var u = Ledigt();

        Assert.Throws<InvalidOperationException>(() => u.KopplaLedighetspost(Guid.NewGuid()));

        u.Godkann(_chef, null);
        var postId = Guid.NewGuid();
        u.KopplaLedighetspost(postId);
        Assert.Equal(postId, u.LedighetspostId);
    }
}
