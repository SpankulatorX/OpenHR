# Wave 8 — KLASSA informationssäkerhetsklassning (key=klassa)

## Vad
Informationsklassningsregister enligt **SKR:s metod KLASSA** (projektdirektivet: "Riskanalys och
informationssäkerhetsklassning kommer ske enligt metoden KLASSA"). Varje informationsmängd klassas i
de tre skyddsaspekterna **Konfidentialitet / Riktighet / Tillgänglighet (K/R/T)** på konsekvensnivå
**1–4** med motivering per aspekt, skyddsåtgärder och lagrum. Fördefinierad standardklassning för
OpenHR:s känsliga datamängder (lön, personnummer, hälsa/rehab, facklig tillhörighet = art. 9-uppgifter)
seedas automatiskt. UI för att se/redigera + sammanställning + CSV-export.

Modellen är verifierad mot KLASSA/MSB via webben: tre aspekter (K/R/T), fyra konsekvensnivåer där
nivå 4 är SKR:s översta nivå (utöver MSB:s ursprungliga matris).

## Placering (utökar GDPR-modulen, rör EJ befintliga GDPR-filer)
Modulen `RegionHR.GDPR` nås redan transitivt av Web (via Infrastructure) och direkt av Infrastructure,
så inga csproj/DI-ändringar behövs.

### Nya filer
- `src/Modules/GDPR/Klassa/KlassaEnums.cs` — `KonsekvensNiva` (1–4), `Skyddsaspekt`, `InformationsKategori`
- `src/Modules/GDPR/Klassa/KlassaRegler.cs` — `Klassningskrav` (record) + `KlassaRegler` (art.9-regel, rekommenderade miniminivåer per kategori, `UppfyllerKrav`, `HogstaNiva`)
- `src/Modules/GDPR/Klassa/InformationsklassPost.cs` — EF-entitet (register-post)
- `src/Modules/GDPR/Klassa/KlassaText.cs` — svenska visningstexter (nivå/aspekt/kategori)
- `src/Modules/GDPR/Klassa/KlassaSeed.cs` — 12 fördefinierade standardklassningar
- `src/Infrastructure/Persistence/Configurations/Klassa/InformationsklassPostConfiguration.cs` — EF-konfig (NY isolerad underkatalog → auto-registreras av `ApplyConfigurationsFromAssembly`)
- `src/Web/Components/Pages/Admin/Klassa.razor` — sidan `/admin/klassa`
- `tests/GDPR.Tests/KlassaTests.cs` — 18 xUnit-tester (nytt filnamn i befintligt testprojekt)

## Datamodell
Tabell `gdpr.informationsklass_poster`. Entiteten registreras i modellen via IEntityTypeConfiguration
(auto-scan) — **ingen DbSet-property** lades till (RegionHRDbContext.cs orörd). Sidan använder
`db.Set<InformationsklassPost>()`. Enum-kolumner lagras som strängar (`HasConversion<string>`); unikt
index på `Informationsmangd`. Fritext-fält (motiveringar, skyddsåtgärder) = `text`.

## Route / behörighet
Ruttprefixet `/admin` är **redan HrAdmin** i `RouteAccessPolicy.cs` → `/admin/klassa` skyddas
automatiskt för HR/Admin. **Ingen ändring i RouteAccessPolicy.cs behövs.**

## Seedning
Sidan self-seedar registret från `KlassaSeed.Fordefinierade()` om tabellen är tom (idempotent, tål
DB-wipe vid redeploy). Unikt index + DbUpdateException-catch skyddar mot dubbelseed vid parallell
laddning. Ingen SeedData.cs-ändring krävs.

## ÄNDRINGAR I SKYDDADE FILER — som exakta snuttar (valfria, förbättrar UX; funkar utan dem)

### 1) NavMenu.razor (VALFRITT) — lägg nav-post efter GDPR-länken (rad ~148)
Filen är skyddad; lägg in denna rad direkt efter den befintliga `/gdpr`-länken i admin-gruppen:
```razor
<MudNavLink Href="/admin/klassa" Icon="@Icons.Material.Filled.Shield">Informationsklassning (KLASSA)</MudNavLink>
```

### 2) SeedData.cs (VALFRITT) — redundant eftersom sidan self-seedar
Om central seedning ändå önskas, lägg in före sista `await db.SaveChangesAsync();` i `SeedAsync`:
```csharp
// KLASSA informationsklassning (wave8)
if (!await db.Set<RegionHR.GDPR.Klassa.InformationsklassPost>().AnyAsync())
    db.Set<RegionHR.GDPR.Klassa.InformationsklassPost>().AddRange(RegionHR.GDPR.Klassa.KlassaSeed.Fordefinierade());
```

## Ej rörda skyddade filer
DependencyInjection.cs, RegionHRDbContext.cs, SeedData.cs, Program.cs, NavMenu.razor, alla *.csproj,
Directory.*.props, RouteAccessPolicy.cs — **inga tvingande ändringar**. Endast de två valfria
snuttarna ovan.

## Paket
Inga nya paket (endast MudBlazor 9.1.0 + EF Core som redan finns).

## Tester (18 st, tests/GDPR.Tests/KlassaTests.cs)
Konsekvensregler (hälsodata→hög konfidentialitet, lön→hög riktighet, art.9-klassning, UppfyllerKrav,
HogstaNiva), entitet (Skapa-validering, Klassningsprofil "K3 R4 T2", HogstaKonsekvens, Uppdatera),
seed-konsistens (unika namn, varje seed-post uppfyller sin miniminivå, innehåller de fyra känsliga
mängderna, art.9 → högsta konfidentialitet), visningstext.

## Byggrisk
Låg. Följer befintligt mönster (GDPRConfiguration, GDPR/Index.razor, Export.razor). Ej byggt lokalt
(bygg fryser maskinen). Enum lagras som sträng → all nivå-sortering sker i minnet i sidan (ej i SQL)
för att undvika EF-översättning av `(int)enum`-cast.
