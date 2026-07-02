using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RegionHR.Infrastructure.Persistence;

namespace RegionHR.Infrastructure.Diagnostics;

/// <summary>
/// Health check för persistenslagret. Verifierar tre saker, i denna ordning:
///
/// 1. Att providern INTE är InMemory. En InMemory-databas betyder att lönedata
///    ligger i flyktigt minne — det är aldrig ett godkänt drifttillstånd, så
///    detta rapporteras som <see cref="HealthStatus.Unhealthy"/> (readiness ska
///    faila). Detta speglar uppstartskravet: prod får aldrig köra på InMemory.
/// 2. Att PostgreSQL faktiskt går att ansluta till.
/// 3. Att kärnschemat existerar (systemet använder EnsureCreated, inte
///    migrationer, så vi ankrar på att kärntabellen Employees går att fråga).
///
/// Registreras med taggen "ready" så att /health/ready gatar produktionstrafik
/// på ett friskt persistenslager.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegionHRDbContext>();
        var provider = db.Database.ProviderName ?? "okänd";

        // 1. InMemory = lönedata i flyktigt minne. Aldrig "ready" i drift.
        if (db.Database.IsInMemory())
        {
            return HealthCheckResult.Unhealthy(
                "Databasen körs i minnet (InMemory) — lönedata persisteras inte. " +
                "Otillåtet drifttillstånd; endast avsett för lokal utveckling.",
                data: BuildData(provider, canConnect: true, schemaPresent: false));
        }

        try
        {
            // 2. Går PostgreSQL att nå?
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy(
                    "PostgreSQL svarar inte (CanConnect=false).",
                    data: BuildData(provider, canConnect: false, schemaPresent: false));
            }

            // 3. Finns kärnschemat? En saknad tabell kastar (Npgsql 42P01) och
            //    fångas nedan. Take(1) håller frågan billig.
            _ = await db.Employees.AsNoTracking().Take(1).AnyAsync(cancellationToken);

            return HealthCheckResult.Healthy(
                "PostgreSQL OK — anslutning och kärnschema verifierat.",
                data: BuildData(provider, canConnect: true, schemaPresent: true));
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL otillgänglig eller kärnschemat saknas " +
                "(kör EnsureCreated/migrationer).",
                exception: ex,
                data: BuildData(provider, canConnect: false, schemaPresent: false));
        }
    }

    private static IReadOnlyDictionary<string, object> BuildData(
        string provider, bool canConnect, bool schemaPresent)
        => new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["canConnect"] = canConnect,
            ["schemaPresent"] = schemaPresent,
        };
}
