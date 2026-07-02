using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SharedKernel.Domain;
using RegionHR.Web.Services.Oidc;

namespace RegionHR.Web.Tests.Oidc;

/// <summary>
/// Verifierar bryggan federerad Entra-identitet → OpenHR-personalregister: koppling via e-post
/// (föredraget) respektive namn, upplösning av aktiv enhet, samt robust fallback när ingen
/// personalpost matchar (rollen står ändå — som demo-login).
/// </summary>
public class OidcAccountLinkerTests
{
    private static readonly Guid VardavdelningId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class TestDbFactory : IDbContextFactory<RegionHRDbContext>
    {
        private readonly DbContextOptions<RegionHRDbContext> _options;
        public TestDbFactory(string dbName)
            => _options = new DbContextOptionsBuilder<RegionHRDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

        public RegionHRDbContext CreateDbContext() => new(_options);
    }

    private static async Task<TestDbFactory> SeedAsync(string dbName)
    {
        var factory = new TestDbFactory(dbName);
        await using var db = factory.CreateDbContext();

        var anna = Employee.Skapa(Personnummer.CreateValidated("19850312123"), "Anna", "Svensson");
        anna.UppdateraKontaktuppgifter("anna.svensson@region.se", null, null);
        anna.LaggTillAnstallning(
            OrganizationId.From(VardavdelningId),
            EmploymentType.Tillsvidare,
            CollectiveAgreementType.AB,
            Money.SEK(32000m),
            Percentage.FullTime,
            new DateOnly(2020, 1, 1));

        // Anställd utan enhet/epost — endast namn att matcha på.
        var bo = Employee.Skapa(Personnummer.CreateValidated("19770101234"), "Bo", "Karlsson");

        db.Employees.Add(anna);
        db.Employees.Add(bo);
        await db.SaveChangesAsync();
        return factory;
    }

    private static EntraIdentity Identity(string name, string? email, string role = "Anställd")
        => new(name, email, "oid-123", role, Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public async Task ResolveAsync_MatchesByEmail_AndResolvesActiveUnit()
    {
        var factory = await SeedAsync(nameof(ResolveAsync_MatchesByEmail_AndResolvesActiveUnit));
        var linker = new OidcAccountLinker(factory);

        var result = await linker.ResolveAsync(Identity("Anna Svensson", "anna.svensson@region.se", "HR"));

        Assert.Equal(AccountMatch.Email, result.MatchedEmployee);
        Assert.NotNull(result.EmployeeId);
        Assert.Equal(VardavdelningId, result.UnitId);
        Assert.Equal("HR", result.Role);
        Assert.Equal("Anna Svensson", result.DisplayName);
    }

    [Fact]
    public async Task ResolveAsync_EmailMatch_IsCaseInsensitive()
    {
        var factory = await SeedAsync(nameof(ResolveAsync_EmailMatch_IsCaseInsensitive));
        var linker = new OidcAccountLinker(factory);

        var result = await linker.ResolveAsync(Identity("Whatever Name", "ANNA.SVENSSON@REGION.SE"));

        Assert.Equal(AccountMatch.Email, result.MatchedEmployee);
        Assert.NotNull(result.EmployeeId);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToNameMatch_WhenNoEmail()
    {
        var factory = await SeedAsync(nameof(ResolveAsync_FallsBackToNameMatch_WhenNoEmail));
        var linker = new OidcAccountLinker(factory);

        var result = await linker.ResolveAsync(Identity("Bo Karlsson", email: null));

        Assert.Equal(AccountMatch.Name, result.MatchedEmployee);
        Assert.NotNull(result.EmployeeId);
        Assert.Null(result.UnitId); // Bo saknar anställning/enhet
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_KeepsRole_WithoutEmployee()
    {
        var factory = await SeedAsync(nameof(ResolveAsync_NoMatch_KeepsRole_WithoutEmployee));
        var linker = new OidcAccountLinker(factory);

        var result = await linker.ResolveAsync(Identity("Ingen Här", "ingen@example.com", "Admin"));

        Assert.Equal(AccountMatch.None, result.MatchedEmployee);
        Assert.Null(result.EmployeeId);
        Assert.Null(result.UnitId);
        Assert.Equal("Admin", result.Role); // rollen står kvar, precis som demo-admin
    }

    [Fact]
    public async Task ResolveAsync_UnmatchedUser_UsesUnitClaimHint()
    {
        var factory = await SeedAsync(nameof(ResolveAsync_UnmatchedUser_UsesUnitClaimHint));
        var linker = new OidcAccountLinker(factory);
        var hint = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        var result = await linker.ResolveAsync(Identity("Ingen Här", "ingen@example.com"), unitClaimHint: hint);

        Assert.Null(result.EmployeeId);
        Assert.Equal(hint, result.UnitId);
    }

    [Fact]
    public async Task ResolveAsync_EmployeeUnit_TakesPrecedenceOverClaimHint()
    {
        var factory = await SeedAsync(nameof(ResolveAsync_EmployeeUnit_TakesPrecedenceOverClaimHint));
        var linker = new OidcAccountLinker(factory);
        var hint = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        var result = await linker.ResolveAsync(
            Identity("Anna Svensson", "anna.svensson@region.se"), unitClaimHint: hint);

        Assert.Equal(VardavdelningId, result.UnitId); // faktisk enhet vinner över claim-hint
    }
}
