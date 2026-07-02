# Wave 2 — Slice `las` (LAS skrivväg + HR-varningar)

## Vad som gjordes
1. **EF-repo** `ILASRepository` implementerad: `src/Infrastructure/LAS/LASRepository.cs`.
   - Self-persisterar (SaveChanges i Add/Update) så att LASService kan anropas direkt från
     Blazor-sidan utan IUnitOfWork och utan att ändra LASService konstruktorsignatur.
   - Hårdgjord borttagning av perioder: markerar frånkopplade barn (LASPeriod/LASEvent) som
     Deleted även om FK:n är valfri, så HR-korrigeringar faktiskt raderas (ingen föräldralös rad).
2. **HR skrivväg** i `LASService` (nya metoder, inga befintliga signaturer ändrade):
   `RegistreraPeriodAsync(..., Guid? utfortAvEmployeeId, string utfortAvNamn, ct)` (overload),
   `KorrigeraPeriodAsync`, `TaBortPeriodAsync`, `KonverteraTillTillsvidareAsync`, `BeviljaForetradesrattAsync`.
   Alla har attestkontroll (kastar `InvalidOperationException` vid självattest).
3. **Domän** `LASAccumulation`: nya metoder `TaBortPeriod`, `AndraPeriod`, `KonverteraTillTillsvidare`
   (nollställer konverteringsdatum om dagar faller under gräns vid korrigering).
4. **Ren regelmotor** `src/Modules/LAS/Services/LASAlertRegler.cs`: trösklar (SAVA 300/330/350/360
   via "dagar kvar till gräns", vikariat skalas mot 730) + `ValjMottagare` (HR + chef, aldrig den anställde).
5. **LASAlertService** (bakgrundsjobb) implementerad: kör var 12:e h, skapar notifieringar till
   **HR och chef (inte den anställde)** via Notifications-tabellen med dedup per nivå+tröskel.
6. **UI** `src/Web/Components/Pages/LAS/Index.razor`: HR-only registrera/korrigera/ta bort/konvertera/
   företrädesrätt. Icke-HR ser läsvy. Wire via DI (`LASService`, `AuthService`).
7. **Tester** i `tests/LAS.Tests/` (nya filer): `LASAlertReglerTests.cs`, `LASKorrigeringKonverteringTests.cs`.

## DI-registreringar (KRÄVS — annars kastar /las-sidan runtime DI-fel)
I `src/Infrastructure/DependencyInjection.cs`, i "Repositories"-blocket (efter rad ~82):
```csharp
// LAS
services.AddScoped<RegionHR.LAS.Services.ILASRepository, RegionHR.Infrastructure.LAS.LASRepository>();
services.AddScoped<RegionHR.LAS.Services.LASService>();
```
`LASAlertService` är redan registrerad som `AddHostedService<LASAlertService>()` (rad ~184) — dess
konstruktor bytte från `(ILogger)` till `(IServiceScopeFactory, ILogger)`, vilket DI auto-löser.
**Ingen** DI-ändring behövs för hosted-servicen.

## DbSets / entiteter
Inga nya. Använder befintliga `DbSet<LASAccumulation> LASAccumulations`, `Notifications`,
`Employees`, `OrganizationUnits`. LASConfiguration.cs oförändrad (rördes ej).

## Signaturändringar
- `LASAlertService` ctor: `(ILogger<LASAlertService>)` → `(IServiceScopeFactory, ILogger<LASAlertService>)`.
  Endast via `AddHostedService` → transparent för DI.
- `LASAccumulation`: nya publika metoder (additivt): `TaBortPeriod`, `AndraPeriod`, `KonverteraTillTillsvidare`.
- `LASService`: nya publika metoder (additivt, se ovan). Befintlig `RegistreraPeriodAsync(5-param)` orörd.
- `ILASRepository`: **oförändrad** (befintlig `InMemoryLASRepository` i testerna fungerar vidare).

## Notifierings-routing (viktigt för demo)
HR-mottagare hittas via **befattning som innehåller "HR"** → i seed = **Eva Nilsson ("HR-chef")**.
Chef hittas via `OrganizationUnit.ChefId` — men seed anropar aldrig `TilldelaChef`, så chefsledet är
tomt i demo. Larmen går alltså i nuläget till Eva Nilsson. Demo-login "HR" är däremot Karl Berg
(befattning "Sjukskoterska"), så Karl ser inte larmen med nuvarande seed.

### Valfritt seed-snitt för att göra demon tydlig (`SeedData.cs`)
Tilldela chef på enheterna så att chefsledet också larmas (Eva = HR-chef som chef för sjukhuset):
```csharp
// efter att employees[] finns och units skapats:
sjukhus.TilldelaChef(evaNilssonEmployee.Id); // ger chef-mottagare för anställda på sjukhuset
```
(Alternativt: sätt Karl Bergs befattning till något som innehåller "HR" om man vill att demo-HR-login
ska vara notifieringsmottagare.)

## Notifieringsdesign
Skapar `Notification`-entiteter direkt (samma mönster som `NotificationReminderService.CheckLASWarnings`),
med `RelatedEntityType = "LAS-HRAlert-{Niva}-{TroskelDagar}"` + `RelatedEntityId = accumulationId` för
dedup inom 20h. `INotificationService.SendAsync` exponerar inte relatedEntity-fälten, därför direkt-add.

OBS: befintliga `NotificationReminderService.CheckLASWarnings` (delad fil, rördes ej) skickar en
LAS-varning till **den anställde** (`las.AnstallId.Value`). Det är den felroutade varning slicen ersätter.
Integratören bör överväga att ta bort/omdirigera den metoden så att LAS-varningar bara går via
LASAlertService (HR/chef). Olika RelatedEntityType → ingen krock, men annars dubbla notiser.

## Build-risk
- LÅG kompileringsrisk: inga delade filer ändrade, inga csproj-ändringar (SDK-glob tar nya filer).
- RUNTIME: /las kräver DI-snittet ovan. Utan det: DI-fel vid sidladdning (appen bygger ändå).
