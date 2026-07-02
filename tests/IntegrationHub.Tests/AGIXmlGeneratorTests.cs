using Xunit;
using System.Xml.Linq;
using RegionHR.IntegrationHub.Adapters.Skatteverket;

namespace RegionHR.IntegrationHub.Tests;

/// <summary>
/// Verifierar att AGI-XML följer Skatteverkets officiella schema (SKV 269, version 1.1):
/// rätt namespace, fältkodsattribut, Specifikationsnummer per IU och HU-summering.
/// </summary>
public class AGIXmlGeneratorTests
{
    private static readonly XNamespace Instans =
        "http://xmls.skatteverket.se/se/skatteverket/da/instans/schema/1.1";
    private static readonly XNamespace Agd =
        "http://xmls.skatteverket.se/se/skatteverket/da/komponent/schema/1.1";

    private static AGIInput CreateSingleIndividInput() => new()
    {
        Organisationsnummer = "162321000016",
        Period = "202603",
        KontaktpersonNamn = "Anna Svensson",
        KontaktpersonTelefon = "010-1234567",
        KontaktpersonEpost = "anna.svensson@region.se",
        Individer =
        [
            new AGIIndivid
            {
                Personnummer = "198501011234",
                Namn = "Erik Johansson",
                KontantBruttolonMm = 35000m,
                AvdragenSkatt = 8750m,
                SkattepliktForman = 500m,
                Traktamente = 0m,
                Milersattning = 0m,
                Avgiftsunderlag = 35500m,
                Arbetsgivaravgifter = 11160m
            }
        ]
    };

    [Fact]
    public void Generate_SingleIndivid_ReturnsOneFile()
    {
        var files = new AGIXmlGenerator().Generate(CreateSingleIndividInput());

        Assert.Single(files);
        Assert.Equal("AGI_162321000016_202603_001.xml", files[0].FileName);
        Assert.NotEmpty(files[0].XmlContent);

        var doc = XDocument.Parse(files[0].XmlContent);
        var iu = doc.Descendants(Agd + "IU").ToList();
        Assert.Single(iu);
    }

    [Fact]
    public void Generate_HasOfficialRootAndNamespaces()
    {
        var files = new AGIXmlGenerator().Generate(CreateSingleIndividInput());
        var doc = XDocument.Parse(files[0].XmlContent);

        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal(Instans + "Skatteverket", root!.Name);
        Assert.Equal("Arbetsgivardeklaration", root.Attribute("omrade")?.Value);
        // agd-prefixet ska peka på komponentschemat.
        Assert.Equal(Agd.NamespaceName, root.GetNamespaceOfPrefix("agd")?.NamespaceName);
    }

    [Fact]
    public void Generate_IndividuppgiftUsesRealPersonnummerInFaltkod215()
    {
        var files = new AGIXmlGenerator().Generate(CreateSingleIndividInput());
        var doc = XDocument.Parse(files[0].XmlContent);

        var betId = doc.Descendants(Agd + "BetalningsmottagarId").Single();
        Assert.Equal("215", betId.Attribute("faltkod")?.Value);
        Assert.Equal("198501011234", betId.Value);
    }

    [Fact]
    public void Generate_IncomeAndTaxHaveCorrectFieldCodes()
    {
        var files = new AGIXmlGenerator().Generate(CreateSingleIndividInput());
        var doc = XDocument.Parse(files[0].XmlContent);
        var iu = doc.Descendants(Agd + "IU").Single();

        var kontant = iu.Element(Agd + "KontantErsattningUlagAG");
        Assert.NotNull(kontant);
        Assert.Equal("011", kontant!.Attribute("faltkod")?.Value);
        Assert.Equal("35000", kontant.Value);

        var skatt = iu.Element(Agd + "AvdrPrelSkatt");
        Assert.NotNull(skatt);
        Assert.Equal("001", skatt!.Attribute("faltkod")?.Value);
        Assert.Equal("8750", skatt.Value);

        var forman = iu.Element(Agd + "SkatteplFormanUlagAG");
        Assert.NotNull(forman);
        Assert.Equal("012", forman!.Attribute("faltkod")?.Value);
    }

    [Fact]
    public void Generate_EachIndividHasSpecifikationsnummer570()
    {
        var input = new AGIInput
        {
            Organisationsnummer = "162321000016",
            Period = "202603",
            KontaktpersonNamn = "Test",
            KontaktpersonTelefon = "010-0000000",
            KontaktpersonEpost = "test@test.se",
            Individer =
            [
                new AGIIndivid { Personnummer = "198501011234", KontantBruttolonMm = 30000m, AvdragenSkatt = 7500m },
                new AGIIndivid { Personnummer = "199002022389", KontantBruttolonMm = 32000m, AvdragenSkatt = 8000m }
            ]
        };

        var files = new AGIXmlGenerator().Generate(input);
        var doc = XDocument.Parse(files[0].XmlContent);

        var specnr = doc.Descendants(Agd + "Specifikationsnummer").ToList();
        Assert.Equal(2, specnr.Count);
        Assert.All(specnr, s => Assert.Equal("570", s.Attribute("faltkod")?.Value));
        // Unika specifikationsnummer per betalningsmottagare.
        Assert.Equal(specnr.Count, specnr.Select(s => s.Value).Distinct().Count());
        Assert.Equal("001", specnr[0].Value);
        Assert.Equal("002", specnr[1].Value);
    }

