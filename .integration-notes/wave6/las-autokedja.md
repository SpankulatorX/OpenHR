# Wave 6 — las-autokedja (LAS auto-kedja: anställningshändelse → LAS-ackumulering)

Kopplar ihop domänevent från Core-anställningar med LAS-modulen så att HR **inte längre
behöver registrera visstidsperioder manuellt**. Tidigare fanns ingen automatisk länk
mellan att en visstidsanställning skapades/avslutades och att LAS-ackumuleringen
uppdaterades — nu driver anställningshändelserna kedjan.

Följer det befintliga domänevent-mönstret: `DomainEventInterceptor` (EF SaveChanges)
samlar events från aggregat och `DomainEventDispatcher` löser ut
`IDomainEventHandler<TEvent>` via `GetServices`. Det fanns **inga** befintliga
`IDomainEventHandler<>`-implementationer (bara interfacet + dispatchern) — dessa är de
första. Dispatchern hittar handlers **enbart** via DI-registrering av det slutna
generiska interfacet (ingen assembly-scan) → se DI nedan.

Core-domänen, `LASService`, `LASRepository` och `LASAccumulation` är **orörda**. All ny
logik ligger i nya filer och återanvänder den befintliga `LASAccumulation`-domänen (som
redan noterar konvertering vid gräns och företrädesrätt vid avslut) och `ILASRepository`.

## Filer skapade
- `src/Modules/LAS/Services/IEmploymentLookup.cs` — läsport + `AnstallningsPeriod`-record.
  `EmploymentCreatedEvent` bär bara id + form (inte start/slut), så kedjan behöver slå upp
  datumen. Porten håller LAS-modulen fri från direkt DB-/Core-beroende och är stubbbar i test.
- `src/Modules/LAS/Services/LASAutoChainService.cs` — orkestreringen (testbar utan DB):
  `RegistreraFranAnstallningAsync(EmploymentId)` och `AvslutaFranAnstallningAsync(EmployeeId, DateOnly)`
  + `LASAutoChainResult`/`LASAutoChainStatus`. Idempotent per anställnings-id (taggar
  `LASPeriod.AnstallningsId` = EmploymentId, hoppar dubbletter).
- `src/Infrastructure/LAS/EmploymentLookup.cs` — EF-implementation av `IEmploymentLookup`
  mot `RegionHRDbContext.Employments` (samma scoped context som skrev anställningen).
- `src/Infrastructure/LAS/EmploymentCreatedLASHandler.cs` — `IDomainEventHandler<EmploymentCreatedEvent>`.
- `src/Infrastructure/LAS/EmploymentEndedLASHandler.cs` — `IDomainEventHandler<EmploymentEndedEvent>`.
- `tests/LAS.Tests/LASAutoChainServiceTests.cs` — 11 xUnit-tester (skapar/ökar period,
  ignorerar tillsvidare/säsong/okänd, idempotens, konvertering över gräns, företrädesrätt vid avslut).

## Filer ändrade
- Inga. (Reglerna kräver att `DependencyInjection.cs` inte rörs → registreringen nedan.)

---

## KRÄVS — DI-registrering (src/Infrastructure/DependencyInjection.cs — får ej röras av mig)

Lägg **inuti `AddInfrastructure(...)`**, direkt efter LAS-blocket (~rad 89, efter
`services.AddScoped<RegionHR.LAS.Services.LASService>();`). `IDomainEventHandler<>` ligger i
`RegionHR.SharedKernel.Abstractions` som redan är `using`-importerat (rad 8). Övriga typer
kvalificeras fullt nedan → inga nya usings behövs.

```csharp
        // LAS auto-kedja (våg 6): anställningshändelse → LAS-ackumulering
        services.AddScoped<RegionHR.LAS.Services.IEmploymentLookup, RegionHR.Infrastructure.LAS.EmploymentLookup>();
        services.AddScoped<RegionHR.LAS.Services.LASAutoChainService>();
        services.AddScoped<IDomainEventHandler<RegionHR.Core.Domain.EmploymentCreatedEvent>,
            RegionHR.Infrastructure.LAS.EmploymentCreatedLASHandler>();
        services.AddScoped<IDomainEventHandler<RegionHR.Core.Domain.EmploymentEndedEvent>,
            RegionHR.Infrastructure.LAS.EmploymentEndedLASHandler>();
```

