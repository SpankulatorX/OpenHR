# Wave 5 — kollektivavtal-lokala (Lokala kollektivavtalsavvikelser)

Override-lager ovanpå det centrala kollektivavtalet: lokala OB-påslag, lokala tillägg
och lokala förmåner **per organisationsenhet** med giltighetsperiod. De centrala
AB/HÖK-satsklasserna (`ABOTillaggSatser`, `ABOvertidSatser`, `ABSemesterRegler`) och
`CollectiveAgreementRulesEngine` är **orörda** — detta är ett rent påläggslager.

## Filer skapade
- `src/Modules/Agreements/Domain/LokalAvvikelseEnums.cs` — `LokalAvvikelseTyp`, `LokalBerakningsTyp`, `LokalBeloppsEnhet`
- `src/Modules/Agreements/Domain/LokalAvtalsAvvikelse.cs` — entiteten (platt, endast skalära fält → ingen jsonb)
- `src/Modules/Agreements/Domain/LokalAvvikelseResolver.cs` — **ren** lookup ("gäller lokal avvikelse för enhet X vid datum Y") + `EffektivObSats`
- `src/Web/Services/LokalAvvikelseService.cs` — DB-backad CRUD + exponerad lookup (`LokalAvvikelseIndata`, `LokalAvvikelseService`)
- `src/Web/Components/Pages/Admin/AvtalLokala/Index.razor` — HR-sida `/admin/avtal/lokala`
- `tests/Agreements.Tests/LokalAvtalsAvvikelseTests.cs`, `tests/Agreements.Tests/LokalAvvikelseResolverTests.cs`

## Filer ändrade
- `src/Web/Components/Pages/Admin/Avtal/AvtalLista.razor` — la till knappen "Lokala avvikelser" (Href `/admin/avtal/lokala`) för åtkomst utan att röra NavMenu.

---

## KRÄVS för att aktivera (RegionHRDbContext.cs — får ej röras av mig)

Lägg DbSet:en i `src/Infrastructure/Persistence/RegionHRDbContext.cs` bland de andra
Agreements-DbSet:arna (efter `AgreementInsurancePackages`, ~rad 310). Namespacet
`RegionHR.Agreements.Domain` är redan `using`-importerat i filen (rad 32) → ingen ny using.

```csharp
    // Agreements — lokala avvikelser (schema: agreements) — våg 5 (kollektivavtal-lokala)
    public DbSet<LokalAvtalsAvvikelse> LokalaAvtalsAvvikelser => Set<LokalAvtalsAvvikelse>();
```

Detta ensamt räcker: `EnsureCreated` mappar entiteten via konventioner och de globalt
registrerade value-converters för `OrganizationId`/`CollectiveAgreementId` (redan i
`ConfigureConventions`). Inga nya converter-registreringar behövs (entiteten använder
`Guid Id`, inte ett nytt strongly-typed id). Tjänsten anropar `db.Set<LokalAvtalsAvvikelse>()`
så den fungerar oavsett DbSet-property eller enbart konfiguration.

## KRÄVS — DI (Program.cs — får ej röras av mig)

Lägg i `src/Web/Program.cs` bland övriga Web-service-registreringar (~rad 103):

```csharp
builder.Services.AddScoped<RegionHR.Web.Services.LokalAvvikelseService>();
```

## REKOMMENDERAT (ej krav) — EF-konfiguration för snake_case + schema `agreements`

Utan denna hamnar tabellen i default-schema (`public`) med PascalCase/int-enums, vilket
fungerar (DB wipas vid redeploy). För konvention (schema-per-modul, snake_case) skapa
`src/Infrastructure/Persistence/Configurations/Agreements/LokalAvtalsAvvikelseConfiguration.cs`
(plockas upp automatiskt av `ApplyConfigurationsFromAssembly`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Agreements.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Agreements;

public class LokalAvtalsAvvikelseConfiguration : IEntityTypeConfiguration<LokalAvtalsAvvikelse>
{
    public void Configure(EntityTypeBuilder<LokalAvtalsAvvikelse> builder)
    {
        builder.ToTable("lokala_avtals_avvikelser", "agreements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        // OrganizationId/CollectiveAgreementId? konverteras av de globala converters — endast kolumnnamn behövs:
        builder.Property(e => e.EnhetId).HasColumnName("enhet_id");
        builder.Property(e => e.AvtalsId).HasColumnName("avtals_id");
        builder.Property(e => e.Typ).HasConversion<string>().HasColumnName("typ").HasMaxLength(30);
        builder.Property(e => e.ObKategori).HasConversion<string>().HasColumnName("ob_kategori").HasMaxLength(30);
        builder.Property(e => e.Namn).HasColumnName("namn").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Beskrivning).HasColumnName("beskrivning"); // text
        builder.Property(e => e.BerakningsTyp).HasConversion<string>().HasColumnName("berakningstyp").HasMaxLength(30);
        builder.Property(e => e.Enhet).HasConversion<string>().HasColumnName("belopps_enhet").HasMaxLength(30);
        builder.Property(e => e.Varde).HasColumnName("varde");
        builder.Property(e => e.GiltigFran).HasColumnName("giltig_fran");
        builder.Property(e => e.GiltigTill).HasColumnName("giltig_till");
        builder.Property(e => e.Aktiv).HasColumnName("aktiv");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(100);
    }
}
```

## Behörighet (RouteAccessPolicy.cs)

**Ingen ändring krävs.** Rutten `/admin/avtal/lokala` fångas av det befintliga
prefix-regeln `("/admin", HrAdmin)` → endast **HR** och **Admin** (löne-/avtalskänsligt).
Bekräftat mot `RouteAccessPolicy.BuildRules()`.

## Nästa steg — inkoppling i lönemotorn (kräver ändring i fil jag inte äger)

Lookupen är exponerad på två nivåer:
- **Ren** (utan EF): `LokalAvvikelseResolver.EffektivObSats(centralObSats, avvikelser, enhetId, kategori, datum, avtalsId?)`
  — kan anropas direkt från Payroll-modulen (den refererar redan `RegionHR.Agreements.Domain`).
- **DB-backad**: `LokalAvvikelseService.EffektivObSatsAsync(...)` (Web-lagret).

För att låta `CollectiveAgreementRulesEngine.GetOBRate(...)` faktiskt beakta lokala påslag
behöver den centrala satsen efterbehandlas med resolvern. Eftersom
`src/Modules/Payroll/Domain/CollectiveAgreementRulesEngine.cs` inte ägs av detta slice
lämnas det som nästa steg: efter att motorn räknat fram central OB-sats, anropa
`LokalAvvikelseResolver.EffektivObSats(central, laddadeAvvikelser, enhetId, kategori, datum)`.
Avvikelserna laddas via `db.Set<LokalAvtalsAvvikelse>()` (grovfiltrera på `EnhetId` + `Aktiv`,
finmatchning sker i resolvern).

## Precedens (dokumenterad, deterministisk) när flera OB-avvikelser gäller samtidigt
1. `ErsattVarde` ersätter basen (senaste `GiltigFran` vinner)
2. `ProcentPaslag` kompounderas
3. `FastBelopp` summeras

`ObKategori == null` = gäller alla OB-kategorier; annars endast angiven kategori.
`AvtalsId == null` = gäller oavsett centralt avtal; annars endast matchande avtal.

## Build-risk
Låg. Nya filer + en liten additiv razor-ändring. Matchar befintliga signaturer/mönster
(IDbContextFactory, MudBlazor, `List<T>`-returer som övriga Web-services). Sidan/tjänsten
kastar i runtime tills DbSet:en (ovan) lagts in — kompilerar oavsett.
