# Wave 8 — Slice: lonefloden (Saknade löneflöden)

Facktillhörighet (#19), Tolkersättning (#6), Ersättning till förtroendevalda/fritidspolitiker (#7).

## Nya filer

### Domän (src/Modules/Payroll/Domain/)
- `Facktillhorighet.cs` — entitet `Facktillhorighet` + enum `FacktillhorighetRoll` + self-contained
  `FacktillhorighetFilGenerator` (+ records `FacktillhorighetRad`, `FacktillhorighetFilInput`, `FacktillhorighetFil`).
  Genererar uppdateringsfil (CSV, ISO-8859-1) till fackförbund.
- `Tolkersattning.cs` — entitet `Tolkersattning` + enums `TolkuppdragTyp`, `TolkersattningStatus` +
  `TolkersattningUnderlagGenerator` (+ records `TolkersattningUnderlagInput`, `TolkersattningUnderlagFil`).
  Flöde uppdrag → ersättning (arvode/F-skatt/skatt) → utbetalningsunderlag (CSV).
- `FortroendevaldErsattning.cs` — entitet `FortroendevaldErsattning` + enum `FortroendevaldStatus` +
  `FortroendevaldUnderlagGenerator` (+ records `FortroendevaldUnderlagInput`, `FortroendevaldUnderlagFil`).
  Arvode + förlorad arbetsinkomst + reseersättning (skattefri schablon 2,50 kr/km, överskott beskattas).

### EF-config (src/Infrastructure/Payroll/) — auto-registreras via ApplyConfigurationsFromAssembly
- `FacktillhorighetConfiguration.cs` → tabell `payroll.facktillhorigheter`
- `TolkersattningConfiguration.cs` → tabell `payroll.tolkersattningar`
- `FortroendevaldErsattningConfiguration.cs` → tabell `payroll.fortroendevald_ersattningar`

### UI (src/Web/Components/Pages/Lon/)
- `Facktillhorighet.razor` → rutt `/lon/facktillhorighet`
- `Tolkersattning.razor` → rutt `/lon/tolkersattning`
- `Fritidspolitiker.razor` → rutt `/lon/fritidspolitiker`

### Tester (tests/Payroll.Tests/)
- `FacktillhorighetTests.cs` (11), `TolkersattningTests.cs` (14), `FortroendevaldErsattningTests.cs` (17)

## DbSets
Entiteterna nås via `db.Set<T>()` (samma mönster som befintliga `Fackavgift`/`Loneutmatning`).
INGEN ändring i RegionHRDbContext.cs behövs — EF-configerna registrerar entitetstyperna i modellen
och EnsureCreatedAsync skapar tabellerna. Om DbSet-properties ändå önskas (valfritt, RÖR EJ om ni följer
mönstret): `Facktillhorigheter`, `Tolkersattningar`, `FortroendevaldErsattningar`.

## Route policy
Inga ändringar behövs. Alla tre rutter ligger under prefixet `/lon` som redan är `HrAdmin`
i RouteAccessPolicy.cs (segment-matchning `/lon/...`).

## NavMenu (RÖR EJ själv — lägg in dessa som snuttar i Lön-gruppen, efter rad 33 `/lon/utmatning`)
```razor
<MudNavLink Href="/lon/facktillhorighet" Icon="@Icons.Material.Filled.Groups">Facktillhörighet</MudNavLink>
<MudNavLink Href="/lon/tolkersattning" Icon="@Icons.Material.Filled.RecordVoiceOver">Tolkersättning</MudNavLink>
<MudNavLink Href="/lon/fritidspolitiker" Icon="@Icons.Material.Filled.HowToVote">Ersättning förtroendevalda</MudNavLink>
```

## Program.cs / DI / paket
Inga ändringar. Inga nya paket (endast System.Text/Globalization ur BCL; `Encoding.Latin1` finns i .NET utan provider).

## Filformat / teckenkodning
Alla tre filer: semikolonseparerad CSV, **ISO-8859-1 (Latin1)**, invariant kultur, ISO-datum (yyyy-MM-dd),
belopp med två decimaler och punkt. Header `#H;...`, kolumnrubrik, datarader, summeringsrad `#S;antal;...`.
Fältseparator och radbrytningar saneras ur fritext.

## Domänregler (systemet = experten)
- Tolk: preliminärskatt (30 % default) dras BARA på arvodet och BARA om tolken saknar F-skatt; reseersättning skattefri.
- Förtroendevald: reseersättning skattefri upp till schablon (2,50 kr/km), överskjutande del skattepliktig;
  skatt beräknas på (arvode + förlorad arbetsinkomst + skattepliktig reseandel).

## Build-risk
Låg. Följer exakt Fackavgift/Loneutmatning/Utmatning-mönstret (entitet + EF-config i src/Infrastructure/Payroll
+ db.Set<T>() + downloadFile-JS). Inga rörda skyddade filer. Ej byggt lokalt (per instruktion).
