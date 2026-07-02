using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace RegionHR.Infrastructure.Integrations;

/// <summary>
/// Genererar en <b>pensionsredovisning</b> för avgiftsbestämd tjänstepension (AKAP-KR)
/// per period och arbetsgivare, i två maskin- och Excel-läsbara format (CSV och XML).
///
/// <para><b>Format-anmärkning:</b> KPA:s och Valcentralen/Pensionsvalets skarpa filformat
/// (samt anslutning till respektive inrapporteringstjänst) kräver <i>tecknat avtal</i> och
/// leverantörens egen formatspecifikation. Denna generator producerar därför en tydligt
/// märkt <b>standardredovisning</b> (OpenHR) med korrekt AKAP-KR-beräknade belopp — redo att
/// mappas mot en leverantörs skarpa format när avtal finns. Se <see cref="Disclaimer"/>.</para>
///
/// Motorn tar emot rena värdetyper (inga domäntyper) så att den kan användas fristående och
/// utan koppling till lönemodulen. Beloppen förväntas redan vara beräknade av
/// <c>RegionHR.Payroll.Domain.PensionsberakningsEngine</c>.
/// </summary>
public sealed class PensionFileGenerator
{
    private const string XmlNs = "urn:openhr:pension:akap-kr:1.0";

    /// <summary>
    /// Klartext-markering som skrivs in i varje genererad fil för att undvika att en
    /// standardredovisning felaktigt skickas som skarp leverantörsfil.
    /// </summary>
    public const string Disclaimer =
        "STANDARDREDOVISNING (OpenHR). Detta är INTE KPA:s eller Valcentralens proprietära " +
        "filformat. Skarp inlämning till pensionsleverantör kräver tecknat avtal och " +
        "leverantörens formatspecifikation. Beloppen är beräknade enligt AKAP-KR.";

    /// <summary>
    /// Generera redovisningsfilen i valt format och returnera innehåll + föreslaget filnamn
    /// och MIME-typ (klart för nedladdning).
    /// </summary>
    public PensionFil Generera(PensionRedovisning redovisning, PensionFilFormat format)
    {
        ArgumentNullException.ThrowIfNull(redovisning);
        var period = $"{redovisning.Ar:D4}{redovisning.Manad:D2}";
        return format switch
        {
            PensionFilFormat.Xml => new PensionFil(
                $"Pensionsredovisning_AKAP-KR_{period}.xml", GenereraXml(redovisning), "application/xml"),
            _ => new PensionFil(
                $"Pensionsredovisning_AKAP-KR_{period}.csv", GenereraCsv(redovisning), "text/csv"),
        };
    }

    /// <summary>
    /// Generera redovisningen som semikolon-separerad CSV med en metadata-header (rader som
    /// inleds med <c>#</c>), en kolumnrubrik, en rad per individ och en avslutande SUMMA-rad.
    /// </summary>
    public string GenereraCsv(PensionRedovisning r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var totalLon = r.Individer.Sum(i => i.PensionsgrundandeLon);
        var totalUnder = r.Individer.Sum(i => i.PremieUnderGrans);
        var totalOver = r.Individer.Sum(i => i.PremieOverGrans);
        var totalPremie = r.Individer.Sum(i => i.TotalPremie);

        var sb = new StringBuilder();
        sb.AppendLine("# OpenHR Pensionsredovisning — avgiftsbestämd tjänstepension");
        sb.AppendLine($"# {Disclaimer}");
        sb.AppendLine($"# Arbetsgivare;{Csv(r.ArbetsgivareNamn)}");
        sb.AppendLine($"# Organisationsnummer;{Csv(r.Organisationsnummer)}");
        sb.AppendLine($"# Period;{r.Ar:D4}-{r.Manad:D2}");
        sb.AppendLine($"# Avtal;{Csv(r.Avtal)}");
        sb.AppendLine($"# Pensionsleverantor;{Csv(r.Pensionsleverantor)}");
        sb.AppendLine($"# Inkomstbasbelopp;{Belopp(r.Inkomstbasbelopp)}");
        sb.AppendLine($"# AntalIndivider;{r.Individer.Count.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"# Genererad_UTC;{DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}");
        sb.AppendLine("Personnummer;Namn;Kostnadsstalle;PensionsgrundandeLon;Premie_6proc;Premie_31_5proc;TotalPremie");

        foreach (var i in r.Individer)
        {
            sb.AppendLine(string.Join(';',
                Csv(i.Personnummer),
                Csv(i.Namn),
                Csv(i.Kostnadsstalle ?? string.Empty),
                Belopp(i.PensionsgrundandeLon),
                Belopp(i.PremieUnderGrans),
                Belopp(i.PremieOverGrans),
                Belopp(i.TotalPremie)));
        }

        sb.AppendLine(string.Join(';',
            "SUMMA",
            $"{r.Individer.Count.ToString(CultureInfo.InvariantCulture)} individer",
            string.Empty,
            Belopp(totalLon),
            Belopp(totalUnder),
            Belopp(totalOver),
            Belopp(totalPremie)));

        return sb.ToString();
    }

    /// <summary>
    /// Generera redovisningen som XML i OpenHR:s egen namnrymd
    /// (<c>urn:openhr:pension:akap-kr:1.0</c>). Innehåller arbetsgivare, period, en post
    /// per individ samt en summering.
    /// </summary>
    public string GenereraXml(PensionRedovisning r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var ns = XNamespace.Get(XmlNs);
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Pensionsredovisning",
                new XAttribute("avtal", r.Avtal),
                new XComment($" {Disclaimer} "),
                new XElement(ns + "Arbetsgivare",
                    new XElement(ns + "Namn", r.ArbetsgivareNamn),
                    new XElement(ns + "Organisationsnummer", r.Organisationsnummer)),
                new XElement(ns + "Period",
                    new XElement(ns + "Ar", r.Ar.ToString(CultureInfo.InvariantCulture)),
                    new XElement(ns + "Manad", r.Manad.ToString(CultureInfo.InvariantCulture))),
                new XElement(ns + "Pensionsleverantor", r.Pensionsleverantor),
                new XElement(ns + "Inkomstbasbelopp", Belopp(r.Inkomstbasbelopp)),
                new XElement(ns + "Individer",
                    r.Individer.Select(i =>
                        new XElement(ns + "Individ",
                            new XElement(ns + "Personnummer", i.Personnummer),
                            new XElement(ns + "Namn", i.Namn),
                            i.Kostnadsstalle is null
                                ? null
                                : new XElement(ns + "Kostnadsstalle", i.Kostnadsstalle),
                            new XElement(ns + "PensionsgrundandeLon", Belopp(i.PensionsgrundandeLon)),
                            new XElement(ns + "PremieUnderGrans", Belopp(i.PremieUnderGrans)),
                            new XElement(ns + "PremieOverGrans", Belopp(i.PremieOverGrans)),
                            new XElement(ns + "TotalPremie", Belopp(i.TotalPremie))))),
                new XElement(ns + "Summering",
                    new XElement(ns + "AntalIndivider",
                        r.Individer.Count.ToString(CultureInfo.InvariantCulture)),
                    new XElement(ns + "TotalPensionsgrundandeLon",
                        Belopp(r.Individer.Sum(i => i.PensionsgrundandeLon))),
                    new XElement(ns + "TotalPremie",
                        Belopp(r.Individer.Sum(i => i.TotalPremie))))));

