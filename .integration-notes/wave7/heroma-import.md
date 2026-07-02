# Wave 7 — heroma-import

## Vad som byggdes
Importsteget som saknades: `HeromaAdapter.ParseAsync` (och övriga adaptrar) PARSAR en fil till
`ParsedMigrationData` men skapade tidigare inga anställda — `MigrationEngineService.ImportInBatchesAsync`
loggade bara rader med slumpade GUID:er. Nu finns en riktig importtjänst som skapar Employee + Employment
via kärnans publika API (`Employee.Skapa` / `Employee.LaggTillAnstallning`).

### Arkitektur (varför uppdelning i två delar)
- Migration-modulen refererar **bara SharedKernel** (inte Core, inte Infrastructure) och dess csproj får
  inte röras. Därför ligger parsning/validering/mappning + en persistens-abstraktion i modulen, och det
  faktiska domän-anropet + DbContext-persistensen i Web-lagret (som ser Core + Infrastructure).
- Web refererar Migration **transitivt via Infrastructure**, så Razor/DI ser `MigrationImportService`
  och DTO:erna utan ny projektreferens.

## Nya filer
- `src/Modules/Migration/Services/MigrationImportService.cs`
  - `MigrationImportService.ImporteraAsync(ParsedMigrationData, SourceSystem, filNamn, skapadAv, uppdateraDubbletter=false, ct)`
  - `IEmployeeImportSink` (persistens-abstraktion, implementeras i Web)
  - DTO:er: `EmployeeImportData`, `EmployeeEmploymentData`, `EmployeeImportOperation`, `ImportOperation`,
    `MigrationImportContext`, `SinkRadUtfall`, `SinkExekveringsResultat`, `MigrationImportResult`,
    `ImportRadResultat`, `ImportRadStatus`
  - Validerar: giltigt personnummer (`new Personnummer`), obligatoriskt för/efternamn, tolkbar
    anställningsform/kollektivavtal/lön/grad/datum. Dubblettkontroll mot DB **och** inom filen.
    Idempotent (befintligt pnr hoppas över, om inte `uppdateraDubbletter`).
- `src/Web/Services/EmployeeImportSink.cs`
  - Konkret `IEmployeeImportSink`. Skapar Employee/Employment via Core, slår upp/skapar `OrganizationUnit`
    per enhetskod (matchas mot kostnadsställe/namn), skriver allt + en `MigrationJob`-historikpost i
    **ett** `SaveChangesAsync` (atomisk transaktion). Per-rad-domänfel (t.ex. LAS-regler) fångas och
    rapporteras utan att fälla hela importen.
- `src/Web/Components/Pages/Admin/Migration/HeromaImport.razor` (rutt `/admin/migration/heroma`)
  - Ladda upp fil → auto-detektera format → förhandsgranska (maskerat pnr) → importera → resultatpanel
    (skapade/uppdaterade/hoppade/fel + per-rad-tabell). Kör-om är säkert (idempotent).
- `tests/Migration.Tests/MigrationImportServiceTests.cs`
  - 16 xUnit-tester med en in-minnes-`FakeSink`. Personnummer via `Personnummer.CreateValidated`.

## Ändrade filer
- `src/Web/Components/Pages/Admin/Migration/Index.razor` — ny knapp "Importera anställda" → `/admin/migration/heroma`.
  (Rörde INTE `/admin/drift-status` eller `/admin/avtal`.)

## Integration — kräver manuella snuttar (får ej röra dessa filer själv)

### Program.cs (src/Web/Program.cs) — lägg till bland "// Application services" (~rad 92-104)
```csharp
builder.Services.AddScoped<RegionHR.Migration.Services.IEmployeeImportSink, RegionHR.Web.Services.EmployeeImportSink>();
builder.Services.AddScoped<RegionHR.Migration.Services.MigrationImportService>();
```

### Route policy
Ingen ändring krävs. `/admin/migration/heroma` faller under befintlig prefix-regel `("/admin", HrAdmin)`
i `RouteAccessPolicy.cs` → HR/Admin. (Noteras för tydlighet.)

### DbSets / EF-konfiguration
Inga nya entiteter. Använder befintliga `Employees`, `OrganizationUnits`, `MigrationJobs`.
Inga nya kolumner. Inga nya `IEntityTypeConfiguration`.

### NuGet
Inga nya paket.

## Byggrisk
Låg. Matchar befintliga signaturer (`Employee.Skapa`, `LaggTillAnstallning`, `OrganizationUnit.Skapa`,
`MigrationJob`-livscykeln, `AnstallningService`-mönstret för `IDbContextFactory` + `(string)e.Personnummer`).
Enda kompileringsberoende utanför mina filer = de två `AddScoped`-raderna i Program.cs (annars kastar DI i
runtime när sidan öppnas, bygget går ändå). Kan inte köra `dotnet build` (fryser maskinen) — koden är
skriven mot lästa signaturer, noll varningar avsedda (TreatWarningsAsErrors).
