using Xunit;
using RegionHR.Agreements.Domain;

namespace RegionHR.Agreements.Tests;

/// <summary>
/// Verifierar övertidsreglerna mot AB § 20 mom. 3.
/// KÄLLA: SKR "Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01", § 20.
/// Enkel övertid 180%, kvalificerad 240%, timlön = månadslön / 165.
/// </summary>
public class ABOvertidSatserTests
{
    [Fact]
    public void Procentsatser_MatcharAB20Mom3()
    {
        Assert.Equal(1.80m, ABOvertidSatser.EnkelOvertidProcent);
        Assert.Equal(2.40m, ABOvertidSatser.KvalificeradOvertidProcent);
        Assert.Equal(0.80m, ABOvertidSatser.EnkelOvertidTillaggFaktor);
        Assert.Equal(1.40m, ABOvertidSatser.KvalificeradOvertidTillaggFaktor);
        Assert.Equal(165m, ABOvertidSatser.Overtidsdelare);
    }

    [Fact]
    public void Overtidstimlon_ArManadslonDelatMed165()
    {
        Assert.Equal(200m, ABOvertidSatser.Overtidstimlon(33000m));
    }

    [Theory]
    [InlineData(33000, 10, false, 1600)]  // 200 * 0.80 * 10
    [InlineData(33000, 10, true, 2800)]   // 200 * 1.40 * 10
    public void OvertidsTillagg_BeraknasKorrekt(decimal manadslon, decimal timmar, bool kvalificerad, decimal forvantat)
    {
        Assert.Equal(forvantat, ABOvertidSatser.OvertidsTillagg(manadslon, timmar, kvalificerad));
    }

    [Fact]
    public void MaxSparadeOvertidstimmar_Ar200()
    {
        Assert.Equal(200m, ABOvertidSatser.MaxSparadeOvertidstimmar);
    }
}
