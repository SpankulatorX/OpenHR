# Wave 2 — Slice `loneoversyn` (Löneöversyn genomförande)

## Vad som byggts
Stänger tre luckor: (1) godkända löneförslag applicerar nu FAKTISKT ny lön,
(2) facklig avstämning har UI + statusflöde, (3) retroaktivitet beräknas från
`IkrafttradandeDatum`.

Flöde i UI: **Planering** (lägg till/godkänn/avvisa förslag) → **Facklig avstämning**
(motpart signerar, ingen självattest) → **Godkänd** → **Genomförd** (ny lön appliceras
på anställningarna via `Employee.AndraAnstallningsLon`, retro beräknas).

## Filer

### Skapade
- `src/Modules/SalaryReview/Domain/SalaryReviewExecutionEngine.cs` — ren domänmotor:
  applicerar godkända förslag på Employee-aggregat + räknar retro. `SalaryReviewExecutionResult`,
  `AppliedSalaryChange`.
- `src/Web/Services/LoneoversynService.cs` — orkestrering via `IDbContextFactory<RegionHRDbContext>`
  (samma mönster som `AnstallningService`). Kör motorn och sparar allt i EN `SaveChanges`.
- `src/Web/Components/Pages/Loneoversyn/Detalj.razor` — ny sida, rutt `/loneoversyn/{RundaId:guid}`.
- `tests/SalaryReview.Tests/SalaryReviewExecutionEngineTests.cs` — 11 xUnit-tester (apply-lön + retro).

### Ändrade
- `src/Modules/SalaryReview/Domain/SalaryReviewRound.cs`
- `src/Web/Components/Pages/Loneoversyn/Index.razor` (klickbar rad → detalj, förslagsräknare)
- `src/Modules/SalaryReview/Services/SalaryReviewService.cs` (endast förtydligad doc-kommentar)

## DbContext / schema (EnsureCreatedAsync — INGA migrationer, DB wipas vid redeploy)
Tre NYA nullable-kolumner mappas automatiskt via befintliga value-converter-conventions
(`EmploymentId`, `Money`). Inget behöver göras i `RegionHRDbContext.cs` — DbSet finns redan
(`SalaryReviewRounds`) och `Forslag`-navigationen mappas per konvention.

- `SalaryProposal.AnstallningId` : `EmploymentId?`  → `uuid` NULL
- `SalaryProposal.RetroaktivtBelopp` : `Money?`      → `numeric` NULL
- `SalaryReviewRound.GenomfordDatum` : `DateOnly?`   → `date` NULL

(Nullable `Money?`/strongly-typed-id? är redan bevisat i modellen: `Employee.JamkningBelopp`,
`Employment.AvtalsId`.)

## DI-registrering
INGEN krävs. `Detalj.razor` injicerar `IDbContextFactory<RegionHRDbContext>` (redan registrerad)
och instansierar `LoneoversynService` själv i `OnInitialized`. `AuthService` (redan registrerad)
injiceras för behörighet.

Valfritt (om integratören vill DI-registrera för återanvändning) i `src/Web/Program.cs`
nära rad 66:
```csharp
builder.Services.AddScoped<LoneoversynService>();
```
Görs detta kan man byta `_svc = new LoneoversynService(DbFactory);` mot `@inject LoneoversynService`.
Inte nödvändigt.

## NavMenu
Ingen ändring. Rutten `/loneoversyn` finns redan i menyn; detaljsidan nås från listan.

## Signaturändringar (bakåtkompatibla — inga anrop behöver ändras)
- `SalaryReviewRound.LaggTillForslag(...)` fick ett efterföljande valfritt argument
  `EmploymentId? anstallningId = null`.
- `SalaryReviewRound.Genomfor(DateOnly? genomfordDatum = null)` — tidigare `Genomfor()`.
  Befintliga anrop `runda.Genomfor()` fungerar oförändrat (sätter datum = idag).
- `LaggTillForslag` validerar nu (systemet = experten): kastar `InvalidOperationException`
  vid fel status / tom motivering / icke-positiv föreslagen lön. Vald exception-typ matchar
  metodens befintliga budgetguard så att API-endpointen `AddSalaryProposal`
  (`HRModuleEndpoints.cs`, catchar `InvalidOperationException`) fortsatt svarar BadRequest.

## Behörighet (ingen självattest)
- Alla mutationer (förslag, facklig, genomförande) gömda bakom `AuthService.IsHR` (HR/Admin).
- Facklig godkännande: motpartens namn får inte vara samma som inloggad handläggare
  (jämförs mot `AuthService.UserName`, case-insensitivt) → blockeras i UI.

## Retroaktivitet
`RetroactiveRecalculationEngine` (Payroll) arbetar på fullständiga `PayrollResult` (original vs
omräknat) som INTE finns i löneöversynskontexten — den ägs av lönekörnings-sidorna (annan agent).
Därför beräknas här retro-brutto direkt: `Okning × antal månader(IkrafttradandeDatum→GenomfordDatum)`
och persisteras per förslag i `SalaryProposal.RetroaktivtBelopp`. Fönstret
`[IkrafttradandeDatum, GenomfordDatum]` finns nu på rundan så att lönekörningen kan generera
den faktiska retro-differensen vid nästa körning.

## Build-risk
Låg. Inga delade/förbjudna filer rörda. Kunde ej bygga (parallella builds förbjudna) — matchade
befintliga signaturer (`Employee.AndraAnstallningsLon`, `AnstallningService`-mönstret,
`Include(r => r.Forslag)` motsvarar bevisat `Include(e => e.Anstallningar)`). Inga onödiga usings.
