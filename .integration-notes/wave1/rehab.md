# Integrationsnoteringar — slice `rehab`

Rehabkedjan förankrad i sjukfallets dag 1 + automatisk rehab-triggning + UI för uppföljning.

## Sammanfattning av ändringen
- `RehabCase` har nytt fält **`SjukfallDag1` (DateOnly?)**. ALLA milstolpar (dag 14/90/180/365)
  beräknas nu från detta datum, inte från ärendets skapandedatum.
- Ny överlagring `RehabCase.Skapa(EmployeeId, RehabTrigger, DateOnly sjukfallDag1)` +
  `RehabCase.SattSjukfallDag1(DateOnly)`. Gamla 2-arg `Skapa(EmployeeId, RehabTrigger)`
  och `SkapaForSeed(...)` är **oförändrade signaturer** (bakåtkompatibla).
- Den döda automatiska triggningen är implementerad: `IRehabRepository` + `ISickLeaveDataProvider`
  har nu implementationer, och ett hosted background job (`RehabAutoTriggerService`) skapar
  rehabärenden automatiskt vid tröskel (14 sammanhängande sjukdagar / 6 tillfällen på 12 mån),
  förankrat i sjukfallets dag 1. Idempotent — ingen dubblett om aktivt ärende finns.
- Nytt UI: `/halsosam/{id}/uppfoljning` för att registrera genomförd uppföljning + korrigera dag 1.

## 1. DI-registreringar (lägg i `AddInfrastructure` i `src/Infrastructure/DependencyInjection.cs`)

Lägg dessa `using`-rader högst upp i filen om de saknas:
```csharp
using RegionHR.HalsoSAM.Services;
using RegionHR.Infrastructure.HalsoSAM;
```

Lägg registreringarna i tjänsteblocket (t.ex. nära övriga `AddScoped` runt rad 160), OBLIGATORISKT för slicen:
```csharp
// HälsoSAM — rehabkedja + automatisk triggning
services.AddScoped<IRehabRepository, RehabRepository>();
services.AddScoped<RehabService>();
services.AddScoped<SickLeaveMonitor>();
services.AddScoped<SickLeaveNotificationDataProvider>();
```

Lägg hosted service bland övriga `AddHostedService` (runt rad 166-170):
```csharp
services.AddHostedService<RehabAutoTriggerService>();
```

VALFRITT (aktiverar sjukfrånvarostatistik-tjänsten om någon sida vill använda den):
```csharp
services.AddScoped<ISickLeaveDataProvider, SickLeaveNotificationDataProvider>();
services.AddScoped<SickLeaveStatisticsService>();
```

## 2. Databas — nytt fält (VIKTIGT)
`RehabCase.SjukfallDag1` blir en ny kolumn (`date`, nullable) på tabellen `RehabCases`.
Mappas automatiskt via konvention (ingen ny `IEntityTypeConfiguration` behövs, ingen jsonb —
`DateOnly` är skalärt, precis som `SickLeaveNotification.StartDatum`).

Appen använder `EnsureCreatedAsync()` (INTE `Migrate()`), så:
- **Färsk DB:** kolumnen skapas automatiskt — inget att göra.
- **Befintlig dev-DB (Postgres):** släpp och återskapa databasen, ELLER kör manuellt:
  ```sql
  ALTER TABLE halsosam."RehabCases" ADD COLUMN "SjukfallDag1" date NULL;
  -- (om tabellen ligger i public: ALTER TABLE "RehabCases" ...)
  ```
  Annars kastar queries mot `RehabCases` "column does not exist".
- Ingen EF-migration krävs för runtime. Om ni ändå vill hålla migrations-snapshotten i synk:
  generera en migration `AddSjukfallDag1` (jag har INTE rört migrations- eller snapshot-filer).

## 3. DbSet
Inga nya DbSet behövs. `RehabCases` och `SickLeaveNotifications` finns redan.

## 4. NavMenu
Ingen ny nav-post krävs. Uppföljningssidan nås via knappen "Registrera uppföljning" i det
expanderade ärendet på `/halsosam`. (Valfritt: lägg en genväg om ni vill.)

