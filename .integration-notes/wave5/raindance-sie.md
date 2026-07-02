# Wave 5 — raindance-sie

## Slice
Konteringsfil till ekonomisystem — leverera den via UI. Ur en lönekörning genereras en
balanserad bokföringsverifikation (lönekostnad, arbetsgivaravgift, prel.skatt-skuld) konterad
per kostnadsställe, som laddas ner som **Raindance-konteringsfil (CSV)** eller **SIE typ 4**.

## Filer skapade
- `src/Modules/IntegrationHub/Adapters/SIE/SIEKonteringsExporter.cs` — SIE typ 4 (SIE4E) export
  som återanvänder `RaindanceKonteringsGenerator`s balanserade `KonteringsRad`-logik. En
  verifikation per körning, `#TRANS` per konto/kostnadsställe, dimensionerad på kostnadsställe
  (SIE-dimension 1). Debet positivt, kredit negativt → summa = 0. Latin-1 (ISO-8859-1) bytes.
- `src/Web/Components/Pages/Lon/Kontering.razor` — sida `/lon/kontering` i den driftsatta appen.
  Väljer lönekörning, visar balanserat underlag + förhandsgranskning, laddar ner CSV/SIE via
  `downloadFile` (samma mönster som `/lon/export`).
- `tests/IntegrationHub.Tests/Kontering/KonteringTestData.cs` — balanserad testkörning (2 KST).
- `tests/IntegrationHub.Tests/Kontering/RaindanceKonteringsGeneratorTests.cs` — 6 tester.
- `tests/IntegrationHub.Tests/Kontering/SIEKonteringsExporterTests.cs` — 9 tester.

## Filer ändrade
- Inga (RaindanceKonteringsGenerator.cs verifierad, oförändrad; logiken var redan korrekt).
- SIE4iAdapter.cs orörd (den är en importer/parser; exporten är ett nytt fristående verktyg).

## Integration — vad integratören behöver göra

### NavMenu (src/Web/Components/Layout/NavMenu.razor) — LÄGG TILL
Inuti Salary-gruppen (`Auth.IsHR`), direkt efter export-länken (efter rad ~30):
```razor
<MudNavLink Href="/lon/kontering" Icon="@Icons.Material.Filled.ReceiptLong">Kontering (Raindance/SIE)</MudNavLink>
```

### RouteAccessPolicy — INGEN ändring behövs
`/lon/kontering` matchas av den befintliga regeln `("/lon", HrAdmin)` (prefix + segmentgräns),
så sidan är redan HR/Admin-skyddad. Ingen ny rad krävs.

### DI / DbContext / Seed — INGET
Inga nya services, DbSets eller seed. `SIEKonteringsExporter`/`RaindanceKonteringsGenerator`
instansieras direkt i sidan (parameterlösa `new`), precis som AGI/Nordea-generatorerna i
`/lon/export`. Inga csproj-ändringar (SIE använder `System.Text.Encoding.Latin1`, inbyggt i
.NET, inget CodePages-paket).

## Ärlig märkning (ingen betald extern koppling)
Ingen live-överföring till ekonomisystemet sker. Endast filen genereras och laddas ner i
webbläsaren. Den skarpa transporten (SFTP/AP-drop till Raindance/Agresso/Visma) är
konfigurationsklar men avsiktligt frånkopplad. Dokumenterat i klassdok på `SIEExportInput`.

## Format-noteringar
- **SIE typ 4**: öppet dokumenterat format (https://sie.se). `#FLAGGA/#PROGRAM/#FORMAT PC8/#GEN/
  #SIETYP 4/#ORGNR/#FNAMN/#RAR`, `#KONTO` per använt konto, `#DIM 1 "Kostnadsställe"` + `#OBJEKT`,
  en balanserad `#VER` med `#TRANS`-rader (CRLF-radslut). Kodas i ISO-8859-1 — samma
  teckenuppsättning som OpenHR:s egen SIE-importer (`SIE4iAdapter`) läser, vilket ger garanterad
  round-trip inom systemet. (`#FORMAT PC8` är standardens enda tillåtna värde; strikt CP437-
  transkodning kan aktiveras i den skarpa konnektorn om ett målsystem kräver det.)
- **Raindance CSV**: oförändrad `RaindanceKonteringsGenerator.GenerateFile` (semikolonseparerad,
  `Kostnadsstalle;Konto;Debet;Kredit;Text;Period`, F2 invariant decimal). UI:t lägger på UTF-8
  BOM så svenska tecken visas rätt i Excel/importverktyg.

## Byggrisk
Låg. Inga rörda delade filer. Nya filer i egna kataloger. Matchar befintliga signaturer
(`RaindanceKonteringsGenerator.GenerateEntries/GenerateFile/ValidateBalance`, `downloadFile`
JS-interop, `IDbContextFactory<RegionHRDbContext>`). SIE-exportern beror bara på KonteringsRad +
PayrollRun (redan refererade av IntegrationHub). Kunde ej bygga (parallella builds förbjudna).
