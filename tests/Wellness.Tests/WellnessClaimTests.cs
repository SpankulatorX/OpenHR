using RegionHR.Wellness.Domain;
using Xunit;

namespace RegionHR.Wellness.Tests;

public class WellnessClaimTests
{
    private readonly Guid _anstallId = Guid.NewGuid();
    private readonly DateOnly _datum = new(2026, 6, 15);

    [Fact]
    public void Skapa_SatterStatusInskickad()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Gymkort", 2000m, _datum);

        Assert.Equal(WellnessClaimStatus.Inskickad, claim.Status);
        Assert.Equal(2000m, claim.Belopp);
    }

    [Fact]
    public void Skapa_KastarVidBeloppOverArstaket()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WellnessClaim.Skapa(_anstallId, "Gymkort", 5001m, _datum));
    }

    [Fact]
    public void Skapa_TillaterExaktArstaket()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Gymkort", WellnessClaim.MaxBeloppPerAr, _datum);

        Assert.Equal(5000m, claim.Belopp);
    }

    [Fact]
    public void Skapa_KastarNarTidigareGodkantPlusBeloppSprangerTaket()
    {
        // 3 500 kr redan godkänt i år + 2 000 kr nytt = 5 500 kr > 5 000 kr.
        Assert.Throws<InvalidOperationException>(() =>
            WellnessClaim.Skapa(_anstallId, "Massage", 2000m, _datum,
                tidigareGodkantUnderAret: 3500m));
    }

    [Fact]
    public void Skapa_TillaterBeloppSomFyllerUppTillTaket()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Massage", 1500m, _datum,
            tidigareGodkantUnderAret: 3500m);

        Assert.Equal(1500m, claim.Belopp);
    }

    [Fact]
    public void Godkann_SatterStatusOchGodkannare()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Simkort", 1000m, _datum);
        var godkannare = Guid.NewGuid();

        claim.Godkann(godkannare);

        Assert.Equal(WellnessClaimStatus.Godkand, claim.Status);
        Assert.Equal(godkannare, claim.GodkandAv);
        Assert.NotNull(claim.GodkandVid);
    }

    [Fact]
    public void Godkann_KastarNarArstaketSkulleOverskridas()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Personlig träning", 2000m, _datum);

        Assert.Throws<InvalidOperationException>(() =>
            claim.Godkann(Guid.NewGuid(), tidigareGodkantUnderAret: 3500m));
        Assert.Equal(WellnessClaimStatus.Inskickad, claim.Status);
    }

    [Fact]
    public void Godkann_TillaterGodkannandeUppTillTaket()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Personlig träning", 1500m, _datum);

        claim.Godkann(Guid.NewGuid(), tidigareGodkantUnderAret: 3500m);

        Assert.Equal(WellnessClaimStatus.Godkand, claim.Status);
    }

    [Fact]
    public void Godkann_KastarOmRedanGodkand()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Yoga", 500m, _datum);
        claim.Godkann(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => claim.Godkann(Guid.NewGuid()));
    }

    [Fact]
    public void Avvisa_SatterStatusOchKommentar()
    {
        var claim = WellnessClaim.Skapa(_anstallId, "Golfklubbor", 4000m, _datum);

        claim.Avvisa(Guid.NewGuid(), "Utrustning omfattas inte av friskvårdsbidraget.");

        Assert.Equal(WellnessClaimStatus.Avslagen, claim.Status);
        Assert.Equal("Utrustning omfattas inte av friskvårdsbidraget.", claim.Kommentar);
    }
}
