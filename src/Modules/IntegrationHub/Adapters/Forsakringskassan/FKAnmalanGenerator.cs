using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RegionHR.IntegrationHub.Adapters.Forsakringskassan;

/// <summary>
/// Genererar arbetsgivarens sjukanmälan till Försäkringskassan för ett sjukfall som
/// passerat sjuklöneperioden (dag 15+) eller som drivs som rehabärende.
///
/// Filen innehåller de uppgifter Försäkringskassan behöver för att ta över
/// sjukpenningärendet: personnummer, arbetsgivare, sjukperiod, sjuklöneperiod,
/// läkarintygsstatus och sjukersättningsgrundande uppgifter (månadslön,
/// sysselsättningsgrad, uppskattad sjukpenninggrundande årsinkomst).
///
/// Rättslig grund:
///   • Sjuklöneperioden är 14 kalenderdagar; arbetsgivaren anmäler sjukdomsfallet
///     till Försäkringskassan från dag 15: Lag (1991:1047) om sjuklön 7 §, 12 §.
///   • Läkarintyg krävs från dag 8: Lag (1991:1047) om sjuklön 8 §.
///   • Sjukpenninggrundande inkomst (SGI): Socialförsäkringsbalken (2010:110) 25 kap.
///
/// VIKTIGT — ÄRLIG MÄRKNING: Denna generator producerar ENDAST underlaget/filen.
/// Skarp överföring till Försäkringskassans e-tjänst (webservice/SSEK) kräver
/// tecknat avtal och teknisk anslutning. Fältet <see cref="OverforingStatus"/>
/// och XML-elementet <c>Overforingsstatus</c> markerar detta tydligt i varje fil.
///
/// Trösklarna (14/8/15) speglar <c>Rehabkedja</c> i HalsoSAM-modulen men upprepas
/// här som konstanter eftersom IntegrationHub inte refererar HalsoSAM. Håll dem i synk.
/// </summary>
public sealed class FKAnmalanGenerator
{
    /// <summary>Sjuklöneperiodens längd i kalenderdagar (dag 1–14).</summary>
    public const int SjuklonePeriodDagar = 14;

    /// <summary>Läkarintyg krävs från och med denna sjukdag.</summary>
    public const int LakarintygFranDag = 8;

    /// <summary>Arbetsgivaren anmäler sjukdomsfallet till Försäkringskassan från denna sjukdag.</summary>
    public const int ForsakringskassanAnmalanFranDag = 15;

    /// <summary>Giltiga sjukskrivningsgrader (procent).</summary>
    public static readonly IReadOnlyList<int> GiltigaSjukskrivningsgrader = [25, 50, 75, 100];

    /// <summary>
    /// Överföringsstatus som stämplas i varje genererad fil. Signalerar att filen
    /// är ett underlag och INTE har överförts till Försäkringskassan.
    /// </summary>
    public const string OverforingStatus = "EJ_OVERFORD_KRAVER_FK_ANSLUTNING";

    private const string NS = "urn:openhr:forsakringskassan:sjukanmalan:1.0";

