# Wave 5 — Slice: pension-fil (Tjänstepension AKAP-KR)

## Vad som byggdes
Avgiftsbestämd tjänstepension enligt **AKAP-KR** (premie 6 % under 7,5 IBB, 31,5 % över, tak 30 IBB),
pensionsredovisningsfil (CSV + XML) och en UI-sida `/lon/pension` för att ta fram/ladda ner
redovisning per lönekörning/period. Skarp inlämning till KPA/Valcentralen är tydligt märkt som
avtalsberoende och **ej aktiverad**.

## Nya filer (inom mina kataloger — inga delade filer rörda)
- `src/Modules/Payroll/Domain/PensionsberakningsEngine.cs` — `PensionsberakningsEngine` + `PensionPremie`-record.
- `src/Infrastructure/Integrations/PensionFileGenerator.cs` — `PensionFileGenerator`, `PensionRedovisning`, `PensionRedovisningIndivid`, `PensionFil`, `PensionFilFormat`.
- `src/Web/Components/Pages/Lon/Pension.razor` — sida `@page "/lon/pension"`.
- `tests/Payroll.Tests/PensionsberakningsEngineTests.cs` — premieberäkningstester (18 fall).
- `tests/RegionHR.Infrastructure.Tests/Integrations/PensionFileGeneratorTests.cs` — filgeneratortester (7 fall).

## Verifierade 2026-värden (via webben)
- Inkomstbasbelopp (IBB) 2026 = **83 400 kr** (SFS 2025:1002) — matchar befintlig `IBB_2026` i lönemotorn.
- 7,5 IBB = 625 500 kr/år (52 125 kr/mån); 30 IBB = 2 502 000 kr/år (208 500 kr/mån).
- Premie: 6,0 % under brytpunkt, 31,5 % över, 0 % över 30 IBB-taket.

## FÖRBJUDNA FILER — INTE rörda
DependencyInjection.cs, RegionHRDbContext.cs, SeedData.cs, Program.cs, NavMenu.razor, *.csproj, Directory.*.props.
Delade Payroll-filer (PayrollInputBuilder, PayrollCalculationEngine) och Export.razor är **inte** rörda.

## Behörighet / RouteAccessPolicy
Ingen ändring krävs. `/lon/pension` täcks redan av prefix-regeln `("/lon", HrAdmin)` i
`src/Web/Services/RouteAccessPolicy.cs` → endast **HR/Admin** når sidan (lönekänsligt). Bekräftat: ingen
egen policyrad behövs.

## DI
Ingen DI-registrering krävs. Sidan instansierar `new PensionsberakningsEngine()` och
`new PensionFileGenerator()` direkt (samma mönster som Export.razor:s `new AGIXmlGenerator()`).
Båda är rena/tillståndslösa. (Valfritt kan de registreras som singletons om annan kod vill injicera dem.)

## DbSet / migrationer
Inga. Använder befintliga `PayrollRuns`, `PayrollResults` (fältet `Pensionsgrundande`, redan mappat via
`MoneyConverter`) och `Employees`.

## NAV — valfri men rekommenderad (kräver ändring i förbjuden NavMenu.razor → integratören lägger in)
I `src/Web/Components/Layout/NavMenu.razor`, i "Salary"-gruppen (HR-gated, efter rad 30), lägg till:

```razor
<MudNavLink Href="/lon/pension" Icon="@Icons.Material.Filled.Savings">Tjänstepension (AKAP-KR)</MudNavLink>
```

## Publika signaturer (klistringsbara för anropare)
```csharp
// RegionHR.Payroll.Domain
public sealed class PensionsberakningsEngine
{
    public const decimal PremiesatsUnderGrans = 0.06m;
    public const decimal PremiesatsOverGrans = 0.315m;
    public const decimal GransIBB = 7.5m;
    public const decimal TakIBB = 30m;
    public static decimal Inkomstbasbelopp(int year);
    public PensionPremie BeraknaArspremie(Money pensionsgrundandeArslon, int year);
    public PensionPremie BeraknaManadspremie(Money pensionsgrundandeManadslon, int year);
}
public sealed record PensionPremie(Money PensionsgrundandeLon, Money PremiegrundandeBelopp,
    Money PremieUnderGrans, Money PremieOverGrans, Money TotalPremie,
    decimal Inkomstbasbelopp, Money GransBelopp, Money TakBelopp)
{ public bool OverstigerTak { get; } }

// RegionHR.Infrastructure.Integrations
public sealed class PensionFileGenerator
{
    public const string Disclaimer;
    public PensionFil Generera(PensionRedovisning r, PensionFilFormat format);
    public string GenereraCsv(PensionRedovisning r);
    public string GenereraXml(PensionRedovisning r);
}
public enum PensionFilFormat { Csv, Xml }
public sealed record PensionRedovisningIndivid(string Personnummer, string Namn,
    decimal PensionsgrundandeLon, decimal PremieUnderGrans, decimal PremieOverGrans,
    decimal TotalPremie, string? Kostnadsstalle = null);
public sealed record PensionRedovisning(string ArbetsgivareNamn, string Organisationsnummer,
    int Ar, int Manad, decimal Inkomstbasbelopp, IReadOnlyList<PensionRedovisningIndivid> Individer,
    string Avtal = "AKAP-KR", string Pensionsleverantor = "Ej vald (kräver avtal)");
public sealed record PensionFil(string FileName, string Content, string ContentType);
```

## Byggrisk
Låg. Byggde inte (parallella builds förbjudna). Matchade befintliga signaturer verifierade i
Export.razor (`PayrollRunId.From/.Value`, `db.PayrollResults/.Employees`, `(string)e.Personnummer`,
`e.FulltNamn`, JS `downloadFile`). `MudSimpleTable`/`MudSelect`-mönster används redan brett i kodbasen.
Inga nya paket. TreatWarningsAsErrors: usings i sidan är alla använda; nullable hanterat.
