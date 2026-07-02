using System.Xml.Linq;
using Xunit;
using RegionHR.IntegrationHub.Adapters.Forsakringskassan;
using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.IntegrationHub.Tests;

public class FKAnmalanGeneratorTests
{
    private const string NS = "urn:openhr:forsakringskassan:sjukanmalan:1.0";

    private static string TestPnr() =>
        Personnummer.CreateValidated("198501011234").ToString(); // "YYYYMMDD-NNNN"

    private static FKAnmalanInput GiltigInput(DateOnly? start = null, DateOnly? slut = null) => new()
    {
        Personnummer = TestPnr(),
        Fornamn = "Anna",
        Efternamn = "Andersson",
        ArbetsgivareNamn = "Region Test",
        ArbetsgivareOrgNr = "232100-0198",
        SjukfranvaroStart = start ?? new DateOnly(2026, 1, 1),
        SjukfranvaroSlut = slut,
        Sjukskrivningsgrad = 100,
        Manadslon = 32000m,
        Sysselsattningsgrad = 100m,
        LakarintygFinns = true,
        LakarintygDatum = new DateOnly(2026, 1, 8),
        Idag = new DateOnly(2026, 1, 30) // 30 dagar in i sjukfallet → FK-pliktig
    };

    [Fact]
    public void Generera_GiltigInput_ArGiltig()
    {
        var result = new FKAnmalanGenerator().Generera(GiltigInput());

        Assert.True(result.Giltig);
        Assert.Empty(result.Fel);
        Assert.NotNull(result.Anmalan);
    }

    [Fact]
    public void Generera_SaknarPersonnummer_ArOgiltig()
    {
        var input = GiltigInput();
        input.Personnummer = "";

        var result = new FKAnmalanGenerator().Generera(input);

        Assert.False(result.Giltig);
        Assert.Contains(result.Fel, f => f.Contains("Personnummer"));
        Assert.Null(result.Anmalan);
        Assert.Equal(string.Empty, result.XmlInnehall);
    }

    [Fact]
    public void Generera_SaknarStartdatum_ArOgiltig()
    {
        var input = GiltigInput();
        input.SjukfranvaroStart = default;

        var result = new FKAnmalanGenerator().Generera(input);

        Assert.False(result.Giltig);
        Assert.Contains(result.Fel, f => f.Contains("dag 1"));
    }

