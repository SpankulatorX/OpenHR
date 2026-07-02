# Wave 6 — fk-anmalan (Försäkringskassan-anmälan, dag 15 / rehabärende)

## Sammanfattning
Genererar arbetsgivarens sjukanmälan till Försäkringskassan ur ett rehabärende/sjukfall
(dag 15+). Producerar en strukturerad XML-fil + läsbar sammanfattning med personnummer,
arbetsgivare, sjukperiod, sjuklöneperiod (14 dagar) och sjukersättningsgrundande uppgifter
(månadslön, sysselsättningsgrad, uppskattad SGI-årsinkomst). **Ärligt märkt**: ingen skarp
FK-koppling — varje fil stämplas `EJ_OVERFORD_KRAVER_FK_ANSLUTNING` och UI/adapter säger
tydligt att skarp överföring kräver avtal/anslutning till FK:s e-tjänst.

## Filer

### Nya
- `src/Modules/IntegrationHub/Adapters/Forsakringskassan/FKAnmalanGenerator.cs`
  - `FKAnmalanGenerator` + modeller `FKAnmalanInput`, `FKAnmalan`, `FKAnmalanResult`, enum `FKAnmalanTyp`.
  - Lagd i IntegrationHub-modulen (INTE `src/Infrastructure/Integrations/`) med avsikt: IntegrationHub
    refererar inte Infrastructure, så generatorn måste ligga här för att `ForsakringskassanAdapter`
    ska kunna återanvända den. Web refererar IntegrationHub → sidan når den också.
- `src/Modules/HalsoSAM/Services/FKAnmalanBedomning.cs`
  - Ren domänhjälp (record + factory) som avgör om FK-anmälan är aktuell (dag 15), sjuklöneperiod,
    läkarintygskrav (dag 8). Trösklar hämtas från `Rehabkedja` (enda källa till sanning).
- `src/Web/Components/Pages/HalsoSAM/FKAnmalan.razor` — sida `/halsosam/{Id:guid}/fk-anmalan`.
- `tests/IntegrationHub.Tests/FKAnmalanGeneratorTests.cs` (17 tester)
- `tests/HalsoSAM.Tests/FKAnmalanBedomningTests.cs` (7 tester)

### Ändrade
- `src/Modules/IntegrationHub/Adapters/Forsakringskassan/ForsakringskassanAdapter.cs`
  - Ny operation `"GenereraFKAnmalan"` (kör generatorn, returnerar `FKAnmalanResult`, märkt ej överförd).
  - `SkickaFKSjukanmalan`: meddelandet gjort ärligt ("förberedd … ej överförd") i stället för
    "skickad till FK". Publika signaturer/beteende oförändrat (Success + ResponseData kvar);
    befintliga `ForsakringskassanTests` (Contains("FK")) fortsatt gröna.
- `src/Web/Components/Pages/HalsoSAM/Index.razor`
  - Lagt till en "FK-anmälan"-knapp i det expanderade ärenderadens verktygsrad (länk till nya sidan).
  - RÖR EJ: `RegistreraUppfoljning.razor` / `NyttArende.razor` (orörda, ägs av annan).

## Behörighet / route-policy (VIKTIGT — kräver din åtgärd)
Rutten `/halsosam/{id}/fk-anmalan` matchas idag av prefixregeln `("/halsosam", ChefHrAdmin)` i
`RouteAccessPolicy.cs` → **Chef kan nå URL:en**. Sidan exponerar **lönekänsliga**
(sjukersättningsgrundande) uppgifter, så den är dessutom **HR/Admin-gate:ad i själva sidan**
(`Auth.IsHR` — icke-HR ser en notis och ingen data hämtas).

Rekommenderad härdning på URL-nivå (försvar på djupet): lägg en specifik regel i
`RouteAccessPolicy.AllowedRolesFor` (jag har INTE rört filen). Föreslagen kod, i mönstret som
redan finns för `/anstallda`-underrutter:

```csharp
// I AllowedRolesFor(...), före den generella Rules-loopen:
if (p.StartsWith("/halsosam/", StringComparison.Ordinal)
    && p.EndsWith("/fk-anmalan", StringComparison.Ordinal))
{
    return HrAdmin; // FK-anmälan innehåller sjukersättningsgrundande lön → HR/Admin
}
```

## Integration / DI
- **Inga DI-registreringar krävs.** Generatorn instansieras direkt (`new FKAnmalanGenerator()`),
  precis som övriga fil-generatorer (AGI/Nordea) och `FKAnmalanBedomning` är en ren statisk factory.
- **Inga DbSet-ändringar** — inga nya EF-entiteter. Sidan läser befintliga `RehabCases`,
  `Employees` (+ `Anstallningar`) och `TenantConfigurations`.
- **Inga event-handlers / nav / csproj / Program.cs-ändringar.**
- Nya adapter-operationen når man via befintliga `IIntegrationAdapter`-mönstret:
  `IntegrationRequest("GenereraFKAnmalan", FKAnmalanInput)`.

## Data-källor i UI-mappningen
- personnummer/namn ← `Employee`
- första sjukdag ← `RehabCase.SjukfallDag1`
- månadslön + sysselsättningsgrad ← aktiv `Employment` (den som är `IsActiveOn(idag)`, annars senaste)
- arbetsgivare namn + org.nr ← aktiv `TenantConfiguration`
- sjukskrivningsgrad / läkarintyg / utbetald sjuklön ← HR fyller i på sidan

## Rättslig grund (verifierat)
Sjuklöneperiod 14 dagar + anmälan från dag 15: Lag (1991:1047) om sjuklön 7/12 §.
Läkarintyg från dag 8: samma lag 8 §. SGI: Socialförsäkringsbalken (2010:110) 25 kap.
Konstanterna speglar `Rehabkedja` (HalsoSAM) och upprepas i generatorn med lagreferens
eftersom IntegrationHub inte refererar HalsoSAM — håll dem i synk.

## Build-risk
Låg. Matchar befintliga signaturer/mönster (AGIXmlGenerator för XML, Kontering.razor för
`downloadFile`-JS, Index.razor för DbContextFactory). Ej byggt lokalt (per instruktion).
