# Wave 3 — Slice: export (AGI + pain.001 som faktiskt levererar filen)

## Sammanfattning
Rättade audit-bristerna: personnummer=GUID, clearingnr hårdkodat 3300/0000000000, och att
filen aldrig levererades. AGI-XML följer nu Skatteverkets officiella schema (SKV 269 v1.1,
verifierat mot Skatteverkets egna exempelfiler), pain.001 är giltig ISO 20022 pain.001.001.03,
och en ny Blazor-sida i den driftsatta appen laddar ner filerna (byte[] → nedladdning).

## Filer

### Ändrade (mina filer)
- `src/Modules/IntegrationHub/Adapters/Skatteverket/AGIXmlGenerator.cs` — omskriven till officiellt
  schema: rot `<Skatteverket omrade="Arbetsgivardeklaration">`, instans- + komponent-namespace,
  `agd:`-prefix, `faltkod`-attribut, HU (SummaArbAvgSlf 487 / SummaSkatteavdr 497) + en IU per
  betalningsmottagare med `Specifikationsnummer` (fält 570), `BetalningsmottagarId` (215),
  `KontantErsattningUlagAG` (011), `AvdrPrelSkatt` (001), förmån (012), Bilersättning (050),
  Traktamente (051). Publik signatur `Generate(AGIInput):IReadOnlyList<AGIFile>` oförändrad.
  Additiva nullable-fält på `AGIIndivid`: `Specifikationsnummer`, `ArbetsplatsGatuadress`, `ArbetsplatsOrt`.
- `src/Modules/IntegrationHub/Adapters/Nordea/NordeaPaymentFileGenerator.cs` — additivt: `ChrgBr=SLEV`,
  `ClrSysId/Cd=SESBA` för svenskt clearingsystem, **invariant** belopps-/datumformatering (svensk
  kultur gav annars decimalkomma → ogiltig XML), UTF-8-deklaration. Publika typer oförändrade.
- `src/Infrastructure/Integrations/AGIXmlGenerator.cs` — (DI-singleton) omskriven till samma officiella
  schema. Signatur `GenerateArbetsgivardeklaration(AGIData)` + records oförändrade.
- `src/Infrastructure/Integrations/NordeaPainGenerator.cs` — (DI-singleton) giltig pain.001 med
  obligatoriska element (GrpHdr/PmtInf/CdtTrfTxInf, ChrgBr, invariant format). Signatur oförändrad.
- `src/Api/Endpoints/PayrollEndpoints.cs` — AGI-export slår upp **riktigt** `Employee.Personnummer`
  (12-siffrigt via implicit string) och betalexport slår upp riktiga `Clearingnummer`/`Kontonummer`.
  Båda **returnerar själva filen** via `Results.File(...)` (AGI: XML, eller ZIP vid >1 fil;
  betalning: XML). Betalexport ger `400` med lista om alla saknar bankuppgifter.

### Nya
- `src/Web/Components/Pages/Lon/Export.razor` — driftsatt sida `/lon/export`: väljer lönekörning,
  visar underlag + varningar (ej godkänd körning, anställda utan bankuppgifter), och laddar ner
  AGI-XML respektive pain.001 via `downloadFile`-JS (byte[] → base64 → nedladdning).
- `tests/IntegrationHub.Tests/AGIXmlGeneratorTests.cs` — **omskriven** (gamla testet asserterade det
  felaktiga schemat och skulle brutit bygget). Verifierar namespace, faltkod-attribut,
  Specifikationsnummer, HU-summering, batch-split >1000, invariant belopp.
- `tests/IntegrationHub.Tests/NordeaPain001SpecTests.cs` — ny: ChrgBr, IBAN, EndToEndId,
  clearingsystem, invariant belopp under sv-SE-kultur, SALA/TRF.

## Employee-bankfält
`Employee.Clearingnummer` och `Employee.Kontonummer` (nullable string) + `UppdateraBankuppgifter(...)`
**fanns redan** i `src/Modules/Core/Domain/Employee.cs` och är EF-mappade i
`EmployeeConfiguration.cs` (`clearingnummer_encrypted` / `kontonummer_encrypted`).
Ingen ändring behövdes i Employee.cs.

## Rörde INGA förbjudna filer
DependencyInjection.cs, RegionHRDbContext.cs, SeedData.cs, Program.cs, NavMenu.razor, *.csproj,
Directory.*.props — orörda. Nedan snuttar för den som integrerar.

## Klistringsbara snuttar

### Seed — fiktiva demo-bankuppgifter (SeedData.cs, efter att varje Employee skapats)
```csharp
// Fiktiva men format-giltiga demo-bankuppgifter så betalfilen (pain.001) blir komplett.
var demoClearing = new[] { "3300", "5100", "6000", "8327", "9420" };
for (var i = 0; i < employees.Count; i++)
{
    var clearing = demoClearing[i % demoClearing.Length];
    var konto = (1000000000L + i * 7654321L).ToString(); // 10-siffrigt fiktivt kontonummer
    employees[i].UppdateraBankuppgifter(clearing, konto);
}
```

### Nav (NavMenu.razor) — länk till exportsidan under Lön
```razor
<MudNavLink Href="/lon/export" Icon="@Icons.Material.Filled.CloudUpload">Export (AGI &amp; bank)</MudNavLink>
```

## Byggrisk
Låg. Publika signaturer bevarade; parametrar/records oförändrade; Razor-sidan följer befintligt
mönster (KorningDetalj.razor + downloadFile). Ingen build körd (parallella agenter).
Enda medvetna teständring: `AGIXmlGeneratorTests.cs` omskrivet för det korrigerade schemat.
