# Slice: tid-flex — Tidrapport-attest + medarbetarstämpling + flexsaldo

## Sammanfattning
1. **Persistens-bugg fixad** (Tidrapporter/Detail.razor). Godkänn/avvisa läste tidigare
   `_ts` i `OnInitializedAsync` sin DbContext (redan disposad) och körde `SaveChangesAsync()`
   på en NY DbContext som aldrig spårade entiteten → attesten sparades aldrig. Nu läses
   tidrapporten i SAMMA context som sparar (`FirstOrDefaultAsync(Id)` → `Godkann/Avvisa` →
   `SaveChangesAsync`), och `_ts` uppdateras för UI:t.
2. **Behörighet på attest** (ingen självattest): godkänn/avvisa kräver `Auth.IsChef`
   (Chef/HR/Admin) OCH attestant ≠ tidrapportens ägare. Kollas i UI (visar varning) *och*
   server-side i båda metoderna.
3. **Medarbetar-stämpling** (ny sida `/minsida/stampling`): stämpla in/ut med statuskort,
   dagens pass och dagens händelser. Avvikelsedetektering mot schema (sen ankomst > 15 min,
   tidig avgång > 15 min, övertid, ej planerat pass). Kopplar och uppdaterar dagens
   `ScheduledShift` (FaktiskStart/FaktiskSlut via befintliga domänmetoder) + skapar `TimeClockEvent`.
4. **Flexsaldo** (ny domän + ny sida `/minsida/saldon` + chef-sida `/tidrapporter/flex`):
   ren `FlexCalculator` räknar flex ur stämplad faktisk tid vs schemalagd tid, med daglig
   flexgräns (kapning) och tak/golv (klampning). Flexinställningar per anställd sätts av chef/HR.
   Saldon-sidan visar flex/komp/semester för medarbetaren.

## KRÄVS av integratören

### 1. DbSets i `RegionHRDbContext.cs` (i Scheduling-regionen, `using RegionHR.Scheduling.Domain;` finns redan rad 5)
```csharp
    public DbSet<FlexInstallning> FlexInstallningar => Set<FlexInstallning>();
    public DbSet<FlexBalance> FlexBalances => Set<FlexBalance>();
```
Utan dessa kastar `db.Set<FlexInstallning>()` / `db.Set<FlexBalance>()` i FlexService runtime.
Nya nullable-kolumner/entiteter är OK (EnsureCreated + DB-wipe vid redeploy).

### 2. DI-registrering i `Program.cs` (bredvid övriga `AddScoped`, ~rad 66-70)
```csharp
builder.Services.AddScoped<StamplingService>();
builder.Services.AddScoped<FlexService>();
```
Båda ligger i `RegionHR.Web.Services` och tar `IDbContextFactory<RegionHRDbContext>` (samma
mönster som `SelfServiceApiClient`).

## REKOMMENDERAS (annars hamnar tabellerna i public-schema med PascalCase-kolumner)
Ny fil `src/Infrastructure/Persistence/Configurations/Scheduling/FlexConfiguration.cs`
(plockas upp automatiskt av `ApplyConfigurationsFromAssembly`). Fungerar utan denna via
konventioner — men detta ger schema `scheduling` + snake_case som resten av modulen:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Scheduling;

public class FlexInstallningConfiguration : IEntityTypeConfiguration<FlexInstallning>
{
    public void Configure(EntityTypeBuilder<FlexInstallning> b)
    {
        b.ToTable("flex_installningar", "scheduling");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.AnstallId).HasConversion(id => id.Value, v => EmployeeId.From(v)).HasColumnName("anstalld_id");
        b.Property(e => e.FlexAktiverad).HasColumnName("flex_aktiverad");
        b.Property(e => e.MaxPlusTimmar).HasColumnName("max_plus_timmar");
        b.Property(e => e.MaxMinusTimmar).HasColumnName("max_minus_timmar");
        b.Property(e => e.DagligFlexgransTimmar).HasColumnName("daglig_flexgrans_timmar");
        b.Property(e => e.SenastAndradAv).HasColumnName("senast_andrad_av");
        b.Property(e => e.UppdateradVid).HasColumnName("uppdaterad_vid");
    }
}