    [Fact]
    public void Generate_HuvuduppgiftSumsTaxAndEmployerContributions()
    {
        var input = new AGIInput
        {
            Organisationsnummer = "162321000016",
            Period = "202603",
            KontaktpersonNamn = "Test",
            KontaktpersonTelefon = "010-0000000",
            KontaktpersonEpost = "test@test.se",
            Individer =
            [
                new AGIIndivid { Personnummer = "198501011234", KontantBruttolonMm = 30000m, AvdragenSkatt = 7500m, Arbetsgivaravgifter = 9426m },
                new AGIIndivid { Personnummer = "199002022389", KontantBruttolonMm = 32000m, AvdragenSkatt = 8000m, Arbetsgivaravgifter = 10054m }
            ]
        };

        var files = new AGIXmlGenerator().Generate(input);
        var doc = XDocument.Parse(files[0].XmlContent);
        var hu = doc.Descendants(Agd + "HU").Single();

        var arbAvg = hu.Element(Agd + "SummaArbAvgSlf");
        Assert.NotNull(arbAvg);
        Assert.Equal("487", arbAvg!.Attribute("faltkod")?.Value);
        Assert.Equal("19480", arbAvg.Value);          // 9426 + 10054

        var skatteavdr = hu.Element(Agd + "SummaSkatteavdr");
        Assert.NotNull(skatteavdr);
        Assert.Equal("497", skatteavdr!.Attribute("faltkod")?.Value);
        Assert.Equal("15500", skatteavdr.Value);       // 7500 + 8000
    }

    [Fact]
    public void Generate_OptionalFieldsEmittedOnlyWhenPresent()
    {
        var input = new AGIInput
        {
            Organisationsnummer = "162321000016",
            Period = "202603",
            KontaktpersonNamn = "Test",
            KontaktpersonTelefon = "010-0000000",
            KontaktpersonEpost = "test@test.se",
            Individer =
            [
                new AGIIndivid
                {
                    Personnummer = "198501011234",
                    KontantBruttolonMm = 35000m,
                    AvdragenSkatt = 8750m,
                    Traktamente = 1500m,
                    Milersattning = 300m
                }
            ]
        };

        var doc = XDocument.Parse(new AGIXmlGenerator().Generate(input)[0].XmlContent);
        var iu = doc.Descendants(Agd + "IU").Single();

        var traktamente = iu.Element(Agd + "Traktamente");
        Assert.NotNull(traktamente);
        Assert.Equal("051", traktamente!.Attribute("faltkod")?.Value);

        var bilersattning = iu.Element(Agd + "Bilersattning");
        Assert.NotNull(bilersattning);
        Assert.Equal("050", bilersattning!.Attribute("faltkod")?.Value);

        // Ingen skattepliktig förmån angiven → fält 012 ska saknas.
        Assert.Null(iu.Element(Agd + "SkatteplFormanUlagAG"));
    }

    [Fact]
    public void Generate_Over1000Individer_SplitsIntoBatchesWithUniqueSpecnr()
    {
        var input = new AGIInput
        {
            Organisationsnummer = "162321000016",
            Period = "202603",
            KontaktpersonNamn = "Anna Svensson",
            KontaktpersonTelefon = "010-1234567",
            KontaktpersonEpost = "anna.svensson@region.se",
            Individer = Enumerable.Range(0, 1500).Select(i => new AGIIndivid
            {
                Personnummer = $"19850101{i:D4}",
                KontantBruttolonMm = 30000m,
                AvdragenSkatt = 7500m,
                Arbetsgivaravgifter = 9420m
            }).ToList()
        };

        var files = new AGIXmlGenerator().Generate(input);

        Assert.Equal(2, files.Count);
        Assert.Contains("_001.xml", files[0].FileName);
        Assert.Contains("_002.xml", files[1].FileName);

        var doc1 = XDocument.Parse(files[0].XmlContent);
        var doc2 = XDocument.Parse(files[1].XmlContent);
        Assert.Equal(1000, doc1.Descendants(Agd + "IU").Count());
        Assert.Equal(500, doc2.Descendants(Agd + "IU").Count());

        // Specifikationsnummer fortsätter räknas globalt (unika över hela deklarationen).
        var specnr1 = doc1.Descendants(Agd + "Specifikationsnummer").Select(s => s.Value);
        var specnr2 = doc2.Descendants(Agd + "Specifikationsnummer").Select(s => s.Value);
        Assert.Empty(specnr1.Intersect(specnr2));
        Assert.Contains("1000", specnr1);
        Assert.Contains("1001", specnr2);
    }

    [Fact]
    public void Generate_UsesInvariantAmountFormatting()
    {
        // Belopp med ören ska avrundas till hela kronor med punkt/utan tusenavgränsare.
        var input = new AGIInput
        {
            Organisationsnummer = "162321000016",
            Period = "202603",
            KontaktpersonNamn = "Test",
            KontaktpersonTelefon = "010-0000000",
            KontaktpersonEpost = "test@test.se",
            Individer = [new AGIIndivid { Personnummer = "198501011234", KontantBruttolonMm = 35250.75m, AvdragenSkatt = 8750m }]
        };

        var doc = XDocument.Parse(new AGIXmlGenerator().Generate(input)[0].XmlContent);
        var kontant = doc.Descendants(Agd + "KontantErsattningUlagAG").Single().Value;

        Assert.Equal("35251", kontant);
        Assert.DoesNotContain(",", kontant);
    }
}
