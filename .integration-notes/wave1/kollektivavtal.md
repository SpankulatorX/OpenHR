# Slice: kollektivavtal — Korrekta AB/HÖK-satser 2026 (O-tillägg) + semesterbug

## Sammanfattning
Rättat AB/HÖK O-tillägg (§21), övertid (§20) och semester (§27) mot SKR:s
"Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01". Alla satser är nu
ÅRSVERSIONERADE i kanoniska, testade tabeller i `RegionHR.Agreements.Domain`.
Legacy `KollektivavtalEngine` (Infrastructure) delegerar nu till samma tabeller
(påsk-storhelg-buggen och semester-proportioneringsbuggen åtgärdade).

## Verifierade lagvärden (källa: SKR AB 25, §§ 20, 21, 27)
O-tillägg kr/tim fr.o.m. 2025-04-01 → fr.o.m. 2026-04-01:
- O-tillägg A (storhelg):   126,90 → 130,70   (natt 22–06: 152,30 → 156,90)
- O-tillägg B (helg):        66,10 →  68,10   (natt 22–06:  76,00 →  78,30)
- O-tillägg C (vardagsnatt): 56,70 →  58,40
- O-tillägg D (vardagskväll):25,60 →  26,40

Övertid (§20 mom. 3): enkel 180 %, kvalificerad 240 %, timlön = månadslön / 165.
Semester (§27): mom. 5 = 25/31/32 dagar per ålder (fyllnadsår under kalenderår);
mom. 15 semesterdagstillägg = 0,605 % av månadslön/dag; mom. 16 rörlig-lön-procent
= 12 / 14,88 / 15,36; intjänandeår = SEMESTERÅR = löpande KALENDERÅR (mom. 2);
betalda dagar proportioneras mot anställd del av året (mom. 6 / SemL § 7).

## Filer skapade (ingen DI-registrering krävs — statiska klasser/records)
- src/Modules/Agreements/Domain/ABOTillaggSatser.cs   (§21, årsversionerad tabell + lookup)
- src/Modules/Agreements/Domain/ABSemesterRegler.cs    (§27, ålder→dagar, %, betalda-dagar-proration)
- src/Modules/Agreements/Domain/ABOvertidSatser.cs      (§20, faktorer + delare 165)
- tests/Agreements.Tests/ABOTillaggSatserTests.cs
- tests/Agreements.Tests/ABSemesterReglerTests.cs
- tests/Agreements.Tests/ABOvertidSatserTests.cs

## Filer ändrade (inom slice)
- src/Modules/Payroll/Domain/CollectiveAgreementRulesEngine.cs
  - GetOBRateAsync: hämtar nu ABOTillaggSatser.Grundsats (AB+HÖK samma tabell), övriga avtal 0.
  - GetOvertimeRulesAsync/GetOvertimeRules: satser från ABOvertidSatser; NY prop `OvertimeRules.Overtidsdelare` (=165).
  - GetVacationRulesAsync(+DB-overload): kalenderår som intjänandeår, åldersbaserad rörlig-%,
    semesterdagstillägg 0,605 %. Publika signaturer OFÖRÄNDRADE.
- src/Infrastructure/Payroll/KollektivavtalEngine.cs
  - Markerad @deprecated (pekar på CollectiveAgreementRulesEngine + SvenskaHelgdagar). Behållen (DI).
  - BeraknaOB delegerar till SvenskaHelgdagar + ABOTillaggSatser → PÅSK ingår nu i storhelg.
  - BeraknaSemester: NY valfri param `ingaendeSparadeDagar = 0` (2-arg-anrop kompilerar fortfarande);
    räknar betalda dagar mot anställningsmånader. Record `SemesterRatt` fick NY andra positional-param
    `BetaldaDagar` → `SemesterRatt(int ArligaDagar, int BetaldaDagar, int SparadeDagar, int MaxSparAr)`.
    (Enda konstruktion sker inne i BeraknaSemester; befintligt test läser bara `.ArligaDagar`.)

