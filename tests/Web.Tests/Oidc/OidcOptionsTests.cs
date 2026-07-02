using RegionHR.Web.Services.Oidc;

namespace RegionHR.Web.Tests.Oidc;

/// <summary>
/// Verifierar togglelogiken: OpenHR federerar mot Entra ID endast när sektionen är påslagen OCH
/// minimalt ifylld. Annars kör demo-login som förut (IsConfigured = false).
/// </summary>
public class OidcOptionsTests
{
    [Fact]
    public void IsConfigured_IsFalse_ByDefault()
    {
        var options = new OidcOptions();
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsFalse_WhenEnabledButTenantMissing()
    {
        var options = new OidcOptions { Enabled = true, ClientId = "client" };
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsFalse_WhenTenantAndClientSetButDisabled()
    {
        var options = new OidcOptions { Enabled = false, TenantId = "tenant", ClientId = "client" };
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenEnabledAndTenantAndClientSet()
    {
        var options = new OidcOptions { Enabled = true, TenantId = "tenant", ClientId = "client" };
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Authority_CombinesInstanceAndTenant_WithoutDoubleSlash()
    {
        var options = new OidcOptions
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = "contoso-tenant-id",
        };
        Assert.Equal("https://login.microsoftonline.com/contoso-tenant-id", options.Authority);
    }

    [Fact]
    public void DefaultRole_DefaultsToLeastPrivilege()
    {
        Assert.Equal("Anställd", new OidcOptions().DefaultRole);
    }
}
