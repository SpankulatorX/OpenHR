# Wave 8 — Folkbokförings-import (#23) + KOLL-export (#3)

Slice-key: `folkbokforing-koll`

## Vad som byggdes

### 1. Folkbokföring (IN) — Skatteverket / Navet
- **`src/Modules/IntegrationHub/Adapters/Skatteverket/FolkbokforingImporter.cs`** (NY, rörde EJ AGIXmlGenerator.cs)
  - `FolkbokforingImporter.Parsa(string?)` → `FolkbokforingImportResult`.
  - Self-contained parser (endast SharedKernel + BCL). Tolkar en representativ Navet-avisering
    (blockformat `#PERSON <pnr>` + `NYCKEL=VÄRDE`, endast ändrade fält skickas).
  - Fält: EFTERNAMN, FORNAMN, MELLANNAMN, GATUADRESS, POSTNUMMER, POSTORT, LAND, AVLIDEN (YYYY-MM-DD),
    SEKRETESS (INGEN / SEKRETESSMARKERING / SKYDDAD_FOLKBOKFORING).
  - Strikt Luhn-validering av personnummer (`new Personnummer(...)`); ogiltiga block → fel, hoppas över.
  - **Skyddad identitet hanteras:** adressuppgifter maskas bort och exponeras/aviseras aldrig; posten
    flaggas (`ArSkyddad`, `SekretessBeskrivning`).
  - Enum `Sekretessmarkering`, records `FolkbokforingAvisering` / `FolkbokforingImportResult`.
- **`src/Web/Components/Pages/Integrationer/Folkbokforing.razor`** (NY, route `/integrationer/folkbokforing`)
  - Filuppladdning (MudFileUpload, samma mönster som Loneoversyn/Import.razor) ELLER inklistrad text.
  - Analyserar → matchar mot `db.Employees` på 12-siffrigt personnummer → visar diff-tabell.
  - "Applicera adressändringar" uppdaterar adress via **Core:s publika API**
    `Employee.UppdateraKontaktuppgifter(epost, telefon, Address)` (bevarar befintlig e-post/telefon)
    + `SaveChangesAsync`.
  - Ärlig märkning: stor varningsruta "Demo — ej skarp Navet-koppling".

### 2. KOLL (UT) — RÖL katalogtjänst
- **`src/Modules/IntegrationHub/Adapters/KOLL/KollExportGenerator.cs`** (NY, rörde EJ KOLLHOSPAdapter.cs)
  - `KollExportGenerator.Generera(KollExportInput)` → `KollExportResult`.
  - Self-contained XML-generator (samma mönster/namespace-stil som FKAnmalanGenerator: XDocument +
    XmlWriter, InvariantCulture, `Overforingsstatus`-stämpel, `Genererad`, org-block).
  - En `Anstallning`-post per anställning med person, anställnings-id, enhet (namn+kostnadsställe),
    befattning, anställningsform (svensk klartext + kod), sysselsättningsgrad (F2 invariant),
    giltighetsperiod, status (Aktiv/Kommande/Avslutad relativt referensdatum), ev. HSA-id.
  - Const `OverforingStatus = "EJ_OVERFORD_KRAVER_KOLL_ANSLUTNING"`.
- **`src/Web/Components/Pages/Integrationer/KollExport.razor`** (NY, route `/integrationer/koll`)
  - Läser Employees+Anstallningar+OrganizationUnits+TenantConfiguration, bygger `KollExportInput`,
    förhandsvisar och laddar ner XML via `downloadFile`-JS (base64/UTF-8) — samma mönster som Export.razor.

### 3. Tester (tests/IntegrationHub.Tests/)
- **`FolkbokforingImporterTests.cs`** (9 tester): namn/adress-tolkning, skyddad identitet maskar adress,
  sekretessmarkering, avliden, ogiltigt pnr, ofullständig adress, flera personer i ordning, tom fil,
  fält utanför block. Testdata via `Personnummer.CreateValidated`.
- **`KollExportGeneratorTests.cs`** (7 tester): överföringsstämpel+antal, statusberäkning mot referensdatum,
  anställningsform-översättning, invariant grad-format, utelämnade Slutdatum/HsaId, varning vid saknad
  befattning, tom katalog.

## Integrationskrok (för integratören)

- **DI:** Inga. Generatorer/importer `new`-as direkt i sidorna (samma som AGI/Nordea/FK-generatorerna).
- **DbSet / RegionHRDbContext:** Inga nya EF-entiteter, inga ändringar i RegionHRDbContext.cs.
- **RouteAccessPolicy:** Inga ändringar behövs. `/integrationer` är redan `HrAdmin` → prefix-matchning
  täcker `/integrationer/folkbokforing` och `/integrationer/koll` automatiskt (HR/Admin).
- **NavMenu / Index.razor (RÖR EJ — noteras):** lägg gärna till länkar till de två nya sidorna.
  Föreslagna rader i `src/Web/Components/Pages/Integrationer/Index.razor` (tabellen "Externa integrationer"):
  ```
  <tr><td><MudLink Href="/integrationer/folkbokforing">Folkbokföring (Skatteverket/Navet)</MudLink></td><td>Fil (SSEK/WS)</td><td>In</td><td><MudChip T="string" Size="Size.Small" Color="Color.Warning">Demo</MudChip></td></tr>
  <tr><td><MudLink Href="/integrationer/koll">KOLL-katalog (RÖL)</MudLink></td><td>Fil (SFTP/WS)</td><td>Ut</td><td><MudChip T="string" Size="Size.Small" Color="Color.Info">Lokal</MudChip></td></tr>
  ```
- **Package refs:** Inga (endast BCL + befintlig MudBlazor/EF Core).

## Domänmetoder som SAKNAS (noteras — Employee.cs utanför detta slice)

Adressuppdatering fungerar idag via befintligt publikt API. För att FULLT applicera övriga
folkbokföringsändringar behöver `RegionHR.Core.Domain.Employee` kompletteras (ej gjort här — filen
ligger utanför slicens filuppsättning):

1. **Namnbyte:** `Employee.UppdateraNamn(string fornamn, string efternamn, string? mellanNamn)`
   — idag är `Fornamn`/`Efternamn`/`MellanNamn` `private set` utan uppdateringsmetod. Sidan visar
   namnändringar men applicerar dem inte ("kräver domänmetod").
2. **Skyddad identitet:** `Employee.MarkeraSkyddadIdentitet(bool skyddad)` + en nullable
   `bool SkyddadIdentitet`-kolumn, samt logik som döljer adress när flaggan är satt. Importern
   flaggar och maskar redan skyddade poster; markeringen persisteras inte utan denna metod.

Tills dessa finns hanteras namnbyte + skyddad identitet manuellt (tydligt märkt i UI:t).

## Ärlig märkning (transport ej skarp)
- Folkbokföring: skarp löpande Navet-hämtning kräver avtal med Skatteverket + SSEK/WS. Filtolkning byggd.
- KOLL: skarp överföring (SFTP-filsläpp eller katalogtjänstens WS) kräver konfigurerad anslutning.
  Varje fil stämplas `EJ_OVERFORD_KRAVER_KOLL_ANSLUTNING`.

## Build-risk
Låg. Nya, isolerade filer; inga rörda delade filer. Matchar befintliga signaturer/mönster
(FKAnmalanGenerator, Export.razor, Loneoversyn/Import.razor). Ej byggt lokalt per instruktion
(bygg fryser maskinen) — verifierat mot lästa domänsignaturer (DateRange.Start/End, Percentage.Value,
EmploymentId.Value, OrganizationId, TenantConfiguration, Employee/Employment publika API).
