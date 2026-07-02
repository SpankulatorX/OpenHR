using RegionHR.Web.Services;

namespace RegionHR.Web.Tests;

/// <summary>
/// Verifierar den centrala rutt → roll-policyn (single source of truth för URL-skydd).
/// Roller: "Admin", "HR", "Chef", "Anställd". null = oinloggad.
/// </summary>
public class RouteAccessPolicyTests
{
    private const string Admin = RouteAccessPolicy.Admin;   // "Admin"
    private const string Hr = RouteAccessPolicy.HR;         // "HR"
    private const string Chef = RouteAccessPolicy.Chef;     // "Chef"
    private const string Anstalld = RouteAccessPolicy.Anstalld; // "Anställd"

    // ── Öppna rutter: alltid nåbara, även oinloggade ──
    [Theory]
    [InlineData("/login")]
    [InlineData("/trust")]
    [InlineData("/Error")]
    public void OpenRoutes_AreAllowed_ForAnonymous(string path)
    {
        Assert.True(RouteAccessPolicy.IsOpen(path));
        Assert.True(RouteAccessPolicy.IsAllowed(path, null));
        Assert.True(RouteAccessPolicy.IsAllowed(path, Anstalld));
    }

    // ── Oinloggad når inget skyddat ──
    [Theory]
    [InlineData("/")]
    [InlineData("/minsida")]
    [InlineData("/audit")]
    [InlineData("/lon/korningar")]
    public void ProtectedRoutes_AreDenied_ForAnonymous(string path)
    {
        Assert.False(RouteAccessPolicy.IsAllowed(path, null));
    }

    // ── Anställd: BLOCKERAD från lönekänsligt/admin/audit/gdpr ──
    [Theory]
    [InlineData("/audit")]
    [InlineData("/gdpr")]
    [InlineData("/admin/konfiguration")]
    [InlineData("/admin/provisionering")]
    [InlineData("/lon/korningar")]
    [InlineData("/lon/statistik")]
    [InlineData("/loneoversyn")]
    [InlineData("/integrationer")]
    [InlineData("/chef")]
    [InlineData("/chef/team")]
    [InlineData("/godkannanden")]
    [InlineData("/schema")]
    [InlineData("/tidrapporter")]
    [InlineData("/anstallda")]
    [InlineData("/rapporter/analytics")]
    [InlineData("/rekrytering/vakanser")]
    public void Anstalld_IsDenied_ForPrivilegedRoutes(string path)
    {
        Assert.False(RouteAccessPolicy.IsAllowed(path, Anstalld));
    }

    // ── Anställd: TILLÅTEN för självservice ──
    [Theory]
    [InlineData("/")]
    [InlineData("/minsida")]
    [InlineData("/minsida/lon")]
    [InlineData("/minsida/lonespecifikationer")]
    [InlineData("/minsida/schema")]
    [InlineData("/ledighet")]
    [InlineData("/ledighet/ny")]
    [InlineData("/notiser")]
    [InlineData("/notiser/installningar")]
    [InlineData("/helpdesk")]
    [InlineData("/karriar")]
    [InlineData("/kompetens/endorsements")]
    public void Anstalld_IsAllowed_ForSelfService(string path)
    {
        Assert.True(RouteAccessPolicy.IsAllowed(path, Anstalld));
    }

    // ── Chef: TILLÅTEN för sina funktioner ──
    [Theory]
    [InlineData("/chef")]
    [InlineData("/chef/team")]
    [InlineData("/chef/bemanning")]
    [InlineData("/chef/franvarokalender")]
    [InlineData("/godkannanden")]
    [InlineData("/schema")]
    [InlineData("/schema/bemanning")]
    [InlineData("/tidrapporter")]
    [InlineData("/anstallda")]
    [InlineData("/anstallda/00000000-0000-0000-0000-000000000001")]
    [InlineData("/rapporter/analytics")]
    [InlineData("/rekrytering/vakanser")]
    public void Chef_IsAllowed_ForManagerRoutes(string path)
    {
        Assert.True(RouteAccessPolicy.IsAllowed(path, Chef));
    }

