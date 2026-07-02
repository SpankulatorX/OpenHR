# Wave 6 — Behovsstyrd schemaautomatik (key=vardschema-automatik)

## Vad som byggts
Behovsdriven auto-generering av dygnet-runt-vårdscheman som bygger PÅ befintlig
`ConstraintScheduleSolver` (lösaren duplicerades INTE). Tar bemanningsbehov ur en
`StaffingTemplate` (min/optimal antal per veckodag/passtyp/tid/kompetens), expanderar dem
till en konkret period, låter constraint-lösaren tilldela pass (ATL + rättvisefördelning),
och rapporterar täckningsgrad (under-/överbemanning) per pass. ATL-varningar surfas ärligt —
lösaren lämnar hellre ett pass obemannat än schemalägger olagligt.

## Filer skapade
- `src/Modules/Scheduling/Optimization/BehovsstyrdSchemaGenerator.cs`
  - `BehovsstyrdSchemaGenerator.Generera(BehovsstyrdSchemaRequest) -> SchemaForslag`
  - Modeller: `BehovsstyrdSchemaRequest` (+ `FranMall(...)`), `SchemaForslag`, `PassTackning`,
    enums `BemanningsMal {Minimum, Optimal}`, `BemanningsLage {Underbemannad, Balanserad, Overbemannad}`
- `src/Web/Components/Pages/Schema/Automatik.razor` — sida `/schema/automatik`
- `tests/Scheduling.Tests/BehovsstyrdSchemaGeneratorTests.cs` — 10 xUnit-tester

## Filer ändrade
- `src/Modules/Scheduling/Optimization/ConstraintScheduleSolver.cs`
  - ADDITIV, bakåtkompatibel ändring: fältet `_atlValidator` sätts nu via konstruktor.
    Ny ctor `ConstraintScheduleSolver(ArbetstidslagenValidator)` gör att sjukvårdsprofilen
    (9h dygnsvila) kan skickas in. Parameterlös ctor bevarad och beter sig identiskt.
    Alla befintliga anrop (`new ConstraintScheduleSolver()` i WFMEndpoints, SchedulingEndpoints,
    Optimering.razor, ConstraintSolverTests) fungerar oförändrat. Inga publika signaturer bröts.

## Integrationskrav för värden (klistra in)

### DbSets
INGA NYA. Sparfunktionen återanvänder befintliga DbSets:
- `db.Schedules` (Schedule/Periodschema + owned `Pass` via `HasMany(e => e.Pass)`)
- `db.SchedulingRuns` (körningshistorik, syns även i /schema/optimering)
Alla redan registrerade i `RegionHRDbContext` (Schedules, ScheduledShifts, StaffingTemplates,
SchedulingRuns) med value-converters på plats. Ingen migration behövs.

### DI-registreringar
INGA. `BehovsstyrdSchemaGenerator` instansieras direkt i sidan (`new BehovsstyrdSchemaGenerator()`),
precis som `ConstraintScheduleSolver` i Optimering.razor. Ingen service att registrera.

### Route policy (RouteAccessPolicy.cs)
INGEN NY RAD. Rutten `/schema/automatik` matchas av befintligt prefix `("/schema", ChefHrAdmin)`
→ redan skyddad för Chef/HR/Admin. Ingen ändring i RouteAccessPolicy.cs.

### NavMenu
Valfritt. Sidan nås via länk och direkt-URL. Om en menypost önskas under Schema-gruppen:
`/schema/automatik` — "Schemaautomatik". (Inte nödvändigt; RÖRDE EJ NavMenu.razor.)

### Program.cs / DependencyInjection.cs / SeedData.cs
INGA ändringar. (Rörde dem inte.)

## Beroenden som redan fanns och användes
- `ConstraintScheduleSolver`, `ArbetstidslagenValidator` (9h vård / 11h standard, veckovila 36h,
  veckoarbetstid 40h, nattarbetstid 8h)
- `StaffingTemplate` / `StaffingRequirementLine` (bemanningsbehov)
- `Schedule.SkapaPeriodschema` + `LaggTillPass`, `SchedulingRun.Starta/Slutfor`
- `db.Skills` + `db.EmployeeSkills` för kompetenskarta (UI bygger PersonalInfo med riktiga kompetenser)

## Testtäckning (10 tester)
Full täckning vid tillräcklig personal (100% + ATL-kompliant + optimalnivå), ärlig
underbemanningsrapport vid personalbrist (50%, fortfarande lagligt), dygnsvila-brott ger
obemannat pass i st f lagbrott, sjukvårdsundantag 9h ger högre täckning än 11h,
rättvis helgfördelning, kompetensstyrd tilldelning, minimumsmål, klassificering av
under-/överbemanning, FranMall-bygge, samt undantag vid period utan slutdatum.

## Byggrisk
LÅG. Additiv solver-ctor är bakåtkompatibel. Ny generator + sida + tester i egna filer.
Nullable/warnings-as-errors respekterat (inga nya varningar avsedda). Byggde EJ lokalt
(per instruktion — matchade befintliga signaturer via kodläsning).
