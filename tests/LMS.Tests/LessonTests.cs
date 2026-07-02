using RegionHR.LMS.Domain;
using Xunit;

namespace RegionHR.LMS.Tests;

public class LessonTests
{
    private readonly Guid _courseId = Guid.NewGuid();

    [Fact]
    public void SkapaText_SatterTypOchInnehall()
    {
        var l = Lesson.SkapaText(_courseId, 1, "Introduktion", "Välkommen till kursen.", 15);

        Assert.Equal(LessonType.Text, l.Typ);
        Assert.Equal("Introduktion", l.Titel);
        Assert.Equal("Välkommen till kursen.", l.TextInnehall);
        Assert.Equal(1, l.Ordning);
        Assert.Equal(15, l.LangdMinuter);
        Assert.Equal(_courseId, l.CourseId);
        Assert.Null(l.MediaUrl);
        Assert.Null(l.ScormPackageId);
        Assert.NotEqual(Guid.Empty, l.Id);
    }

    [Fact]
    public void SkapaVideo_SatterMediaUrl()
    {
        var l = Lesson.SkapaVideo(_courseId, 2, "Film", "https://example.com/v.mp4", 20);

        Assert.Equal(LessonType.Video, l.Typ);
        Assert.Equal("https://example.com/v.mp4", l.MediaUrl);
        Assert.Null(l.TextInnehall);
    }

    [Fact]
    public void SkapaVideo_UtanUrl_KastarUndantag()
    {
        Assert.Throws<ArgumentException>(() => Lesson.SkapaVideo(_courseId, 1, "Film", "  "));
    }

    [Fact]
    public void SkapaFil_SatterLagringsvagOchFilnamn()
    {
        var l = Lesson.SkapaFil(_courseId, 3, "Handbok", "lms-lessons/2026-07/abc_handbok.pdf", "handbok.pdf");

        Assert.Equal(LessonType.Fil, l.Typ);
        Assert.Equal("lms-lessons/2026-07/abc_handbok.pdf", l.FilStoragePath);
        Assert.Equal("handbok.pdf", l.FilNamn);
    }

    [Fact]
    public void SkapaScorm_KopplarPaketId()
    {
        var pkgId = Guid.NewGuid();
        var l = Lesson.SkapaScorm(_courseId, 4, "Interaktiv modul", pkgId);

        Assert.Equal(LessonType.Scorm, l.Typ);
        Assert.Equal(pkgId, l.ScormPackageId);
    }

    [Fact]
    public void Skapa_TomTitel_KastarUndantag()
    {
        Assert.Throws<ArgumentException>(() => Lesson.SkapaText(_courseId, 1, "   ", "text"));
    }

    [Fact]
    public void Skapa_NegativOrdning_KastarUndantag()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Lesson.SkapaText(_courseId, -1, "Titel", "text"));
    }

    [Fact]
    public void AndraOrdning_UppdaterarOrdning()
    {
        var l = Lesson.SkapaText(_courseId, 1, "Titel", "text");

        l.AndraOrdning(5);

        Assert.Equal(5, l.Ordning);
    }

    [Fact]
    public void Uppdatera_TextlektionAndrarTitelOchText()
    {
        var l = Lesson.SkapaText(_courseId, 1, "Gammal", "gammalt", 10);

        l.Uppdatera("Ny titel", "nytt innehåll", null, 25);

        Assert.Equal("Ny titel", l.Titel);
        Assert.Equal("nytt innehåll", l.TextInnehall);
        Assert.Equal(25, l.LangdMinuter);
    }

    [Fact]
    public void Uppdatera_Videolektion_BevararTextInnehallNull()
    {
        var l = Lesson.SkapaVideo(_courseId, 1, "Film", "https://a.se/v", 10);

        l.Uppdatera("Ny film", "ignoreras för video", "https://b.se/v", 12);

        Assert.Equal("Ny film", l.Titel);
        Assert.Equal("https://b.se/v", l.MediaUrl);
        Assert.Null(l.TextInnehall);
    }

    [Fact]
    public void Titel_Trimmas()
    {
        var l = Lesson.SkapaText(_courseId, 1, "  Med mellanslag  ", "x");

        Assert.Equal("Med mellanslag", l.Titel);
    }
}
