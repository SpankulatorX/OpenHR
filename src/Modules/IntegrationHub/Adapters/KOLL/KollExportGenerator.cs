using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.IntegrationHub.Adapters.KOLL;

/// <summary>
/// Genererar en katalogexportfil med anställningsmasterdata till KOLL — Region Örebro läns
/// katalogtjänst (RÖL). Katalogtjänsten behöver en aktuell bild av vilka personer som är
/// anställda, på vilken enhet, med vilken befattning, anställningsform och status för att
/// kunna sätta behörigheter och synliggöra organisationen i regionens system.
///
/// Filen är ett XML-dokument (parallellt med AGI-/FK-generatorerna) med:
/// <list type="bullet">
///   <item>Överföringsstatus + genereringstidpunkt + avsändande organisation.</item>
///   <item>En <c>Anstallning</c>-post per aktiv/kommande/avslutad anställning med
///     person, anställnings-id, enhet (namn + kostnadsställe), befattning, anställningsform,
///     sysselsättningsgrad, giltighetsperiod och status.</item>
/// </list>
///
/// VIKTIGT — ÄRLIG MÄRKNING: Generatorn producerar ENDAST filen. Skarp överföring till
/// KOLL (SFTP-filsläpp eller katalogtjänstens webbtjänst) kräver konfigurerad anslutning.
/// Elementet <c>Overforingsstatus</c> stämplas i varje fil.
/// </summary>
public sealed class KollExportGenerator
{
    /// <summary>Överföringsstatus som stämplas i varje genererad fil.</summary>
    public const string OverforingStatus = "EJ_OVERFORD_KRAVER_KOLL_ANSLUTNING";

    private const string NS = "urn:openhr:koll:katalog:1.0";

    /// <summary>
    /// Genererar en KOLL-katalogfil ur <paramref name="input"/>. Statusen (Aktiv/Kommande/Avslutad)
    /// beräknas mot <see cref="KollExportInput.Referensdatum"/> (default = idag).
    /// </summary>
    public KollExportResult Generera(KollExportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var varningar = new List<string>();
        var referens = input.Referensdatum ?? DateOnly.FromDateTime(DateTime.Today);
        var nu = input.Genererad ?? DateTime.Now;

        var poster = new List<KollKatalogpost>(input.Poster.Count);
        foreach (var p in input.Poster)
        {
            if (string.IsNullOrWhiteSpace(p.Befattning))
                varningar.Add($"{p.PersonnummerFormaterat}: saknar befattning i katalogposten.");

            var status = BeraknaStatus(p.Startdatum, p.Slutdatum, referens);
            poster.Add(new KollKatalogpost(p, status));
        }

        var xml = ByggXml(input, poster, nu);
        var filnamn = $"KOLL-katalog_{Sanera(input.Organisationsnummer)}_{referens:yyyyMMdd}.xml";
        var sammanfattning = ByggSammanfattning(input, poster, referens);

        return new KollExportResult(
            Filnamn: filnamn,
            XmlInnehall: xml,
            AntalPoster: poster.Count,
            AntalAktiva: poster.Count(p => p.Status == KollAnstallningsStatus.Aktiv),
            Overforingsstatus: OverforingStatus,
            Sammanfattning: sammanfattning,
            Varningar: varningar);
    }

    private static KollAnstallningsStatus BeraknaStatus(DateOnly start, DateOnly? slut, DateOnly referens)
    {
        if (start > referens)
            return KollAnstallningsStatus.Kommande;
        if (slut is { } s && s < referens)
            return KollAnstallningsStatus.Avslutad;
        return KollAnstallningsStatus.Aktiv;
    }

