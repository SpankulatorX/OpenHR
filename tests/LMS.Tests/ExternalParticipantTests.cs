using RegionHR.LMS.Domain;
using Xunit;

namespace RegionHR.LMS.Tests;

public class ExternalParticipantTests
{
    [Fact]
    public void Bjudin_SatterInbjudenStatusOchToken()
    {
        var p = ExternalParticipant.Bjudin("konsult@extern.se", "Anna Andersson", "Konsult AB");

        Assert.Equal("konsult@extern.se", p.Epost);
        Assert.Equal("Anna Andersson", p.Namn);
        Assert.Equal("Konsult AB", p.Organisation);
        Assert.Equal(ExternalParticipantStatus.Inbjuden, p.Status);
        Assert.False(string.IsNullOrWhiteSpace(p.AccessToken));
        Assert.True(p.HarAktivAccess);
        Assert.Null(p.SenastAktivVid);
        Assert.NotEqual(Guid.Empty, p.Id);
    }

    [Fact]
    public void Bjudin_NormaliserarEpost()
    {
        var p = ExternalParticipant.Bjudin("  Konsult@Extern.SE  ", "Namn");

        Assert.Equal("konsult@extern.se", p.Epost);
    }

    [Fact]
    public void Bjudin_UtanOrganisation_ArNull()
    {
        var p = ExternalParticipant.Bjudin("a@b.se", "Namn", "   ");

        Assert.Null(p.Organisation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("utan-snabel-a.se")]
    [InlineData("@domän.se")]
    [InlineData("namn@")]
    [InlineData("namn@@dubbel.se")]
    [InlineData("namn@utandot")]
    public void Bjudin_OgiltigEpost_KastarUndantag(string epost)
    {
        Assert.Throws<ArgumentException>(() => ExternalParticipant.Bjudin(epost, "Namn"));
    }

    [Fact]
    public void Bjudin_TomtNamn_KastarUndantag()
    {
        Assert.Throws<ArgumentException>(() => ExternalParticipant.Bjudin("a@b.se", "  "));
    }

    [Fact]
    public void RegistreraAktivitet_UppgraderarInbjudenTillAktiv()
    {
        var p = ExternalParticipant.Bjudin("a@b.se", "Namn");

        p.RegistreraAktivitet();

        Assert.Equal(ExternalParticipantStatus.Aktiv, p.Status);
        Assert.NotNull(p.SenastAktivVid);
    }

    [Fact]
    public void Inaktivera_TarBortAccess()
    {
        var p = ExternalParticipant.Bjudin("a@b.se", "Namn");

        p.Inaktivera();

        Assert.Equal(ExternalParticipantStatus.Inaktiverad, p.Status);
        Assert.False(p.HarAktivAccess);
    }

    [Fact]
    public void RegistreraAktivitet_PaInaktiverad_UppgraderarInte()
    {
        var p = ExternalParticipant.Bjudin("a@b.se", "Namn");
        p.Inaktivera();

        p.RegistreraAktivitet();

        Assert.Equal(ExternalParticipantStatus.Inaktiverad, p.Status);
    }

    [Fact]
    public void Ateraktivera_FranInaktiverad_BlirInbjuden()
    {
        var p = ExternalParticipant.Bjudin("a@b.se", "Namn");
        p.Inaktivera();

        p.Ateraktivera();

        Assert.Equal(ExternalParticipantStatus.Inbjuden, p.Status);
        Assert.True(p.HarAktivAccess);
    }

    [Fact]
    public void NyttAccessToken_ByterToken()
    {
        var p = ExternalParticipant.Bjudin("a@b.se", "Namn");
        var gammalt = p.AccessToken;

        p.NyttAccessToken();

        Assert.NotEqual(gammalt, p.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(p.AccessToken));
    }

    [Fact]
    public void AccessToken_ArUnikaMellanDeltagare()
    {
        var a = ExternalParticipant.Bjudin("a@b.se", "A");
        var b = ExternalParticipant.Bjudin("c@d.se", "B");

        Assert.NotEqual(a.AccessToken, b.AccessToken);
    }
}