    [Fact]
    public void Generera_Personnummer_NormaliserasTill12Siffror()
    {
        var result = new FKAnmalanGenerator().Generera(GiltigInput());

        Assert.NotNull(result.Anmalan);
        Assert.Equal(12, result.Anmalan!.Personnummer.Length);
        Assert.All(result.Anmalan.Personnummer, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void Generera_30Dagar_ArFKPliktig()
    {
        var result = new FKAnmalanGenerator().Generera(GiltigInput());

        Assert.True(result.Anmalan!.ArFKPliktig);
        Assert.Equal(30, result.Anmalan.AntalKalenderdagar);
    }

    [Fact]
    public void Generera_Under15Dagar_VarnarFortidaUtkast()
    {
        // 10 dagar in → ännu ej FK-pliktig
        var input = GiltigInput(start: new DateOnly(2026, 1, 1));
        input.Idag = new DateOnly(2026, 1, 10);

        var result = new FKAnmalanGenerator().Generera(input);

        Assert.True(result.Giltig); // genereras ändå som utkast
        Assert.False(result.Anmalan!.ArFKPliktig);
        Assert.Contains(result.Varningar, v => v.Contains("förtida"));
    }

    [Fact]
    public void Generera_LakarintygSaknasEfterDag8_Varnar()
    {
        var input = GiltigInput();
        input.LakarintygFinns = false;
        input.LakarintygDatum = null;

        var result = new FKAnmalanGenerator().Generera(input);

        Assert.Contains(result.Varningar, v => v.Contains("Läkarintyg saknas"));
    }

    [Fact]
    public void Generera_Sjukloneperiod_Ar14DagarFranDag1()
    {
        var start = new DateOnly(2026, 1, 1);
        var result = new FKAnmalanGenerator().Generera(GiltigInput(start: start));

        var a = result.Anmalan!;
        Assert.Equal(start, a.SjuklonePeriodFran);
        Assert.Equal(start.AddDays(13), a.SjuklonePeriodTill);          // dag 14
        Assert.Equal(start.AddDays(14), a.ForsakringskassanFranDatum);  // dag 15
        Assert.Equal(start.AddDays(7), a.LakarintygKravsFranDatum);     // dag 8
    }

    [Fact]
    public void Generera_SjukpenninggrundandeArsinkomst_ArManadslonGanger12()
    {
        var input = GiltigInput();
        input.Manadslon = 30000m;

        var result = new FKAnmalanGenerator().Generera(input);

        Assert.Equal(360000m, result.Anmalan!.SjukpenninggrundandeArsinkomst);
    }

    [Fact]
    public void Generera_AvslutatSjukfall_RaknarInklusivaDagar_OchEjPagaende()
    {
        var start = new DateOnly(2026, 1, 1);
        var slut = new DateOnly(2026, 1, 20); // 20 dagar inklusive
        var result = new FKAnmalanGenerator().Generera(GiltigInput(start: start, slut: slut));

        Assert.False(result.Anmalan!.Pagaende);
        Assert.Equal(20, result.Anmalan.AntalKalenderdagar);
    }

    [Fact]
    public void Generera_IckeStandardSjukskrivningsgrad_Varnar()
    {
        var input = GiltigInput();
        input.Sjukskrivningsgrad = 40;

        var result = new FKAnmalanGenerator().Generera(input);

        Assert.Contains(result.Varningar, v => v.Contains("standardnivå"));
    }

    [Fact]
    public void Generera_MarkerarOverforingsstatus_SomEjOverford()
    {
        var result = new FKAnmalanGenerator().Generera(GiltigInput());

        Assert.Equal("EJ_OVERFORD_KRAVER_FK_ANSLUTNING", result.Overforingsstatus);
        Assert.Contains("EJ_OVERFORD_KRAVER_FK_ANSLUTNING", result.XmlInnehall);
        Assert.Contains("Ej överförd", result.Sammanfattning);
    }

    [Fact]
    public void Generera_Xml_ArValidOchInnehallerKarnelement()
    {
        var result = new FKAnmalanGenerator().Generera(GiltigInput());

        var doc = XDocument.Parse(result.XmlInnehall); // kastar om ogiltig XML
        XNamespace ns = NS;

        Assert.Equal(ns + "Sjukanmalan", doc.Root!.Name);
        Assert.Equal("Sjukanmalan", doc.Root.Attribute("typ")?.Value); // FKAnmalanTyp.Sjukanmalan (default)

        Assert.NotNull(doc.Root.Element(ns + "Overforingsstatus"));
        Assert.NotNull(doc.Root.Element(ns + "Arbetsgivare"));
        Assert.NotNull(doc.Root.Element(ns + "Medarbetare"));
        Assert.NotNull(doc.Root.Element(ns + "Sjukfall"));
        Assert.NotNull(doc.Root.Element(ns + "Sjukloneperiod"));
        Assert.NotNull(doc.Root.Element(ns + "ForsakringskassansPeriod"));
        Assert.NotNull(doc.Root.Element(ns + "SjukersattningsgrundandeUppgifter"));

        var pnr = doc.Root.Element(ns + "Medarbetare")!.Element(ns + "Personnummer")!.Value;
        Assert.Equal(12, pnr.Length);
    }

    [Fact]
    public void Generera_Filnamn_FoljerKonvention()
    {
        var result = new FKAnmalanGenerator().Generera(GiltigInput(start: new DateOnly(2026, 1, 1)));

        Assert.StartsWith("FK-sjukanmalan_", result.Filnamn);
        Assert.EndsWith("_20260101.xml", result.Filnamn);
    }

    [Fact]
    public async Task Adapter_GenereraFKAnmalan_ReturnerarGeneratOchMarkerarEjOverford()
    {
        var adapter = new ForsakringskassanAdapter();
        var request = new IntegrationRequest("GenereraFKAnmalan", GiltigInput());

        var result = await adapter.ExecuteAsync(request);

        Assert.True(result.Success);
        Assert.Contains("Ej överförd", result.Message);
        var payload = Assert.IsType<FKAnmalanResult>(result.ResponseData);
        Assert.True(payload.Giltig);
    }

    [Fact]
    public async Task Adapter_GenereraFKAnmalan_OgiltigInput_ReturnerarFel()
    {
        var adapter = new ForsakringskassanAdapter();
        var input = GiltigInput();
        input.Personnummer = "";
        var request = new IntegrationRequest("GenereraFKAnmalan", input);

        var result = await adapter.ExecuteAsync(request);

        Assert.False(result.Success);
        Assert.Contains("kunde inte genereras", result.Message);
    }
}
