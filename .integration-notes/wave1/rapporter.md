# Integrationsnoteringar — slice `rapporter`

Riktig rapport-/utdatamotor. Ersätter den hårdkodade platshållaren (rader=0), den
fabricerade löneberäkningen i KorRapport och TODO:t i ScheduledReportService.

## 1. DI-registrering (src/Infrastructure/DependencyInjection.cs — AddInfrastructure)

Lägg EN rad i "Reporting"-blocket (finns runt rad 116, direkt efter
`services.AddScoped<ReportGenerator>();`):

```csharp
        // Reporting
        services.AddScoped<ReportGenerator>();
        services.AddScoped<RegionHR.Infrastructure.Reporting.ReportExecutionService>();   // <-- NY
```

Måste vara **Scoped** (beror på den scoped-registrerade `IDbContextFactory<RegionHRDbContext>`).
`ScheduledReportService` (redan `AddHostedService`, oförändrad registrering) resolvar den
ur sin egen scope, och `ReportBuilder.razor` injicerar den direkt.

Inga andra DI-ändringar behövs — `ExportService` (Singleton), `EmailNotificationSender`
(Singleton), scoped `RegionHRDbContext` och scoped `IDbContextFactory<RegionHRDbContext>`
finns redan.

## 2. DbSets (src/Infrastructure/Persistence/RegionHRDbContext.cs)

Inga nya DbSets. `ReportDefinitions`, `ReportExecutions`, `ScheduledReports`,
`PayrollResults` m.fl. finns redan.

## 3. Ny egenskap på ReportDefinition — schema

`ReportDefinition` fick en ny persisterad kolumn `Datakalla` (string?, i mitt eget
domänfilsägande `src/Modules/Reporting/Domain/ReportDefinition.cs`). Den auto-mappas av
EF-konventionen (text-kolumn) och skapas automatiskt av `Database.EnsureCreatedAsync()`
(det är så appen bygger schemat i Program.cs — inte via Migrate). **Ingen migration krävs
för runtime.**

Viktigt: `EnsureCreated` ändrar INTE en redan skapad tabell. Om en gammal Postgres-volym
återanvänds måste `reporting.report_definitions` få kolumnen (enklast: släng dev-databasen
och låt den återskapas, eller kör `ALTER TABLE reporting.report_definitions ADD COLUMN "Datakalla" text;`).

Valfritt (kosmetiskt), om ni vill sätta maxlängd — lägg i
`src/Infrastructure/Persistence/Configurations/Reporting/ReportingConfiguration.cs`
(ReportDefinitionConfiguration.Configure), men det är INTE nödvändigt:

```csharp
        builder.Property(x => x.Datakalla).HasMaxLength(50);
```

## 4. Signaturändring (bevakas — bara min egen kod anropar den)

`ReportDefinition.SattRapportmall(...)` fick ett nytt FÖRSTA argument `datakalla`:

```csharp
// FÖRE: SattRapportmall(kolumner, filter, gruppering, visualiseringsTyp)
// EFTER: SattRapportmall(datakalla, kolumner, filter, gruppering, visualiseringsTyp)
```

Enda anroparen är `ReportBuilder.razor` (redan uppdaterad). Inget annat i repo:t anropar den
(verifierat med grep).

## 5. Seed-snutt (src/Infrastructure/Persistence/SeedData.cs)

Ingen rapportdefinition seedas idag, så byggaren-listan och den schemalagda tjänsten är
tomma. Klistra in FÖRE `await db.SaveChangesAsync();` (sista raden i `SeedAsync`). Kompilerar
utan nya using (fullt kvalificerad). Ger 2 körbara sparade rapporter + 1 schemalagd som
`ScheduledReportService` faktiskt exekverar:

