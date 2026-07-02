# Wave 5 — Slice: Löneutmätning (Kronofogden) + fackavgift i lönekörningen

Key: `utmatning-fackavgift`

## Sammanfattning
Nytt register för aktiva löneutmätningar (KFM) och fackavgifter per anställd, inkopplat i
`PayrollInputBuilder` som nu fyller `input.Loneutmatning` och `input.Fackavgift` ur registren
(tidigare hårdkodat till 0 kr). HR administrerar posterna på ny sida `/lon/utmatning`.

## VIKTIGT: Inga ändringar krävs i förbjudna filer
Entiteterna registreras i EF-modellen via mina `IEntityTypeConfiguration`-klasser (ligger i
`src/Infrastructure/Payroll/`), som plockas upp automatiskt av det befintliga
`modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegionHRDbContext).Assembly)` i
`RegionHRDbContext.OnModelCreating`. All kod använder `db.Set<Loneutmatning>()` /
`db.Set<Fackavgift>()`, så **inga DbSet-properties behöver läggas till** och
`EnsureCreatedAsync` skapar tabellerna `payroll.loneutmatningar` och `payroll.fackavgifter`
automatiskt. Ingen redigering av `RegionHRDbContext.cs`, `DependencyInjection.cs`, `Program.cs`
eller `NavMenu.razor` är nödvändig för att bygga/köra.

## dbsets (VALFRITT — endast för läsbarhet/framtida kod)
Koden fungerar utan dessa. Om ni ändå vill ha namngivna DbSets i `RegionHRDbContext.cs`
(under `// Payroll (schema: payroll)`):
```csharp
public DbSet<Loneutmatning> Loneutmatningar => Set<Loneutmatning>();
public DbSet<Fackavgift> Fackavgifter => Set<Fackavgift>();
```
(kräver `using RegionHR.Payroll.Domain;` — finns redan i filen)

## EF-config
Klart. Ligger i mina kataloger, auto-registreras:
- `src/Infrastructure/Payroll/LoneutmatningConfiguration.cs`
- `src/Infrastructure/Payroll/FackavgiftConfiguration.cs`

## route_policy
Ingen ändring krävs. `RouteAccessPolicy.cs` mappar redan prefixet `/lon` → `{HR, Admin}`,
och nya sidan ligger på `/lon/utmatning` (lönekänsligt → korrekt bakom HR/Admin).

## nav_entries (VALFRITT)
Sidan nås via direkt-URL och är rättighetsskyddad. Om ni vill exponera den i menyn:
lägg en post under Lön-gruppen i `NavMenu.razor` (visas för HR/Admin), t.ex.:
```razor
<MudNavLink Href="/lon/utmatning" Icon="@Icons.Material.Filled.Gavel">Utmätning &amp; fackavgift</MudNavLink>
```

## di_registrations
Inga. Sidan injicerar `IDbContextFactory<RegionHRDbContext>` direkt (redan registrerat).

## signature_changes
Inga publika signaturer ändrade. `PayrollInputBuilder.BuildAsync(...)` är oförändrad;
den fyller nu `input.Loneutmatning`/`input.Fackavgift` från registren i stället för 0 kr.

## Filer skapade
- `src/Modules/Payroll/Domain/Loneutmatning.cs` (entitet + `UtmatningTyp` + `BeraknaAvdrag`)
- `src/Modules/Payroll/Domain/Fackavgift.cs` (entitet + `FackavgiftTyp` + `BeraknaAvgift`)
- `src/Infrastructure/Payroll/LoneutmatningConfiguration.cs`
- `src/Infrastructure/Payroll/FackavgiftConfiguration.cs`
- `src/Web/Components/Pages/Lon/Utmatning.razor` (`/lon/utmatning`)
- `tests/Payroll.Tests/UtmatningFackavgiftTests.cs` (18 domäntester)
- `tests/RegionHR.Infrastructure.Tests/Payroll/PayrollInputBuilderDeductionsTests.cs` (8 builder-tester)

## Filer ändrade
- `src/Infrastructure/Payroll/PayrollInputBuilder.cs` — läser registren och fyller nettoavdragen.

## Domänbeslut / regler
- **Löneutmätning** (Utsökningsbalken 7 kap.): fast belopp per månad ELLER andel av netto,
  KFM-målnummer, förbehållsbelopp, giltighetsperiod. `BeraknaAvdrag` kapar alltid avdraget så
  att netto efter skatt aldrig understiger förbehållsbeloppet. Flera samtidiga beslut använder
  det HÖGSTA förbehållsbeloppet (dubbelräknas ej) och fördelas i turordning efter startdatum.
- **Fackavgift**: fast belopp ELLER procent av bruttolön; frivilligt nettoavdrag efterställt utmätning.
- **Förbehållsbelopp i builder**: eftersom motorn beräknar skatt EFTER att builder kört, används en
  medvetet FÖRSIKTIG nettouppskattning (proportionerad grundlön × (1 − kommunalskatt); default
  32 % när kommunalskattesats saknas). Uppskattningen exkluderar tillägg och underskattar nettot,
  vilket garanterar att vi aldrig drar mer än vad som säkert finns över förbehållsbeloppet; exakt
  avstämning sker mot lönespecifikationen. Motorn tillämpar sedan beloppet i
  `Netto = Brutto − Skatt − Loneutmatning − Fackavgift − OvrigaAvdrag`.

## build_risk
Låg. Inga förbjudna filer rörda; entiteter auto-registreras via configs. Har EJ byggt (parallella
builds). Verifierat mot befintliga signaturer: `Money`, `EmployeeId.From`, `Employment.Manadslon`/
`Sysselsattningsgrad.Value`, `Employee.KommunalSkattesats`, `PayrollInput.Loneutmatning/Fackavgift`,
`ApplyConfigurationsFromAssembly`-mönstret, MudTabs/MudTabPanel-API. Inga `List<string>`/collection
mappas som jsonb (endast text/decimal/Money/enum-som-string/DateOnly). Befintligt test
`PayrollInputBuilderTests` fortsätter passera (0 register → 0 kr).