    /// <summary>
    /// Genererar en FK-anmälan från <paramref name="input"/>. Returnerar alltid ett
    /// resultat: vid blockerande fel (saknat personnummer / startdatum) är
    /// <see cref="FKAnmalanResult.Giltig"/> false och filinnehållet tomt.
    /// </summary>
    public FKAnmalanResult Generera(FKAnmalanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fel = new List<string>();
        var varningar = new List<string>();

        var personnummer = NormalisceraPersonnummer(input.Personnummer);
        if (string.IsNullOrEmpty(personnummer))
            fel.Add("Personnummer saknas eller har fel format (12 siffror krävs).");

        if (input.SjukfranvaroStart == default)
            fel.Add("Sjukfrånvarons första dag (dag 1) saknas.");

        // Blockerande fel → returnera tomt resultat med felmeddelanden.
        if (fel.Count > 0)
        {
            return new FKAnmalanResult(
                Giltig: false, Fel: fel, Varningar: varningar, Anmalan: null,
                Filnamn: "FK-sjukanmalan.xml", XmlInnehall: string.Empty,
                Sammanfattning: string.Empty, Overforingsstatus: OverforingStatus);
        }

        var idag = input.Idag ?? DateOnly.FromDateTime(DateTime.Today);
        var forstaSjukdag = input.SjukfranvaroStart;

        // Referensdatum: sista sjukdagen om avslutat, annars idag (pågående).
        var referens = input.SjukfranvaroSlut ?? idag;
        if (referens < forstaSjukdag) referens = forstaSjukdag;
        var pagaende = input.SjukfranvaroSlut is null;

        var antalDagar = referens.DayNumber - forstaSjukdag.DayNumber + 1;

        var sjuklonFran = forstaSjukdag;
        var sjuklonTill = forstaSjukdag.AddDays(SjuklonePeriodDagar - 1);              // dag 14
        var fkFran = forstaSjukdag.AddDays(SjuklonePeriodDagar);                       // dag 15
        var lakarintygKravsFran = forstaSjukdag.AddDays(LakarintygFranDag - 1);        // dag 8

        var arFKPliktig = antalDagar >= ForsakringskassanAnmalanFranDag;

        // Sjukskrivningsgrad: default 100, validera mot tillåtna nivåer.
        var grad = input.Sjukskrivningsgrad == 0 ? 100 : input.Sjukskrivningsgrad;
        if (!GiltigaSjukskrivningsgrader.Contains(grad))
            varningar.Add($"Sjukskrivningsgrad {grad}% är inte en standardnivå (25/50/75/100).");

        if (!arFKPliktig)
            varningar.Add(
                $"Sjukfallet är {antalDagar} dagar. Anmälan till Försäkringskassan görs normalt " +
                $"först från dag {ForsakringskassanAnmalanFranDag} (efter sjuklöneperioden). " +
                "Detta är ett förtida utkast.");

        if (!input.LakarintygFinns && antalDagar >= LakarintygFranDag)
            varningar.Add(
                $"Läkarintyg saknas registrerat. Läkarintyg krävs från dag {LakarintygFranDag} " +
                $"({lakarintygKravsFran:yyyy-MM-dd}).");

        if (string.IsNullOrWhiteSpace(input.ArbetsgivareOrgNr))
            varningar.Add("Arbetsgivarens organisationsnummer saknas.");

        if (string.IsNullOrWhiteSpace(input.ArbetsgivareNamn))
            varningar.Add("Arbetsgivarens namn saknas.");

        // Sjukpenninggrundande årsinkomst (uppskattad) = månadslön × 12.
        decimal? sgiArsinkomst = input.Manadslon.HasValue
            ? Math.Round(input.Manadslon.Value * 12m, 0, MidpointRounding.AwayFromZero)
            : null;

        var anmalan = new FKAnmalan(
            Typ: input.Typ,
            Personnummer: personnummer,
            Fornamn: input.Fornamn?.Trim() ?? string.Empty,
            Efternamn: input.Efternamn?.Trim() ?? string.Empty,
            ArbetsgivareNamn: input.ArbetsgivareNamn?.Trim() ?? string.Empty,
            ArbetsgivareOrgNr: input.ArbetsgivareOrgNr?.Trim() ?? string.Empty,
            ForstaSjukdag: forstaSjukdag,
            SistaSjukdag: input.SjukfranvaroSlut,
            Pagaende: pagaende,
            AntalKalenderdagar: antalDagar,
            Sjukskrivningsgrad: grad,
            LakarintygFinns: input.LakarintygFinns,
            LakarintygDatum: input.LakarintygDatum,
            LakarintygKravsFranDatum: lakarintygKravsFran,
            SjuklonePeriodFran: sjuklonFran,
            SjuklonePeriodTill: sjuklonTill,
            ForsakringskassanFranDatum: fkFran,
            Manadslon: input.Manadslon,
            Sysselsattningsgrad: input.Sysselsattningsgrad,
            UtbetaldSjuklon: input.UtbetaldSjuklon,
            SjukpenninggrundandeArsinkomst: sgiArsinkomst,
            ArFKPliktig: arFKPliktig);

        var xml = ByggXml(anmalan);
        var sammanfattning = ByggSammanfattning(anmalan, varningar);
        var filnamn = $"FK-sjukanmalan_{personnummer}_{forstaSjukdag:yyyyMMdd}.xml";

        return new FKAnmalanResult(
            Giltig: true, Fel: fel, Varningar: varningar, Anmalan: anmalan,
            Filnamn: filnamn, XmlInnehall: xml, Sammanfattning: sammanfattning,
            Overforingsstatus: OverforingStatus);
    }

