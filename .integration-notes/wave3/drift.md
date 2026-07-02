# Våg 3 — Slice `drift` (Drifthärdning)

Integrationsnoteringar för integratören. Allt som rör förbjudna filer levereras här
som klistringsbara snuttar.

## Nya filer (mina)
- `src/Infrastructure/Diagnostics/StartupDatabaseGuard.cs` — testbar uppstartsvakt
  (InMemory-fallback endast i Development, annars hård-fail).
- `src/Infrastructure/Diagnostics/DatabaseHealthCheck.cs` — `IHealthCheck`
  (InMemory→Unhealthy, DB-anslutning + kärnschema).
- `src/Web/Components/Pages/Admin/DriftStatus.razor` — sida `/admin/drift-status`.
- `docs/drift-reservloneplan.md` — reservlöneplan/DR.
- `tests/RegionHR.Infrastructure.Tests/Diagnostics/StartupDatabaseGuardTests.cs`
- `tests/RegionHR.Infrastructure.Tests/Diagnostics/DatabaseHealthCheckTests.cs`

---

## 1. PACKAGE REFS (KRÄVS — annars bygger inte Infrastructure)

`DatabaseHealthCheck` implementerar `IHealthCheck` som ligger i paketet
`Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`. Infrastructure har
det INTE idag (endast Web via ramverket). Lägg till:

**`Directory.Packages.props`** — i `<ItemGroup>`:
```xml
<PackageVersion Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" Version="9.0.4" />
```

**`src/Infrastructure/RegionHR.Infrastructure.csproj`** — i första `<ItemGroup>` (paket):
```xml
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" />
```
Testprojektet får typerna transitivt via projektreferensen till Infrastructure —
ingen ändring i `tests/RegionHR.Infrastructure.Tests/*.csproj` behövs.

---

## 2. PROGRAM.CS CHANGES (`src/Web/Program.cs`) — exakta ersättningar

### 2a. Usings (rad 5) — byt ut `RegionHR.Web.Health`
> Måste bytas ut, inte bara läggas till: annars blir `DatabaseHealthCheck`
> tvetydig mellan `RegionHR.Web.Health` och `RegionHR.Infrastructure.Diagnostics`.

FÖRE:
```csharp
using RegionHR.Web.Health;
```
EFTER:
```csharp
using RegionHR.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
```

### 2b. InMemory-fallbacken (rad 44–56) — hård-faila utanför Development
FÖRE:
```csharp
var useInMemory = false;
try
{
    using var testConn = new Npgsql.NpgsqlConnection(connectionString);
    testConn.Open();
    testConn.Close();
}
catch
{
    useInMemory = true;
    Console.WriteLine("PostgreSQL unavailable — using InMemory database with seed data.");
}
builder.Services.AddInfrastructure(connectionString, useInMemory);
```
EFTER:
```csharp
// Startup-vakt: endast Development får falla tillbaka på InMemory. Alla andra
// miljöer hård-failar hellre än att tyst servera en tom InMemory-databas
// (lönedata får aldrig hamna i flyktigt minne).
var useInMemory = false;
if (!StartupDatabaseGuard.CanReachPostgres(connectionString, out var dbError))
{
    if (StartupDatabaseGuard.AllowInMemoryFallback(builder.Environment.EnvironmentName))
    {
        useInMemory = true;
        Console.Error.WriteLine("============================================================");
        Console.Error.WriteLine("  VARNING: PostgreSQL är otillgänglig.");
        Console.Error.WriteLine("  Startar med InMemory-databas (endast Development).");
        Console.Error.WriteLine("  INGEN data persisteras — allt försvinner vid omstart.");
        Console.Error.WriteLine("============================================================");
    }
    else
    {
        var fatal = StartupDatabaseGuard.BuildFatalNoDatabaseException(
            builder.Environment.EnvironmentName, connectionString, dbError);
        Console.Error.WriteLine(fatal.Message);
        throw fatal;
    }
}
builder.Services.AddInfrastructure(connectionString, useInMemory);
```

### 2c. Health-check-registrering (rad 61–63)
FÖRE:
```csharp
// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql");
```
EFTER:
```csharp
// Health checks — DB-anslutning + kärnschema. Taggen "ready" gatar readiness.
string[] readinessTags = { "ready", "db" };
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: readinessTags);
```
(`DatabaseHealthCheck` refererar nu `RegionHR.Infrastructure.Diagnostics`-versionen.)

### 2d. Endpoints (rad 122) — liveness + readiness
FÖRE:
```csharp
app.MapHealthChecks("/health");
```
EFTER:
```csharp
// Liveness: processen lever (kör inga checks → 200 så länge appen svarar).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
// Readiness: DB nås + kärnschema finns (InMemory rapporteras som ohälsosam).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
```

---

## 3. NAV-POST (valfritt) — `src/Web/Components/Layout/NavMenu.razor`
Sidan nås redan via URL. För admin-menyn (i Admin/System-gruppen):
```razor
<MudNavLink Href="/admin/drift-status" Icon="@Icons.Material.Filled.MonitorHeart">Driftstatus</MudNavLink>
```

## 4. Städning (valfritt, ej byggkritiskt)
`src/Web/Health/DatabaseHealthCheck.cs` är nu ersatt av
`Infrastructure.Diagnostics.DatabaseHealthCheck` och kan tas bort. Bygget klarar
sig även om filen ligger kvar (den blir oanvänd, men ger ingen varning) — så länge
`using RegionHR.Web.Health;` tas bort ur Program.cs enligt 2a.

## 5. Beteende efter integration
- `ASPNETCORE_ENVIRONMENT=Production` + DB nere → appen loggar `FATALT …` och
  avslutar (startar EJ på tom InMemory).
- `Development` + DB nere → startar på InMemory med tydlig varningsbanner.
- `GET /health` → 200 om processen lever (liveness).
- `GET /health/ready` → 200 om PostgreSQL nås + kärnschema finns; annars 503.
  InMemory ger 503 (medvetet — dokumenterat i testerna).
- `/admin/drift-status` visar hälsan i klartext.
