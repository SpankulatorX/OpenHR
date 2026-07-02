# Integration notes — slice `employment`

Gör anställ / ändra / avsluta anställning fullt funktionellt i UI:t och persisterat via `IDbContextFactory`.

## TL;DR för integratören
**Inget krävs i de delade filerna.** Inga nya DI-registreringar, inga nya DbSet-rader, ingen NavMenu-ändring, inga paket. Allt återanvänder befintliga routes, services och DbSets. Enda "notera"-punkten är LAS 6c§-kopplingen i PdfGenerator (valfri, se nedan).

## Filer skapade
- `src/Modules/Core/Contracts/Anstallningsavtal.cs` — `AnstallningsavtalUppgifter`, `AvtalsAvsnitt`, `AnstallningsavtalGenerator` (LAS 6c§-innehåll + LAS 11§ uppsägningstidstabell).
- `tests/Core.Tests/EmploymentLifecycleTests.cs` — 20+ domäntester för livscykel + LAS-validering.

## Filer ändrade
- `src/Modules/Core/Domain/Employment.cs` — LAS-validering i `Skapa` (ny publik `Validera(...)`), `KraverSlutdatum(form)`, `ArProvanstallning`, `ArAvslutad`, `MaxProvanstallningManader=6`. Change/end-metoder tar nu valfritt `andradAv`.
- `src/Modules/Core/Domain/Employee.cs` — `LaggTillAnstallning` fick 2 **valfria** trailing-parametrar (`befattningstitel`, `avtalsId`). Nya aggregat-metoder: `AndraAnstallningsLon`, `AndraAnstallningsSysselsattningsgrad`, `SattAnstallningsBefattning`, `AvslutaAnstallning(EmploymentId,...)`.
- `src/Web/Services/AnstallningService.cs` — nya metoder (se nedan) + records `EnhetVal`, `AvtalVal`.
- `src/Web/Components/Pages/Anstallda/NyAnstalld.razor` — riktigt anställningsformulär (enhet/form/lön/grad/avtal/period/BESTA/AID/befattning) som **persisterar** employee+employment. Tidigare fejk (slumpat pnr + sväljda DB-fel) borttagen.
- `src/Web/Components/Pages/Anstallda/AnstallningAndring.razor` — riktig ändra/avsluta (löne-/grad-/befattningsändring + avsluta) som persisterar. Tidigare "skickad till godkännande"-fejk borttagen.
- `src/Web/Components/Pages/Anstallda/Detalj.razor` — la till knapp "Ändra anställning" → `/anstallda/{id}/anstallning`.

## DI-registreringar
Inga. `AnstallningService` och `AuthService` är redan registrerade i `src/Web/Program.cs` (rad 66 resp. 70).

## Nya DbSet-rader
Inga. Använder befintliga `Employees`, `OrganizationUnits`, `CollectiveAgreements`.

## Seed-data
Inga ändringar krävs. Den nya LAS-valideringen i `Employment.Skapa` är **bakåtkompatibel** med befintlig seed:
- `SeedData.cs` (8 st) och `DevDataSeeder.cs` (5 st) skapar endast Tillsvidare (null slutdatum) eller tidsbegränsade med slutdatum → alla passerar valideringen. Verifierat mot kod.

## NavMenu-poster
Inga nya. Sidorna ligger på befintliga routes: `/anstallda/ny` och `/anstallda/{Id:guid}/anstallning` (redan nådda från personallistan resp. den nya knappen på detaljsidan).

## Signaturändringar (alla bakåtkompatibla — endast valfria trailing-parametrar / nya metoder)
- `Employee.LaggTillAnstallning(..., string? befattningstitel = null, CollectiveAgreementId? avtalsId = null)`
- `Employment.SattBefattning(string, string? andradAv = null)`
- `Employment.AndraSysselsattningsgrad(Percentage, string? andradAv = null)`
- `Employment.AvslutaAnstallning(DateOnly, string? andradAv = null)`
- Nya publika: `Employment.Validera(...)`, `Employment.KraverSlutdatum(EmploymentType)`, `Employee.AndraAnstallningsLon/…Sysselsattningsgrad/SattAnstallningsBefattning/AvslutaAnstallning(EmploymentId,…)`.

Inga existerande anrop bröts (grep-verifierat: DevDataSeeder, SeedData, Core.Tests, LAS.Tests).

## Nya service-metoder (AnstallningService) som UI:t anropar
- `HamtaEnheterAsync()` → `List<EnhetVal>`
- `HamtaAvtalAsync()` → `List<AvtalVal>`
- `SkapaMedAnstallningAsync(pnr, förnamn, efternamn, epost, telefon, enhetId, form, avtal, lön, grad, start, slut, besta, aid, befattning, avtalsId)` → `EmployeeId`
- `LaggTillAnstallningAsync(anstalldId, …)` → `EmploymentId`
- `AndraLonAsync / AndraSysselsattningsgradAsync / SattBefattningAsync / AvslutaAnstallningAsync(anstalldId, anstallningId, …, andradAv)`

## LAS 6c§ (NOTERA — valfri koppling, rör ej mina filer)
Anställningsavtalet ska innehålla LAS 6c§-informationen. Jag rör **inte** `PdfGenerator` (Infrastructure, ej min fil), så innehållsbyggaren ligger i Core:
- `RegionHR.Core.Contracts.AnstallningsavtalGenerator.Skapa6cInformation(AnstallningsavtalUppgifter)` → `IReadOnlyList<AvtalsAvsnitt>` (9 obligatoriska avsnitt).
- `.Skapa6cText(...)` → färdig löptext för inbäddning.

Rekommendation till den som äger `src/Infrastructure/.../PdfGenerator.cs`: när anställningsavtal genereras, mappa Employee/Employment/OrganizationUnit → `AnstallningsavtalUppgifter` och skjut in `Skapa6cText(u)` i dokumentet. Verifierade lagkällor: LAS 6c§ (SFS 1982:80, lydelse fr.o.m. 2022-06-29, arbetsvillkorsdirektivet 2019/1152), LAS 6§ (provanställning ≤ 6 mån), LAS 11§ (uppsägningstidstrappan 1/2/3/4/5/6 mån).

## Tester
`tests/Core.Tests/EmploymentLifecycleTests.cs` (domännivå — Core.Tests refererar redan Core + SharedKernel, ingen csproj-ändring). Täcker: LAS-validering (tillsvidare utan slutdatum, tidsbegränsad kräver slutdatum, provanställning ≤6 mån, lön>0, grad>0, slutdatum≥start), ändra lön/grad/befattning via aggregatet, avsluta + event, samt LAS 6c§/11§-generatorn.
- **Service-integrationstest saknas medvetet:** `AnstallningService` bor i Web och kräver Web+Infrastructure+EF InMemory. Det finns inget Web.Tests/Infrastructure.Tests-projekt och jag får ej röra `.csproj`. Om integratören vill ha service-nivåtester: skapa ett nytt testprojekt som refererar `RegionHR.Web` + `RegionHR.Infrastructure` + `Microsoft.EntityFrameworkCore.InMemory` och testa create/change/end mot `IDbContextFactory`.

## Byggrisk
Låg. Domänkod matchar befintliga signaturer; razor-markup använder endast MudBlazor-mönster som redan finns i kodbasen (MudSelect/MudDatePicker `Clearable`, `Min="0m"` etc.). `db.CollectiveAgreements` nås transitivt via Infrastructure→Agreements (samma transitivitet som Detalj.razor redan använder för Documents/Leave/Competence).
