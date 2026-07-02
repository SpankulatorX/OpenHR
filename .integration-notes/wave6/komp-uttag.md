# Wave 6 — Komptidsuttag (key: komp-uttag)

Medarbetare/chef kan nu ta ut intjänad komptid, antingen som **kompledigt** (godkänd
ledighetspost av typ `Komptid` + avbokade pass) eller som **utbetalning**. Saldot
(`FlexBalance`) kan aldrig övertrasseras; dragningen sker atomiskt vid godkännande.

## Filer

Skapade:
- `src/Modules/Scheduling/Domain/KomptidUttag.cs` — ny entitet + enums `KomputtagTyp`, `KomputtagStatus`.
- `src/Infrastructure/Persistence/Configurations/Scheduling/KomptidUttagConfiguration.cs` — EF-config.
- `tests/Scheduling.Tests/FlexBalanceKomptidTests.cs` — saldodragning (kan ej övertrassera).
- `tests/Scheduling.Tests/KomptidUttagTests.cs` — arbetsflöde/statusövergångar.

Ändrade:
- `src/Modules/Scheduling/Domain/FlexBalance.cs` — nya kolumner `UttagnaKompTimmar` + computed
  `[NotMapped] TillgangligKomptidTimmar`, samt `RegistreraKomputtag` / `AterforKomputtag`.
- `src/Web/Services/FlexService.cs` — orkestrering: `HamtaKomptidsaldoAsync`, `HamtaMinaUttagAsync`,
  `HamtaVantandeUttagAsync`, `BegarKomputtagAsync`, `GodkannKomputtagAsync`, `AvslaKomputtagAsync`,
  `AterkallaKomputtagAsync` + records `KomptidSaldo`, `KomptidUttagRad`.
- `src/Web/Components/Pages/MinSida/Saldon.razor` — begär-uttag-formulär, "mina uttag"-historik
  med återkalla, samt chefs-godkännandelista (role-gated på `Auth.IsChef`).

## Integration — inget MÅSTE göras (allt auto-registreras), men bra att veta

### DbSet (VALFRITT)
`KomptidUttag` ingår redan i modellen via `KomptidUttagConfiguration`
(ApplyConfigurationsFromAssembly), och nås i koden via `db.Set<KomptidUttag>()`.
Vill man ha en explicit DbSet i `RegionHRDbContext.cs` (jag rörde den INTE):

```csharp
public DbSet<KomptidUttag> KomptidUttag => Set<KomptidUttag>();
```

### Nya kolumner (auto, DB wipas vid redeploy)
`FlexBalance` får kolumnen `UttagnaKompTimmar` (decimal, default 0) via konvention.
`TillgangligKomptidTimmar` är `[NotMapped]` (computed) — skapar ingen kolumn.
`scheduling.komptid_uttag`-tabellen skapas av EnsureCreated.

### DI — inga ändringar
Nya metoder ligger på befintliga `FlexService` (redan registrerad, Program.cs:101).
Inga nya services, inga nya registreringar.

### Route/policy — inga ändringar
Sidan är kvar på `/minsida/saldon` (RouteAccessPolicy: `/minsida` = alla inloggade).
Chefsgodkännandet är en role-gated sektion (`Auth.IsChef`) inne på sidan, ingen ny rutt.

### LeaveType — INGEN enum-ändring
`LeaveType.Komptid` fanns redan i `RegionHR.Leave.Domain.LeaveType`; kompledigt uttag
återanvänder den och skapar en `LeaveRequest` som sätts direkt i status `Godkand`
(dyker alltså INTE upp som pending i /godkannanden — undviker dubbel godkännandeväg där
komptidssaldot annars inte skulle dras).

## Designbeslut / begränsningar (ärligt märkta)

- **Bruttosaldo = rullande 365-dagarsfönster** ur övertidsunderlaget (`ScheduledShift.OvertidTimmar`).
  Netto = brutto − `UttagnaKompTimmar` (persisterad huvudbok). Huvudboken räknas ALDRIG om ur
  stämplingar, så godkända uttag överlever att bruttot räknas om (t.ex. av `RaknaOmOchSparaAsync`
  som Flex.razor anropar med 90-dagarsfönster — påverkar inte komptidsflödet, som räknar brutto
  färskt själv).
- **Hård saldokontroll sker vid GODKÄNNANDE** (`FlexBalance.RegistreraKomputtag`), inte vid begäran
  (där görs en mjuk förhandskontroll). Detta är den enda platsen saldot faktiskt dras.
- **Chefsvyn scoped inte per team** — `HamtaVantandeUttagAsync` returnerar ALLA väntande uttag
  (ingen manager→report-koppling finns på den här nivån). Om team-scoping önskas: filtrera på
  `Employee.EnhetId` mot `Auth.UnitId`. Noterat som medvetet val, inte glömska.
- **Utbetalning** skapar bara en notis ("tas med i nästa lönekörning") + drar saldot; själva
  löneraden/utbetalningsfilen är en separat (betald) lönekörnings-/bankintegration utanför scope.

## Build-risk
Låg. Följer befintliga mönster (LedighetService för orkestrering, VacationBalance för
saldo-domänmetod, WFMConfigurations för EF-config). Byggde INTE (per instruktion). Verifiera:
`dotnet build` + `dotnet test tests/Scheduling.Tests`.
