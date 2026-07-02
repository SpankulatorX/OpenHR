using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

/// <summary>
/// Tester för komptidshuvudboken på <see cref="FlexBalance"/>. Kärninvarianten:
/// ett uttag kan aldrig övertrassera tillgänglig (intjänad − uttagen) komptid.
/// </summary>
public class FlexBalanceKomptidTests
{
    private static FlexBalance Balans(decimal intjanat, decimal uttaget = 0m) => new()
    {
        AnstallId = EmployeeId.New(),
        KompsaldoTimmar = intjanat,
        UttagnaKompTimmar = uttaget
    };

    [Fact]
    public void Tillganglig_ArIntjanatMinusUttaget()
    {
        var b = Balans(intjanat: 12m, uttaget: 4.5m);

        Assert.Equal(7.5m, b.TillgangligKomptidTimmar);
    }

    [Fact]
    public void RegistreraKomputtag_DrarFranTillgangligt()
    {
        var b = Balans(intjanat: 10m);

        b.RegistreraKomputtag(4m);

        Assert.Equal(4m, b.UttagnaKompTimmar);
        Assert.Equal(6m, b.TillgangligKomptidTimmar);
    }

    [Fact]
    public void RegistreraKomputtag_FleraUttag_Ackumuleras()
    {
        var b = Balans(intjanat: 10m);

        b.RegistreraKomputtag(4m);
        b.RegistreraKomputtag(2.5m);

        Assert.Equal(6.5m, b.UttagnaKompTimmar);
        Assert.Equal(3.5m, b.TillgangligKomptidTimmar);
    }

    [Fact]
    public void RegistreraKomputtag_HelaSaldot_GerNoll()
    {
        var b = Balans(intjanat: 8m);

        b.RegistreraKomputtag(8m);

        Assert.Equal(0m, b.TillgangligKomptidTimmar);
    }

    [Fact]
    public void RegistreraKomputtag_OverTillgangligt_Kastar_OchLamnarSaldotOrort()
    {
        var b = Balans(intjanat: 5m, uttaget: 3m); // tillgängligt = 2

        var ex = Assert.Throws<InvalidOperationException>(() => b.RegistreraKomputtag(2.5m));

        Assert.Contains("Otillräcklig komptid", ex.Message);
        Assert.Equal(3m, b.UttagnaKompTimmar); // oförändrat
        Assert.Equal(2m, b.TillgangligKomptidTimmar);
    }

    [Fact]
    public void RegistreraKomputtag_ExaktEnHundradelOverTaket_Kastar()
    {
        var b = Balans(intjanat: 6m, uttaget: 6m); // tillgängligt = 0

        Assert.Throws<InvalidOperationException>(() => b.RegistreraKomputtag(0.01m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void RegistreraKomputtag_IckePositivt_Kastar(double timmar)
    {
        var b = Balans(intjanat: 10m);

        Assert.Throws<ArgumentOutOfRangeException>(() => b.RegistreraKomputtag((decimal)timmar));
    }

    [Fact]
    public void AterforKomputtag_MinskarUttaget()
    {
        var b = Balans(intjanat: 10m, uttaget: 6m);

        b.AterforKomputtag(4m);

        Assert.Equal(2m, b.UttagnaKompTimmar);
        Assert.Equal(8m, b.TillgangligKomptidTimmar);
    }

    [Fact]
    public void AterforKomputtag_MerAnUttaget_KlamparVidNoll()
    {
        var b = Balans(intjanat: 10m, uttaget: 3m);

        b.AterforKomputtag(5m);

        Assert.Equal(0m, b.UttagnaKompTimmar);
    }

    [Fact]
    public void AterforKomputtag_IckePositivt_Kastar()
    {
        var b = Balans(intjanat: 10m, uttaget: 3m);

        Assert.Throws<ArgumentOutOfRangeException>(() => b.AterforKomputtag(0m));
    }

    [Fact]
    public void UttagOchAterfor_GerNettoNoll_KanTasUtIgen()
    {
        var b = Balans(intjanat: 8m);

        b.RegistreraKomputtag(8m);   // allt uttaget
        b.AterforKomputtag(8m);      // återfört

        Assert.Equal(8m, b.TillgangligKomptidTimmar);
        b.RegistreraKomputtag(8m);   // ska gå igen
        Assert.Equal(0m, b.TillgangligKomptidTimmar);
    }
}
