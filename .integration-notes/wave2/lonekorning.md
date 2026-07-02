# Slice: lonekorning — Lönekörning end-to-end med den riktiga motorn

## Sammanfattning
NyKorning.razor räknade tidigare en platt schablon (30 % skatt, 31,42 % AG rakt av). Den är nu
ersatt av den riktiga brutto-till-netto-motorn (`PayrollCalculationEngine`) via en ny
orkestrerare i Infrastructure. Underlaget (arbetade dagar, OB, övertid, jour, beredskap,
sjuk/semester/föräldraledighet, kostnadsställe) byggs ur databasen (schema + frånvaro).
Retroaktiv körning är kopplad i UI och kör `RetroactiveRecalculationEngine`. KorningDetalj
visar nu riktiga rader per anställd (brutto/skatt/netto/OB/AG/pension) med expanderbar
löneartsspecifikation.

## KRÄVS: DI-registreringar (src/Infrastructure/DependencyInjection.cs)
Lägg till i `AddInfrastructure`, i "Payroll services"-blocket (efter rad 90,
`services.AddScoped<PayrollCalculationEngine>();`). Utan dessa kastar NyKorning i runtime.

```csharp
        // Payroll batch-orkestrering (wave2 slice: lonekorning)
        services.AddSingleton<RegionHR.Infrastructure.Payroll.PayrollInputBuilder>();
        services.AddScoped<RegionHR.Payroll.Domain.RetroactiveRecalculationEngine>();
        services.AddScoped<RegionHR.Infrastructure.Payroll.PayrollBatchService>();
```

Livstider (viktigt): `RetroactiveRecalculationEngine` MÅSTE vara Scoped/Transient (den tar
scoped `ITaxTableProvider` → captive-dependency om Singleton). `PayrollInputBuilder` är
tillståndslös → Singleton är ok. `PayrollBatchService` är Scoped (tar
`IDbContextFactory`, scoped `PayrollCalculationEngine`, scoped `RetroactiveRecalculationEngine`).

Alla övriga beroenden finns redan registrerade: `PayrollCalculationEngine` (scoped, rad 90),
`ITaxTableProvider` (rad 88), `ICollectiveAgreementRulesEngine` (rad 89),
`IDbContextFactory<RegionHRDbContext>` (rad 53/71), `ICoreHRModule` (rad 85).

## Filer skapade
- `src/Infrastructure/Payroll/PayrollInputBuilder.cs` — bygger `PayrollInput` per anställning/månad
  ur `ScheduledShifts` (OB per kategori, övertid, jour, beredskap; faktiska timmar annars planerade),
  `Timesheets` (övertids-fallback) och `LeaveRequests` (godkänd sjuk/semester/föräldraledighet).
  Arbetade dagar proportioneras mot anställningens giltighetsperiod; helgdagar exkluderas via
  `SvenskaHelgdagar`. Sjuklön kappas till 14 dagar (dag 2–14). Löneutmätning/fackavgift = 0 kr
  (saknar register ännu — strukturen finns för att koppla in dem).
- `src/Infrastructure/Payroll/PayrollBatchService.cs` — orkestrerar hela körningen.
  `ExecutePayrollRunAsync(year, month, startadAv)` räknar upp varje anställd med aktiv anställning,
  bygger riktigt underlag och kör motorn. `ExecuteRetroactiveRunAsync(year, month, retroPeriod, startadAv)`
  laddar tidigare körning, räknar om och skapar differensrader via `RetroactiveRecalculationEngine`.
  Returnerar `PayrollRunResult(Run, Fel)` (per-anställd-fel sväljs inte tyst — de rapporteras).
- `tests/RegionHR.Infrastructure.Tests/Payroll/PayrollInputBuilderTests.cs` — 10 xUnit-tester
  (InMemory) för underlagsbyggandet.

## Filer ändrade
- `src/Web/Components/Pages/Lon/NyKorning.razor` — injicerar nu `PayrollBatchService` (ej DbFactory),
  schablonberäkningen borttagen. Ny radioväxel Ordinarie/Retroaktiv + omräkningsperiod-fält.
- `src/Web/Components/Pages/Lon/KorningDetalj.razor` — ny MudTable "Löneunderlag per anställd"
  (brutto/skatt/netto/OB/AG/pension) + expanderbar löneartsspecifikation; visar retroaktiv-info.

## Audit-fynd åtgärdade
Den modulinterna `RegionHR.Payroll.Services.PayrollBatchService` var trasig
(`GetRootOrganizationUnitsAsync` → tom lista → 0 anställda; `BuildPayrollInputAsync` → tomt underlag)
OCH oregistrerad/oanvänd (endast en TODO-kommentar i `src/Api/Endpoints/PayrollEndpoints.cs`).
Rotorsaken: den låg i Payroll-modulen och hade bara `ICoreHRModule`, utan åtkomst till schema/frånvaro.
Den nya orkestreraren ligger i Infrastructure (full DB-åtkomst) och löser samma problem korrekt.
Den gamla filen rördes INTE (utanför mina filer). Valfri städning för integratören: ta bort
`src/Modules/Payroll/Services/PayrollBatchService.cs` (dead code) — inte nödvändigt för bygget.

## DbSet / schema
Inga nya entiteter eller DbSets. Endast läsning av befintliga: `ScheduledShifts`, `Timesheets`,
`LeaveRequests`, `Employments`. Skriver `PayrollRuns`/`PayrollResults` (befintliga).

## Signaturändringar
Inga publika signaturer ändrade. Nya publika typer:
`RegionHR.Infrastructure.Payroll.PayrollBatchService`, `PayrollInputBuilder`,
`PayrollRunResult`, `PayrollRunError`.

## Seed-tips (valfritt, för rikare demo)
Motorn ger redan riktig skatt/AG/pension för alla seedade anställda. För att se OB/övertid/jour i
körningen kan fler `ScheduledShift` seedas med `Status = Avslutad` + `FaktiskStart/FaktiskSlut`
(seedade pass idag är `Planerad`; planerade timmar används ändå för OB via `OBKategori`). Godkända
`LeaveRequest` (Semester/Sjukfranvaro/Foraldraledighet) som överlappar körningsmånaden ger
semester-/sjuk-/föräldralönerader. Befintlig seed har redan godkänd föräldraledighet (Maria) och
semester (Erik) som slår igenom om körningens månad överlappar.

## Bygg-risk
Låg. Nya .cs/.razor matchar befintliga signaturer (verifierat mot motor, retro-motor, DTO:er,
domän och EF-config). Enda hårda kravet: de tre DI-raderna ovan. Kunde inte bygga lokalt
(parallella agenter) — matchning gjord genom kodläsning.
```
