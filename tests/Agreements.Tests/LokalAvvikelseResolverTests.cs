using Xunit;
using RegionHR.Agreements.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Agreements.Tests;

public class LokalAvvikelseResolverTests
{
    private static readonly OrganizationId EnhetA = OrganizationId.New();
    private static readonly OrganizationId EnhetB = OrganizationId.New();
    private static readonly DateOnly Datum = new(2026, 6, 1);

    private static LokalAvtalsAvvikelse Ob(
        OrganizationId enhet,
        LokalBerakningsTyp berakning,
        LokalBeloppsEnhet enhetTyp,
        decimal varde,
        OBCategory? kategori = OBCategory.VardagKvall,
        DateOnly? fran = null,
        DateOnly? till = null,
        CollectiveAgreementId? avtalsId = null)
        => LokalAvtalsAvvikelse.Skapa(
            enhet, LokalAvvikelseTyp.ObPaslag, "OB",
            berakning, enhetTyp, varde,
            fran ?? new DateOnly(2026, 4, 1), till, kategori, avtalsId);

    [Fact]
    public void GallandeAvvikelser_FiltrerarPaEnhetOchDatum()
    {
        var alla = new[]
        {
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m),
            Ob(EnhetB, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 7m),
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 9m,
                fran: new DateOnly(2026, 7, 1)) // framtida → gäller inte 2026-06-01
        };

        var gallande = LokalAvvikelseResolver.GallandeAvvikelser(alla, EnhetA, Datum);

        Assert.Single(gallande);
        Assert.Equal(5m, gallande[0].Varde);
    }

    [Fact]
    public void GallandeAvvikelser_IgnorerarInaktiva()
    {
        var inaktiv = Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m);
        inaktiv.Inaktivera();
        var alla = new[] { inaktiv };

        Assert.Empty(LokalAvvikelseResolver.GallandeAvvikelser(alla, EnhetA, Datum));
    }

    [Fact]
    public void GallandeAvvikelser_AvtalslasningRespekteras()
    {
        var avtal = CollectiveAgreementId.New();
        var annatAvtal = CollectiveAgreementId.New();
        var alla = new[]
        {
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m, avtalsId: avtal),
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 8m, avtalsId: null)
        };

        // Fråga med annatAvtal → den avtalslåsta (avtal) exkluderas, den öppna (null) kvarstår.
        var gallande = LokalAvvikelseResolver.GallandeAvvikelser(alla, EnhetA, Datum, annatAvtal);
        Assert.Single(gallande);
        Assert.Equal(8m, gallande[0].Varde);

        // Fråga med matchande avtal → båda gäller.
        Assert.Equal(2, LokalAvvikelseResolver.GallandeAvvikelser(alla, EnhetA, Datum, avtal).Count);

        // Fråga utan avtalsfilter → båda gäller.
        Assert.Equal(2, LokalAvvikelseResolver.GallandeAvvikelser(alla, EnhetA, Datum).Count);
    }

    [Fact]
    public void FinnsAvvikelse_HittarRattTyp()
    {
        var alla = new[]
        {
            LokalAvtalsAvvikelse.Skapa(EnhetA, LokalAvvikelseTyp.Forman, "Friskvård",
                LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerManad, 500m, new DateOnly(2026, 1, 1))
        };

        Assert.True(LokalAvvikelseResolver.FinnsAvvikelse(alla, EnhetA, Datum, LokalAvvikelseTyp.Forman));
        Assert.False(LokalAvvikelseResolver.FinnsAvvikelse(alla, EnhetA, Datum, LokalAvvikelseTyp.ObPaslag));
    }

    [Fact]
    public void EffektivObSats_UtanAvvikelser_ReturnerarCentralSats()
    {
        var resultat = LokalAvvikelseResolver.EffektivObSats(
            26.40m, Array.Empty<LokalAvtalsAvvikelse>(), EnhetA, OBCategory.VardagKvall, Datum);

        Assert.Equal(26.40m, resultat);
    }

    [Fact]
    public void EffektivObSats_FastBelopp_LaggsTill()
    {
        var alla = new[] { Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m) };

        Assert.Equal(31.40m, LokalAvvikelseResolver.EffektivObSats(
            26.40m, alla, EnhetA, OBCategory.VardagKvall, Datum));
    }

    [Fact]
    public void EffektivObSats_Procent_RaknasUpp()
    {
        var alla = new[] { Ob(EnhetA, LokalBerakningsTyp.ProcentPaslag, LokalBeloppsEnhet.Procent, 10m) };

        Assert.Equal(110m, LokalAvvikelseResolver.EffektivObSats(
            100m, alla, EnhetA, OBCategory.VardagKvall, Datum));
    }

    [Fact]
    public void EffektivObSats_Ersatt_ByterUtBas()
    {
        var alla = new[] { Ob(EnhetA, LokalBerakningsTyp.ErsattVarde, LokalBeloppsEnhet.KronorPerTimme, 40m) };

        Assert.Equal(40m, LokalAvvikelseResolver.EffektivObSats(
            26.40m, alla, EnhetA, OBCategory.VardagKvall, Datum));
    }

    [Fact]
    public void EffektivObSats_Precedens_ErsattSedanProcentSedanFast()
    {
        // Ersätt 100 → +10% = 110 → +5 = 115. Ordningen ska vara deterministisk.
        var alla = new[]
        {
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m),
            Ob(EnhetA, LokalBerakningsTyp.ProcentPaslag, LokalBeloppsEnhet.Procent, 10m),
            Ob(EnhetA, LokalBerakningsTyp.ErsattVarde, LokalBeloppsEnhet.KronorPerTimme, 100m)
        };

        Assert.Equal(115m, LokalAvvikelseResolver.EffektivObSats(
            26.40m, alla, EnhetA, OBCategory.VardagKvall, Datum));
    }

    [Fact]
    public void EffektivObSats_KategoriNull_GallerAllaKategorier()
    {
        var alla = new[]
        {
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m, kategori: null)
        };

        // Påslaget saknar kategori → gäller även Helg.
        Assert.Equal(73.10m, LokalAvvikelseResolver.EffektivObSats(
            68.10m, alla, EnhetA, OBCategory.Helg, Datum));
    }

    [Fact]
    public void EffektivObSats_SpecifikKategori_PaverkarEjAnnanKategori()
    {
        var alla = new[]
        {
            Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m, kategori: OBCategory.VardagKvall)
        };

        // Påslaget gäller bara VardagKvall → Helg lämnas orörd.
        Assert.Equal(68.10m, LokalAvvikelseResolver.EffektivObSats(
            68.10m, alla, EnhetA, OBCategory.Helg, Datum));
    }

    [Fact]
    public void EffektivObSats_AnnanEnhet_PaverkasInte()
    {
        var alla = new[] { Ob(EnhetA, LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerTimme, 5m) };

        Assert.Equal(26.40m, LokalAvvikelseResolver.EffektivObSats(
            26.40m, alla, EnhetB, OBCategory.VardagKvall, Datum));
    }

    [Fact]
    public void EffektivObSats_IckeObAvvikelse_PaverkarInte()
    {
        var forman = LokalAvtalsAvvikelse.Skapa(EnhetA, LokalAvvikelseTyp.Forman, "Friskvård",
            LokalBerakningsTyp.FastBelopp, LokalBeloppsEnhet.KronorPerManad, 500m, new DateOnly(2026, 1, 1));
        var alla = new[] { forman };

        Assert.Equal(26.40m, LokalAvvikelseResolver.EffektivObSats(
            26.40m, alla, EnhetA, OBCategory.VardagKvall, Datum));
    }
}
