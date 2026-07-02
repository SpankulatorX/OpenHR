using Npgsql;

namespace RegionHR.Infrastructure.Diagnostics;

/// <summary>
/// Startup-validering av databasanslutningen.
///
/// Bakgrund: den ursprungliga uppstartslogiken föll TYST tillbaka på en
/// InMemory-databas i ALLA miljöer när PostgreSQL var otillgänglig. Det innebar
/// att systemet kunde starta och servera en tom databas i produktion — lönedata
/// skulle då hamna i flyktigt minne och försvinna vid omstart, utan att någon
/// märkte det förrän skadan var skedd.
///
/// Den här vakten centraliserar (och gör testbar) beslutet: endast Development
/// får falla tillbaka på InMemory; alla andra miljöer ska hård-faila.
/// </summary>
public static class StartupDatabaseGuard
{
    /// <summary>
    /// Sant endast för miljön Development. Alla andra miljöer (Production,
    /// Staging, egendefinierade) måste hård-faila hellre än att servera en tom
    /// InMemory-databas. Case-insensitivt så att "development"/"DEVELOPMENT" också
    /// tolkas rätt.
    /// </summary>
    public static bool AllowInMemoryFallback(string? environmentName)
        => string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Öppnar och stänger en testanslutning mot PostgreSQL. Returnerar true om
    /// anslutningen lyckades, annars false med felet i <paramref name="error"/>.
    /// Ingen exception läcker ut — anroparen får själv besluta vad som ska hända.
    /// </summary>
    public static bool CanReachPostgres(string connectionString, out Exception? error)
    {
        try
        {
            using var testConn = new NpgsqlConnection(connectionString);
            testConn.Open();
            testConn.Close();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// Maskerar lösenordet i en anslutningssträng så att den kan loggas utan att
    /// läcka credentials. Host/port/databas/användarnamn behålls för felsökning.
    /// </summary>
    public static string RedactPassword(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
            {
                builder.Password = "***";
            }
            return builder.ConnectionString;
        }
        catch
        {
            // Om strängen inte kan tolkas: läck absolut ingenting.
            return "(ogiltig eller otolkbar anslutningssträng)";
        }
    }

    /// <summary>
    /// Bygger det fatala undantag som ska kastas när PostgreSQL saknas i en
    /// miljö som INTE tillåter InMemory-fallback. Innehåller en tydlig,
    /// åtgärdbar text och det maskerade anslutningsmålet.
    /// </summary>
    public static InvalidOperationException BuildFatalNoDatabaseException(
        string? environmentName, string connectionString, Exception? cause)
        => new(
            $"FATALT: PostgreSQL är otillgänglig i miljön '{environmentName}'. " +
            $"Anslutningsmål: {RedactPassword(connectionString)}. " +
            "Startar INTE med InMemory-fallback utanför Development — lönedata får " +
            "aldrig tyst hamna i flyktigt minne. Åtgärda databasanslutningen och starta om.",
            cause);
}
