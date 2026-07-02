# Slice: hsa — HSA-katalogen (struktur + adapter)

Bygger HSA-integrationen **konfigurationsklar men ärligt märkt som demo**. Ingen skarp
Inera-anslutning görs — sandbox-adaptern genererar deterministiska demo-HSA-id lokalt.

## Nya filer

- `src/Infrastructure/Integrations/HSA/IHsaCatalogAdapter.cs` — adapter-interface (status, uppslag enhet/person, hämta org-träd).
- `src/Infrastructure/Integrations/HSA/HsaDtos.cs` — DTO:er: `HsaUnit`, `HsaPerson`, `HsaConnectionStatus`, `HsaSyncResult`, enum `HsaUnitKind`.
- `src/Infrastructure/Integrations/HSA/SandboxHsaCatalogAdapter.cs` — DEMO-impl (IsSandbox=true, deterministiskt FNV-1a-baserat demo-HSA-id, fiktivt org-träd).
- `src/Infrastructure/Integrations/HSA/HsaCatalogSyncService.cs` — synk-tjänst. `SynkaAsync()` läser DB + sparar; `SynkaEntiteterAsync(...)` = ren, testbar synk på inlästa entiteter. Idempotent (rör bara enheter/personer som saknar HsaId).
- `src/Web/Components/Pages/Integrationer/Hsa.razor` — sida `/integrationer/hsa`: status, "Kör demo-synk"-knapp, enhets-/HSA-id-tabell, demo-org-trädförhandsvy.
- `tests/RegionHR.Infrastructure.Tests/Integrations/HSA/SandboxHsaCatalogAdapterTests.cs` — kontrakts-test mot sandbox.
- `tests/RegionHR.Infrastructure.Tests/Integrations/HSA/HsaCatalogSyncServiceTests.cs` — synk-logik (idempotens, tomma listor, full koppling).

## Ändrade filer (endast additivt)

- `src/Modules/Core/Domain/OrganizationUnit.cs` — nytt nullable-fält `public string? HsaId { get; private set; }` + metod `SattHsaId(string?)`.
- `src/Modules/Core/Domain/Employee.cs` — nytt nullable-fält `public string? HsaId { get; private set; }` + metod `SattHsaId(string?)`.
- `src/Web/Components/Pages/Integrationer/Index.razor` — en additiv rad i tabellen "Externa integrationer" som länkar till `/integrationer/hsa` (status: Demo).

## KRÄVS AV INTEGRATÖREN

### 1. DI-registrering — `src/Infrastructure/DependencyInjection.cs`

Lägg i `AddInfrastructure(...)` (t.ex. i integrationer-blocket runt rad 96–116):

```csharp
// HSA-katalogen (Inera) — DEMO/sandbox. Byt ut SandboxHsaCatalogAdapter mot skarp
// adapter (Inera-avtal + SITHS-cert + WS/LDAP-endpoint) när det finns.
services.AddSingleton<RegionHR.Infrastructure.Integrations.HSA.IHsaCatalogAdapter,
                      RegionHR.Infrastructure.Integrations.HSA.SandboxHsaCatalogAdapter>();
services.AddScoped<RegionHR.Infrastructure.Integrations.HSA.HsaCatalogSyncService>();
```

Sidan `Hsa.razor` `@inject`:ar `IHsaCatalogAdapter` + `HsaCatalogSyncService` + befintlig `IDbContextFactory<RegionHRDbContext>` — utan registreringen ovan kraschar sidan i runtime.

### 2. NavMenu — `src/Web/Components/Layout/NavMenu.razor`

Lägg en post under Integrationer-gruppen (bredvid befintliga `/integrationer` och `/integrationer/platsbanken`):

```razor
<MudNavLink Href="/integrationer/hsa" Icon="@Icons.Material.Filled.AccountTree">HSA-katalogen</MudNavLink>
```

(Matcha exakt hur syskonlänkarna är skrivna i filen — grupp/roll-filter.)

### 3. (VALFRITT) EF-kolumnnamn — CoreHR-konfigurationer

`HsaId` mappas automatiskt av EF-konvention som nullable text-kolumn `"HsaId"` (ingen global snake_case-konvention finns). DB byggs via `EnsureCreatedAsync` och wipas vid redeploy, så additivt nullable-fält funkar utan migration. **Ingen åtgärd krävs.** Vill man ha snake_case-namn för konsekvens, lägg i respektive config:

- `src/Infrastructure/Persistence/Configurations/CoreHR/OrganizationUnitConfiguration.cs`:
  `builder.Property(e => e.HsaId).HasColumnName("hsa_id").HasMaxLength(64);`
- `src/Infrastructure/Persistence/Configurations/CoreHR/EmployeeConfiguration.cs`:
  `builder.Property(e => e.HsaId).HasColumnName("hsa_id").HasMaxLength(64);`

Ingen ny DbSet behövs (bara kolumner).

## Vad som krävs för SKARP HSA-anslutning (ej gjort — medvetet)

Demo-adaptern gör INGEN nätverksanslutning. En produktionsadapter (som ersätter
`SandboxHsaCatalogAdapter` bakom `IHsaCatalogAdapter`) kräver:

- **Inera-avtal / kundanslutning** till HSA (Hälso- och sjukvårdens adressregister).
- **SITHS-funktionscertifikat** (klientcert) för mTLS-autentisering mot Ineras tjänster.
- **HSA-endpoint**: antingen HSA WS (SOAP, t.ex. HSA WS Collaboration Engine / VPTjänst) över TLS, eller LDAPS mot HSA-katalogen.
- Konfiguration (endpoint-URL, cert-sökväg/thunbprint, sök-bas-DN) — läggs lämpligen i `appsettings` + en `HsaOptions`-klass.
- Mappningsregler enhet↔HSA-id (t.ex. via kostnadsställe/orgnr) för produktionssynk.

## Byggrisk

Låg. Additiva nullable-fält + nya filer. Inga delade filer ändrade utom en additiv rad i
`Integrationer/Index.razor`. Inga signaturändringar på befintliga publika API:er.
Enda runtime-beroendet är DI-registreringen ovan.
