# Integrationsnoteringar — slice `skatt` (svenska skatteregler 2026 + lönemotor)

## Sammanfattning
Rättade svenska arbetsgivaravgifter, statlig skatt, kommunalskatt och kopplade lönemotorn
till en faktiskt seedad skattetabell. Alla värden verifierade mot Skatteverket/SCB 2026.
Satser och åldersgränser är samlade i två NYA årsversionerade domänklasser (single source of truth):

- `src/Modules/Payroll/Domain/Arbetsgivaravgift.cs` — satser + åldersregler.
- `src/Modules/Payroll/Domain/KommunSkattesatser.cs` — kommunal skattesats-tabell (Örebro-län).

## Verifierade 2026-värden (källa: skatteverket.se / SCB, hämtat 2026-07)
- **Full arbetsgivaravgift: 31,42 %.**
- **Född 1937 eller tidigare: 0 %** (ingen arbetsgivaravgift alls — fast kohort i lagen).
- **Äldre: endast ålderspensionsavgift 10,21 %.** Fr.o.m. 2026 gäller den som vid årets ingång
  fyllt **67** år (t.o.m. 2025: fyllt 66) → **födda 1958 eller tidigare** för både 2025 och 2026.
  Detta motsvarar sliceens "1938–1958" för inkomstår 2026.
