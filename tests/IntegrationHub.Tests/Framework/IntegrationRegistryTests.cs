using RegionHR.IntegrationHub.Framework;
using Xunit;

namespace RegionHR.IntegrationHub.Tests.Framework;

/// <summary>
/// Tester för <see cref="IntegrationRegistry"/> — registret som beskriver alla
/// integrationer i regionens personalstöds-arkitektur.
/// </summary>
public class IntegrationRegistryTests
{
    [Fact]
    public void Alla_HarUnikaNycklar()
    {
        var nycklar = IntegrationRegistry.Alla.Select(d => d.Key).ToList();
        Assert.Equal(nycklar.Count, nycklar.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Alla_TackerHelaArkitekturen()
    {
        // "Personalstöd v7" ≈ 26 integrationer; registret ska vara i den storleksordningen.
        Assert.True(IntegrationRegistry.Alla.Count >= 26,
            $"Förväntade minst 26 integrationer, fann {IntegrationRegistry.Alla.Count}.");
    }

    [Theory]
    [InlineData("skatteverket-agi")]
    [InlineData("nordea-pain001")]
    [InlineData("kpa-pension")]
    [InlineData("fk-ssbtek")]
    [InlineData("scb-klr")]
    [InlineData("skr-statistik")]
    [InlineData("platsbanken")]
    [InlineData("koll-katalog")]
    [InlineData("hc-manifest")]
    public void HittaOrNull_HittarKandaNycklar(string key)
    {
        Assert.NotNull(IntegrationRegistry.HittaOrNull(key));
    }

    [Fact]
    public void HittaOrNull_ArSkiftlagesokanslig()
    {
        Assert.NotNull(IntegrationRegistry.HittaOrNull("SKATTEVERKET-AGI"));
    }

    [Fact]
    public void HittaOrNull_ReturnerarNullForOkand()
    {
        Assert.Null(IntegrationRegistry.HittaOrNull("finns-inte"));
    }

    [Fact]
    public void Alla_HarKompletta_ObligatoriskaFalt()
    {
        Assert.All(IntegrationRegistry.Alla, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Key));
            Assert.False(string.IsNullOrWhiteSpace(d.Namn));
            Assert.False(string.IsNullOrWhiteSpace(d.Motpart));
            Assert.False(string.IsNullOrWhiteSpace(d.Frekvens));
            Assert.False(string.IsNullOrWhiteSpace(d.Format));
            Assert.False(string.IsNullOrWhiteSpace(d.Beskrivning));
        });
    }

    [Fact]
    public void UtgaendeOchInkommande_InkluderarBadaRiktningar()
    {
        var bada = IntegrationRegistry.Alla
            .First(d => d.Riktning == IntegrationDirection.BadaRiktningar);

        Assert.Contains(bada, IntegrationRegistry.Utgaende);
        Assert.Contains(bada, IntegrationRegistry.Inkommande);
    }

    [Fact]
    public void Utgaende_UteslutterRentInkommande()
    {
        Assert.DoesNotContain(IntegrationRegistry.Utgaende,
            d => d.Riktning == IntegrationDirection.Inkommande);
    }

    [Fact]
    public void Alla_ArSorteratPaNamn()
    {
        var namn = IntegrationRegistry.Alla.Select(d => d.Namn).ToList();
        var sorterat = namn.OrderBy(n => n,
            StringComparer.Create(new System.Globalization.CultureInfo("sv-SE"), ignoreCase: true)).ToList();
        Assert.Equal(sorterat, namn);
    }
}
