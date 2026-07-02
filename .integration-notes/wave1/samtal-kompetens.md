# Integration notes — slice `samtal-kompetens`

Kopplar medarbetarsamtal → kompetensgap → utvecklingsplan (i steg, med visualisering).

## Sammanfattning av vad som byggts
- **Gap-motor** (ren, testbar, ingen EF): `RegionHR.Competence.Services.CompetenceGapAnalyzer`
  räknar EmployeeSkill vs PositionSkillRequirement → `GapAnalys`/`SkillGap`.
- **Plan-generator** (ren): `RegionHR.Competence.Services.UtvecklingsplanGenerator`
  gör en `DevelopmentPlan` med en milstolpe per gap, kopplad till samtalet.
- **DevelopmentPlan/DevelopmentMilestone** utökade med spårbar länk + nivåspår
  (bakåtkompatibelt — inga befintliga signaturer ändrade).
- **Blazor**: ny detaljsida som driver hela samtalsprocessen via DbContext + domänmetoder,
  gap-stapel per skill, och en utvecklingsplanssida (process i steg).

## 1. DI-registreringar  (LÄGG i `AddInfrastructure` i src/Infrastructure/DependencyInjection.cs)
Båda är tillståndslösa → singletons. Lägg t.ex. nära de andra motorerna (rad ~124):
```csharp
// Competence — gap-analys + utvecklingsplan (slice samtal-kompetens)
services.AddSingleton<RegionHR.Competence.Services.CompetenceGapAnalyzer>();
services.AddSingleton<RegionHR.Competence.Services.UtvecklingsplanGenerator>();
```
Utan dessa kraschar sidorna `/kompetens/gapanalys`, `/kompetens/utvecklingsplaner`
och `/medarbetarsamtal/{id}` vid render (de `@inject`:ar motorerna).

## 2. Nya DbSet-rader
**INGA.** Jag återanvänder befintliga entiteter (`DevelopmentPlan`, `DevelopmentMilestone`,
`PerformanceReview`) som redan har DbSets. Inga nya tabeller.

## 3. EF Core-konfiguration
**INGEN ändring krävs.** Jag lägger bara till *nullable* kolumner på redan mappade entiteter;
EF Core mappar dem via konvention (befintliga `DevelopmentPlanConfiguration` /
`DevelopmentMilestoneConfiguration` i `Configurations/Competence/TalentConfiguration.cs`
behöver INTE röras). Nya kolumner (skapas automatiskt av `EnsureCreatedAsync`):
- `competence.development_plans.KopplatSamtalId`  (uuid, null)
- `competence.development_milestones.SkillId`     (uuid, null)
- `competence.development_milestones.FranNiva`    (int, null)
- `competence.development_milestones.MalNiva`      (int, null)

## 4. Schema/DB-varning (gäller hela vågen)
Appen kör `EnsureCreatedAsync()` (ingen migration). En **befintlig** dev-databas får inte
de nya kolumnerna automatiskt → droppa/återskapa DB-volymen vid integration (samma krav som
för alla wave-1-slices som lägger till kolumner/entiteter).

## 5. Seed-data (VALFRITT — demodata så /kompetens/utvecklingsplaner inte är tom)
Klistra i `SeedData.SeedAsync` DIREKT EFTER PerformanceReviews-blocket (efter att `reviewAnna`
skapats/genomförts, rad ~841) och FÖRE `await db.SaveChangesAsync();`. Variablerna `employees`,
`reviewAnna`, `lakemedel` finns redan i scope. Anna (SSK Avd 32) uppfyller allt utom
Läkemedelshantering (nivå 3, krav 4) → 1 milstolpe, verifierat mot seedens kravprofil:
```csharp
// === Utvecklingsplan ur Annas medarbetarsamtal (samtal → kompetensgap → plan) ===
var utvPlanAnna = RegionHR.Competence.Domain.DevelopmentPlan.Skapa(
    employees[0].Id.Value, "Sjukskoterska Avd 32",
    DateOnly.FromDateTime(DateTime.Today));
utvPlanAnna.KopplaTillSamtal(reviewAnna.Id);
utvPlanAnna.LaggTillMilstolpe(
    "Hoj \"Lakemedelshantering\" fran niva 3 till 4", "Skill",
    DateOnly.FromDateTime(DateTime.Today.AddMonths(3)), lakemedel.Id, 3, 4);
utvPlanAnna.Aktivera();
db.DevelopmentPlans.Add(utvPlanAnna);
```

## 6. NavMenu (VALFRITT men rekommenderat)
Lägg i Kompetens-gruppen i src/Web/Components/Layout/NavMenu.razor, nära `/kompetens/gapanalys`
(rad ~16–62):
```razor
<MudNavLink Href="/kompetens/utvecklingsplaner" Icon="@Icons.Material.Filled.Timeline">Utvecklingsplaner</MudNavLink>
```
Detaljsidan `/medarbetarsamtal/{id}` nås via knappen "Driv samtal" på `/medarbetarsamtal`
(ingen egen nav-post behövs).

## 7. Paket
Inga nya NuGet-paket.

## 8. Signaturändringar (bakåtkompatibla — inga anrop bryts)
- `DevelopmentPlan.LaggTillMilstolpe(...)` fick 3 nya **valfria** parametrar
  (`Guid? skillId=null, int? franNiva=null, int? malNiva=null`). Befintligt anrop i
  `src/Web/Components/Pages/Karriar/MinUtveckling.razor` och `TalentMarketplaceTests` kompilerar oförändrat.
- Nya publika medlemmar: `DevelopmentPlan.KopplatSamtalId`, `DevelopmentPlan.KopplaTillSamtal(Guid)`,
  `DevelopmentMilestone.SkillId/FranNiva/MalNiva`. Inga borttagningar.

## Filer i slicen
Nya:
- src/Modules/Competence/Services/CompetenceGapAnalyzer.cs
- src/Modules/Competence/Services/UtvecklingsplanGenerator.cs
- src/Web/Components/Pages/Medarbetarsamtal/Samtal.razor        (/medarbetarsamtal/{id})
- src/Web/Components/Pages/Kompetens/Utvecklingsplaner.razor     (/kompetens/utvecklingsplaner)
- tests/Competence.Tests/CompetenceGapAnalyzerTests.cs
- tests/Competence.Tests/UtvecklingsplanGeneratorTests.cs
Ändrade:
- src/Modules/Competence/Domain/DevelopmentPlan.cs              (länk + nivåspår, bakåtkompatibelt)
- src/Web/Components/Pages/Kompetens/GapAnalys.razor            (använder gap-motorn + stapel-vis)
- src/Web/Components/Pages/Medarbetarsamtal/Index.razor         ("Driv samtal"-knapp → detaljsida)
