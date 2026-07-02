# Wave 7 — scorm-lms (E-learning / utbildningsportal med kursinnehåll + externa deltagare)

Grade-ersättning: LMS hade tidigare Course/CourseEnrollment men INGET kursinnehåll,
ingen SCORM-hantering och ingen extern deltagarhantering. Denna slice bygger:
lektioner, SCORM-uppladdning + manifest-tolkning, en kursspelare med
genomförandespårning, och inbjudan/åtkomst för externa (icke-anställda) deltagare.

## Nya filer

### Domän (src/Modules/LMS/Domain/)
- `Lesson.cs` — lektion (Text / Video-URL / Fil / SCORM) i en kurs, ordnad via `Ordning`.
- `ScormPackage.cs` — uppladdat SCORM-paket (metadata) + `ScormVersion`-enum + `ScormManifestInfo`-record.
- `ScormManifestParser.cs` — namnrymds-agnostisk tolk av `imsmanifest.xml` (SCORM 1.2 + 2004).
- `LessonCompletion.cs` — genomförd-lektion-spårning (intern + extern) + `BeraknaGrad()`.
- `ExternalParticipant.cs` — extern deltagare (utan anställning) + `ExternalParticipantStatus`.
- `ExternalCourseEnrollment.cs` — extern deltagares kursanmälan + genomförandegrad.

### Infrastruktur (src/Infrastructure/Persistence/Configurations/LMS/)
- `LMSContentConfiguration.cs` — IEntityTypeConfiguration för de 5 nya entiteterna
  (auto-registreras via `ApplyConfigurationsFromAssembly`). Alla tabeller i schema `lms`.

### Web (src/Web/Components/Pages/Utbildning/)
- `Kursinnehall.razor` — `/utbildning/kursinnehall` — admin: bygg lektioner, ordna, ta bort (HR/Admin).
- `Scorm.razor` — `/utbildning/scorm` — admin: ladda upp SCORM-zip, tolka manifest (HR/Admin).
- `Kursspelare.razor` — `/utbildning/kurs/{CourseId:guid}` — kursspelare med genomförandespårning (alla inloggade).
- `ExternaDeltagare.razor` — `/utbildning/externa` — admin: bjud in/hantera externa deltagare (HR/Admin).
- `ExternPortal.razor` — `/utbildning/extern/{Token}` — PUBLIK token-portal för externa (EmptyLayout).

### Tester (tests/LMS.Tests/)
- `LessonTests.cs`, `ScormManifestParserTests.cs`, `ExternalParticipantTests.cs`, `CompletionTrackingTests.cs`

## Ändrade filer (mina)
- `src/Modules/LMS/Domain/CourseEnrollment.cs` — LADE TILL nullable `int? GenomforandeGrad`
  + metod `UppdateraGenomforande(int procent)`. Befintlig publik signatur (Anmala/Paborja/Genomfor/Avbryt) OFÖRÄNDRAD.
- `src/Web/Components/Pages/Utbildning/Elearning.razor` — lade till toolbar-länkar + "Öppna"-knapp (kursspelare).

## INTEGRATION — åtgärder för orkestratorn

### route_policy (src/Web/Services/RouteAccessPolicy.cs → BuildRules())
Lägg till dessa rader i `rules`-listan (mer specifika prefix än `/utbildning`, vinner via längdsortering).
Admin-vyerna ska vara HR/Admin; kursspelaren ligger kvar som "alla inloggade" (via `/utbildning`).
```csharp
("/utbildning/kursinnehall", HrAdmin),
("/utbildning/scorm", HrAdmin),
("/utbildning/externa", HrAdmin),
```
OBS: `/utbildning/extern/{token}` (ExternPortal) använder EmptyLayout → INTE policy-gated
(precis som `/login`). Ingen RouteAccessPolicy-post behövs; åtkomst gated av token. Om sidan
någon gång byter till AdminLayout måste `/utbildning/extern` läggas i `OpenRoutes`.

### dbsets (VALFRITT — src/Infrastructure/Persistence/RegionHRDbContext.cs, LMS-sektionen)
Sidorna använder `db.Set<T>()` och fungerar UTAN dessa (entiteterna finns i modellen via config).
Lägg till för bekvämt LINQ (`db.Lessons` osv.) om önskvärt:
```csharp
public DbSet<Lesson> Lessons => Set<Lesson>();
public DbSet<ScormPackage> ScormPackages => Set<ScormPackage>();
public DbSet<LessonCompletion> LessonCompletions => Set<LessonCompletion>();
public DbSet<ExternalParticipant> ExternalParticipants => Set<ExternalParticipant>();
public DbSet<ExternalCourseEnrollment> ExternalCourseEnrollments => Set<ExternalCourseEnrollment>();
```

### nav_entries (VALFRITT — src/Web/Components/Layout/NavMenu.razor)
Redan nåbara via toolbar-länkar på `/utbildning/elearning`. Om egna menyposter önskas
(under Utbildning-gruppen, HR/Admin-synliga):
- Kursinnehåll → `/utbildning/kursinnehall`
- SCORM-paket → `/utbildning/scorm`
- Externa deltagare → `/utbildning/externa`

### di_registrations
INGA nya. `IFileStorageService` (LocalFileStorageService) är redan registrerad i
`src/Infrastructure/DependencyInjection.cs` och används för fil-/SCORM-lagring.

### package_refs
INGA. Använder endast BCL (System.IO.Compression, System.Xml.Linq) + befintlig MudBlazor/EF.

## VAD SOM ÄR FÖRENKLAT (ärligt märkt i UI + kod)
- **SCORM**: uppladdning + `imsmanifest.xml`-tolkning (identifier, titel, version,
  launch-URL, masteryscore) + katalogisering är fullt implementerade. En komplett
  SCORM-RUNTIME ingår INTE: ingen JS SCORM-API (1.2/2004), ingen cmi-datamodell,
  ingen uppackning/statisk servering av paketinnehåll, ingen imsss-sekvensering.
  Genomförande markeras manuellt i spelaren (`LessonCompletion`). Märkt med MudAlert
  på Scorm.razor och i kursspelaren.
- **Extern access**: token-baserad länk (bärartoken), "enkel access". INGEN federerad
  inloggning (BankID/eduID/IdP), inget lösenord, ingen e-postverifiering; e-postutskick
  av länken sker inte skarpt (visas för admin att kopiera). Märkt med MudAlert på
  ExternaDeltagare.razor och dokumenterat i ExternalParticipant.cs.

## build_risk
Låg–medel. Matchar befintliga mönster (DbContextFactory-sidor, MudFileUpload/IFileStorageService,
MudBlazor @switch-i-markup som i Audit/Index.razor, `db.Set<T>()`). Nullable-fält kapslas i
lokaler före varje await (TreatWarningsAsErrors). DB wipas vid redeploy → nya lms-tabeller
skapas av EnsureCreatedAsync.
