using RegionHR.Infrastructure.Diagnostics;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Diagnostics;

public class StartupDatabaseGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    public void AllowInMemoryFallback_TrueOnlyForDevelopment(string env)
    {
        Assert.True(StartupDatabaseGuard.AllowInMemoryFallback(env));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("")]
    [InlineData(null)]
    public void AllowInMemoryFallback_FalseForEverythingElse(string? env)
    {
        // Kärnkravet: endast Development får falla tillbaka på InMemory.
        // Alla andra miljöer måste hård-faila hellre än att servera tom DB.
        Assert.False(StartupDatabaseGuard.AllowInMemoryFallback(env));
    }

    [Fact]
    public void RedactPassword_MasksPassword_KeepsHostAndDatabase()
    {
        const string cs = "Host=db.internal;Port=5432;Database=regionhr;Username=svc;Password=sup3rs3cret";

        var redacted = StartupDatabaseGuard.RedactPassword(cs);

        Assert.DoesNotContain("sup3rs3cret", redacted);
        Assert.Contains("db.internal", redacted);
        Assert.Contains("regionhr", redacted);
        Assert.Contains("svc", redacted);
    }

    [Fact]
    public void RedactPassword_InvalidString_LeaksNothing()
    {
        var redacted = StartupDatabaseGuard.RedactPassword("this is not a valid=connection=string===");

        Assert.DoesNotContain("secret", redacted);
    }

    [Fact]
    public void CanReachPostgres_UnreachableTarget_ReturnsFalseWithError()
    {
        // Port 1 på loopback → connection refused direkt (kort timeout håller testet snabbt).
        const string cs = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Command Timeout=1";

        var reachable = StartupDatabaseGuard.CanReachPostgres(cs, out var error);

        Assert.False(reachable);
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildFatalNoDatabaseException_ContainsEnvAndRedactedTarget()
    {
        const string cs = "Host=db.internal;Database=regionhr;Username=svc;Password=topsecret";

        var ex = StartupDatabaseGuard.BuildFatalNoDatabaseException("Production", cs, cause: null);

        Assert.Contains("Production", ex.Message);
        Assert.Contains("db.internal", ex.Message);
        Assert.DoesNotContain("topsecret", ex.Message);
    }
}
