using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RegionHR.IntegrationHub.Adapters.Skatteverket;

/// <summary>
/// Genererar AGI-XML (Arbetsgivardeklaration på individnivå) enligt Skatteverkets
/// officiella tekniska beskrivning (SKV 269, schemaversion 1.1).
///
/// Filen är uppbyggd som ett <c>Skatteverket</c>-dokument med:
/// <list type="bullet">
///   <item>Avsändare (programnamn, org.nr, teknisk kontaktperson)</item>
///   <item>Blankettgemensamt (arbetsgivare + kontaktperson)</item>
///   <item>En huvuduppgift (HU) per fil med summa arbetsgivaravgifter/skatteavdrag</item>
///   <item>En individuppgift (IU) per betalningsmottagare med fältkodsattribut</item>
/// </list>
/// Varje värdebärande element har ett <c>faltkod</c>-attribut och varje IU har ett
/// unikt <c>Specifikationsnummer</c> (fältkod 570). Max 1000 individer per fil.
/// </summary>
public sealed class AGIXmlGenerator
{
    // Instansschema (rot) och komponentschema (agd-prefix) enligt Skatteverkets XSD.
    private const string INSTANS_NS = "http://xmls.skatteverket.se/se/skatteverket/da/instans/schema/1.1";
    private const string KOMPONENT_NS = "http://xmls.skatteverket.se/se/skatteverket/da/komponent/schema/1.1";
    private const string XSI_NS = "http://www.w3.org/2001/XMLSchema-instance";
    private const string SCHEMA_LOCATION =
        "http://xmls.skatteverket.se/se/skatteverket/da/instans/schema/1.1 " +
        "http://xmls.skatteverket.se/se/skatteverket/da/arbetsgivardeklaration/arbetsgivardeklaration_1.1.xsd";

    private const int MAX_INDIVIDER_PER_FIL = 1000;

    /// <summary>
    /// Generera AGI-XML-filer för en löneperiod. Returnerar en eller flera XML-filer
    /// (max 1000 individer per fil). Varje fil är ett fullständigt Skatteverket-dokument.
    /// </summary>
    public IReadOnlyList<AGIFile> Generate(AGIInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var files = new List<AGIFile>();
        var totalt = input.Individer.Count;
        var antalFiler = Math.Max(1, (int)Math.Ceiling(totalt / (double)MAX_INDIVIDER_PER_FIL));

        for (var filIndex = 0; filIndex < antalFiler; filIndex++)
        {
            var start = filIndex * MAX_INDIVIDER_PER_FIL;
            var batch = input.Individer.Skip(start).Take(MAX_INDIVIDER_PER_FIL).ToList();
            var xml = GenerateXml(input, batch, start);
            var fileName = $"AGI_{input.Organisationsnummer}_{input.Period}_{filIndex + 1:D3}.xml";
            files.Add(new AGIFile(fileName, xml));
        }

        return files;
    }

