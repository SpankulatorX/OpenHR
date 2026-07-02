using System.Xml.Linq;
using RegionHR.IntegrationHub.Adapters.KOLL;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.IntegrationHub.Tests;

public class KollExportGeneratorTests
{
    private const string NS = "urn:openhr:koll:katalog:1.0";

    private static KollExportInput Input(params KollAnstallningsPost[] poster) => new()
    {
        Organisationsnummer = "232100-0164",
        Organisationsnamn = "Region Örebro län",
        Referensdatum = new DateOnly(2026, 7, 1),
        Genererad = new DateTime(2026, 7, 1, 8, 0, 0),
        Poster = poster
    };

    private static KollAnstallningsPost Post(
        DateOnly start, DateOnly? slut = null,
        EmploymentType form = EmploymentType.Tillsvidare,
        string? befattning = "Sjuksköterska",
        string? hsaId = "SE2321000164-ABC123") => new()
    {
        PersonnummerFormaterat = "19850101-1234",
        Efternamn = "Andersson",
        Fornamn = "Anna",
        AnstallningsId = "emp-1",
        EnhetNamn = "Akutmottagningen",
        EnhetKostnadsstalle = "4711",
        Befattning = befattning,
        Anstallningsform = form,
        Sysselsattningsgrad = 100m,
        Startdatum = start,
        Slutdatum = slut,
        HsaId = hsaId
    };

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    [Fact]
    public void Generera_StamplarOverforingsstatusOchAntal()
    {
        var result = new KollExportGenerator().Generera(
            Input(Post(new DateOnly(2020, 1, 1))));

        Assert.Equal(KollExportGenerator.OverforingStatus, result.Overforingsstatus);
        Assert.Equal(1, result.AntalPoster);

        XNamespace ns = NS;
        var doc = Parse(result.XmlInnehall);
        Assert.Equal(KollExportGenerator.OverforingStatus, doc.Root!.Element(ns + "Overforingsstatus")!.Value);
        Assert.Equal("1", doc.Root.Element(ns + "AntalPoster")!.Value);
        Assert.Single(doc.Root.Element(ns + "Anstallningar")!.Elements(ns + "Anstallning"));
    }

    [Fact]
    public void Generera_BeraknarStatusMotReferensdatum()
    {
        var referens = new DateOnly(2026, 7, 1);
        var result = new KollExportGenerator().Generera(new KollExportInput
        {
            Organisationsnummer = "232100-0164",
            Organisationsnamn = "Region Örebro län",
            Referensdatum = referens,
            Poster =
            [
                Post(new DateOnly(2020, 1, 1)),                                   // aktiv
                Post(new DateOnly(2026, 9, 1)),                                   // kommande
                Post(new DateOnly(2020, 1, 1), new DateOnly(2026, 1, 1)),         // avslutad
            ]
        });

        XNamespace ns = NS;
        var statusar = Parse(result.XmlInnehall).Root!
            .Element(ns + "Anstallningar")!
            .Elements(ns + "Anstallning")
            .Select(a => a.Element(ns + "Status")!.Value)
            .ToList();

        Assert.Equal(new[] { "Aktiv", "Kommande", "Avslutad" }, statusar);
        Assert.Equal(1, result.AntalAktiva);
    }

    [Fact]
    public void Generera_AnstallningsformOversattsTillSvenska()
    {
        var result = new KollExportGenerator().Generera(
            Input(Post(new DateOnly(2020, 1, 1), form: EmploymentType.SAVA)));

        XNamespace ns = NS;
        var form = Parse(result.XmlInnehall).Root!
            .Element(ns + "Anstallningar")!.Element(ns + "Anstallning")!
            .Element(ns + "Anstallningsform")!;

        Assert.Equal("SAVA", form.Attribute("kod")!.Value);
        Assert.Equal("Allmän visstidsanställning", form.Value);
    }

    [Fact]
    public void Generera_SysselsattningsgradInvariantFormat()
    {
        var post = Post(new DateOnly(2020, 1, 1));
        post.Sysselsattningsgrad = 75.5m;
        var result = new KollExportGenerator().Generera(Input(post));

        XNamespace ns = NS;
        var grad = Parse(result.XmlInnehall).Root!
            .Element(ns + "Anstallningar")!.Element(ns + "Anstallning")!
            .Element(ns + "Sysselsattningsgrad")!.Value;

        Assert.Equal("75.50", grad); // punkt som decimaltecken, oberoende av kultur
    }

    [Fact]
    public void Generera_UtelamnarSlutdatumOchHsaIdNarSaknas()
    {
        var result = new KollExportGenerator().Generera(
            Input(Post(new DateOnly(2020, 1, 1), slut: null, hsaId: null)));

        XNamespace ns = NS;
        var anst = Parse(result.XmlInnehall).Root!
            .Element(ns + "Anstallningar")!.Element(ns + "Anstallning")!;

        Assert.Null(anst.Element(ns + "Slutdatum"));
        Assert.Null(anst.Element(ns + "HsaId"));
        Assert.Equal("19850101-1234", anst.Element(ns + "Personnummer")!.Value);
    }

    [Fact]
    public void Generera_SaknadBefattning_GerVarning()
    {
        var result = new KollExportGenerator().Generera(
            Input(Post(new DateOnly(2020, 1, 1), befattning: null)));

        Assert.Contains(result.Varningar, v => v.Contains("befattning", System.StringComparison.OrdinalIgnoreCase));

        XNamespace ns = NS;
        var anst = Parse(result.XmlInnehall).Root!
            .Element(ns + "Anstallningar")!.Element(ns + "Anstallning")!;
        Assert.Null(anst.Element(ns + "Befattning"));
    }

    [Fact]
    public void Generera_TomKatalog_GerFilUtanPoster()
    {
        var result = new KollExportGenerator().Generera(Input());

        Assert.Equal(0, result.AntalPoster);
        XNamespace ns = NS;
        var doc = Parse(result.XmlInnehall);
        Assert.Empty(doc.Root!.Element(ns + "Anstallningar")!.Elements());
        Assert.Equal("0", doc.Root.Element(ns + "AntalPoster")!.Value);
    }
}
