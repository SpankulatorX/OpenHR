using RegionHR.Infrastructure.Integrations.HSA;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Integrations.HSA;

public class SandboxHsaCatalogAdapterTests
{
    private readonly SandboxHsaCatalogAdapter _adapter = new();

    [Fact]
    public void IsSandbox_IsTrue()
    {
        Assert.True(_adapter.IsSandbox);
        Assert.False(string.IsNullOrWhiteSpace(_adapter.SystemName));
    }

    [Fact]
    public async Task GetStatus_ReportsSandboxAndReachable()
    {
        var status = await _adapter.GetStatusAsync();

        Assert.True(status.IsSandbox);
        Assert.True(status.IsReachable);
        Assert.Contains("Demo", status.Description);
        Assert.Contains("Inera", status.Description);
    }

    [Fact]
    public async Task SlaUppEnhet_ReturnsUnitWithDemoRootHsaId()
    {
        var unit = await _adapter.SlaUppEnhetAsync("Akutmottagningen USÖ");

        Assert.NotNull(unit);
        Assert.StartsWith(SandboxHsaCatalogAdapter.DemoHsaRoot, unit!.HsaId);
        Assert.Equal("Akutmottagningen USÖ", unit.Namn);
    }

    [Fact]
    public async Task SlaUppEnhet_IsDeterministic()
    {
        var first = await _adapter.SlaUppEnhetAsync("Vårdcentralen Lindesberg");
        var second = await _adapter.SlaUppEnhetAsync("Vårdcentralen Lindesberg");

        Assert.Equal(first!.HsaId, second!.HsaId);
    }

    [Fact]
    public async Task SlaUppEnhet_DifferentInputs_DifferentIds()
    {
        var a = await _adapter.SlaUppEnhetAsync("Enhet A");
        var b = await _adapter.SlaUppEnhetAsync("Enhet B");

        Assert.NotEqual(a!.HsaId, b!.HsaId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SlaUppEnhet_BlankTerm_ReturnsNull(string term)
    {
        Assert.Null(await _adapter.SlaUppEnhetAsync(term));
    }

    [Fact]
    public async Task SlaUppPerson_IsDeterministicAndDistinctFromUnitId()
    {
        var p1 = await _adapter.SlaUppPersonAsync("198112289874");
        var p2 = await _adapter.SlaUppPersonAsync("198112289874");
        var enhet = await _adapter.SlaUppEnhetAsync("198112289874");

        Assert.NotNull(p1);
        Assert.Equal(p1!.HsaId, p2!.HsaId);
        // Person- och enhets-id ska inte kollidera även för samma söknyckel (olika typprefix).
        Assert.NotEqual(p1.HsaId, enhet!.HsaId);
    }

    [Fact]
    public async Task SlaUppPerson_BlankTerm_ReturnsNull()
    {
        Assert.Null(await _adapter.SlaUppPersonAsync(""));
    }

    [Fact]
    public async Task HamtaOrganisationstrad_IsConsistent()
    {
        var tree = await _adapter.HamtaOrganisationstradAsync();

        Assert.NotEmpty(tree);

        // Alla HSA-id är unika.
        var ids = tree.Select(u => u.HsaId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // Exakt en rot (utan överordnad).
        Assert.Single(tree, u => u.OverordnadHsaId is null);

        // Varje överordnad-referens pekar på en enhet som finns i trädet.
        var idSet = ids.ToHashSet();
        foreach (var unit in tree.Where(u => u.OverordnadHsaId is not null))
            Assert.Contains(unit.OverordnadHsaId!, idSet);
    }
}