        return doc.ToString();
    }

    private static string Belopp(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

/// <summary>Filformat för pensionsredovisning.</summary>
public enum PensionFilFormat
{
    /// <summary>Semikolon-separerad CSV (Excel-vänlig).</summary>
    Csv,

    /// <summary>XML i OpenHR:s namnrymd.</summary>
    Xml
}

/// <summary>En rad i pensionsredovisningen: en anställds AKAP-KR premie för perioden.</summary>
/// <param name="Personnummer">Anställdes personnummer (12 siffror).</param>
/// <param name="Namn">Anställdes fullständiga namn.</param>
/// <param name="PensionsgrundandeLon">Pensionsgrundande lön för perioden.</param>
/// <param name="PremieUnderGrans">Premie på delen under 7,5 IBB (6,0 %).</param>
/// <param name="PremieOverGrans">Premie på delen över 7,5 IBB (31,5 %).</param>
/// <param name="TotalPremie">Total premie för perioden.</param>
/// <param name="Kostnadsstalle">Valfritt kostnadsställe för konteringen.</param>
public sealed record PensionRedovisningIndivid(
    string Personnummer,
    string Namn,
    decimal PensionsgrundandeLon,
    decimal PremieUnderGrans,
    decimal PremieOverGrans,
    decimal TotalPremie,
    string? Kostnadsstalle = null);

/// <summary>En komplett pensionsredovisning för en arbetsgivare och period.</summary>
/// <param name="ArbetsgivareNamn">Arbetsgivarens namn.</param>
/// <param name="Organisationsnummer">Arbetsgivarens organisationsnummer.</param>
/// <param name="Ar">Redovisningsår.</param>
/// <param name="Manad">Redovisningsmånad (1–12).</param>
/// <param name="Inkomstbasbelopp">Använt inkomstbasbelopp för perioden.</param>
/// <param name="Individer">Individrader.</param>
/// <param name="Avtal">Pensionsavtal (standard: AKAP-KR).</param>
/// <param name="Pensionsleverantor">Vald pensionsleverantör (fritext; skarp inlämning kräver avtal).</param>
public sealed record PensionRedovisning(
    string ArbetsgivareNamn,
    string Organisationsnummer,
    int Ar,
    int Manad,
    decimal Inkomstbasbelopp,
    IReadOnlyList<PensionRedovisningIndivid> Individer,
    string Avtal = "AKAP-KR",
    string Pensionsleverantor = "Ej vald (kräver avtal)");

/// <summary>En genererad fil: föreslaget filnamn, innehåll och MIME-typ.</summary>
/// <param name="FileName">Föreslaget filnamn.</param>
/// <param name="Content">Filinnehåll (text).</param>
/// <param name="ContentType">MIME-typ.</param>
public sealed record PensionFil(string FileName, string Content, string ContentType);