    private static string ByggXml(KollExportInput input, IReadOnlyList<KollKatalogpost> poster, DateTime nu)
    {
        XNamespace ns = NS;

        var anstallningar = new XElement(ns + "Anstallningar",
            poster.Select(p =>
            {
                var k = p.Post;
                return new XElement(ns + "Anstallning",
                    new XAttribute("anstallningsId", k.AnstallningsId),
                    new XElement(ns + "Personnummer", k.PersonnummerFormaterat),
                    new XElement(ns + "Efternamn", k.Efternamn),
                    new XElement(ns + "Fornamn", k.Fornamn),
                    new XElement(ns + "Enhet",
                        new XAttribute("kostnadsstalle", k.EnhetKostnadsstalle ?? string.Empty),
                        k.EnhetNamn),
                    string.IsNullOrWhiteSpace(k.Befattning)
                        ? null
                        : new XElement(ns + "Befattning", k.Befattning),
                    new XElement(ns + "Anstallningsform",
                        new XAttribute("kod", k.Anstallningsform.ToString()),
                        AnstallningsformText(k.Anstallningsform)),
                    new XElement(ns + "Sysselsattningsgrad",
                        k.Sysselsattningsgrad.ToString("F2", CultureInfo.InvariantCulture)),
                    new XElement(ns + "Startdatum", Datum(k.Startdatum)),
                    k.Slutdatum.HasValue ? new XElement(ns + "Slutdatum", Datum(k.Slutdatum.Value)) : null,
                    new XElement(ns + "Status", StatusText(p.Status)),
                    string.IsNullOrWhiteSpace(k.HsaId) ? null : new XElement(ns + "HsaId", k.HsaId));
            }));

        var root = new XElement(ns + "Katalogexport",
            new XAttribute("version", "1.0"),
            new XComment(" OpenHR: Katalogexport av anställningsmasterdata till KOLL (RÖL katalogtjänst). " +
                         "Ej överförd — skarp överföring kräver konfigurerad SFTP-/WS-anslutning. "),
            new XElement(ns + "Overforingsstatus", OverforingStatus),
            new XElement(ns + "Genererad", nu.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(ns + "Organisation",
                new XElement(ns + "Organisationsnummer", input.Organisationsnummer ?? string.Empty),
                new XElement(ns + "Namn", input.Organisationsnamn ?? string.Empty)),
            new XElement(ns + "AntalPoster", poster.Count.ToString(CultureInfo.InvariantCulture)),
            anstallningar);

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

    private static string ByggSammanfattning(
        KollExportInput input, IReadOnlyList<KollKatalogpost> poster, DateOnly referens)
    {
        var sb = new StringBuilder();
        sb.AppendLine("KOLL-KATALOGEXPORT (UTKAST)");
        sb.AppendLine("Ej överförd — skarp överföring kräver konfigurerad anslutning till katalogtjänsten.");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"Organisation:  {input.Organisationsnamn} (org.nr {input.Organisationsnummer})");
        sb.AppendLine($"Referensdatum: {referens:yyyy-MM-dd}");
        sb.AppendLine($"Antal poster:  {poster.Count}");
        sb.AppendLine($"  Aktiva:      {poster.Count(p => p.Status == KollAnstallningsStatus.Aktiv)}");
        sb.AppendLine($"  Kommande:    {poster.Count(p => p.Status == KollAnstallningsStatus.Kommande)}");
        sb.AppendLine($"  Avslutade:   {poster.Count(p => p.Status == KollAnstallningsStatus.Avslutad)}");
        return sb.ToString();
    }

    /// <summary>Svensk klartext för anställningsform (KOLL-katalogens visningsvärde).</summary>
    public static string AnstallningsformText(EmploymentType form) => form switch
    {
        EmploymentType.Tillsvidare => "Tillsvidareanställning",
        EmploymentType.Vikariat => "Vikariat",
        EmploymentType.Provanstallning => "Provanställning",
        EmploymentType.SAVA => "Allmän visstidsanställning",
        EmploymentType.Timavlonad => "Timavlönad",
        EmploymentType.Sasongsanstallning => "Säsongsanställning",
        _ => form.ToString()
    };

    private static string StatusText(KollAnstallningsStatus status) => status switch
    {
        KollAnstallningsStatus.Aktiv => "Aktiv",
        KollAnstallningsStatus.Kommande => "Kommande",
        KollAnstallningsStatus.Avslutad => "Avslutad",
        _ => status.ToString()
    };

    private static string Datum(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Sanera(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "OKAND" : new string(s.Where(char.IsLetterOrDigit).ToArray());

    private sealed record KollKatalogpost(KollAnstallningsPost Post, KollAnstallningsStatus Status);
}

/// <summary>Status för en anställning i katalogexporten relativt referensdatumet.</summary>
public enum KollAnstallningsStatus
{
    /// <summary>Anställningen är aktiv på referensdatumet.</summary>
    Aktiv,

    /// <summary>Anställningen börjar efter referensdatumet.</summary>
    Kommande,

    /// <summary>Anställningen är avslutad före referensdatumet.</summary>
    Avslutad
}

/// <summary>Indata till <see cref="KollExportGenerator"/>. Byggs upp från medarbetar- och anställningsregistret.</summary>
public sealed class KollExportInput
{
    public string Organisationsnummer { get; set; } = string.Empty;
    public string Organisationsnamn { get; set; } = string.Empty;

    /// <summary>Referensdatum för statusberäkning. Null → idag. Injiceras i test för determinism.</summary>
    public DateOnly? Referensdatum { get; set; }

    /// <summary>Genereringstidpunkt. Null → nu. Injiceras i test för determinism.</summary>
    public DateTime? Genererad { get; set; }

    public IReadOnlyList<KollAnstallningsPost> Poster { get; set; } = [];
}

/// <summary>En anställningsrad (masterdata) för katalogexporten.</summary>
public sealed class KollAnstallningsPost
{
    /// <summary>Personnummer i visningsform (YYYYMMDD-NNNN).</summary>
    public string PersonnummerFormaterat { get; set; } = string.Empty;
    public string Efternamn { get; set; } = string.Empty;
    public string Fornamn { get; set; } = string.Empty;

    /// <summary>Stabil identifierare för anställningen (t.ex. EmploymentId).</summary>
    public string AnstallningsId { get; set; } = string.Empty;

    public string EnhetNamn { get; set; } = string.Empty;
    public string? EnhetKostnadsstalle { get; set; }
    public string? Befattning { get; set; }
    public EmploymentType Anstallningsform { get; set; }
    public decimal Sysselsattningsgrad { get; set; }
    public DateOnly Startdatum { get; set; }
    public DateOnly? Slutdatum { get; set; }
    public string? HsaId { get; set; }
}

/// <summary>Resultatet av en KOLL-katalogexport: fil + metadata + varningar.</summary>
public sealed record KollExportResult(
    string Filnamn,
    string XmlInnehall,
    int AntalPoster,
    int AntalAktiva,
    string Overforingsstatus,
    string Sammanfattning,
    IReadOnlyList<string> Varningar);