Utan raderna 3–4 (handler-registreringarna) körs kedjan **inte** — dispatchern hittar bara
handlers som är DI-registrerade på det slutna generiska interfacet. Raderna 1–2 är kedjans
beroenden.

## DbSet / EF
- Inga nya entiteter. Återanvänder `LASAccumulation`/`LASPeriod`/`LASEvent` (schema `las`,
  redan konfigurerade i `Configurations/LAS/LASConfiguration.cs`). `LASPeriod.AnstallningsId`
  (kolumn `anstallnings_id`, max 100) persisterar idempotensnyckeln (Guid-sträng = 36 tecken).
- `RegionHRDbContext.Employments` (rad 48) + global `EmploymentId`-converter (rad 368) räcker
  för uppslaget `e.Id == employmentId`. Inga DbContext-ändringar.

## Route/policy
- Inga nya rutter eller sidor → ingen `RouteAccessPolicy.cs`-rad. Auto-kedjan är intern
  reaktion på befintliga anställningsflöden (bakom deras egna roller).

---

## Beteende & designval (viktigt för granskning)
- **Endast SAVA + vikariat ackumuleras.** Det är formerna §5a LAS konverterar till
  tillsvidare (SAVA 365 dagar, vikariat 730 dagar) och som `LASAccumulation` modellerar
  (dess `Skapa` avvisar övriga former). Konvertering noteras automatiskt av domänen när
  gränsen passeras (`Omberakna` → `UppdateraStatus` → `LASConversionTriggeredEvent`).
- **Säsongsanställning hoppas över** (returnerar `Ignorerad`). Säsong ger företrädesrätt men
  konverteras inte enligt §5a och saknar stöd i nuvarande ackumuleringsmodell — samma
  begränsning som den manuella `RegistreraPeriodAsync`-vägen (som också går via `Skapa`).
  **Känd lucka** att åtgärda om/när `LASAccumulation` utökas med företrädesrätts-only-former.
- **Företrädesrätt** (§25 LAS) sätts vid `EmploymentEndedEvent` via `SattForetradesratt(slutDatum)`
  — domänen avgör om dagkravet (SAVA ~274 / vikariat ~365 dagar i 3-årsfönster) är uppfyllt.
- **Idempotens:** perioden taggas med EmploymentId; kommer samma händelse igen registreras
  ingen dubblettperiod (`RedanRegistrerad`).
- **Robusthet:** handlers fångar och loggar undantag (ILogger) — en LAS-bokföringsmiss får
  aldrig rulla tillbaka/fälla själva anställningsregistreringen (samma filosofi som
  automations-/webhook-stegen i `DomainEventDispatcher`).
- **Reentrant SaveChanges:** handlern skriver via `ILASRepository.Add/UpdateAsync` (som gör
  `SaveChangesAsync`) inifrån `DomainEventInterceptor.SavedChangesAsync`. Detta är samma
  beprövade mönster som `AutomationEngineService` redan använder i samma dispatch-väg;
  pass-2-interceptorn plockar bara upp `LASAccumulation`-events (ev. `LASConversionTriggeredEvent`),
  inte anställningens → ingen loop.

## Byggrisk
- Låg. Inga rörda befintliga filer, inga rörda publika signaturer. Alla nya usings används
  (TreatWarningsAsErrors-säkert). Testerna speglar exakt det befintliga mönstret i
  `LASServiceTests.cs` (återanvänder dess interna `InMemoryLASRepository`; `Assert.NotNull`
  är `[NotNull]`-annoterad i xUnit → ingen CS8602). **Kräver DI-raderna ovan för att aktiveras.**