## DI-registreringar
INGA nya krävs. `ICollectiveAgreementRulesEngine`/`CollectiveAgreementRulesEngine`
och `KollektivavtalEngine` är redan registrerade (DependencyInjection.cs rad 87 resp. 125).
De nya typerna är statiska (ingen registrering).

## Nya DbSet
Inga.

## Seed-data (SHARED: src/Infrastructure/Persistence/SeedData.cs — INTEGRATÖR APPLICERAR)
Ersätt AB- och HÖK-avtalens O-tilläggssatser (rad 58–61 resp. 75–78) med kanoniska
AB §21-satser (giltigFran = 2025-04-01). HÖK följer AB §21 → samma belopp.

AB (ersätt rad 58–61):
```csharp
        ab.LaggTillOBSats(OBCategory.VardagKvall, 25.60m, giltigFran);
        ab.LaggTillOBSats(OBCategory.VardagNatt, 56.70m, giltigFran);
        ab.LaggTillOBSats(OBCategory.Helg, 66.10m, giltigFran);
        ab.LaggTillOBSats(OBCategory.Storhelg, 126.90m, giltigFran);
```
HÖK (ersätt rad 75–78; sätt även övertidsmultiplikatorn rad 79 till 1.8m enligt AB §20):
```csharp
        hok.LaggTillOBSats(OBCategory.VardagKvall, 25.60m, giltigFran);
        hok.LaggTillOBSats(OBCategory.VardagNatt, 56.70m, giltigFran);
        hok.LaggTillOBSats(OBCategory.Helg, 66.10m, giltigFran);
        hok.LaggTillOBSats(OBCategory.Storhelg, 126.90m, giltigFran);
```
Valfritt (2026-satser): lägg till en andra uppsättning med `new DateOnly(2026,4,1)` och
beloppen 26.40 / 58.40 / 68.10 / 130.70 — `HamtaOBSats` väljer senast giltiga.
Doc-tabellen i samma fil (rad ~1451–1456, "Vardag kväll 126,50 …") bör uppdateras till
25,60 / 56,70 / 66,10 / 126,90 för att inte motsäga koden.

## NavMenu / paket
Inga.

## VIKTIGT — korsande sanningar i filer jag INTE äger (till respektive ägaragent)
1. src/Modules/Payroll/Engine/PayrollCalculationEngine.cs (skatt/payroll-agent):
   - `SEMESTER_TILLAGG_PROCENT = 0.0043m` är fel för AB. AB §27 mom. 15 = **0,00605m** (0,605 %).
   - Övertidstimlön beräknas som `månadslön / (38.25m*52m/12m)` = /165,75. AB §20 mom. 3 använder
     delaren **165** exakt (se `ABOvertidSatser.Overtidsdelare`). Liten avvikelse.
   - Dessa värden ändrades INTE av mig (filen ägs parallellt). Motorns testfil
     tests/Payroll.Tests/PayrollCalculationEngineTests.cs använder en egen FakeRulesEngine med
     gamla OB-belopp (126,50/152/89/195) — påverkas inte av mina ändringar men speglar inte AB.
2. src/Modules/Scheduling/Optimization/ConstraintScheduleSolver.cs rad ~336 och
   src/Web/Components/Shared/OhrAssistant.razor rad ~399 hårdkodar OB 126,50 kr/h.
   Bör hämta från motorn/ABOTillaggSatser i stället (kosmetisk/estimat, ej blockerande).

## Byggrisk
Låg. Publika signaturer bevarade (utom additiv valfri param + additiv record-fält/prop).
Befintliga tester i Agreements.Tests, Payroll.Tests och Infrastructure.Tests förblir gröna:
- Infrastructure KollektivavtalEngineTests läser bara `.ArligaDagar` (25/32) och `.Typ`/`.Belopp>0`.
- Payroll.Tests använder egen FakeRulesEngine (opåverkad).
Ej byggt lokalt (parallella builds). Kod läst mot anropad kod.