public class FlexBalanceConfiguration : IEntityTypeConfiguration<FlexBalance>
{
    public void Configure(EntityTypeBuilder<FlexBalance> b)
    {
        b.ToTable("flex_balances", "scheduling");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.AnstallId).HasConversion(id => id.Value, v => EmployeeId.From(v)).HasColumnName("anstalld_id");
        b.Property(e => e.SaldoTimmar).HasColumnName("saldo_timmar");
        b.Property(e => e.KompsaldoTimmar).HasColumnName("kompsaldo_timmar");
        b.Property(e => e.BeraknadTom).HasColumnName("beraknad_tom");
        b.Property(e => e.UppdateradVid).HasColumnName("uppdaterad_vid");
    }
}
```

## VALFRITT — seed för rikare flexdemo (`SeedData.cs`, efter ScheduledShifts.AddRange ~rad 806)
Utan detta är flexsaldot 0 tills en medarbetare stämplar in/ut. Historiska avslutade pass
med faktisk tid ger direkt ett saldo att titta på (Anna = employees[0]):
```csharp
// Historiska pass med faktisk tid för flexdemo (Anna, employees[0])
db.ScheduledShifts.AddRange(
    new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[0].Id,
        Datum = DateOnly.FromDateTime(DateTime.Today.AddDays(-3)), PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag,
        PlaneradStart = new TimeOnly(8,0), PlaneradSlut = new TimeOnly(16,0), Rast = TimeSpan.FromMinutes(30),
        FaktiskStart = new TimeOnly(8,0), FaktiskSlut = new TimeOnly(17,0), OvertidTimmar = 1m,
        Status = RegionHR.Scheduling.Domain.ShiftStatus.Avslutad, OBKategori = OBCategory.Ingen },   // +1.0h flex, +1h komp
    new RegionHR.Scheduling.Domain.ScheduledShift { Id = Guid.NewGuid(), SchemaId = schemaId, AnstallId = employees[0].Id,
        Datum = DateOnly.FromDateTime(DateTime.Today.AddDays(-2)), PassTyp = RegionHR.Scheduling.Domain.ShiftType.Dag,
        PlaneradStart = new TimeOnly(8,0), PlaneradSlut = new TimeOnly(16,0), Rast = TimeSpan.FromMinutes(30),
        FaktiskStart = new TimeOnly(8,15), FaktiskSlut = new TimeOnly(15,30),
        Status = RegionHR.Scheduling.Domain.ShiftStatus.Avslutad, OBKategori = OBCategory.Ingen });  // -0.75h flex
```

## Nya filer (mina)
- `src/Modules/Scheduling/Domain/FlexInstallning.cs` — inställningsentitet (gränser per anställd)
- `src/Modules/Scheduling/Domain/FlexBalance.cs` — sparad saldo-ögonblicksbild
- `src/Modules/Scheduling/Domain/FlexCalculator.cs` — REN beräkning + result-records
- `src/Web/Services/StamplingService.cs` — medarbetarstämpling mot IDbContextFactory
- `src/Web/Services/FlexService.cs` — flexberäkning/persistens + inställningar
- `src/Web/Components/Pages/MinSida/Stampling.razor` — `/minsida/stampling`
- `src/Web/Components/Pages/MinSida/Saldon.razor` — `/minsida/saldon`
- `src/Web/Components/Pages/Tidrapporter/Flex.razor` — `/tidrapporter/flex` (chef/HR)
- `tests/Scheduling.Tests/FlexCalculatorTests.cs` — 13 xUnit-tester

## Ändrade filer (mina)
- `src/Web/Components/Pages/Tidrapporter/Detail.razor` — detached-bugg fixad + attest-behörighet
- `src/Web/Components/Pages/Tidrapporter/Index.razor` — knapp till Flexhantering
- `src/Web/Components/Pages/MinSida/Index.razor` — två kort (Stämpling + Mina saldon)

## Signaturändringar
INGA. Befintliga publika signaturer orörda (Timesheet/ScheduledShift/TimeClockEvent
domänmetoder används oförändrade). `ScheduledShift.cs` och `Schedule.cs` INTE modifierade.

## Nav (NavMenu.razor är integratörens — INTE ändrad av mig)
Sidorna nås redan via UI: Min sida-korten → `/minsida/stampling` + `/minsida/saldon`,
och Tidrapporter-knappen → `/tidrapporter/flex`. Om nav-poster önskas:
- Min sida-grupp: "Stämpling" → `/minsida/stampling`, "Mina saldon" → `/minsida/saldon`
- Schema & Tid-grupp (Chef/HR): "Flexhantering" → `/tidrapporter/flex`

## Flexmodellens regler (för den som granskar/utökar)
- Flex/dag = faktiska − planerade timmar (ur `ScheduledShift.FaktiskaTimmar` vs `PlaneradeTimmar`).
- Endast dagar med instämplad faktisk tid räknas; ostämplade hoppas över.
- Dagsdelta kapas till ±`DagligFlexgransTimmar` (0 = ingen dagsgräns).
- Utgående saldo klampas till `[MaxMinusTimmar, MaxPlusTimmar]`; `MaxPlusTimmar == 0` = obegränsat tak.
- `FlexAktiverad == false` → saldot rörs inte alls.
- Kompsaldo = summa av `ScheduledShift.OvertidTimmar` i perioden.
```

## Byggrisk
LÅG. Nya services/entiteter/sidor. Enda hårda beroendet: de 2 DbSet-raderna + 2 DI-rader
ovan (annars runtime-fel på flex-sidorna). Stämpling+attest fungerar UTAN nya DbSets
(TimeClockEvents/ScheduledShifts/Timesheets finns redan).