```csharp
        // === Rapportdefinitioner (self-service builder + schemalagd) ===
        if (!await db.ReportDefinitions.AnyAsync())
        {
            var rapAnstallda = RegionHR.Reporting.Domain.ReportDefinition.Skapa(
                "Aktiva anställda per enhet", "Antal aktiva anställda grupperat på enhet",
                RegionHR.Reporting.Domain.ReportType.AdHoc);
            rapAnstallda.SattRapportmall(
                "Anstallda",
                "[\"Fornamn\",\"Efternamn\",\"Enhet\",\"Befattning\",\"Sysselsattningsgrad\"]",
                "{\"Enhet\":\"Alla\",\"Status\":\"Aktiv\"}",
                "Enhet", "Bar");
            db.ReportDefinitions.Add(rapAnstallda);

            var rapLon = RegionHR.Reporting.Domain.ReportDefinition.Skapa(
                "Lönekörningar per period", "Brutto/skatt/netto ur persisterad lönedata",
                RegionHR.Reporting.Domain.ReportType.AdHoc);
            rapLon.SattRapportmall(
                "Lonekorngar",
                "[\"Period\",\"Brutto\",\"Skatt\",\"Netto\",\"Arbetsgivaravgift\"]",
                "{}", null, "Table");
            db.ReportDefinitions.Add(rapLon);

            // Schemalagd rapport: körs månadsvis (1:a kl 06:00) och mejlas HR.
            var rapSchema = RegionHR.Reporting.Domain.ReportDefinition.Skapa(
                "Månatligt löneregister", "Automatiskt löneregister till HR",
                RegionHR.Reporting.Domain.ReportType.Loneregister);
            rapSchema.SattRapportmall(
                "Lonekorngar",
                "[\"Period\",\"Brutto\",\"Skatt\",\"Netto\",\"Arbetsgivaravgift\"]",
                "{}", null, "Table");
            rapSchema.SattSchemalagd("0 6 1 * *", "hr@region.se");
            db.ReportDefinitions.Add(rapSchema);

            db.ScheduledReports.Add(RegionHR.Reporting.Domain.ScheduledReport.Skapa(
                rapSchema.Id, "Monthly", "hr@region.se", "Excel"));
        }
```

## 6. NavMenu

Ingen ny nav-post. Rapportbyggaren (`/rapporter/bygg`) och körning (`/rapporter/kor/{Namn}`)
finns redan i navigationen/rapportbiblioteket.

## 7. Paket (package_refs)

**Inga nya paket krävs.** Cron hanteras av en egen minimal 5-fälts-utvärderare
(`src/Modules/Reporting/Engine/CronSchedule.cs`) — inget Cronos/NCrontab behövs.
Om ni senare vill ha full cron-standard: byt internt i `CronSchedule` mot `Cronos`
(paket `Cronos`), signaturen (`TryParse`/`NastaEfter`/`ArForfallenSedan`) kan behållas.

## 8. Vad som byggdes (för E2E/verifiering)

- Ren, EF-fri query-motor i Reporting-modulen: `ReportQuerySpec` (parsar sparad
  kolumn/filter/gruppering-JSON), `ReportQueryEngine` (filter → gruppering/aggregering →
  projektion → kulturoberoende strängar), `ReportResult`, `CronSchedule`.
- Infra-brygga `ReportExecutionService` materialiserar 7 datakällor (Anstallda,
  Lonekorngar, Franvaro, Schema, Certifieringar, LAS-ackumuleringar, Rekrytering) med
  kolumnnycklar som exakt matchar byggarens `GetColumnsForDataSource`.
- `ReportBuilder.razor`: "Förhandsgranska data" + "Kör" på sparade rapporter kör motorn
  mot DB och visar faktiska rader (ersätter hårdkodat rader=0). Sparar även `Datakalla`.
- `KorRapport.razor`: "Löneregister" läser nu persisterad `PayrollResults` (Period, Brutto,
  Skatt, Netto, AG-avgift) i stället för fabricerad platt 32%-skatt/30000-default;
  `Take(100)`-locken borttagna; fel visas nu (tyst 0-rader-bugg åtgärdad).
- `ScheduledReportService`: kör förfallna definitioner (cron/ScheduledReport.NastaKorning),
  exporterar via `ExportService`, mejlar via `EmailNotificationSender`, registrerar
  `ReportExecution` och markerar nästa körning (`ScheduledReport.MarkeraSomKord`).

## 9. Tester

`tests/Reporting.Tests/ReportQueryEngineTests.cs` (13 fakta/teorier) och
`CronScheduleTests.cs` (9 fakta). Testar endast Reporting-modulens rena kod (ingen EF /
inga nya paket / ingen csproj-ändring — testprojektet refererar redan bara modulen).
