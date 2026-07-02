using Xunit;
using RegionHR.Agreements.Domain;
using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Agreements.Tests;

public class LokalAvtalsAvvikelseTests
{
    private static readonly OrganizationId Enhet = OrganizationId.New();
    private static readonly DateOnly Fran = new(2026, 4, 1);

    private static LokalAvtalsAvvikelse ObPaslag(
        LokalBerakningsTyp berakning,
        LokalBeloppsEnhet enhet,
        decimal varde,
        OBCategory? kategori = OBCategory.VardagKvall,
        OrganizationId? enhetId = null,
        DateOnly? fran = null,
        DateOnly? till = null,
        CollectiveAgreementId? avtalsId = null)
        => LokalAvtalsAvvikelse.Skapa(
            enhetId ?? Enhet,
            LokalAvvikelseTyp.ObPaslag,
            "OB-påslag test",
            berakning,
            enhet,
            varde,
            fran ?? Fran,
            till,
            kategori,
            avtalsId);

    [Fact]
    public void Skapa_SkaparAvvikelse_MedKorrektaFalt()
    {
        var a = ObPaslag(LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m);

        Assert.Equal(Enhet, a.EnhetId);
        Assert.Equal(LokalAvvikelseTyp.ObPaslag, a.Typ);
        Assert.Equal(OBCategory.VardagKvall, a.ObKategori);
        Assert.Equal(5m, a.Varde);
        Assert.True(a.Aktiv);
        Assert.NotEqual(Guid.Empty, a.Id);
    }

    [Fact]
    public void Skapa_TomtNamn_KastarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => LokalAvtalsAvvikelse.Skapa(
            Enhet, LokalAvvikelseTyp.Tillagg, "   ", LokalBerakningsTyp.FastBelopp,
            LokalBeloppsEnhet.KronorPerManad, 1000m, Fran));
        Assert.Equal("LOKAL_AVVIKELSE_NAMN_SAKNAS", ex.ErrorCode);
    }

    [Fact]
    public void Skapa_SlutForeStart_KastarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => LokalAvtalsAvvikelse.Skapa(
            Enhet, LokalAvvikelseTyp.Tillagg, "Test", LokalBerakningsTyp.FastBelopp,
            LokalBeloppsEnhet.Kronor, 100m, new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)));
        Assert.Equal("LOKAL_AVVIKELSE_OGILTIG_PERIOD", ex.ErrorCode);
    }

    [Fact]
    public void Skapa_ProcentUtanProcentenhet_KastarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => LokalAvtalsAvvikelse.Skapa(
            Enhet, LokalAvvikelseTyp.ObPaslag, "Test", LokalBerakningsTyp.ProcentPaslag,
            LokalBeloppsEnhet.KronorPerTimme, 10m, Fran));
        Assert.Equal("LOKAL_AVVIKELSE_PROCENT_KRAVER_PROCENTENHET", ex.ErrorCode);
    }

    [Fact]
    public void Skapa_ProcentenhetUtanProcentpaslag_KastarDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => LokalAvtalsAvvikelse.Skapa(
            Enhet, LokalAvvikelseTyp.ObPaslag, "Test", LokalBerakningsTyp.FastBelopp,
            LokalBeloppsEnhet.Procent, 10m, Fran));
        Assert.Equal("LOKAL_AVVIKELSE_PROCENTENHET_KRAVER_PROCENT", ex.ErrorCode);
    }

    [Fact]
    public void Skapa_ObKategori_NollstallsForIckeObTyp()
    {
        var a = LokalAvtalsAvvikelse.Skapa(
            Enhet, LokalAvvikelseTyp.Tillagg, "Storstadstillägg", LokalBerakningsTyp.FastBelopp,
            LokalBeloppsEnhet.KronorPerManad, 1500m, Fran, obKategori: OBCategory.Helg);

        Assert.Null(a.ObKategori);
    }

    [Theory]
    [InlineData(LokalBerakningsTyp.FastBelopp, 5, 26.40, 31.40)]
    [InlineData(LokalBerakningsTyp.ErsattVarde, 40, 26.40, 40)]
    public void TillampaPa_FastOchErsatt(LokalBerakningsTyp typ, double varde, double bas, double forvantat)
    {
        var enhet = typ == LokalBerakningsTyp.ProcentPaslag ? LokalBeloppsEnhet.Procent : LokalBeloppsEnhet.KronorPerTimme;
        var a = ObPaslag(typ, enhet, (decimal)varde);
        Assert.Equal((decimal)forvantat, a.TillampaPa((decimal)bas));
    }

    [Fact]
    public void TillampaPa_Procent_GerUppraknatVarde()
    {
        var a = ObPaslag(LokalBerakningsTyp.ProcentPaslag, LokalBeloppsEnhet.Procent, 10m);
        Assert.Equal(110m, a.TillampaPa(100m));
    }

    [Fact]
    public void GallerVid_RespekterarFonsterOchAktiv()
    {
        var a = ObPaslag(LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m,
            fran: new DateOnly(2026, 4, 1), till: new DateOnly(2026, 12, 31));

        Assert.False(a.GallerVid(new DateOnly(2026, 3, 31)));
        Assert.True(a.GallerVid(new DateOnly(2026, 4, 1)));
        Assert.True(a.GallerVid(new DateOnly(2026, 12, 31)));
        Assert.False(a.GallerVid(new DateOnly(2027, 1, 1)));

        a.Inaktivera();
        Assert.False(a.GallerVid(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void GallerForEnhet_MatcharEndastRattEnhet()
    {
        var annan = OrganizationId.New();
        var a = ObPaslag(LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m);

        Assert.True(a.GallerForEnhet(Enhet, new DateOnly(2026, 6, 1)));
        Assert.False(a.GallerForEnhet(annan, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Uppdatera_AndrarFaltOchStamplarUpdatedAt()
    {
        var a = ObPaslag(LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m);

        var nyEnhet = OrganizationId.New();
        a.Uppdatera(nyEnhet, LokalAvvikelseTyp.Tillagg, "Nytt namn", LokalBerakningsTyp.FastBelopp,
            LokalBeloppsEnhet.KronorPerManad, 2000m, Fran, null, null, null, "beskrivning");

        Assert.Equal(nyEnhet, a.EnhetId);
        Assert.Equal("Nytt namn", a.Namn);
        Assert.Equal(LokalAvvikelseTyp.Tillagg, a.Typ);
        Assert.Equal(2000m, a.Varde);
        Assert.Null(a.ObKategori);
        Assert.NotNull(a.UpdatedAt);
    }
}
