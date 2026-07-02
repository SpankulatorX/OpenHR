# Wave 8 — SCB-statistikfil (#20) + SKR-novemberstatistik (#25)

Slice-key: `scb-skr-statistik`

## Vad som byggts

Tre self-contained, aggregerande filgeneratorer (samma mönster som AGIXmlGenerator /
NordeaPaymentFileGenerator / FKAnmalanGenerator: `Generera(input)` → resultatobjekt med
`Filnamn` + `Innehall` (sträng) + `Bytes` (ISO-8859-1) + strukturerade grupper för
test/förhandsvisning + ärlig `Overforingsstatus`-stämpel) samt en Blazor-sida.

### Nya filer
- `src/Modules/IntegrationHub/Adapters/SCB/SCBLonestatistikGenerator.cs`
  - SCB Lönestrukturstatistik för regioner (aggregerad **per yrke (SSYK/AID) × kön**).
  - Individlön räknas upp till heltid (`lön / (sysselsättningsgrad/100)`) före aggregering,
    som SCB gör. Redovisar antal, medel-heltidslön, lönespridning P10/P25/median/P75/P90,
    medel-sysselsättningsgrad, ev. medel-arbetstid, samt kvinnors löneandel i % av mäns.
- `src/Modules/IntegrationHub/Adapters/SCB/SCBSjuklonestatistikGenerator.cs`
  - SCB Sjuklönestatistik (KSju-stil, aggregerad **per kön + total**): antal anställda,
    antal med sjuklön, summa sjukdagar i sjuklöneperioden (dag 1–14), sjukfrånvaro i %,
    summa utbetald sjuklön.
- `src/Modules/IntegrationHub/Adapters/SKR/SKRNovemberstatistikGenerator.cs`
  - SKR Novemberstatistik (mättidpunkt **1 november**), aggregerad **per personalgrupp/AID ×
    kön + total**: antal anställda (tillsvidare/visstid), årsarbetare (= summan av
    sysselsättningsgraderna), medel-sysselsättningsgrad, medelålder, sjukfrånvaro-%.
- `src/Web/Components/Pages/Rapporter/Statistik.razor` — ny sida `/rapporter/statistik`
  (rör EJ befintliga SCBExport.razor `/rapporter/scb` eller andra rapportsidor). Väljer
  år + kvartal + uppgiftslämnare, bygger underlaget ur `Employees`/`Employments` +
  `LeaveRequests` (Sjukfrånvaro), och laddar ner de tre filerna via `downloadFile`-JS.
- Tester:
  - `tests/IntegrationHub.Tests/SCBLonestatistikGeneratorTests.cs`
  - `tests/IntegrationHub.Tests/SCBSjuklonestatistikGeneratorTests.cs`
  - `tests/IntegrationHub.Tests/SKRNovemberstatistikGeneratorTests.cs`

## Verifierad statistikstruktur (övergripande, via webben)
- SCB Lönestrukturstatistik, regioner: variabler = yrke (SSYK 2012), kön, ålder,
  tjänstgöringens omfattning; mått = genomsnittlig månadslön/grundlön + lönespridning;
  regionernas löner samlas partsgemensamt in per 1 november (Medlingsinstitutet/SCB).
- SKR Novemberstatistik: mättidpunkt 1 november; klassning via AID (Arbetsidentifikation);
  antal anställda, årsarbetare, sysselsättningsgrad, frånvaro; underlag för avtal/planering.

## Ärlig märkning (skarp transport frånkopplad)
Varje fil stämplas med status i fält `#STATUS`:
- SCB-filerna: `EJ_INLAMNAD_KRAVER_SCB_INLOGGNING`
- SKR-filen: `EJ_INLAMNAD_KRAVER_SKR_INLOGGNING`
Filerna byggs och laddas ner; skarp inlämning sker via SCB:s resp. SKR:s portal och kräver
inloggning/behörighet — det steget är avsiktligt inte kopplat.

## Format / kodning
- Semikolon-separerad CSV, en `#`-metadata-header (typ, uppgiftslämnare, period, mättidpunkt,
  kodning, decimaltecken, status).
- **Invariant kultur** (decimaltecken = `.`), radbrytning `\r\n`.
- **Teckenkodning ISO-8859-1 (Latin-1)** via `Encoding.Latin1` (samma som SIE-exporten);
  `Resultat.Bytes` är redan Latin-1-kodade, sidan gör bara `Convert.ToBase64String(bytes)`.

## Integration som huvudagenten måste göra

### route_policy (RÖR EJ RouteAccessPolicy.cs själv — lägg in snutten)
Ny känslig rutt `/rapporter/statistik` exponerar aggregerad **lönedata** → bör vara HR/Admin
(inte Chef, som `/rapporter` annars tillåter). Lägg till i `Rules`-listan i
`src/Web/Services/RouteAccessPolicy.cs` (sorteras efter längsta prefix → mer specifik vinner):

```csharp
// ── Statistik med lönedata (HR/Admin — mer specifik än /rapporter) ──
("/rapporter/statistik", HrAdmin),
```

## Inga övriga integrationskrav
- Ingen DI-registrering behövs (generatorerna instansieras direkt i sidan, precis som
  AGIXmlGenerator/NordeaPaymentFileGenerator/FKAnmalanGenerator).
- Inga nya EF-entiteter, inga DbSet-ändringar, inga nya paket.
- RÖR EJ: DependencyInjection.cs, RegionHRDbContext.cs, SeedData.cs, Program.cs,
  NavMenu.razor, *.csproj, Directory.*.props — inga ändringar där krävs.
- (Valfritt) Nav-post: lägg ev. en länk till `/rapporter/statistik` under Rapporter i
  `NavMenu.razor` för HR/Admin. Inte nödvändigt för funktion.

## Byggrisk
Låg. Följer beprövade mönster; matchar befintliga signaturer (Employee/Employment/
LeaveRequest/Personnummer). Inga publika signaturer ändrade. Ej byggd lokalt (per instruktion).
```
