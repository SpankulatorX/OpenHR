using System.Security.Claims;
using Microsoft.Extensions.Options;
using RegionHR.Web.Services.Oidc;

namespace RegionHR.Web.Tests.Oidc;

/// <summary>
/// Verifierar den dokumenterade claim → OpenHR-roll-mappningen för federerad Entra ID-inloggning.
/// Roller (fallande privilegium): Admin &gt; HR &gt; Chef &gt; Anställd. Okänd användare ⇒ default.
/// </summary>
public class EntraClaimsMapperTests
{
    private static EntraClaimsMapper CreateMapper(OidcOptions? options = null)
        => new(Options.Create(options ?? DefaultOptions()));

    private static OidcOptions DefaultOptions() => new()
    {
        GroupRoleMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["admin-group-id"] = "Admin",
            ["hr-group-id"] = "HR",
            ["chef-group-id"] = "Chef",
        },
        AppRoleMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenHR.Payroll.Admin"] = "HR",
        },
        DefaultRole = "Anställd",
    };

    // ── Grupp → roll ──

    [Fact]
    public void MapRole_MapsSingleGroup_ToConfiguredRole()
    {
        var role = CreateMapper().MapRole(new[] { "hr-group-id" }, Array.Empty<string>());
        Assert.Equal("HR", role);
    }

    [Fact]
    public void MapRole_GroupKey_IsCaseInsensitive()
    {
        var role = CreateMapper().MapRole(new[] { "HR-GROUP-ID" }, Array.Empty<string>());
        Assert.Equal("HR", role);
    }

    [Theory]
    [InlineData(new[] { "chef-group-id", "hr-group-id" }, "HR")]     // HR slår Chef
    [InlineData(new[] { "admin-group-id", "hr-group-id" }, "Admin")] // Admin slår HR
    [InlineData(new[] { "chef-group-id", "admin-group-id" }, "Admin")]
    public void MapRole_MultipleGroups_PicksHighestPrivilege(string[] groups, string expected)
    {
        var role = CreateMapper().MapRole(groups, Array.Empty<string>());
        Assert.Equal(expected, role);
    }

    // ── App-roll → roll ──

    [Fact]
    public void MapRole_MapsAppRole_ViaConfiguredMapping()
    {
        var role = CreateMapper().MapRole(Array.Empty<string>(), new[] { "OpenHR.Payroll.Admin" });
        Assert.Equal("HR", role);
    }

    [Fact]
    public void MapRole_AppRole_ThatIsAlreadyAnOpenHrRole_IsAcceptedDirectly()
    {
        // App-roll vars värde redan är en giltig OpenHR-roll behöver ingen mappningspost.
        var role = CreateMapper().MapRole(Array.Empty<string>(), new[] { "Chef" });
        Assert.Equal("Chef", role);
    }

    [Fact]
    public void MapRole_DirectRoleValue_IsCaseInsensitive()
    {
        var role = CreateMapper().MapRole(Array.Empty<string>(), new[] { "admin" });
        Assert.Equal("Admin", role);
    }

    [Fact]
    public void MapRole_GroupsAndAppRoles_CombinedHighestWins()
    {
        var role = CreateMapper().MapRole(new[] { "chef-group-id" }, new[] { "Admin" });
        Assert.Equal("Admin", role);
    }

    // ── Fallback / default ──

    [Fact]
    public void MapRole_NoMatch_FallsBackToDefaultRole()
    {
        var role = CreateMapper().MapRole(new[] { "unknown-group" }, new[] { "unknown-role" });
        Assert.Equal("Anställd", role);
    }

    [Fact]
    public void MapRole_EmptyInput_ReturnsDefaultRole()
    {
        var role = CreateMapper().MapRole(Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal("Anställd", role);
    }

    [Fact]
    public void MapRole_InvalidConfiguredDefault_FallsBackToAnstalld()
    {
        var opts = DefaultOptions();
        opts.DefaultRole = "Superuser"; // ogiltig OpenHR-roll
        var role = CreateMapper(opts).MapRole(Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal("Anställd", role);
    }

    [Fact]
    public void MapRole_ConfiguredDefaultRole_IsHonored()
    {
        var opts = DefaultOptions();
        opts.DefaultRole = "Chef";
        var role = CreateMapper(opts).MapRole(Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal("Chef", role);
    }

    [Fact]
    public void MapRole_IgnoresBlankGroupAndRoleValues()
    {
        var role = CreateMapper().MapRole(new[] { "", "  " }, new[] { "" });
        Assert.Equal("Anställd", role);
    }

    // ── Map(ClaimsPrincipal) ──

    [Fact]
    public void Map_ExtractsName_Email_ObjectId_AndRole()
    {
        var principal = BuildPrincipal(new[]
        {
            new Claim("name", "Eva Nilsson"),
            new Claim("preferred_username", "eva.nilsson@region.se"),
            new Claim("oid", "11111111-2222-3333-4444-555555555555"),
            new Claim("groups", "chef-group-id"),
        });

        var identity = CreateMapper().Map(principal);

        Assert.Equal("Eva Nilsson", identity.DisplayName);
        Assert.Equal("eva.nilsson@region.se", identity.Email);
        Assert.Equal("11111111-2222-3333-4444-555555555555", identity.ObjectId);
        Assert.Equal("Chef", identity.Role);
        Assert.Contains("chef-group-id", identity.Groups);
    }

    [Fact]
    public void Map_CollectsMultipleGroups()
    {
        var principal = BuildPrincipal(new[]
        {
            new Claim("name", "Test Testsson"),
            new Claim("groups", "hr-group-id"),
            new Claim("groups", "chef-group-id"),
        });

        var identity = CreateMapper().Map(principal);

        Assert.Equal(2, identity.Groups.Count);
        Assert.Equal("HR", identity.Role); // högsta privilegiet av de två grupperna
    }

    [Fact]
    public void Map_FallsBackToEmail_WhenNameMissing()
    {
        var principal = BuildPrincipal(new[]
        {
            new Claim("preferred_username", "user@region.se"),
        });

        var identity = CreateMapper().Map(principal);

        Assert.Equal("user@region.se", identity.DisplayName);
    }

    [Fact]
    public void Map_FallsBackToPlaceholder_WhenNameAndEmailMissing()
    {
        var principal = BuildPrincipal(new[]
        {
            new Claim("oid", "abc"),
        });

        var identity = CreateMapper().Map(principal);

        Assert.Equal("Entra-användare", identity.DisplayName);
        Assert.Equal("Anställd", identity.Role);
    }

    [Fact]
    public void Map_ReadsAppRoles_FromStandardRoleClaimToo()
    {
        var principal = BuildPrincipal(new[]
        {
            new Claim("name", "Admin Adminsson"),
            new Claim(ClaimTypes.Role, "Admin"),
        });

        var identity = CreateMapper().Map(principal);

        Assert.Equal("Admin", identity.Role);
        Assert.Contains("Admin", identity.AppRoles);
    }

    private static ClaimsPrincipal BuildPrincipal(IEnumerable<Claim> claims)
        => new(new ClaimsIdentity(claims, "TestAuth"));
}
