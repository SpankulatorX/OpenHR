# Wave 5 — loneoversyn-import

Löneöversyn: filinläsning (CSV/Excel) av löneförslag med förhandsgranskning och commit.

## Vad som byggdes
- **Ren parser** `src/Modules/SalaryReview/Services/SalaryProposalImportParser.cs`
  - `ParseCsv(string)` — auto-upptäcker avgränsare (`;` / tab / `,`), respekterar citerade fält.
  - `ParseRutnat(IReadOnlyList<IReadOnlyList<string?>>)` — delas av Excel-vägen.
  - Rubrikigenkänning (Personnummer / Ny lön / Motivering / valfri Anställnings-id) med positionell fallback.
  - Validerar per rad: giltigt personnummer (Luhn via `Personnummer`), positiv lön, obligatorisk motivering, talformat (mellanslag/NBSP tusental, svensk komma-decimal, `kr`-suffix), samt dubbletter i filen.
  - Resultattyper `SalaryImportRad`, `SalaryImportParseResult` (i samma fil, namespace `RegionHR.SalaryReview.Services`).
- **Web-orkestrering** i `src/Web/Services/LoneoversynService.cs` (utökad, inga signaturändringar på befintliga metoder):
  - `FortolkaFilAsync(rundaId, Stream, filnamn)` → `SalaryImportForhandsvisning` (matchar mot anställd/aktiv anställning, kontrollerar att anställd finns, ej redan förslag i rundan, ej dubblett, budget i filordning — committar inget).
  - `CommittaImportAsync(rundaId, IReadOnlyList<SalaryImportForslagRad>)` → `SalaryImportResultat` (läser om nuvarande lön ur DB, lägger till via aggregatets `LaggTillForslag`, en `SaveChanges`).
  - Excel läses via ClosedXML (`XLWorkbook`) — endast celluppackning i Web-lagret; all validering ligger i den rena parsern.
  - Nya publika record-typer i samma fil: `SalaryImportForhandsvisning`, `SalaryImportForslagRad`, `SalaryImportResultat`.
- **UI** `src/Web/Components/Pages/Loneoversyn/Import.razor` (ny sida) + knapp "Importera från fil" i `Detalj.razor` (planeringsfasen).

## Rutt & behörighet
- Ny rutt: **`/loneoversyn/{RundaId:guid}/import`**.
- **Ingen ändring i `RouteAccessPolicy.cs` krävs** — prefixregeln `("/loneoversyn", HrAdmin)` matchar redan `/loneoversyn/{id}/import` (HR/Admin). Sidan har dessutom egen `Auth.IsHR`-guard.

## DI / DbSet / Seed / Nav
- **Ingen DI-registrering** behövs: `LoneoversynService` instansieras direkt av sidorna (`new LoneoversynService(DbFactory)`), samma mönster som befintlig `Detalj.razor`.
- **Inga nya DbSet/entiteter** — importen skapar `SalaryProposal` i befintlig `SalaryReviewRound` (befintlig `db.SalaryReviewRounds`).
- **Ingen seed** krävs.
- **Ingen nav-post** krävs (nås från rundans detaljsida).

## Bygg-risk / paket
- `SalaryProposalImportParser` (SalaryReview-modulen) använder **inga externa paket** — bara BCL + SharedKernel. Ingen csproj-ändring.
- Excel-läsningen i `LoneoversynService` använder **ClosedXML**, som redan är `PackageReference` i `RegionHR.Infrastructure` och därmed transitivt tillgängligt i `RegionHR.Web` (Web → Infrastructure). Ingen csproj-ändring gjord eller behövd.

## Tester
- `tests/SalaryReview.Tests/SalaryProposalImportParserTests.cs` (16 fall): giltig fil m/rubrik, positionell fil, ogiltigt pnr, ogiltig/negativ/noll lön, saknad motivering, dubbletter, talformat (Theory), komma-avgränsare, citerade fält, tom fil, saknad lönekolumn, valfri anställnings-id, `ParseRutnat` (Excel-simulering), blandade rader med rätt räkning.
- Testprojektet refererar redan SalaryReview + Core + SharedKernel — ingen csproj-ändring.
- Testdata-personnummer skapas via `Personnummer.CreateValidated(...)` per regel.
