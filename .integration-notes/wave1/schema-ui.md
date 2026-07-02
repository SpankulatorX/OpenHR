# Integrationsnoteringar — slice `schema-ui`

Visuellt schemagränssnitt + grundschema i UI + ATL-validering där schemat ändras.

## Sammanfattning av ändringar
1. **NyttSchema.razor** — nu kan man välja **Grundschema** (rullande mall) eller **Periodschema**. Grundschema anropar `Schedule.SkapaGrundschema(enhetId, namn, start, cykelVeckor)`.
2. **Ny sida `Redigera.razor`** (`/schema/redigera` + `/schema/redigera/{SchemaId:guid}`) — visuell vecko/dag-grid: rader = aktiva anställda på schemats enhet, kolumner = 7 dagar. Lägg till/ta bort pass per anställd/dag.
3. **ATL-validering vid manuell inläggning** — när ett pass läggs till körs `ArbetstidslagenValidator.ValidateNyttPass(...)` (dygnsvila §13, veckovila §14, veckoarbetstid §5, nattarbetstid §13a). Brott visas i realtid; hårt brott kräver explicit "lägg till ändå"-bekräftelse (systemet är experten). Vård-toggle byter dygnsvila 11h→9h (AB/HÖK-undantag).
4. **SchemaOptimizer** — `ViloRegelBrott` var hårdkodat `0`; räknas nu på riktigt via `ArbetstidslagenValidator.ValidateSchedule` (dygns- + veckovilebrott).

## DI-registreringar
**Inga nya krävs.** Sidorna använder redan globalt injicerad `IDbContextFactory<RegionHRDbContext>`, `ISnackbar` och `NavigationManager`. `ArbetstidslagenValidator` instansieras direkt i sidan (ingen DI). `SchemaOptimizer` är redan registrerad (`services.AddSingleton<SchemaOptimizer>();` rad 133 i `DependencyInjection.cs`) — ingen ändring.

## Nya DbSet-rader
**Inga.** `DbSet<Schedule> Schedules` och `DbSet<ScheduledShift> ScheduledShifts` finns redan (RegionHRDbContext rad 59–60). EF-konfig (`ScheduleConfiguration`, `ScheduledShiftConfiguration`) oförändrad och räcker.

## Seed-data
**Ingen ändring krävs.** `SeedData.SeedAsync` skapar redan ett grundschema för Avdelning 32 (`SeedData.cs` ~rad 792) med pass på dagens datum. Det schemat är direkt redigerbart via `/schema/redigera` (välj det i väljaren). Griden visar rader för de anställda vars aktiva anställning ligger på schemats enhet.

## NavMenu-poster (VALFRITT)
Index-sidan (`/schema`) länkar redan till editorn via knappen **"Redigera pass"**, så ingen NavMenu-post är strikt nödvändig. Vill man ändå ha en egen menypost under Schema-gruppen i `NavMenu.razor`:
```razor
<MudNavLink Href="/schema/redigera" Icon="@Icons.Material.Filled.GridView">Redigera pass</MudNavLink>
```

## Paket
Inga nya paket. Använder befintlig MudBlazor 9.1 (`MudTimePicker`, `MudSwitch`, `MudCheckBox`, `MudSimpleTable`).

## Signaturändringar (bakåtkompatibelt)
- `Schedule` (MY file): **ny** publik metod `bool TaBortPass(Guid passId)`. Additiv.
- `ArbetstidslagenValidator` (MY file): **nya** publika metoder `static ShiftAssignment TillShiftAssignment(ScheduledShift)` och `ValidationResult ValidateNyttPass(ScheduledShift nyttPass, IEnumerable<ScheduledShift> befintligaPass)`. Additiva; inga befintliga signaturer ändrade.
- `SchemaOptimizer.Optimera(SchemaRequest)` → `SchemaForslag` (MY file per slice): **oförändrad** signatur och `SchemaForslag`-recordform. Endast internräkningen av `ViloRegelBrott` ändrad. Inga befintliga anropare påverkas (endast DI-registrerad, ingen konsument i UI/API läser fältet positionellt).

## OBS — sökvägsavvikelse (viktigt för integratören)
Sliceet listade `SchemaOptimizer` under `src/Modules/Scheduling/Optimization/`, men filen ligger faktiskt i **`src/Infrastructure/Scheduling/SchemaOptimizer.cs`**. Det är den fil jag ändrat (den med det hårdkodade `ViloRegelBrott: 0`). Den finns INTE i listan över delade/förbjudna filer, och ingen annan agent förväntas äga den. Om någon annan slice också rör Infrastructure/Scheduling: endast `SchemaOptimizer.cs` är berörd av mig.

## Verifierade lagvärden (2026)
- ATL §13 dygnsvila = **11 h** sammanhängande / 24h — Arbetstidslag (1982:673) §13, Arbetsmiljöverket.
- ATL §14 veckovila = **36 h** sammanhängande / 7 dygn — ATL §14.
- Vårdundantag dygnsvila = **9 h** (AB/HÖK, befintlig konstant `MIN_DYGNSVILA_SJUKVARD`, oförändrad).
Alla värden återanvänds från befintliga konstanter i `ArbetstidslagenValidator` — inga nya magiska konstanter införda.

## Filer
Skapade:
- `src/Web/Components/Pages/Schema/Redigera.razor`
- `tests/Scheduling.Tests/SchemaRedigeraTests.cs`
- `tests/Scheduling.Tests/SchemaOptimizerTests.cs`

Ändrade:
- `src/Web/Components/Pages/Schema/NyttSchema.razor` (grundschema-val + länk till editor)
- `src/Web/Components/Pages/Schema/Index.razor` ("Redigera pass"-knapp)
- `src/Modules/Scheduling/Domain/Schedule.cs` (`TaBortPass`)
- `src/Modules/Scheduling/Domain/ArbetstidslagenValidator.cs` (`ValidateNyttPass`, `TillShiftAssignment`)
- `src/Infrastructure/Scheduling/SchemaOptimizer.cs` (räkna `ViloRegelBrott` på riktigt)

## Byggrisk
Låg. Ingen delad fil rörd. Nullable/`TreatWarningsAsErrors` beaktat (null-forgiving där flödesanalys inte kan bevisa non-null i markup/lambda). Kunde ej köra `dotnet build` (regel: inga parallella builds).
