using System.Text;
using RegionHR.Documents.Domain;
using Xunit;

namespace RegionHR.Documents.Tests;

public class ArchivedDocumentTests
{
    private static readonly DateTime Ref = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ArchivedDocument Arkivera(
        ArchiveClass klass = ArchiveClass.Gallras5Ar,
        byte[]? innehall = null,
        DateTime? nar = null)
    {
        var bytes = innehall ?? Encoding.UTF8.GetBytes("arkivinnehåll");
        return ArchivedDocument.Arkivera(
            sourceDocumentId: Guid.NewGuid(),
            anstallId: Guid.NewGuid(),
            diarienummer: "HR-2026-00123",
            titel: "avtal.pdf",
            kategori: DocumentCategory.Ovrigt,
            arkivklass: klass,
            ansvarig: "HR-arkivarie",
            arkiveratAv: "admin@region.se",
            integritetsHash: ArchiveIntegrity.Hash(bytes),
            storagePath: "/uploads/ovrigt/avtal.pdf",
            contentType: "application/pdf",
            fileSizeBytes: bytes.Length,
            arkiveratVid: nar ?? Ref);
    }

    // ── Arkivering + metadata ───────────────────────────────────────────────

    [Fact]
    public void Arkivera_SetterMetadataOchStatus()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.Equal("HR-2026-00123", a.Diarienummer);
        Assert.Equal(ArchiveStatus.Arkiverad, a.Status);
        Assert.False(a.Bevaras);
        Assert.Equal(Ref.AddYears(5), a.GallringsFrist);
        Assert.Equal("SHA-256", a.HashAlgoritm);
        Assert.False(a.GallringsSparr);
    }

    [Fact]
    public void Arkivera_Bevaras_HarIngenFrist()
    {
        var a = Arkivera(ArchiveClass.Bevaras);

        Assert.True(a.Bevaras);
        Assert.Null(a.GallringsFrist);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Arkivera_UtanDiarienummer_Kastar(string diarienummer)
    {
        Assert.Throws<ArgumentException>(() => ArchivedDocument.Arkivera(
            Guid.NewGuid(), Guid.NewGuid(), diarienummer, "titel", DocumentCategory.Ovrigt,
            ArchiveClass.Gallras5Ar, "ansvarig", "av", "hash", "/path", "application/pdf", 10));
    }

    [Fact]
    public void Arkivera_UtanHash_Kastar()
    {
        Assert.Throws<ArgumentException>(() => ArchivedDocument.Arkivera(
            Guid.NewGuid(), Guid.NewGuid(), "HR-1", "titel", DocumentCategory.Ovrigt,
            ArchiveClass.Gallras5Ar, "ansvarig", "av", "", "/path", "application/pdf", 10));
    }

    // ── Gallringsfrist-beräkning ────────────────────────────────────────────

    [Theory]
    [InlineData(ArchiveClass.Gallras2Ar, 2)]
    [InlineData(ArchiveClass.Gallras5Ar, 5)]
    [InlineData(ArchiveClass.Gallras7Ar, 7)]
    [InlineData(ArchiveClass.Gallras10Ar, 10)]
    public void BeraknaGallringsfrist_GerRattAntalAr(ArchiveClass klass, int ar)
    {
        var frist = ArchiveClassificationPolicy.BeraknaGallringsfrist(klass, Ref);
        Assert.Equal(Ref.AddYears(ar), frist);
    }

    [Fact]
    public void BeraknaGallringsfrist_Bevaras_GerNull()
    {
        Assert.Null(ArchiveClassificationPolicy.BeraknaGallringsfrist(ArchiveClass.Bevaras, Ref));
    }

    [Theory]
    [InlineData(DocumentCategory.Anstallningsavtal, ArchiveClass.Bevaras)]
    [InlineData(DocumentCategory.Policy, ArchiveClass.Bevaras)]
    [InlineData(DocumentCategory.Legitimation, ArchiveClass.Gallras10Ar)]
    [InlineData(DocumentCategory.Lonespecifikation, ArchiveClass.Gallras7Ar)]
    [InlineData(DocumentCategory.Lakarintyg, ArchiveClass.Gallras2Ar)]
    [InlineData(DocumentCategory.Betyg, ArchiveClass.Gallras2Ar)]
    [InlineData(DocumentCategory.Tjanstgoringsbevis, ArchiveClass.Gallras5Ar)]
    [InlineData(DocumentCategory.Ovrigt, ArchiveClass.Gallras5Ar)]
    public void ForeslaArkivklass_MapparKategoriRatt(DocumentCategory kategori, ArchiveClass forvantad)
    {
        Assert.Equal(forvantad, ArchiveClassificationPolicy.ForeslaArkivklass(kategori));
    }

    // ── Oföränderlighet / integritet ────────────────────────────────────────

    [Fact]
    public void VerifieraIntegritet_MedSammaInnehall_ArSant()
    {
        var innehall = Encoding.UTF8.GetBytes("originaldokument v1");
        var a = Arkivera(innehall: innehall);

        Assert.True(a.VerifieraIntegritet(innehall));
    }

    [Fact]
    public void VerifieraIntegritet_MedManipuleratInnehall_ArFalskt()
    {
        var original = Encoding.UTF8.GetBytes("originaldokument v1");
        var manipulerat = Encoding.UTF8.GetBytes("manipulerat dokument v2");
        var a = Arkivera(innehall: original);

        // Efter arkivering är handlingen oföränderlig — ett ändrat innehåll upptäcks.
        Assert.False(a.VerifieraIntegritet(manipulerat));
    }

    // ── Gallring ────────────────────────────────────────────────────────────

    [Fact]
    public void Gallra_InnanFrist_Kastar()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar); // frist = 2031-01-01
        var ex = Assert.Throws<InvalidOperationException>(() => a.Gallra("arkivarie", Ref.AddYears(1)));
        Assert.Contains("frist", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ArchiveStatus.Arkiverad, a.Status);
    }

    [Fact]
    public void Gallra_EfterFrist_Gallras()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);
        a.Gallra("arkivarie", Ref.AddYears(6));

        Assert.Equal(ArchiveStatus.Gallrad, a.Status);
        Assert.Equal("arkivarie", a.GallradAv);
        Assert.Equal(Ref.AddYears(6), a.GallradVid);
    }

    [Fact]
    public void Gallra_Bevaras_Kastar()
    {
        var a = Arkivera(ArchiveClass.Bevaras);
        Assert.Throws<InvalidOperationException>(() => a.Gallra("arkivarie", Ref.AddYears(50)));
    }

    [Fact]
    public void Gallra_MedGallringsSparr_Kastar()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);
        a.SattGallringsSparr("Pågående rättstvist", "jurist");

        Assert.Throws<InvalidOperationException>(() => a.Gallra("arkivarie", Ref.AddYears(6)));
    }

    // ── Gallringsspärr (legal hold) ──────────────────────────────────────────

    [Fact]
    public void GallringsSparr_SattOchHav_Roundtrip()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);

        a.SattGallringsSparr("Utredning", "jurist");
        Assert.True(a.GallringsSparr);
        Assert.Equal("Utredning", a.GallringsSparrOrsak);

        a.TaBortGallringsSparr("jurist");
        Assert.False(a.GallringsSparr);
        Assert.Null(a.GallringsSparrOrsak);
    }

    // ── Arkivlagen > GDPR ────────────────────────────────────────────────────

    [Fact]
    public void FarRaderasEnligtGdpr_Bevaras_ArAlltidFalskt()
    {
        var a = Arkivera(ArchiveClass.Bevaras);
        // Arkivpliktig handling får inte raderas på GDPR-grund.
        Assert.False(a.FarRaderasEnligtGdpr(Ref.AddYears(100)));
    }

    [Fact]
    public void FarRaderasEnligtGdpr_InnanFrist_ArFalskt()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);
        Assert.False(a.FarRaderasEnligtGdpr(Ref.AddYears(1)));
    }

    [Fact]
    public void FarRaderasEnligtGdpr_EfterFristUtanSparr_ArSant()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);
        Assert.True(a.FarRaderasEnligtGdpr(Ref.AddYears(6)));
    }

    [Fact]
    public void FarRaderasEnligtGdpr_MedGallringsSparr_ArFalskt()
    {
        var a = Arkivera(ArchiveClass.Gallras5Ar);
        a.SattGallringsSparr("Rättstvist", "jurist");
        Assert.False(a.FarRaderasEnligtGdpr(Ref.AddYears(6)));
    }
}
