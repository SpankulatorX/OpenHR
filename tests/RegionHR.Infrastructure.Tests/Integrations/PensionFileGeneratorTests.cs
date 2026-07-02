using System.Xml.Linq;
using RegionHR.Infrastructure.Integrations;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Integrations;

/// <summary>
/// Tester för <see cref="PensionFileGenerator"/> — CSV- och XML-redovisning av
/// avgiftsbestämd tjänstepension (AKAP-KR).
/// </summary>
public class PensionFileGeneratorTests
{
    private static readonly XNamespace Ns = "urn:openhr:pension:akap-kr:1.0";

    private static PensionRedovisning Redovisning(params PensionRedovisningIndivid[] individer) =>
        new(
            ArbetsgivareNamn: "Region Örebro län",
            Organisationsnummer: "162321000016",
            Ar: 2026,
            Manad: 3,
            Inkomstbasbelopp: 83_400m,
            Individer: individer,
            Avtal: "AKAP-KR",
            Pensionsleverantor: "KPA (test)");

    private static PensionRedovisningIndivid Individ(
        string namn = "Anna Andersson", decimal lon = 62_125m,
        decimal under = 3_127.5m, decimal over = 3_150m, decimal total = 6_277.5m,
        string pnr = "198112289874", string? kst = null) =>
        new(pnr, namn, lon, under, over, total, kst);

    [Fact]
    public void Csv_InnehallerDisclaimerMetadataOchSummaRad()
    {
        var csv = new PensionFileGenerator().GenereraCsv(Redovisning(Individ()));

        Assert.Contains(PensionFileGenerator.Disclaimer, csv);
        Assert.Contains("# Arbetsgivare;Region Örebro län", csv);
        Assert.Contains("# Organisationsnummer;162321000016", csv);
        Assert.Contains("# Period;2026-03", csv);
        Assert.Contains("# Inkomstbasbelopp;83400.00", csv);
        Assert.Contains("Personnummer;Namn;Kostnadsstalle;PensionsgrundandeLon;Premie_6proc;Premie_31_5proc;TotalPremie", csv);
        // Individrad med belopp i F2-format.
        Assert.Contains("198112289874;Anna Andersson;;62125.00;3127.50;3150.00;6277.50", csv);
        // Summa-rad.
        Assert.Contains("SUMMA;1 individer;;62125.00;3127.50;3150.00;6277.50", csv);
    }

    [Fact]
    public void Csv_SummerarFleraIndivider()
    {
        var csv = new PensionFileGenerator().GenereraCsv(Redovisning(
            Individ(namn: "A", lon: 30_000m, under: 1_800m, over: 0m, total: 1_800m, pnr: "198112289874"),
            Individ(namn: "B", lon: 62_125m, under: 3_127.5m, over: 3_150m, total: 6_277.5m, pnr: "198503152383")));

        // 1 800 + 6 277,50 = 8 077,50 total premie; 30 000 + 62 125 = 92 125 lön.
        Assert.Contains("SUMMA;2 individer;;92125.00;4927.50;3150.00;8077.50", csv);
    }

    [Fact]
    public void Csv_CiterarFaltMedSeparatortecken()
    {
        var csv = new PensionFileGenerator().GenereraCsv(Redovisning(
            Individ(namn: "Ek; Berg AB")));

        Assert.Contains("\"Ek; Berg AB\"", csv);
    }

    [Fact]
    public void Xml_ArValidOchInnehallerSummering()
    {
        var xml = new PensionFileGenerator().GenereraXml(Redovisning(Individ(kst: "2011")));

        var doc = XDocument.Parse(xml);
        var root = doc.Root!;
        Assert.Equal(Ns + "Pensionsredovisning", root.Name);
        Assert.Equal("AKAP-KR", root.Attribute("avtal")!.Value);

        var individ = root.Element(Ns + "Individer")!.Element(Ns + "Individ")!;
        Assert.Equal("198112289874", individ.Element(Ns + "Personnummer")!.Value);
        Assert.Equal("2011", individ.Element(Ns + "Kostnadsstalle")!.Value);
        Assert.Equal("6277.50", individ.Element(Ns + "TotalPremie")!.Value);

        var summering = root.Element(Ns + "Summering")!;
        Assert.Equal("1", summering.Element(Ns + "AntalIndivider")!.Value);
        Assert.Equal("6277.50", summering.Element(Ns + "TotalPremie")!.Value);
    }

    [Fact]
    public void Xml_UtelamnarKostnadsstalleNarNull()
    {
        var xml = new PensionFileGenerator().GenereraXml(Redovisning(Individ(kst: null)));

        var doc = XDocument.Parse(xml);
        var individ = doc.Root!.Element(Ns + "Individer")!.Element(Ns + "Individ")!;
        Assert.Null(individ.Element(Ns + "Kostnadsstalle"));
    }

    [Theory]
    [InlineData(PensionFilFormat.Csv, "Pensionsredovisning_AKAP-KR_202603.csv", "text/csv")]
    [InlineData(PensionFilFormat.Xml, "Pensionsredovisning_AKAP-KR_202603.xml", "application/xml")]
    public void Generera_SatterFilnamnOchContentType(PensionFilFormat format, string filnamn, string contentType)
    {
        var fil = new PensionFileGenerator().Generera(Redovisning(Individ()), format);

        Assert.Equal(filnamn, fil.FileName);
        Assert.Equal(contentType, fil.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(fil.Content));
    }
}