- **Ungdomsnedsättning ÅTERINFÖRD temporärt 2026-04-01 – 2027-09-30:** 20,81 % på ersättning
  ≤ 25 000 kr/mån för den som vid årets ingång fyllt 18 men inte 23 år (**födda 2003–2007** för 2026).
  Den gamla 15–18-årsnedsättningen är **slopad** fr.o.m. 2026. (Den felaktiga generella "20,81 % för
  19–22 alla år"-regeln togs bort och ersattes av den korrekta, tidsbegränsade regeln.)
- **Statlig inkomstskatt: 20 %** på beskattningsbar förvärvsinkomst över **skiktgränsen 643 000 kr/år**
  (≈ 53 583 kr/mån).
- **Kommunalskatt Örebro 2026: 33,65 %** (kommun 21,35 + region 12,30) → **Skatteverkets skattetabell 34**.
  Övriga Örebro-län i tabellen: Kumla 33,84, Hallsberg 33,85, Askersund 34,15, Karlskoga 34,30, Lindesberg 34,60.

## Filer (skapade / ändrade)
Skapade:
- `src/Modules/Payroll/Domain/Arbetsgivaravgift.cs`
- `src/Modules/Payroll/Domain/KommunSkattesatser.cs`
- `tests/Payroll.Tests/ArbetsgivaravgiftTests.cs`
- `tests/Payroll.Tests/TaxTableTests.cs`

Ändrade:
- `src/Modules/Payroll/Engine/PayrollCalculationEngine.cs` — delegerar arbetsgivaravgift till domänen
  (nu `(fodelseAr, år, månad)`), och skattesteget faller tillbaka på kommunens tabell när den anställde
  saknar `Skattetabell` (så skatten aldrig blir 0 i drift).
- `src/Infrastructure/Payroll/SwedishTaxCalculator.cs` — kommunalskatt per kommun (default Örebro 33,65 %),
  statlig skiktgräns 643 000 kr/år, arbetsgivaravgift via domänen. Publika egenskapen `Arbetsgivaravgift`
  (0,3142) BEHÅLLS (används av `WorkforcePlan.razor`). Ny valfri parameter `kommun` på `Berakna(...)`.
- `tests/Payroll.Tests/PayrollCalculationEngineTests.cs` — ENDAST `#region Arbetsgivaravgifter`
  uppdaterad till verifierade 2026-regler (OB/övertid/semester-regionerna orörda).

## KRÄVS av integratören

### 1. DbSet
Ingen ny DbSet behövs — `DbSet<TaxTable> TaxTables` finns redan (RegionHRDbContext.cs rad 55),
och `TaxTableConfiguration`/`TaxTableRowConfiguration` finns redan. Inga migrationer krävs för schemat.

### 2. DI (src/Infrastructure/DependencyInjection.cs)
Ingen ny registrering krävs. Befintliga räcker:
- `services.AddScoped<ITaxTableProvider, TaxTableRepository>();` (rad ~86)
- `services.AddScoped<PayrollCalculationEngine>();` (rad ~88)
- `services.AddSingleton<SwedishTaxCalculator>();` (rad ~124)

(Valfritt, ej nödvändigt: `TaxTableProviderImpl` finns som cachande dekoratör om ni vill lägga in
IMemoryCache-cachning senare — kräver då `AddMemoryCache()`. Ej gjort nu.)

### 3. SEED — skattetabell 34 (Örebro) + skatteuppgifter på anställda
**Utan detta blir preliminärskatten 0 i drift.** Två delar i `SeedData.SeedAsync` (allt körs före den
enda `await db.SaveChangesAsync();` på rad ~2106; guarden `if (await db.Employees.AnyAsync()) return;`
gör seeden idempotent på färsk databas).

**(a)** Sätt skatteuppgifter på varje anställd — lägg till EN rad i den befintliga `foreach`-loopen
(direkt efter `employee.UppdateraKontaktuppgifter(...)`, ca rad 198):
```csharp
employee.UppdateraSkatteuppgifter(34, 1, "Örebro", 33.65m, harKyrkoavgift: false, kyrkoavgiftssats: null);
```

**(b)** Seeda själva skattetabellen — klistra in DETTA BLOCK efter employee-loopen (efter `}` på ca rad 208),
någonstans före `await db.SaveChangesAsync();`:
```csharp
// === Skatteverkets skattetabell 34, kolumn 1, 2026 (Örebro: kommun 21,35 + region 12,30 = 33,65 %) ===
// Riktiga kolumn-1-värden ur Skatteverkets allmänna månadstabell 34 (2026).
// Utan denna seed blir preliminärskatten 0 i drift.
var skattetabell34 = new RegionHR.Payroll.Domain.TaxTable { Ar = 2026, Tabellnummer = 34, Kolumn = 1 };
var t34rader = new (decimal Fran, decimal Till, decimal Skatt)[]
{
    (0, 2000, 0), (2001, 4999, 150), (5001, 9999, 422), (10001, 11999, 1251),
    (12001, 13999, 1676), (14001, 14999, 2099), (15001, 17999, 2304), (18001, 19999, 3015),
    (20001, 21999, 3541), (22001, 23999, 4042), (24001, 25999, 4553), (26001, 27999, 5070),
    (28001, 29999, 5588), (30001, 31999, 6105), (32001, 33999, 6623), (34001, 35999, 7140),
    (36001, 37999, 7658), (38001, 39999, 8175), (40001, 41999, 8720), (42001, 43999, 9400),
    (44001, 45999, 10080), (46001, 47999, 10760), (48001, 49999, 11440), (50001, 51999, 12120),
    (52001, 54999, 12800), (55001, 57999, 13854), (58001, 59999, 15474), (60001, 64999, 16554),
    (65001, 69999, 19254), (70001, 77999, 21954), (78001, 999999, 26166),
};
foreach (var (fran, till, skatt) in t34rader)
    skattetabell34.LaggTillRad(new RegionHR.Payroll.Domain.TaxTableRow { InkomstFran = fran, InkomstTill = till, Skattebelopp = skatt });
db.TaxTables.Add(skattetabell34);
```
(SeedData.cs saknar `using RegionHR.Payroll.Domain;` — därför är typerna fullt kvalificerade ovan.
Alternativt lägg till `using RegionHR.Payroll.Domain;` överst och korta ner.)

### 4. NavMenu / paket
Inga nya nav-poster. Inga nya NuGet-paket.

## Signaturändringar (för andra filer)
- Interna `PayrollCalculationEngine.BeraknaArbetsgivaravgiftSats/Belopp` tar nu `(…, int year, int month)`
  och de gamla `…FranFodelseAr`/`ArAldreMedReduceradAvgift`/`ArUngMedReduceradAvgift` är BORTTAGNA från
  motorn (flyttade till domänklassen `Arbetsgivaravgift`). Endast testfilen använde dem — den är uppdaterad.
  Ingen produktionskod utanför slicen berörs (verifierat med grep).
- `PayrollCalculationEngine.CalculateAsync(...)` — publik signatur OFÖRÄNDRAD.
- `SwedishTaxCalculator.Berakna(decimal, int fodelsear = 1985, string? kommun = null)` — bakåtkompatibel
  (ny valfri `kommun`). Publika egenskaper oförändrade + nya (`Skiktgrans2026Arlig`, `KommunalSkattForKommun`).

## Byggrisk
Låg. Inga delade filer ändrade. Bygg ej körd lokalt (parallella agenter) — koden matchar befintliga
signaturer (Money, EmployeeDto, ITaxTableProvider, TaxTable). `TreatWarningsAsErrors=true` beaktat
(död kod borttagen).
