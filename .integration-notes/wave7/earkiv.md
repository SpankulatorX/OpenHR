# Wave 7 — earkiv (E-arkiv enligt arkivlagen)

## Sammanfattning
Ett oföränderligt e-arkiv ovanpå befintlig dokumenthantering. En arkiverad handling
(`ArchivedDocument`) är låst efter arkivering: innehåll och arkivmetadata (diarienummer,
arkivklass, gallringsfrist, ansvarig, arkiveringsdatum, SHA-256-integritetshash) kan inte
ändras. Manipulation av det fysiska innehållet upptäcks genom att hashen räknas om och
jämförs. Arkivering låser källdokumentet (`Document.Archive()` + retention), sätter
gallringsfrist enligt arkivklass och loggar i granskningsloggen. Gallringsspärr (legal hold)
och gallringsvy ingår. **Arkivlagen > GDPR**: en arkivpliktig handling (bevaras / spärrad /
frist ej passerad) får inte raderas på GDPR-grund — `FarRaderasEnligtGdpr()` speglar
`KanGallras()`.

Skarp anslutning till kommunalt slutarkiv (FGS-paketering / OAIS) är **inte** byggd — det
kräver externt avtal. Integritetshash, klassning, gallringslogik och config-klar tjänst ER
byggda; hashen räknas över den fysiska filen när den är åtkomlig, annars över ett deterministiskt
metadata-fingeravtryck (ärligt märkt demoläge i UI).

## Filer

### Nya (mina kataloger — inga befintliga filer rörda)
- `src/Modules/Documents/Domain/ArchiveClass.cs` — enum `ArchiveClass` (Bevaras, Gallras2Ar/5Ar/7Ar/10Ar) + enum `ArchiveStatus` (Arkiverad, Gallrad).
- `src/Modules/Documents/Domain/ArchiveIntegrity.cs` — statisk SHA-256-hash/verifiering + `MetadataFingerprint` (demo-fallback).
- `src/Modules/Documents/Domain/ArchiveClassificationPolicy.cs` — `BeraknaGallringsfrist`, `GallringsfristAr`, `ForeslaArkivklass(DocumentCategory)`, `Etikett`.
- `src/Modules/Documents/Domain/ArchivedDocument.cs` — den oföränderliga arkiventiteten + livscykelmetoder (`Arkivera`, `SattGallringsSparr`/`TaBortGallringsSparr`, `Gallra`, `KanGallras`, `FarRaderasEnligtGdpr`, `VerifieraIntegritet`).
- `src/Infrastructure/Documents/IArchiveService.cs` + `ArchiveService.cs` — arkivtjänst (arkivera/spärra/gallra/lista gallringsbara/verifiera). Använder `IDbContextFactory<RegionHRDbContext>` + `IFileStorageService` (båda redan DI-registrerade). Skriver användarattribuerad `AuditEntry`.
- `src/Infrastructure/Persistence/Configurations/Documents/ArchivedDocumentConfiguration.cs` — EF-config (auto-registreras via `ApplyConfigurationsFromAssembly`; tabell `documents.archived_documents`).
- `src/Web/Components/Pages/Dokument/EArkiv.razor` — `/dokument/earkiv` (arkivera-flöde, lista, integritetsverifiering, legal hold).
- `src/Web/Components/Pages/Dokument/Gallring.razor` — `/dokument/earkiv/gallring` (får gallras nu / kommande / bevaras+spärrade, gallra-åtgärd).
- `tests/Documents.Tests/ArchivedDocumentTests.cs` — 22 xUnit-tester (oföränderlighet/integritet, gallringsfrist-beräkning, kategori→klass, gallringsregler, legal hold, arkivlagen>GDPR).

### Ändrade
- Inga befintliga filer ändrade. Befintliga dokument-mallsidor (`Index`, `Upload`, `MallGenerator`, `Policyer`, `Organisationsdokument`, `PolicyDetalj`) är orörda.

## KRÄVER DIN ÅTGÄRD (jag rörde inte de skyddade filerna)

### di_registrations — `src/Infrastructure/DependencyInjection.cs`
Filen har redan `using RegionHR.Infrastructure.Documents;` (rad 18). Lägg vid de övriga
dokument-/lagringsregistreringarna (t.ex. nära rad 127–131):
```csharp
services.AddScoped<IArchiveService, ArchiveService>();
```
Utan denna rad bygger allt, men `/dokument/earkiv`-sidorna kastar vid runtime (saknad tjänst).

### dbsets — `src/Infrastructure/Persistence/RegionHRDbContext.cs` (VALFRITT)
Entiteten mappas och tabellen skapas via EF-configen även utan DbSet (tjänsten använder
`Set<ArchivedDocument>()`). Lägg gärna ändå till för bekvämlighet i Documents-blocket (rad ~127–130):
```csharp
public DbSet<ArchivedDocument> ArchivedDocuments => Set<ArchivedDocument>();
```

### route_policy — `src/Web/Components/Pages/.../RouteAccessPolicy.cs` (REKOMMENDERAT)
`/dokument`-prefixet är i dag `ChefHrAdmin`. E-arkiv/gallring är arkivadministration (gallringsbeslut,
legal hold) → begränsa till HR/Admin. Lägg en mer specifik regel i `Rules`-listan i `BuildRules()`
(mest specifik prefix vinner, så den slår `/dokument`):
```csharp
("/dokument/earkiv", HrAdmin),
```
Täcker både `/dokument/earkiv` och `/dokument/earkiv/gallring`.

## Övrigt
- **Inga** ändringar i Program.cs, NavMenu, SeedData, csproj, Directory.*.props.
- **Inga nya NuGet-paket** (bara BCL: `System.Security.Cryptography`).
- Granskningslogg: `AuditInterceptor` loggar automatiskt (userId "system") vid SaveChanges; utöver det
  skriver `ArchiveService` en **användarattribuerad** `AuditEntry` per arkiv-/spärr-/gallringsåtgärd.
- Nav (valfritt): en länk till `/dokument/earkiv` kan läggas i dokument-menyn; ej nödvändigt (nås
  via knapp går att lägga i `Index.razor` av mall-ägaren om önskat — jag rörde den inte).
- DB wipas vid redeploy (EnsureCreated) → nya tabellen `documents.archived_documents` skapas rent.
