# Wave 8 — Integrationsramverk + övervakning (key=integrationsramverk)

Health Connect-kompatibelt fundament: register över alla integrationer, en jobb-runner
(genererar fil → skriver till drop-katalog via `ISftpTransport` → loggar utfall) och en
övervakningssida med manuell omkörning. Skarp SFTP/WS/REST-transport till integrationsmotorn
Health Connect är **config-ready men EJ live** — den kräver endast endpoint/nycklar, inget
betalt avtal för själva ramverket.

## Nya filer

### Modul (`src/Modules/IntegrationHub/Framework/`) — abstraktioner + register
- `IntegrationEnums.cs` — `IntegrationDirection`, `IntegrationTransport`, `IntegrationRunStatus`
- `IntegrationDefinition.cs` — record som beskriver en integration
- `IntegrationRegistry.cs` — statiskt register över **27** integrationer (Skatteverket AGI/Navet,
  Nordea, KPA, Skandia, AFA, FK/SSBTEK, SCB x2, SKR, Platsbanken, KOLL/HOSP, KOLL RÖL-katalog,
  HSA, Kronofogden, fackförbund, Raindance, SIE, Entra SCIM, Diver, Power BI, Epassi, Grade,
  MinKompetens, Microweb, Troman, HC-manifest)
- `ISftpTransport.cs` — transportabstraktion + `SftpLeveransResultat` + `SftpTransportOptions`
  (host/port/nyckel-fält förberedda men oanvända av lokal drop)
- `IIntegrationJob.cs` — plug-in-kontrakt för körbara jobb + `IntegrationJobKontext`/`IntegrationJobResultat`
- `IIntegrationRunLogStore.cs` — persistensabstraktion + `IntegrationKorningsResultat` (EF-fri DTO)

### Infrastruktur (`src/Infrastructure/Integrations/Framework/`)
- `IntegrationRunLog.cs` — EF-entitet (schema `integration_hub`, tabell `integration_run_log`) + mapp DTO↔entitet
- `LocalFileDropSftpTransport.cs` — `ISftpTransport`, skriver till utkatalog, path-traversal-säkrad, `ArSkarp=false`
- `EfIntegrationRunLogStore.cs` — EF-backad store via `db.Set<IntegrationRunLog>()`
- `IntegrationJobRunner.cs` — kör en integration end-to-end, fångar fel, loggar (`Lyckad`/`Misslyckad`/`SaknarJobb`)
- `HealthConnectManifestJob.cs` — inbyggt referensjobb (CSV-manifest av registret), fungerar utan externa system

### Konfiguration
- `src/Infrastructure/Persistence/Configurations/Integrations/IntegrationRunLogConfiguration.cs`
  — auto-registreras via `ApplyConfigurationsFromAssembly` (ingen ändring i RegionHRDbContext)

### Web
- `src/Web/Components/Pages/Integrationer/Oversikt.razor` — `/integrationer/oversikt`
  (register-tabell med riktning/transport/motpart/frekvens/senaste status, summeringskort,
  transport- + outbox-status, "Kör"-knapp per rad)

### Tester
- `tests/IntegrationHub.Tests/Framework/IntegrationRegistryTests.cs` (10 fall)
- `tests/RegionHR.Infrastructure.Tests/Integrations/Framework/IntegrationJobRunnerTests.cs`
  (11 fall: runner lyckad/saknar-jobb/fel/okänd nyckel, faktiskt filinnehåll m. `Personnummer.CreateValidated`,
  lokal fil-drop + path-traversal, manifest-CSV)

## KRÄVS AV ORKESTRERAREN (förbjudna filer — lägg in som snuttar)

### DI — `src/Infrastructure/DependencyInjection.cs`, i `AddInfrastructure(...)` efter HSA-blocket (~rad 100)
```csharp
// ── Integrationsramverk (Health Connect-kompatibelt) — våg 8 ────────────────
// Lokal fil-drop. Skarp SFTP är config-ready (fyll i Host/Anvandarnamn/PrivatNyckelSokvag
// och byt ISftpTransport-implementation) men kräver INGET betalt avtal.
services.AddSingleton(new RegionHR.IntegrationHub.Framework.SftpTransportOptions
{
    LokalDropKatalog = System.IO.Path.Combine(AppContext.BaseDirectory, "integration-drop")
});
services.AddSingleton<RegionHR.IntegrationHub.Framework.ISftpTransport,
                      RegionHR.Infrastructure.Integrations.Framework.LocalFileDropSftpTransport>();
services.AddScoped<RegionHR.IntegrationHub.Framework.IIntegrationRunLogStore,
                   RegionHR.Infrastructure.Integrations.Framework.EfIntegrationRunLogStore>();
// Referensjobb som fungerar direkt. Andra agenters self-contained generatorer
// registreras som ytterligare IIntegrationJob här när de kopplas in.
services.AddScoped<RegionHR.IntegrationHub.Framework.IIntegrationJob,
                   RegionHR.Infrastructure.Integrations.Framework.HealthConnectManifestJob>();
services.AddScoped(sp => new RegionHR.Infrastructure.Integrations.Framework.IntegrationJobRunner(
    sp.GetServices<RegionHR.IntegrationHub.Framework.IIntegrationJob>(),
    sp.GetRequiredService<RegionHR.IntegrationHub.Framework.ISftpTransport>(),
    sp.GetRequiredService<RegionHR.IntegrationHub.Framework.IIntegrationRunLogStore>()));
```
(Runner registreras via fabrik för att slippa kräva en `TimeProvider`-registrering — den valfria
konstruktorparametern faller tillbaka på `TimeProvider.System`.)

### NavMenu (VALFRITT) — `src/Web/Components/Layout/NavMenu.razor`, i HR-blocket efter `/admin/drift-status`
```razor
<MudNavLink Href="/integrationer/oversikt" Icon="@Icons.Material.Filled.Sync">Integrationsöversikt</MudNavLink>
```

## Route policy
Ingen ändring behövs. `/integrationer/oversikt` matchar den befintliga prefix-regeln
`("/integrationer", HrAdmin)` i `RouteAccessPolicy.cs` → redan HR/Admin-skyddad.

## Anknytning till Outbox
`IntegrationRunLog` kompletterar `OutboxMessage`: run-loggen är en revisionslogg **per körning**
(vad genererades, antal poster, vart det levererades, ev. fel); outboxen är den pålitliga
**leveranskön**. Översiktssidan visar båda (outbox pending/failed/dead-letter-räknare).

## Build-risk
Låg. Inga rörda delade filer, ingen `*.csproj`-ändring (nya filer auto-inkluderas i SDK-projekt).
Ny EF-entitet auto-registreras via IEntityTypeConfiguration; DB wipas vid redeploy (EnsureCreated).
Byggde ej lokalt (regel). Enda runtime-beroendet är DI-snutten ovan — utan den kastar sidan vid
resolve av `IntegrationJobRunner`/`ISftpTransport` (kompileringen påverkas ej).