    // ── Chef: BLOCKERAD från lön/audit/gdpr/admin ──
    [Theory]
    [InlineData("/lon/korningar")]
    [InlineData("/loneoversyn")]
    [InlineData("/audit")]
    [InlineData("/gdpr")]
    [InlineData("/admin/konfiguration")]
    [InlineData("/integrationer")]
    public void Chef_IsDenied_ForHrOnlyRoutes(string path)
    {
        Assert.False(RouteAccessPolicy.IsAllowed(path, Chef));
    }

    // ── HR: TILLÅTEN för lön/audit/gdpr/admin ──
    [Theory]
    [InlineData("/lon/korningar")]
    [InlineData("/loneoversyn")]
    [InlineData("/audit")]
    [InlineData("/gdpr")]
    [InlineData("/admin/konfiguration")]
    [InlineData("/integrationer")]
    [InlineData("/chef")]
    [InlineData("/minsida")]
    public void Hr_IsAllowed_ForHrRoutes(string path)
    {
        Assert.True(RouteAccessPolicy.IsAllowed(path, Hr));
    }

    // ── Admin: TILLÅTEN överallt ──
    [Theory]
    [InlineData("/")]
    [InlineData("/audit")]
    [InlineData("/gdpr")]
    [InlineData("/lon/korningar")]
    [InlineData("/admin/konfiguration")]
    [InlineData("/anstallda/ny")]
    [InlineData("/nagon/okand/rutt")]
    public void Admin_IsAllowed_Everywhere(string path)
    {
        Assert.True(RouteAccessPolicy.IsAllowed(path, Admin));
    }

    // ── Känsliga person-/löneunderåtgärder: HR/Admin, inte Chef ──
    [Theory]
    [InlineData("/anstallda/ny")]
    [InlineData("/anstallda/00000000-0000-0000-0000-000000000001/anstallning")]
    [InlineData("/anstallda/00000000-0000-0000-0000-000000000001/lonehistorik")]
    public void SensitiveEmployeeSubpaths_AreHrOnly(string path)
    {
        Assert.False(RouteAccessPolicy.IsAllowed(path, Chef));
        Assert.True(RouteAccessPolicy.IsAllowed(path, Hr));
        Assert.True(RouteAccessPolicy.IsAllowed(path, Admin));
    }

    // ── Prefix-gräns: "/lon" ska INTE matcha "/loneoversyn" av misstag ──
    // (båda är HR-only här, men de matchas via olika regler — verifiera segmentgräns)
    [Fact]
    public void PrefixMatch_RespectsSegmentBoundary()
    {
        // /minsida/schema styrs av /minsida-regeln (alla), INTE av /schema-regeln (chef).
        Assert.True(RouteAccessPolicy.IsAllowed("/minsida/schema", Anstalld));
        // /schema styrs av chef-regeln.
        Assert.False(RouteAccessPolicy.IsAllowed("/schema", Anstalld));
    }

    // ── Normalisering: query, trailing slash, versaler ──
    [Theory]
    [InlineData("/audit?tab=2")]
    [InlineData("/audit/")]
    [InlineData("/AUDIT")]
    [InlineData("/Audit")]
    public void Normalization_Handles_Query_Slash_And_Case(string path)
    {
        Assert.False(RouteAccessPolicy.IsAllowed(path, Anstalld));
        Assert.True(RouteAccessPolicy.IsAllowed(path, Hr));
    }

    // ── Okänd rutt: fail-safe = kräver minst inloggning ──
    [Fact]
    public void UnknownRoute_RequiresAuthentication()
    {
        Assert.False(RouteAccessPolicy.IsAllowed("/nagon/helt/okand/rutt", null));
        Assert.True(RouteAccessPolicy.IsAllowed("/nagon/helt/okand/rutt", Anstalld));
    }
}
