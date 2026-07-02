using RegionHR.IntegrationHub.Adapters.Skatteverket;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.IntegrationHub.Tests;

public class FolkbokforingImporterTests
{
    private static string Pnr(string yyyymmddnnn) =>
        Personnummer.CreateValidated(yyyymmddnnn).ToString(); // "YYYYMMDD-NNNN"

    private static string Pnr12(string yyyymmddnnn) =>
        Personnummer.CreateValidated(yyyymmddnnn); // implicit -> 12 digits

    [Fact]
    public void Parsa_NamnOchAdressandring_TolkasKorrekt()
    {
        var pnr = Pnr("198501011234");
        var content =
            $"""
             # Folkbokföringsavisering (Navet) — demo
             #PERSON {pnr}
             EFTERNAMN=Nyström
             FORNAMN=Anna
             MELLANNAMN=Maria
             GATUADRESS=Storgatan 1
             POSTNUMMER=702 10
             POSTORT=Örebro
             LAND=Sverige
             SEKRETESS=INGEN
             """;

        var result = new FolkbokforingImporter().Parsa(content);

        Assert.True(result.Giltig);
        Assert.Empty(result.Fel);
        var a = Assert.Single(result.Aviseringar);
        Assert.True(a.HarNamnAndring);
        Assert.Equal("Nyström", a.Efternamn);
        Assert.Equal("Anna", a.Fornamn);
        Assert.Equal("Maria", a.MellanNamn);
        Assert.True(a.HarAdressAndring);
        Assert.Equal("Storgatan 1", a.Gatuadress);
        Assert.Equal("70210", a.Postnummer); // mellanslag normaliserat bort
        Assert.Equal("Örebro", a.Postort);
        Assert.Equal("Sverige", a.Land);
        Assert.False(a.ArSkyddad);
        Assert.False(a.ArAvliden);
        Assert.Equal(Pnr12("198501011234"), a.Personnummer12);
    }

    [Fact]
    public void Parsa_SkyddadFolkbokforing_MaskarAdressOchFlaggar()
    {
        var pnr = Pnr("199012312392");
        var content =
            $"""
             #PERSON {pnr}
             GATUADRESS=Hemlig väg 3
             POSTNUMMER=11122
             POSTORT=Stockholm
             SEKRETESS=SKYDDAD_FOLKBOKFORING
             """;

        var result = new FolkbokforingImporter().Parsa(content);

        var a = Assert.Single(result.Aviseringar);
        Assert.True(a.ArSkyddad);
        Assert.Equal(Sekretessmarkering.SkyddadFolkbokforing, a.Sekretess);
        Assert.False(a.HarAdressAndring);
        Assert.Null(a.Gatuadress);
        Assert.Null(a.Postnummer);
        Assert.Null(a.Postort);
        Assert.Contains(result.Varningar, v => v.Contains("skyddad identitet", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parsa_Sekretessmarkering_Tolkas()
    {
        var pnr = Pnr("197003032380");
        var content =
            $"""
             #PERSON {pnr}
             SEKRETESS=SEKRETESSMARKERING
             """;

        var a = Assert.Single(new FolkbokforingImporter().Parsa(content).Aviseringar);
        Assert.True(a.ArSkyddad);
        Assert.Equal(Sekretessmarkering.Sekretessmarkering, a.Sekretess);
    }

    [Fact]
    public void Parsa_Avliden_SattsMedDatum()
    {
        var pnr = Pnr("194005052388");
        var content =
            $"""
             #PERSON {pnr}
             AVLIDEN=2026-03-15
             """;

        var a = Assert.Single(new FolkbokforingImporter().Parsa(content).Aviseringar);
        Assert.True(a.ArAvliden);
        Assert.Equal(new DateOnly(2026, 3, 15), a.AvlidenDatum);
    }

    [Fact]
    public void Parsa_OgiltigtPersonnummer_GerFelOchHopparOverBlock()
    {
        var content =
            """
            #PERSON 123456789012
            EFTERNAMN=Testsson
            """;

        var result = new FolkbokforingImporter().Parsa(content);

        Assert.Empty(result.Aviseringar);
        Assert.Contains(result.Fel, f => f.Contains("ogiltigt personnummer", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parsa_OfullstandigAdress_GerVarningOchIngenAdressandring()
    {
        var pnr = Pnr("198501011234");
        var content =
            $"""
             #PERSON {pnr}
             GATUADRESS=Enbart gata 5
             """;

        var result = new FolkbokforingImporter().Parsa(content);

        var a = Assert.Single(result.Aviseringar);
        Assert.False(a.HarAdressAndring);
        Assert.Null(a.Gatuadress);
        Assert.Contains(result.Varningar, v => v.Contains("ofullständig adress", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parsa_FleraPersoner_TolkasIOrdning()
    {
        var p1 = Pnr("198501011234");
        var p2 = Pnr("199012312392");
        var content =
            $"""
             #PERSON {p1}
             FORNAMN=Ett

             # kommentar mellan blocken
             #PERSON {p2}
             FORNAMN=Två
             """;

        var result = new FolkbokforingImporter().Parsa(content);

        Assert.Equal(2, result.AntalPoster);
        Assert.Equal("Ett", result.Aviseringar[0].Fornamn);
        Assert.Equal("Två", result.Aviseringar[1].Fornamn);
    }

    [Fact]
    public void Parsa_TomFil_GerVarningIngenPost()
    {
        var result = new FolkbokforingImporter().Parsa("   ");

        Assert.True(result.Giltig);
        Assert.Empty(result.Aviseringar);
        Assert.NotEmpty(result.Varningar);
    }

    [Fact]
    public void Parsa_FaltUtanforBlock_GerFel()
    {
        var content = "EFTERNAMN=Utanför";

        var result = new FolkbokforingImporter().Parsa(content);

        Assert.Empty(result.Aviseringar);
        Assert.Contains(result.Fel, f => f.Contains("utanför", System.StringComparison.OrdinalIgnoreCase));
    }
}
