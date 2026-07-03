using RegionHR.Web.Components.Pages;
using RegionHR.Web.Services;
using Xunit;

namespace RegionHR.Web.Tests.Pages;

/// <summary>
/// Låser den rollmedvetna startsidan (Home.razor): varje roll ska mötas av sin
/// EGEN variant. Det viktigaste testet är att en Anställd (eller en okänd/saknad
/// roll) ALDRIG får HR-/org-varianten — det var det kända problemet där en
/// anställds startsida visade en HR-dashboard med organisationsövergripande KPI:er.
/// </summary>
public class HomeDashboardVariantTests
{
    [Theory]
    [InlineData(RouteAccessPolicy.Admin, Home.DashboardVariant.Admin)]
    [InlineData(RouteAccessPolicy.HR, Home.DashboardVariant.HR)]
    [InlineData(RouteAccessPolicy.Chef, Home.DashboardVariant.Chef)]
    [InlineData(RouteAccessPolicy.Anstalld, Home.DashboardVariant.Personal)]
    public void VariantFor_maps_each_role_to_its_own_view(string role, Home.DashboardVariant expected)
    {
        Assert.Equal(expected, Home.VariantFor(role));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Okänd")]
    [InlineData("Administrator")] // nästan-men-inte "Admin" → får inte tolkas som Admin
    public void VariantFor_defaults_to_Personal_for_unknown_or_missing_role(string? role)
    {
        // Fail-safe: en icke-igenkänd roll får den personliga vyn, aldrig org-KPI:er.
        Assert.Equal(Home.DashboardVariant.Personal, Home.VariantFor(role));
    }

    [Fact]
    public void Anstalld_never_gets_the_HR_or_Admin_org_view()
    {
        var variant = Home.VariantFor(RouteAccessPolicy.Anstalld);
        Assert.NotEqual(Home.DashboardVariant.HR, variant);
        Assert.NotEqual(Home.DashboardVariant.Admin, variant);
        Assert.Equal(Home.DashboardVariant.Personal, variant);
    }

    [Fact]
    public void Chef_gets_unit_scoped_chef_view_not_the_HR_org_view()
    {
        Assert.Equal(Home.DashboardVariant.Chef, Home.VariantFor(RouteAccessPolicy.Chef));
    }
}