    private static string GenerateXml(AGIInput input, List<AGIIndivid> individer, int globalStartIndex)
    {
        XNamespace instans = INSTANS_NS;
        XNamespace agd = KOMPONENT_NS;
        XNamespace xsi = XSI_NS;

        var root = new XElement(instans + "Skatteverket",
            new XAttribute("omrade", "Arbetsgivardeklaration"),
            new XAttribute(XNamespace.Xmlns + "agd", KOMPONENT_NS),
            new XAttribute(XNamespace.Xmlns + "xsi", XSI_NS),
            new XAttribute(xsi + "schemaLocation", SCHEMA_LOCATION),
            BuildAvsandare(agd, input),
            BuildBlankettgemensamt(agd, input),
            BuildHuvuduppgift(agd, input, individer),
            individer.Select((ind, i) =>
                BuildIndividuppgift(agd, input, ind, globalStartIndex + i)));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "no"), root);

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

    private static XElement BuildAvsandare(XNamespace agd, AGIInput input) =>
        new(agd + "Avsandare",
            new XElement(agd + "Programnamn", "OpenHR"),
            new XElement(agd + "Organisationsnummer", input.Organisationsnummer),
            new XElement(agd + "TekniskKontaktperson",
                new XElement(agd + "Namn", input.KontaktpersonNamn),
                new XElement(agd + "Telefon", input.KontaktpersonTelefon),
                new XElement(agd + "Epostadress", input.KontaktpersonEpost)),
            new XElement(agd + "Skapad", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));

    private static XElement BuildBlankettgemensamt(XNamespace agd, AGIInput input) =>
        new(agd + "Blankettgemensamt",
            new XElement(agd + "Arbetsgivare",
                new XElement(agd + "AgRegistreradId", input.Organisationsnummer),
                new XElement(agd + "Kontaktperson",
                    new XElement(agd + "Namn", input.KontaktpersonNamn),
                    new XElement(agd + "Telefon", input.KontaktpersonTelefon),
                    new XElement(agd + "Epostadress", input.KontaktpersonEpost))));

    private static XElement BuildHuvuduppgift(XNamespace agd, AGIInput input, List<AGIIndivid> individer) =>
        new(agd + "Blankett",
            new XElement(agd + "Arendeinformation",
                new XElement(agd + "Arendeagare", input.Organisationsnummer),
                new XElement(agd + "Period", input.Period)),
            new XElement(agd + "Blankettinnehall",
                new XElement(agd + "HU",
                    new XElement(agd + "ArbetsgivareHUGROUP",
                        Falt(agd, "AgRegistreradId", "201", input.Organisationsnummer)),
                    Falt(agd, "RedovisningsPeriod", "006", input.Period),
                    Falt(agd, "SummaArbAvgSlf", "487", Belopp(individer.Sum(i => i.Arbetsgivaravgifter))),
                    Falt(agd, "SummaSkatteavdr", "497", Belopp(individer.Sum(i => i.AvdragenSkatt))))));

    private static XElement BuildIndividuppgift(XNamespace agd, AGIInput input, AGIIndivid ind, int globalIndex)
    {
        var iu = new XElement(agd + "IU",
            new XElement(agd + "ArbetsgivareIUGROUP",
                Falt(agd, "AgRegistreradId", "201", input.Organisationsnummer)),
            new XElement(agd + "BetalningsmottagareIUGROUP",
                new XElement(agd + "BetalningsmottagareIDChoice",
                    Falt(agd, "BetalningsmottagarId", "215", ind.Personnummer))),
            Falt(agd, "RedovisningsPeriod", "006", input.Period),
            Falt(agd, "Specifikationsnummer", "570", SpecifikationsNummer(ind, globalIndex)));

        if (!string.IsNullOrWhiteSpace(ind.ArbetsplatsGatuadress))
            iu.Add(Falt(agd, "ArbetsplatsensGatuadress", "245", ind.ArbetsplatsGatuadress!));
        if (!string.IsNullOrWhiteSpace(ind.ArbetsplatsOrt))
            iu.Add(Falt(agd, "ArbetsplatsensOrt", "246", ind.ArbetsplatsOrt!));

        // Fält 011: Kontant ersättning som är underlag för arbetsgivaravgifter
        iu.Add(Falt(agd, "KontantErsattningUlagAG", "011", Belopp(ind.KontantBruttolonMm)));

        // Fält 012: Skattepliktiga förmåner (utom bil och drivmedel)
        if (ind.SkattepliktForman > 0)
            iu.Add(Falt(agd, "SkatteplFormanUlagAG", "012", Belopp(ind.SkattepliktForman)));

        // Fält 001: Avdragen preliminär skatt
        iu.Add(Falt(agd, "AvdrPrelSkatt", "001", Belopp(ind.AvdragenSkatt)));

        // Fält 050: Bilersättning (skattefri milersättning)
        if (ind.Milersattning > 0)
            iu.Add(Falt(agd, "Bilersattning", "050", Belopp(ind.Milersattning)));

        // Fält 051: Traktamente
        if (ind.Traktamente > 0)
            iu.Add(Falt(agd, "Traktamente", "051", Belopp(ind.Traktamente)));

        return iu;
    }

    private static XElement Falt(XNamespace agd, string element, string faltkod, string value) =>
        new(agd + element, new XAttribute("faltkod", faltkod), value);

    private static string SpecifikationsNummer(AGIIndivid ind, int globalIndex) =>
        !string.IsNullOrWhiteSpace(ind.Specifikationsnummer)
            ? ind.Specifikationsnummer!
            : (globalIndex + 1).ToString("D3", CultureInfo.InvariantCulture);

    // Skatteverket redovisar belopp i hela kronor.
    private static string Belopp(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
}

// Input/output-modeller.

public sealed class AGIInput
{
    public string Organisationsnummer { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;  // "YYYYMM"
    public string KontaktpersonNamn { get; set; } = string.Empty;
    public string KontaktpersonTelefon { get; set; } = string.Empty;
    public string KontaktpersonEpost { get; set; } = string.Empty;
    public List<AGIIndivid> Individer { get; set; } = [];
}

public sealed class AGIIndivid
{
    public string Personnummer { get; set; } = string.Empty;  // YYYYMMDDNNNN (12 siffror)
    public string Namn { get; set; } = string.Empty;          // Behålls för spårbarhet (ej i AGI-schemat)
    public decimal KontantBruttolonMm { get; set; }           // Fält 011
    public decimal AvdragenSkatt { get; set; }                 // Fält 001
    public decimal SkattepliktForman { get; set; }             // Fält 012
    public decimal SkattefriForman { get; set; }               // Behålls (redovisas ej i AGI)
    public decimal Traktamente { get; set; }                   // Fält 051
    public decimal Milersattning { get; set; }                 // Fält 050
    public decimal Avgiftsunderlag { get; set; }               // Underlag för arbetsgivaravgifter
    public decimal Arbetsgivaravgifter { get; set; }           // Summeras i HU (fält 487)
    public string? Specifikationsnummer { get; set; }          // Fält 570 (auto om null)
    public string? ArbetsplatsGatuadress { get; set; }         // Fält 245 (valfritt)
    public string? ArbetsplatsOrt { get; set; }                // Fält 246 (valfritt)
    public DateOnly? AnstallningFrom { get; set; }             // Behålls för spårbarhet
    public DateOnly? AnstallningTom { get; set; }              // Behålls för spårbarhet
}

public sealed record AGIFile(string FileName, string XmlContent);
