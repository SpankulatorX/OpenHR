# Våg 3 — Slice: rekrytering (vakans → ansökan → intervju → erbjudande → TILLSÄTTNING → ANSTÄLLNING)

## Vad som stängdes
Kedjan rekrytering → anställning är nu komplett. En kandidat som fått ett erbjudande kan
tillsättas, varvid systemet:
1. tillsätter vakansen (`Vacancy.Tillsatt`, ansökan → `Anstalld`),
2. skapar ett **riktigt** `Employee` + `Employment` via Core-domänens publika API
   (`Employee.Skapa` + `LaggTillAnstallning`) — Core-domänfiler orörda,
3. skapar och persisterar en `OnboardingChecklist` kopplad till den nya anställningen.

Efter tillsättning är personen en **sökbar anställd i personalregistret** (`/anstallda`),
och UI:t navigerar direkt till den nya personalakten `/anstallda/{id}`.

## Nya filer
- `src/Modules/Recruitment/Services/KandidatKonvertering.cs` — ren, I/O-fri konverterare
  (`KandidatKonvertering.TillsattTillAnstalld`) + records `KandidatAnstallningsData`,
  `KandidatAnstallningsResultat`. Validerar LAS via `Employment.Validera` FÖRE någon
  tillståndsändring (ingen halv-tillsättning) och kräver att ansökan är i status `Erbjudande`.
- `src/Web/Services/RekryteringService.cs` — Blazor-tjänst (injicerar
  `IDbContextFactory<RegionHRDbContext>`). Läser vakanser/pipeline + onboarding, driver
  pipeline-stegen (bedöm/intervju/erbjud/avslå) och kör tillsättningen atomiskt i en
  DbContext (Employee + OnboardingChecklist + Vacancy sparas tillsammans).
- `tests/Recruitment.Tests/KandidatKonverteringTests.cs` — 9 xUnit-tester för
  tillsätt→anställd (personnummer via `Personnummer.CreateValidated`).

## Ändrade filer (endast egna slice-filer)
- `src/Web/Components/Pages/Rekrytering/Vakanser.razor` — interaktiv pipeline: bedöm → boka
  intervju → **erbjud tjänst** → **Tillsätt & anställ** (formulär förifyllt från vakansen).
- `src/Web/Components/Pages/Rekrytering/Onboarding.razor` — visar nu de **riktiga**,
  persisterade onboarding-checklistorna (tidigare mock) + kryssa steg som klara.
- `src/Web/Components/Pages/Rekrytering/Pipeline.razor` — går via `RekryteringService`
  (fixar tidigare saknad `Include(v => v.Ansokngar)` → visade tomma pipelines).

## KLISTRA IN: DI-registrering i src/Web/Program.cs
Lägg bredvid de andra `AddScoped`-raderna (t.ex. efter `AddScoped<AnstallningService>()`):

```csharp
builder.Services.AddScoped<RegionHR.Web.Services.RekryteringService>();
```

## DbContext / DbSets
Inga ändringar krävs. Använder befintliga `DbSet<Vacancy> Vacancies`,
`DbSet<OnboardingChecklist> OnboardingChecklists`, `DbSet<Employee> Employees`.

## Seed
Inga ändringar krävs. Befintliga seedade vakanser (Sjukskoterska m. 2 sökande i status
Mottagen) räcker för att gå hela vägen till tillsättning i UI:t.

## Signaturändringar
Inga publika signaturer ändrade. Enbart tillägg. `RecruitmentService` (befintlig) orörd.

## Byggrisk
Låg. Speglar exakt befintliga mönster (AnstallningService + NyAnstalld.razor). Ej byggt
(parallella byggen förbjudna). Enda runtime-kravet är DI-raden ovan — utan den kastar
`/rekrytering/vakanser`, `/rekrytering/onboarding` och `/rekrytering/pipeline` vid render.
