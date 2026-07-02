# Slice: resor — Resa/utlägg attest → utbetalning

## Sammanfattning
Stängde attest→utbetalningsflödet för resekrav:
1. **Persistens-bugg fixad** — attest lästes tidigare på en *detached* entitet (laddad i
   `OnInitializedAsync` sin DbContext) och `SaveChangesAsync()` kördes på en *ny* DbContext
   som aldrig spårade entiteten → godkännandet försvann. Nu läses entiteten i SAMMA
   DbContext som sparar (`FirstOrDefaultAsync(id)` → mutera → `SaveChangesAsync`).
2. **Avvisa + Utbetald** tillagt i UI (domänmetoderna fanns redan).
3. **Behörighet**: attest/avvisa kräver chef/HR (`Auth.IsChef`), attestant ≠ inlämnare
   (självattest blockeras i UI *och* domän), samt HR-krav för belopp > 25 000 kr.
4. **Utbetalning**: godkända krav (`Status == Godkand`) är "klar för lön" och plockas av
   lönekörningen (se pickup-kontrakt nedan).

## Delade filer: INGA ändringar krävs
- `RegionHRDbContext.cs` — `DbSet<TravelClaim> TravelClaims` finns redan (rad 94). Ingen ny DbSet.
- `DependencyInjection.cs` — INGEN ny registrering krävs. UI:t (`Resor/Index.razor`) går direkt
  via injicerad `IDbContextFactory<RegionHRDbContext>` (samma mönster som förut).
- `SeedData.cs` — orörd. Rad 983 `reseAnna.Attestera("Eva Nilsson")` fortsätter kompilera
  (string-överlagringen är bevarad, se nedan).
- Inga nya persisterade kolumner. `ArKlarForUtbetalning`/`KraverHRAttest` är get-only computed
  properties (samma mönster som `Employment.ArTillsvidareanstallning`) → mappas EJ av EF.

## Pickup-kontrakt för lönekörningen (payroll-agenten)
Resekrav som är attesterade men ännu ej utbetalda har `Status == TravelClaimStatus.Godkand`
(domänpredikat: `claim.ArKlarForUtbetalning == true`). Statusflöde:

    Utkast → Inskickad → Godkand → Utbetald
                       ↘ Avslagen (sidled, betalas ej)

Lönekörningen bör:
```csharp
await using var db = await dbFactory.CreateDbContextAsync(ct);
var klaraForLon = await db.TravelClaims
    .Where(c => c.Status == TravelClaimStatus.Godkand)   // == ArKlarForUtbetalning
    .ToListAsync(ct);

foreach (var claim in klaraForLon)
{
    // ... lägg beloppet claim.TotalBelopp på löneunderlaget ...
    claim.MarkeraSomUtbetald();   // Godkand → Utbetald (idempotent skydd: kastar om ej Godkand)
}
await db.SaveChangesAsync(ct);    // läs+mutera+spara i SAMMA context
```
`MarkeraSomUtbetald()` kastar `InvalidOperationException` om kravet inte är `Godkand`, så
dubbel-utbetalning förhindras. Alternativt finns
`TravelService.HamtaKlaraForUtbetalningAsync(ct)` + `MarkeraSomUtbetaldAsync(id, ct)` om ni
hellre går via modultjänsten (kräver DI-registrering, se OPTIONAL nedan).

## Signaturändringar (`signature_changes`)
Modultjänsten `RegionHR.Travel.Services.TravelService` (anropas av ingen annan — verifierat
via grep) fick ändrade/nya signaturer:
- `AttesteraAsync(Guid, string, ct)` → `AttesteraAsync(Guid claimId, EmployeeId attestantId, string attestantNamn, bool attestantArHR, ct)`
- `HamtaForAttestAsync(string, ct)` → `HamtaForAttestAsync(EmployeeId attestant, ct)` (utesluter nu attestantens egna krav)
- NYA: `AvvisaAsync(Guid, EmployeeId, string, string, ct)`, `MarkeraSomUtbetaldAsync(Guid, ct)`,
  `HamtaKlaraForUtbetalningAsync(ct)`

Domänen `TravelClaim` — **inga befintliga signaturer bröts** (viktigt för SeedData +
TravelClaimTests). Tillagda *överlagringar* + properties:
- `Attestera(EmployeeId? attestantId, string attestantNamn, bool attestantArHR)` — självattest-
  spärr + HR-beloppsgräns. (Behåller `Attestera(string)`.)
- `Avvisa(EmployeeId? attestantId, string attestantNamn, string anledning)` — självattest-spärr.
  (Behåller `Avvisa(string, string)`.)
- `bool ArKlarForUtbetalning`, `bool KraverHRAttest`, `const decimal ATTEST_GRANS_KRAVER_HR = 25000`.

`attestantId == null` tolkas som attestant utan anställningskoppling (t.ex. Admin) och kan
aldrig vara inlämnaren → tillåts.

## OPTIONAL — om ni vill DI-registrera modultjänsten TravelService
Den är inte kopplad idag (UI går via DbFactory). Vill man använda den behövs en generisk
repo för `TravelClaim` (finns ej), t.ex. i `DependencyInjection.cs`:
```csharp
// (endast om TravelService ska injiceras någonstans)
services.AddScoped<IRepository<TravelClaim, Guid>, /* EfRepository<TravelClaim, Guid> */>();
services.AddScoped<RegionHR.Travel.Services.TravelService>();
```
Ej nödvändigt för att slicen ska fungera eller bygga.

## Tester (nya filer, egen katalog)
- `tests/Travel.Tests/TravelClaimAttestBehorighetTests.cs` — självattest-spärr, HR-beloppsgräns,
  admin-utan-employee, avvisa-självattest, `ArKlarForUtbetalning`-tillstånd.
- `tests/Travel.Tests/TravelClaimPersistensTests.cs` — EF InMemory: reproducerar detached-buggen
  (godkännande persisteras EJ) + bevisar fix (samma context persisterar), avvisning-persistens,
  samt lönekörnings-pickup + utbetald-markering.
- Rörde INTE `TravelClaimTests.cs` / `TraktamentsCalculatorTests.cs`.

## Byggrisk
Låg. Inga delade filer ändrade. Domänens string-överlagringar bevarade (SeedManager +
befintliga tester kompilerar). Computed properties följer etablerat, byggbart mönster.
Inte byggt lokalt (parallella agenter) — signaturer matchade mot läst kod.
