# Wave 8 — BI/DW-export + realtids-beslutsstöd (key=bi-dw-export)

Integrationsvågen, effektmål #8–9: "realtidsdata och BI-lösningar för beslutsstöd".
Regionen kör Diver / Power BI / REDA DW → detta slice levererar (1) en
dimensionsmodellerad (stjärnschema) BI/DW-export i CSV + JSON och (2) en
realtids-beslutsstöds-dashboard för chefer/HR med KPI:er beräknade live ur DB.

## Nya filer

### Modul (ren, endast SharedKernel-beroende)
- `src/Modules/Analytics/Domain/BiExport/BiDimensionModel.cs`
  Stjärnschema-poster: `BiStjarnschema` + dimensioner (`BiDimTid`, `BiDimEnhet`,
  `BiDimBefattning`, `BiDimKon`, `BiDimAlder`) + fakta (`BiFaktaAnstallning`,
  `BiFaktaLon`, `BiFaktaFranvaro`). Enbart primitiver → serialiserbara rakt av.
- `src/Modules/Analytics/Domain/BiExport/BiExportGenerator.cs`
  Ren serialiserare (statisk). CSV (RFC 4180, komma, citering/escaping,
  **invariant** decimaltecken) + JSON (System.Text.Json, invariant) + platt
  denormaliserad anställnings-CSV. **UTF-8 utan BOM** (Power BI/Diver; ej Latin1
  som SIE). Metoder: `GenereraJson/GenereraJsonBytes`, `GenereraCsvPaket`
  (8 tabeller, filnamn→innehåll), `GenereraPlattAnstallningCsv`, `EscapeCsv`.

### Infrastruktur (statiska, ingen DB-åtkomst — anroparen laddar listorna)
- `src/Infrastructure/Reporting/BiDwExportBuilder.cs`
  Mappar domänentiteter (Employee/Employment, PayrollResult, LeaveRequest,
  OrganizationUnit) → `BiStjarnschema`. Kön härleds ur `Personnummer.LegalGender`,
  ålder ur `BirthDate`, tid ur period. `Bygg(employees, payrollResults,
  leaveRequests, enheter, snapshotDatum)`.
- `src/Infrastructure/Analytics/BeslutsstodKpiService.cs`
  Statisk KPI-motor per enhet + regionöversikt: personalomsättning %,
  sjukfrånvaro %, bemanningsgrad %, LAS-riskantal, lönekostnad/mån & /FTE.
  Returnerar `BeslutsstodResultat(Oversikt, PerEnhet, SnapshotDatum)`.
  LAS-trösklar grundade i `RegionHR.LAS.Domain.LASAccumulation`-konstanter.

### Web
- `src/Web/Components/Pages/Rapporter/Beslutsstod.razor`  — rutt `/rapporter/beslutsstod`
  (AdminLayout). Laddar data via injicerad `IDbContextFactory`, anropar de statiska
  tjänsterna, visar live-KPI-kort + tabell per enhet och laddar ner BI-exporten
  (JSON / platt CSV / dimensionsmodell som ZIP) via befintlig `downloadFile`-JS.
  Rör INTE befintliga `/rapporter/analytics` eller andra analytics-sidor.

### Tester (tests/Analytics.Tests/ — projektet refererar redan Analytics + Infrastructure)
- `tests/Analytics.Tests/BiExportGeneratorTests.cs`     (11 test: CSV-struktur, citering, invariant decimal, JSON, BOM)
- `tests/Analytics.Tests/BiDwExportBuilderTests.cs`      (10 test: fakta/dim-mappning, kön, ålder, OKÄND-fallback)
- `tests/Analytics.Tests/BeslutsstodKpiServiceTests.cs`  (10 test: omsättning, sjukfrånvaro-fönster, bemanning, LAS-risk, lönekostnad, översikt, tomt indata)

## Integration — vad orkestratorn behöver veta

- **DI (DependencyInjection.cs): INGA ändringar krävs.** Både `BeslutsstodKpiService`
  och `BiDwExportBuilder`/`BiExportGenerator` är **statiska** och anropas direkt från
  sidan (samma stil som `KPICalculationService.CalculateTrend`/`ParsePeriodDates`).
  Ingen scoped/transient-registrering behövs.
- **RegionHRDbContext.cs / DbSets: INGA ändringar.** Använder befintliga
  `Employees` (Include `Anstallningar`), `PayrollResults`, `LeaveRequests`,
  `Positions_Table`, `OrganizationUnits`.
- **Program.cs / *.csproj / Directory.*.props: INGA ändringar.** Endast
  framework-paket (System.Text.Json, System.IO.Compression) — inga nya NuGet-referenser.
- **RouteAccessPolicy.cs: INGEN ändring krävs.** `/rapporter` är redan `ChefHrAdmin`
  (prefix-matchning täcker `/rapporter/beslutsstod`) → exakt rätt målgrupp (chefer/HR).
- **Publika signaturer:** inga befintliga rörda.

### Valfri navigering (NavMenu.razor — RÖRS EJ av detta slice; lägg in vid behov)
Under Rapporter-gruppen:
```razor
<MudNavLink Href="/rapporter/beslutsstod" Icon="@Icons.Material.Filled.Insights">Beslutsstöd</MudNavLink>
```

## Byggrisk
Låg. Alla nya filer i egna kataloger; inga delade filer rörda. CA1861-säkrat
(rubrikrader extraherade till `static readonly`-fält). Nullable/warnings beaktade
(TreatWarningsAsErrors). Ej byggt lokalt (per instruktion — bygg fryser maskinen);
signaturer verifierade mot läst domänkod (Employee/Employment/PayrollResult/
LeaveRequest/Position/OrganizationUnit/Personnummer).