    private static string ByggXml(FKAnmalan a)
    {
        XNamespace ns = NS;

        var medarbetare = new XElement(ns + "Medarbetare",
            new XElement(ns + "Personnummer", a.Personnummer),
            new XElement(ns + "Fornamn", a.Fornamn),
            new XElement(ns + "Efternamn", a.Efternamn));

        var sjukfall = new XElement(ns + "Sjukfall",
            new XElement(ns + "ForstaSjukdag", Datum(a.ForstaSjukdag)),
            a.SistaSjukdag.HasValue
                ? new XElement(ns + "SistaSjukdag", Datum(a.SistaSjukdag.Value))
                : null,
            new XElement(ns + "Pagaende", a.Pagaende ? "true" : "false"),
            new XElement(ns + "AntalKalenderdagar", a.AntalKalenderdagar.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "Sjukskrivningsgrad", a.Sjukskrivningsgrad.ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "Lakarintyg",
                new XAttribute("finns", a.LakarintygFinns ? "true" : "false"),
                a.LakarintygDatum.HasValue ? new XAttribute("datum", Datum(a.LakarintygDatum.Value)) : null,
                new XAttribute("kravsFran", Datum(a.LakarintygKravsFranDatum))));

        var sjukloneperiod = new XElement(ns + "Sjukloneperiod",
            new XElement(ns + "Fran", Datum(a.SjuklonePeriodFran)),
            new XElement(ns + "Till", Datum(a.SjuklonePeriodTill)),
            a.UtbetaldSjuklon.HasValue
                ? new XElement(ns + "UtbetaldSjuklon", Belopp(a.UtbetaldSjuklon.Value))
                : null);

        var fkPeriod = new XElement(ns + "ForsakringskassansPeriod",
            new XElement(ns + "FranDag15", Datum(a.ForsakringskassanFranDatum)),
            new XElement(ns + "Anmalningspliktig", a.ArFKPliktig ? "true" : "false"));

        var sgi = new XElement(ns + "SjukersattningsgrundandeUppgifter",
            a.Manadslon.HasValue ? new XElement(ns + "Manadslon", Belopp(a.Manadslon.Value)) : null,
            a.Sysselsattningsgrad.HasValue
                ? new XElement(ns + "Sysselsattningsgrad",
                    a.Sysselsattningsgrad.Value.ToString("F2", CultureInfo.InvariantCulture))
                : null,
            a.SjukpenninggrundandeArsinkomst.HasValue
                ? new XElement(ns + "SjukpenninggrundandeArsinkomst", Belopp(a.SjukpenninggrundandeArsinkomst.Value))
                : null);

        var root = new XElement(ns + "Sjukanmalan",
            new XAttribute("typ", a.Typ.ToString()),
            new XComment(" OpenHR: Arbetsgivarens sjukanmälan till Försäkringskassan (UTKAST). " +
                         "Ej överförd — skarp överföring kräver avtal/anslutning till FK:s e-tjänst. "),
            new XElement(ns + "Overforingsstatus", OverforingStatus),
            new XElement(ns + "Genererad",
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            ByggArbetsgivare(ns, a),
            medarbetare,
            sjukfall,
            sjukloneperiod,
            fkPeriod,
            sgi);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        });
        doc.WriteTo(xmlWriter);
        xmlWriter.Flush();
        return writer.ToString();
    }

    private static XElement ByggArbetsgivare(XNamespace ns, FKAnmalan a)
    {
        var arbetsgivare = new XElement(ns + "Arbetsgivare",
            new XElement(ns + "Namn", a.ArbetsgivareNamn),
            new XElement(ns + "Organisationsnummer", a.ArbetsgivareOrgNr));
        return arbetsgivare;
    }

    private static string Datum(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Belopp i kronor med två decimaler, punkt som decimaltecken (maskinläsbart).
    private static string Belopp(decimal v) =>
        Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private static string ByggSammanfattning(FKAnmalan a, IReadOnlyList<string> varningar)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SJUKANMÄLAN TILL FÖRSÄKRINGSKASSAN (UTKAST)");
        sb.AppendLine("Ej överförd — skarp överföring kräver avtal/anslutning till FK:s e-tjänst.");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"Typ:                 {(a.Typ == FKAnmalanTyp.Rehabarende ? "Rehabärende" : "Sjukanmälan")}");
        sb.AppendLine($"Medarbetare:         {a.Fornamn} {a.Efternamn} ({a.Personnummer})");
        sb.AppendLine($"Arbetsgivare:        {a.ArbetsgivareNamn} (org.nr {a.ArbetsgivareOrgNr})");
        sb.AppendLine();
        sb.AppendLine($"Första sjukdag:      {a.ForstaSjukdag:yyyy-MM-dd}");
        sb.AppendLine(a.Pagaende
            ? "Sista sjukdag:       pågående"
            : $"Sista sjukdag:       {a.SistaSjukdag:yyyy-MM-dd}");
        sb.AppendLine($"Antal kalenderdagar: {a.AntalKalenderdagar}");
        sb.AppendLine($"Sjukskrivningsgrad:  {a.Sjukskrivningsgrad}%");
        sb.AppendLine($"Läkarintyg:          {(a.LakarintygFinns ? $"ja ({a.LakarintygDatum:yyyy-MM-dd})" : "saknas")} " +
                      $"(krävs från {a.LakarintygKravsFranDatum:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine($"Sjuklöneperiod:      {a.SjuklonePeriodFran:yyyy-MM-dd} – {a.SjuklonePeriodTill:yyyy-MM-dd} (arbetsgivaren)");
        if (a.UtbetaldSjuklon.HasValue)
            sb.AppendLine($"Utbetald sjuklön:     {a.UtbetaldSjuklon.Value:N2} kr");
        sb.AppendLine($"Försäkringskassan:   från {a.ForsakringskassanFranDatum:yyyy-MM-dd} (dag {ForsakringskassanAnmalanFranDag})");
        sb.AppendLine($"Anmälningspliktig:   {(a.ArFKPliktig ? "ja" : "nej — förtida utkast")}");
        sb.AppendLine();
        sb.AppendLine("Sjukersättningsgrundande uppgifter:");
        sb.AppendLine($"  Månadslön:                    {(a.Manadslon.HasValue ? $"{a.Manadslon.Value:N2} kr" : "-")}");
        sb.AppendLine($"  Sysselsättningsgrad:          {(a.Sysselsattningsgrad.HasValue ? $"{a.Sysselsattningsgrad.Value:F2}%" : "-")}");
        sb.AppendLine($"  Sjukpenninggrundande årsink.: {(a.SjukpenninggrundandeArsinkomst.HasValue ? $"{a.SjukpenninggrundandeArsinkomst.Value:N0} kr (uppskattad)" : "-")}");

        if (varningar.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Varningar:");
            foreach (var v in varningar)
                sb.AppendLine($"  • {v}");
        }

        return sb.ToString();
    }

    /// <summary>Tar bort allt utom siffror och kräver exakt 12 siffror (YYYYMMDDNNNN).</summary>
    private static string NormalisceraPersonnummer(string? pnr)
    {
        if (string.IsNullOrWhiteSpace(pnr)) return string.Empty;
        var digits = new string(pnr.Where(char.IsDigit).ToArray());
        return digits.Length == 12 ? digits : string.Empty;
    }
}