## 5. Seed-data (VALFRITT — demo så att auto-triggern syns direkt)
Klistras i `SeedData.SeedAsync` FÖRE `SaveChanges`, efter rehab-seed-blocket. Skapar ett
pågående långtidssjukfall för en anställd UTAN aktivt rehabärende (employees[0]); nästa körning
av `RehabAutoTriggerService` skapar då automatiskt ett rehabärende förankrat i dag 1:
```csharp
// Demo: pågående långtidssjukfrånvaro (21 dagar) som auto-triggern fångar upp.
var demoSjukfall = RegionHR.Leave.Domain.SickLeaveNotification.Skapa(
    employees[0].Id.Value, DateOnly.FromDateTime(DateTime.Today.AddDays(-20)));
demoSjukfall.UppdateraDag(21);
db.SickLeaveNotifications.Add(demoSjukfall);
```

## 6. Paket
Inga nya NuGet-paket.

## 7. Signaturändringar som andra kan behöva känna till
- Inga brytande ändringar. Enbart TILLLAGDA överlagringar/metoder:
  - `RehabCase.Skapa(EmployeeId, RehabTrigger, DateOnly)` (ny overload)
  - `RehabCase.SattSjukfallDag1(DateOnly)`
  - `RehabCase.ArUppfoljningRegistrerad(int)`
  - `RehabCase.SjukfallDag1` (ny property, private setter)
  - `RehabService.SkapaFranSignalAsync(EmployeeId, RehabTrigger, DateOnly, ct)` (ny overload)
  - `RehabService.SkapaOmSaknasAsync / HarAktivtArendeAsync / RegistreraUppfoljningAsync / SattSjukfallDag1Async`
  - `SickLeaveMonitor.AnalyseraSignal(...)` → `RehabSignal?` (ny; `Analysera` oförändrad)
- VALFRI uppgradering (ej gjord — API:t är inte den driftsatta appen): `src/Api/Endpoints/HalsoSAMEndpoints.cs`
  `CreateRehabCase` använder fortfarande 2-arg `Skapa`. Vill man ha korrekt dag 1 även via API,
  lägg till `DateOnly SjukfallDag1` i `CreateRehabRequest` och anropa 3-arg-överlagringen.

## Filer i slicen
Nya:
- src/Modules/HalsoSAM/Domain/Rehabkedja.cs (årsversionerad lagtabell)
- src/Infrastructure/HalsoSAM/RehabRepository.cs
- src/Infrastructure/HalsoSAM/SickLeaveNotificationDataProvider.cs
- src/Infrastructure/HalsoSAM/RehabAutoTriggerService.cs
- src/Web/Components/Pages/HalsoSAM/RegistreraUppfoljning.razor
- tests/HalsoSAM.Tests/RehabMilstolpeTests.cs
- tests/HalsoSAM.Tests/RehabSignalTests.cs

Ändrade:
- src/Modules/HalsoSAM/Domain/RehabCase.cs
- src/Modules/HalsoSAM/Services/SickLeaveMonitor.cs
- src/Modules/HalsoSAM/Services/RehabService.cs
- src/Web/Components/Pages/HalsoSAM/Index.razor
- src/Web/Components/Pages/HalsoSAM/NyttArende.razor

## Verifierade lagvärden (2026, källa Försäkringskassan + Socialförsäkringsbalken)
- Rehabiliteringskedjan 90/180/365 dagar: SFB (2010:110) 27 kap. 46–49 §§.
- Plan för återgång i arbete senast dag 30 (om sjukfrånvaro antas > 60 dagar): SFB 30 kap. 6 §.
- Sjuklöneperiod 14 dagar → FK-anmälan dag 15: Lag (1991:1047) om sjuklön 12 §.
- Läkarintyg från dag 8: Lag (1991:1047) om sjuklön 8 §.
Alla kodade i `Rehabkedja` (Version = 2026), inte som magiska konstanter.
