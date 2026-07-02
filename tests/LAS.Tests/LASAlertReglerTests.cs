using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.LAS.Tests;

/// <summary>
/// Tester för den rena LAS-larmmotorn (trösklar + mottagarval).
/// Verifierar bl.a. att SAVA-trösklarna landar exakt på 300/330/350/360 dagar
/// och att den anställde ALDRIG blir mottagare av sitt eget larm.
/// </summary>
public class LASAlertReglerTests
{
    [Theory]
    [InlineData(200, LASAlertNiva.Ingen)]
    [InlineData(299, LASAlertNiva.Ingen)]
    [InlineData(300, LASAlertNiva.Varning)]
    [InlineData(329, LASAlertNiva.Varning)]
    [InlineData(330, LASAlertNiva.Kritisk)]
    [InlineData(349, LASAlertNiva.Kritisk)]
    [InlineData(350, LASAlertNiva.MycketKritisk)]
    [InlineData(359, LASAlertNiva.MycketKritisk)]
    [InlineData(360, LASAlertNiva.Konvertering)]
    [InlineData(365, LASAlertNiva.Konvertering)]
    [InlineData(400, LASAlertNiva.Konvertering)]
    public void Bedom_SAVA_GerRattNiva_VidDokumenteradeTrosklar(int dagar, LASAlertNiva forvantad)
    {
        var b = LASAlertRegler.Bedom(EmploymentType.SAVA, dagar);
        Assert.Equal(forvantad, b.Niva);
        Assert.Equal(365, b.GransDagar);
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(330, 330)]
    [InlineData(350, 350)]
    [InlineData(360, 360)]
    public void Bedom_SAVA_SatterKorrektTroskelDagarForDedup(int dagar, int forvantadTroskel)
    {
        var b = LASAlertRegler.Bedom(EmploymentType.SAVA, dagar);
        Assert.Equal(forvantadTroskel, b.TroskelDagar);
    }

    [Theory]
    [InlineData(600, LASAlertNiva.Ingen)]
    [InlineData(665, LASAlertNiva.Varning)]
    [InlineData(695, LASAlertNiva.Kritisk)]
    [InlineData(715, LASAlertNiva.MycketKritisk)]
    [InlineData(725, LASAlertNiva.Konvertering)]
    [InlineData(730, LASAlertNiva.Konvertering)]
    public void Bedom_Vikariat_SkalarTrosklarMotTvaArsgrans(int dagar, LASAlertNiva forvantad)
    {
        var b = LASAlertRegler.Bedom(EmploymentType.Vikariat, dagar);
        Assert.Equal(forvantad, b.Niva);
        Assert.Equal(730, b.GransDagar);
    }

    [Theory]
    [InlineData(EmploymentType.Tillsvidare)]
    [InlineData(EmploymentType.Provanstallning)]
    public void Bedom_IckeLASForm_GerAldrigLarm(EmploymentType form)
    {
        var b = LASAlertRegler.Bedom(form, 100000);
        Assert.Equal(LASAlertNiva.Ingen, b.Niva);
    }

    [Fact]
    public void Bedom_RaknarDagarKvarTillGrans()
    {
        var b = LASAlertRegler.Bedom(EmploymentType.SAVA, 350);
        Assert.Equal(15, b.DagarKvar); // 365 - 350
    }

    [Fact]
    public void ValjMottagare_InkluderarHROchChef()
    {
        var hr = Guid.NewGuid();
        var chef = Guid.NewGuid();
        var anstalld = Guid.NewGuid();

        var mottagare = LASAlertRegler.ValjMottagare(new[] { hr }, chef, anstalld);

        Assert.Contains(hr, mottagare);
        Assert.Contains(chef, mottagare);
        Assert.Equal(2, mottagare.Count);
    }

    [Fact]
    public void ValjMottagare_ExkluderarAlltidDenAnstallde_AvenOmHR()
    {
        var anstalld = Guid.NewGuid();
        var hrKollega = Guid.NewGuid();

        // Den anställde råkar själv finnas i HR-listan — ska ändå aldrig få sitt eget larm.
        var mottagare = LASAlertRegler.ValjMottagare(new[] { anstalld, hrKollega }, chefId: null, anstalld);

        Assert.DoesNotContain(anstalld, mottagare);
        Assert.Contains(hrKollega, mottagare);
    }

    [Fact]
    public void ValjMottagare_ExkluderarAnstalldSomArSinEgenChef_GerTomMangd()
    {
        var anstalld = Guid.NewGuid();

        var mottagare = LASAlertRegler.ValjMottagare(Array.Empty<Guid>(), chefId: anstalld, anstalld);

        Assert.Empty(mottagare);
    }

    [Fact]
    public void ValjMottagare_UtanChef_GerBaraHR()
    {
        var hr = Guid.NewGuid();
        var anstalld = Guid.NewGuid();

        var mottagare = LASAlertRegler.ValjMottagare(new[] { hr }, chefId: null, anstalld);

        Assert.Single(mottagare);
        Assert.Contains(hr, mottagare);
    }
}
