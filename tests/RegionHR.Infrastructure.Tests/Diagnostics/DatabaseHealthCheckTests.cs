using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RegionHR.Infrastructure.Diagnostics;
using RegionHR.Infrastructure.Persistence;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Diagnostics;

public class DatabaseHealthCheckTests
{
    private static (DatabaseHealthCheck check, ServiceProvider provider) BuildInMemoryCheck()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RegionHRDbContext>(options =>
            options.UseInMemoryDatabase($"HealthCheck-{Guid.NewGuid()}"));
        var provider = services.BuildServiceProvider();

        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());
        return (check, provider);
    }

    private static HealthCheckContext ContextFor(DatabaseHealthCheck check)
        => new()
        {
            Registration = new HealthCheckRegistration("database", check, HealthStatus.Unhealthy, tags: null)
        };

    [Fact]
    public async Task InMemoryProvider_IsReportedUnhealthy_ForProductionRequirement()
    {
        // Dokumenterat drifthärdnings-krav: en InMemory-databas betyder att lönedata
        // ligger i flyktigt minne. Readiness ska då faila (Unhealthy → HTTP 503),
        // så att en produktionsinstans aldrig tas i trafik på InMemory.
        var (check, provider) = BuildInMemoryCheck();
        await using var _ = provider;

        var result = await check.CheckHealthAsync(ContextFor(check));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task InMemoryProvider_ExposesProviderNameInData()
    {
        var (check, provider) = BuildInMemoryCheck();
        await using var _ = provider;

        var result = await check.CheckHealthAsync(ContextFor(check));

        Assert.True(result.Data.ContainsKey("provider"));
        var providerName = Assert.IsType<string>(result.Data["provider"]);
        Assert.Contains("InMemory", providerName);
        Assert.False((bool)result.Data["schemaPresent"]);
    }
}
