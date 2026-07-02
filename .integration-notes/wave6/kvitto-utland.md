# Wave 6 slice: kvitto-utland

Kvittouppladdning till resekrav/utlägg + väsentligt utökad utlandstraktamente-lista.

## Vad som gjorts

### 1. Kvittouppladdning (Resor-sidan)
`src/Web/Components/Pages/Resor/Index.razor`:
- Nytt resekrav skapas nu som **Utkast** (tidigare skickades det in direkt), så utlägg och
  kvitton kan läggas till innan inskick. Utläggspanelen öppnas automatiskt för det nya utkastet.
- Ny panel "Utlägg och kvitton" (togglas via knappen **Utlägg** på varje resekravsrad):
  - Listar kravets utlägg (beskrivning, belopp, kvitto).
  - Ägare med status Utkast kan lägga till utlägg med beskrivning + belopp + valfritt kvitto
    (`MudFileUpload`, .pdf/.jpg/.jpeg/.png, max 10 MB). Filen sparas via
    `IFileStorageService.UploadAsync("kvitton", ...)`; returnerad lagringsväg sätts som
    `ExpenseItem.KvittoBildId` genom `TravelClaim.LaggTillUtlagg(...)`.
  - Kvitton kan laddas ner av alla som ser kravet (t.ex. attesterande chef) via
    `IFileStorageService.DownloadAsync(...)` → base64 → `window.downloadFile` (befintlig
    `wwwroot/js/download.js`). MIME sätts från filändelsen.
- Ny **Skicka in**-knapp på egna utkast.
- Behörighet: skriv (lägg till utlägg / skicka in) gated till kravets ägare + status Utkast,
  både i domänen och i UI (self-check). Nedladdning av kvitto är läsning för den som redan ser
  kravet. `/resor` är redan `AllaInloggade` i `RouteAccessPolicy` → **ingen ny policy-rad**.
- Traktamente-kalkylatorns landsdropdown fylls nu från `TraktamentsCalculator.UtrikesLander(år)`
  i stället för en hårdkodad 7-landslista.

### 2. Utökad utlandstraktamente-lista
`src/Infrastructure/Payroll/TraktamentsCalculator.cs`:
- `UtrikesNormalbelopp[2026]` utökad från 9 → **52 länder**. Lagras nu med svenskt visningsnamn
  som nyckel och `StringComparer.OrdinalIgnoreCase` (uppslag är skiftläges-/whitespace-okänsligt).
- Nya publika API:er (bakåtkompatibla, inga befintliga signaturer ändrade):
  - `static IReadOnlyList<string> UtrikesLander(int inkomstAr)` — sorterad landslista för UI.
  - `GetUtrikesNormalbelopp` oförändrad signatur; default-fallback (493 kr) bevarad.
- Belopp urvalsverifierade mot Skatteverkets normalbelopp 2026 via två oberoende
  sammanställningar (Björn Lundén + foretagande.se, hämtade 2026-07). Källa dokumenterad i
  klass-XML-dok. Grekland utelämnat (källorna gav motstridiga värden 746 vs 695).

### 3. Tester
- `tests/Travel.Tests/ExpenseReceiptTests.cs` — kvitto-metadata (domän) + persistens-round-trip
  via `Include(c => c.Utlagg)` + regression att flera utlägg ackumuleras i totalen.
- `tests/Travel.Tests/UtlandsNormalbeloppTests.cs` — urval av landsbelopp (21 länder),
  skiftläges-/whitespace-tolerans, default-fallback, `UtrikesLander`-egenskaper (≥40, sorterad,
  unik), beräkning med nytt land.

## Integration (klistringsbart)

- **DI:** Inga ändringar. `IFileStorageService` är redan registrerad i
  `src/Infrastructure/DependencyInjection.cs` (`AddSingleton<IFileStorageService>(new LocalFileStorageService())`),
  och `TraktamentsCalculator` är redan injicerad på Resor-sidan.
- **DbSet / schema:** Inga ändringar. `ExpenseItem` (inkl. `KvittoBildId`-kolumnen) mappas redan
  via konvention genom `TravelClaim.Utlagg`-navigationen (finns i model-snapshot, tabell
  `ExpenseItem`). EnsureCreated skapar den.
- **Route/policy:** Ingen ny rad. `/resor` = `AllaInloggade` redan.
- **Nav / handlers / seed / program.cs:** Inga ändringar.

## Filer
- Ändrade: `src/Web/Components/Pages/Resor/Index.razor`,
  `src/Infrastructure/Payroll/TraktamentsCalculator.cs`
- Nya: `tests/Travel.Tests/ExpenseReceiptTests.cs`,
  `tests/Travel.Tests/UtlandsNormalbeloppTests.cs`

## Byggrisk
Låg. Inga rörda förbjudna filer, inga ändrade publika signaturer. Ej byggt lokalt (per instruktion).
Kvittofiler lagras utanför wwwroot (CWD/uploads) → nås bara via autentiserad nedladdning i appen.
