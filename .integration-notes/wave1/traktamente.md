# Slice: traktamente — Korrekt traktamente 2026

## Vad som ändrats
- `src/Infrastructure/Payroll/TraktamentsCalculator.cs` — omskriven, årsversionerad, verifierad mot
  **Skatteverket SKV 354 utgåva 36 (dec 2025, inkomstår 2026)**.
  - Inrikes: helt **300 kr** (var 260 = 2023-värde), halvt/natt **150 kr** (var 130).
  - **Måltidsavdrag tillagt** (saknades helt):
    - Inrikes (andel av helt maximibelopp): frukost **20 %** (60 kr), lunch **35 %** (105 kr),
      middag **35 %** (105 kr) → helt fri kost **90 %** (270 kr), 30 kr kvar för småutgifter.
    - Utrikes (andel av normalbelopp): frukost **15 %**, lunch **35 %**, middag **35 %** → allt **85 %**.
  - Bilersättning (milersättning) verifierad + exponerad: egen bil **25 kr/mil**, förmånsbil 12, förmånsbil el 9,50.
  - Satserna årsversionerade per inkomstår (2023/2024/2025/2026), fallback till närmaste kända år.
  - Utlands-normalbelopp gjorda årsversionerade men **provisoriska** (se nedan).
- `tests/Travel.Tests/TraktamentsCalculatorTests.cs` — ny, 21 test som låser 2026-värdena.

## ⚠️ ENDA obligatoriska ändring i delad fil (integratören)
Testprojektet `tests/Travel.Tests/RegionHR.Travel.Tests.csproj` refererar idag inte Infrastructure
(där kalkylatorn ligger). Lägg till i dess `<ItemGroup>` med `<ProjectReference>`:

```xml
<ProjectReference Include="..\..\src\Infrastructure\RegionHR.Infrastructure.csproj" />
```

Ingen cykel uppstår (Infrastructure → Travel; testprojektet är löv). Utan denna rad kompilerar inte
`TraktamentsCalculatorTests.cs`. Om detta bryter något i wave-orkestreringen kan testfilen alternativt
flyttas till `tests/RegionHR.Infrastructure.Tests/Payroll/` (som redan refererar Infrastructure) —
men filens innehåll är oförändrat.

## DI / DbSet / Seed / NavMenu / paket
- **DI:** Inget att göra. `services.AddSingleton<TraktamentsCalculator>();` finns redan
  (`DependencyInjection.cs` rad 126). Parameterlös konstruktor bevarad.
- **DbSet:** Inga.
- **Seed:** Ingen.
- **NavMenu:** Ingen (sidan `/resor` finns redan och injicerar `TraktamentsCalculator`).
- **Paket:** Inga nya.

## Bevarade publika signaturer (inga anropare bryts)
- `BeraknaInrikes(DateTime avresa, DateTime hemkomst, bool hotell, int friaFrukostar=0, int friaLuncher=0, int friaMiddagar=0)`
  — 3-args-anropet i `Web/Components/Pages/Resor/Index.razor` (rad 210) fungerar oförändrat.
- `BeraknaUtrikes(string land, DateTime avresa, DateTime hemkomst, int friaFrukostar=0, int friaLuncher=0, int friaMiddagar=0)`
  — 3-args-anropet (rad 211) fungerar oförändrat.
- `record TraktamentsBerakning(...)` — fälten Dagtraktamente/Natttillagg/Totalt/AntalDagar/Beskrivning
  oförändrade; **nytt fält `decimal Maltidsavdrag = 0m`** tillagt sist (valfritt). Razor läser via namn → ok.

## Ny publik API-yta (för framtida UI-inkoppling, valfritt)
- `TraktamentsCalculator.SatserForAr(int år)` → `TraktamenteSatser` (alla satser för året).
- `BeraknaMaltidsavdragInrikes(int frukost, int lunch, int middag, int år)` → kr.
- `BeraknaMaltidsavdragUtrikes(decimal normalbelopp, int frukost, int lunch, int middag, int år)` → kr.
- `BeraknaMilersattning(decimal mil, int år, BilTyp typ = EgenBil)` → kr.
- `GetUtrikesNormalbelopp(string land, int år)` (static).
- Resor-sidan kan (senare, ägs av annan slice) lägga till fält för fria måltider och skicka in dem
  till Berakna*-metoderna för att visa `Maltidsavdrag`.

## ⚠️ Korsslice-flagga: `src/Modules/Travel/Domain/TravelClaim.cs` (INTE min fil)
Denna aggregatrot har egna **inaktuella** hårdkodade konstanter (annan slice äger filen):
- `TRAKTAMENTE_HELDAG_INRIKES = 260m` → bör vara **300** (2026).
- `TRAKTAMENTE_HALVDAG_INRIKES = 130m` → bör vara **150** (2026).
- `MILERSATTNING_SATS = 25m` → korrekt för 2026.
Om `TravelClaim` uppdateras till 300/150 så bryts assertions i det befintliga
`tests/Travel.Tests/TravelClaimTests.cs` (t.ex. `SattTraktamente(2,1)` förväntar 650 kr → blir 750).
Rekommendation: den slice som äger `TravelClaim` bör antingen använda `TraktamentsCalculator` eller
uppdatera konstanter + tester. Jag har inte rört den filen (utanför mitt slice).

## Verifierade värden (källa: SKV 354 utg. 36, 2026; prisbasbelopp 2026 = 59 200 kr)
| Regel | Värde 2026 |
|---|---|
| Helt traktamente inrikes | 300 kr |
| Halvt / nattraktamente inrikes | 150 kr |
| Måltidsavdrag inrikes frukost/lunch/middag | 20 % / 35 % / 35 % (60/105/105 kr) |
| Måltidsavdrag inrikes helt fri kost | 90 % (270 kr, 30 kr kvar) |
| Måltidsavdrag utrikes frukost/lunch/middag/allt | 15 % / 35 % / 35 % / 85 % av normalbelopp |
| Bilersättning egen bil | 25 kr/mil |
| Bilersättning förmånsbil / förmånsbil el | 12 / 9,50 kr/mil |

## Provisoriskt / att förbättra
Utlands-normalbeloppen per land (Norge 1054, Danmark 1226, Tyskland 760, … i koden) är
sekundärkälle-hämtade (Björn Lundén/co-redovisning för 2026) och **inte** primärverifierade — källorna
gav motstridiga tal. Den fullständiga listan (~200 länder) fastställs i Skatteverkets allmänna råd
"Normalbelopp för ökade levnadskostnader i utlandet" och bör laddas från datakälla/DB. Testerna
påstår därför **inte** specifika landsbelopp som lag — de låser strukturen (default-fallback) och de
verifierade procentandelarna (15/35/35). Default "Övriga länder" = 493 kr (dokumenterad).
