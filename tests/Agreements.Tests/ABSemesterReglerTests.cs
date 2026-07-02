using Xunit;
using RegionHR.Agreements.Domain;

namespace RegionHR.Agreements.Tests;

/// <summary>
/// Verifierar semesterreglerna mot AB § 27.
/// KÄLLA: SKR "Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01", § 27.
/// mom. 5: 25 / 31 / 32 dagar per ålder. mom. 15: semesterdagstillägg 0,605%.
/// mom. 16: rörlig-lön-procent 12 / 14,88 / 15,36. mom. 6 / SemL § 7: betalda dagar
/// proportioneras mot anställd del av intjänandeåret.
/// </summary>
public class ABSemesterReglerTests
{
    [Theory]
    [InlineData(20, 25)]
    [InlineData(39, 25)]  // t.o.m. det år man fyller 39 → 25
    [InlineData(40, 31)]  // fr.o.m. det år man fyller 40 → 31
    [InlineData(49, 31)]
    [InlineData(50, 32)]  // fr.o.m. det år man fyller 50 → 32
    [InlineData(64, 32)]
    public void ArligSemesterratt_TrappasMedAlder(int alder, int forvantat)
    {
        Assert.Equal(forvantat, ABSemesterRegler.ArligSemesterratt(alder));
    }

    [Theory]
    [InlineData(1990, 2026, 25)]  // fyller 36
    [InlineData(1986, 2026, 31)]  // fyller 40
    [InlineData(1976, 2026, 32)]  // fyller 50
    public void ArligSemesterrattForFodelsear_AnvanderAlderUnderIntjanandear(int fodelsear, int intjanandear, int forvantat)
    {
        Assert.Equal(forvantat, ABSemesterRegler.ArligSemesterrattForFodelsear(fodelsear, intjanandear));
    }

    [Fact]
    public void RorligLonProcent_MatcharAB27Mom16()
    {
        Assert.Equal(12.00m, ABSemesterRegler.RorligLonProcent(25));
        Assert.Equal(14.88m, ABSemesterRegler.RorligLonProcent(31));
        Assert.Equal(15.36m, ABSemesterRegler.RorligLonProcent(32));
    }

    [Fact]
    public void SemesterdagstillaggProcent_Ar0Komma605()
    {
        Assert.Equal(0.605m, ABSemesterRegler.SemesterdagstillaggProcent);
    }

    [Theory]
    [InlineData(25, 12, 25)]  // hel årsanställning → full rätt
    [InlineData(25, 6, 13)]   // 25*6/12 = 12,5 → avrundas uppåt till 13
    [InlineData(25, 0, 0)]    // ingen anställning → 0 betalda
    [InlineData(31, 6, 16)]   // 31*6/12 = 15,5 → 16
    [InlineData(32, 3, 8)]    // 32*3/12 = 8,0 → 8
    [InlineData(25, 18, 25)]  // clampas till 12 månader → full rätt
    public void BetaldaDagar_ProportioneraMotAnstallningstid(int arsratt, int manader, int forvantat)
    {
        Assert.Equal(forvantat, ABSemesterRegler.BetaldaDagar(arsratt, manader));
    }

    [Fact]
    public void BetaldaDagar_RattarTidigareBugg_DarAnstallningsmanaderIgnorerades()
    {
        // Tidigare gav motorn full årsrätt oavsett anställningstid. En nyanställd
        // (t.ex. 3 månader) ska INTE få 25 betalda dagar utan bara en proportionell del.
        var betalda = ABSemesterRegler.BetaldaDagar(25, 3);
        Assert.Equal(7, betalda); // 25*3/12 = 6,25 → 7
        Assert.True(betalda < ABSemesterRegler.BasDagar);
    }

    [Theory]
    [InlineData(25, 5)]   // 25-20
    [InlineData(31, 11)]  // 31-20
    [InlineData(32, 12)]  // 32-20
    [InlineData(20, 0)]   // exakt 20 → inget sparbart
    [InlineData(15, 0)]   // under 20 → inget sparbart
    public void MaxSparbaraDagar_EndastOver20(int betalda, int forvantat)
    {
        Assert.Equal(forvantat, ABSemesterRegler.MaxSparbaraDagar(betalda));
    }
}
