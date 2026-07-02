using System.Globalization;
using System.Xml.Linq;

namespace RegionHR.Infrastructure.Integrations;

/// <summary>
/// Enkel AGI-generator (Arbetsgivardeklaration på individnivå) enligt Skatteverkets
/// schemaversion 1.1. För fullständig batch-hantering (max 1000 individer/fil,
/// kontaktperson, förmåner, traktamenten) används
/// <see cref="RegionHR.IntegrationHub.Adapters.Skatteverket.AGIXmlGenerator"/>.
/// </summary>
public class AGIXmlGenerator
{
    private const string INSTANS_NS = "http://xmls.skatteverket.se/se/skatteverket/da/instans/schema/1.1";
    private const string KOMPONENT_NS = "http://xmls.skatteverket.se/se/skatteverket/da/komponent/schema/1.1";
    private const string XSI_NS = "http://www.w3.org/2001/XMLSchema-instance";

    public string GenerateArbetsgivardeklaration(AGIData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        XNamespace instans = INSTANS_NS;
        XNamespace agd = KOMPONENT_NS;

        var blanketter = new List<XElement>
        {
            // Huvuduppgift (HU) — summor för perioden.
            new(agd + "Blankett",
                new XElement(agd + "Arendeinformation",
                    new XElement(agd + "Arendeagare", data.OrgNr),
                    new XElement(agd + "Period", data.Period)),
                new XElement(agd + "Blankettinnehall",
                    new XElement(agd + "HU",
                        new XElement(agd + "ArbetsgivareHUGROUP",
                            Falt(agd, "AgRegistreradId", "201", data.OrgNr)),
                        Falt(agd, "RedovisningsPeriod", "006", data.Period),
                        Falt(agd, "SummaArbAvgSlf", "487", Belopp(data.Anstallda.Sum(a => a.Avgift))),
                        Falt(agd, "SummaSkatteavdr", "497", Belopp(data.Anstallda.Sum(a => a.Skatt))))))
        };

        // En individuppgift (IU) per betalningsmottagare.
        var specnr = 0;
        foreach (var a in data.Anstallda)
        {
            specnr++;
            blanketter.Add(new XElement(agd + "Blankett",
                new XElement(agd + "Arendeinformation",
                    new XElement(agd + "Arendeagare", data.OrgNr),
                    new XElement(agd + "Period", data.Period)),
                new XElement(agd + "Blankettinnehall",
                    new XElement(agd + "IU",
                        new XElement(agd + "ArbetsgivareIUGROUP",
                            Falt(agd, "AgRegistreradId", "201", data.OrgNr)),
                        new XElement(agd + "BetalningsmottagareIUGROUP",
                            new XElement(agd + "BetalningsmottagareIDChoice",
                                Falt(agd, "BetalningsmottagarId", "215", a.Personnummer))),
                        Falt(agd, "RedovisningsPeriod", "006", data.Period),
                        Falt(agd, "Specifikationsnummer", "570", specnr.ToString("D3", CultureInfo.InvariantCulture)),
                        Falt(agd, "KontantErsattningUlagAG", "011", Belopp(a.Brutto)),
                        Falt(agd, "AvdrPrelSkatt", "001", Belopp(a.Skatt))))));
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "no"),
            new XElement(instans + "Skatteverket",
                new XAttribute("omrade", "Arbetsgivardeklaration"),
                new XAttribute(XNamespace.Xmlns + "agd", KOMPONENT_NS),
                new XAttribute(XNamespace.Xmlns + "xsi", XSI_NS),
                new XElement(agd + "Avsandare",
                    new XElement(agd + "Programnamn", "OpenHR"),
                    new XElement(agd + "Organisationsnummer", data.OrgNr),
                    new XElement(agd + "Skapad",
                        DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))),
                new XElement(agd + "Blankettgemensamt",
                    new XElement(agd + "Arbetsgivare",
                        new XElement(agd + "AgRegistreradId", data.OrgNr))),
                blanketter));

        return doc.ToString();
    }

    private static XElement Falt(XNamespace agd, string element, string faltkod, string value) =>
        new(agd + element, new XAttribute("faltkod", faltkod), value);

    private static string Belopp(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
}

public record AGIData(string OrgNr, string Period, List<AGIAnstallData> Anstallda);
public record AGIAnstallData(string Personnummer, decimal Brutto, decimal Skatt, decimal Avgift);