/// <summary>Typ av FK-anmälan.</summary>
public enum FKAnmalanTyp
{
    /// <summary>Ordinarie sjukanmälan (dag 15+).</summary>
    Sjukanmalan,

    /// <summary>Anmälan i samband med ett pågående rehabärende.</summary>
    Rehabarende
}

/// <summary>
/// Indata till <see cref="FKAnmalanGenerator"/>. Fylls i från medarbetare, aktiv
/// anställning, rehabärende (sjukfallets dag 1) och tenant-konfiguration.
/// </summary>
public sealed class FKAnmalanInput
{
    public string Personnummer { get; set; } = string.Empty;
    public string Fornamn { get; set; } = string.Empty;
    public string Efternamn { get; set; } = string.Empty;
    public string ArbetsgivareNamn { get; set; } = string.Empty;
    public string ArbetsgivareOrgNr { get; set; } = string.Empty;

    /// <summary>Sjukfallets första dag (dag 1). Milstolparna räknas härifrån.</summary>
    public DateOnly SjukfranvaroStart { get; set; }

    /// <summary>Sista sjukdag, eller null om sjukfallet pågår.</summary>
    public DateOnly? SjukfranvaroSlut { get; set; }

    /// <summary>Sjukskrivningsgrad i procent (25/50/75/100). Default 100.</summary>
    public int Sjukskrivningsgrad { get; set; } = 100;

    public decimal? Manadslon { get; set; }
    public decimal? Sysselsattningsgrad { get; set; }

    /// <summary>Utbetald sjuklön under sjuklöneperioden (valfritt).</summary>
    public decimal? UtbetaldSjuklon { get; set; }

    public bool LakarintygFinns { get; set; }
    public DateOnly? LakarintygDatum { get; set; }

    public FKAnmalanTyp Typ { get; set; } = FKAnmalanTyp.Sjukanmalan;

    /// <summary>
    /// Referensdatum för "idag" (pågående sjukfall). Injiceras i test för determinism;
    /// null → <see cref="DateTime.Today"/>.
    /// </summary>
    public DateOnly? Idag { get; set; }
}

/// <summary>Strukturerad, beräknad FK-anmälan (utdata från generatorn).</summary>
public sealed record FKAnmalan(
    FKAnmalanTyp Typ,
    string Personnummer,
    string Fornamn,
    string Efternamn,
    string ArbetsgivareNamn,
    string ArbetsgivareOrgNr,
    DateOnly ForstaSjukdag,
    DateOnly? SistaSjukdag,
    bool Pagaende,
    int AntalKalenderdagar,
    int Sjukskrivningsgrad,
    bool LakarintygFinns,
    DateOnly? LakarintygDatum,
    DateOnly LakarintygKravsFranDatum,
    DateOnly SjuklonePeriodFran,
    DateOnly SjuklonePeriodTill,
    DateOnly ForsakringskassanFranDatum,
    decimal? Manadslon,
    decimal? Sysselsattningsgrad,
    decimal? UtbetaldSjuklon,
    decimal? SjukpenninggrundandeArsinkomst,
    bool ArFKPliktig);

/// <summary>Resultatet av en FK-anmälangenerering: fil + validering + läsbar sammanfattning.</summary>
public sealed record FKAnmalanResult(
    bool Giltig,
    IReadOnlyList<string> Fel,
    IReadOnlyList<string> Varningar,
    FKAnmalan? Anmalan,
    string Filnamn,
    string XmlInnehall,
    string Sammanfattning,
    string Overforingsstatus);
