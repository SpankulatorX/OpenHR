using RegionHR.LMS.Domain;
using Xunit;

namespace RegionHR.LMS.Tests;

public class LessonCompletionTests
{
    [Fact]
    public void Markera_SatterFalt()
    {
        var courseId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var deltagareId = Guid.NewGuid();

        var c = LessonCompletion.Markera(courseId, lessonId, deltagareId, arExtern: true);

        Assert.Equal(courseId, c.CourseId);
        Assert.Equal(lessonId, c.LessonId);
        Assert.Equal(deltagareId, c.DeltagareId);
        Assert.True(c.ArExtern);
        Assert.NotEqual(Guid.Empty, c.Id);
        Assert.True(c.GenomfordVid <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 50)]
    [InlineData(10, 10, 100)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(3, 4, 75)]
    public void BeraknaGrad_ReturnerarKorrektProcent(int genomforda, int totalt, int forvantat)
    {
        Assert.Equal(forvantat, LessonCompletion.BeraknaGrad(genomforda, totalt));
    }

    [Fact]
    public void BeraknaGrad_NollLektioner_ReturnerarNoll()
    {
        Assert.Equal(0, LessonCompletion.BeraknaGrad(0, 0));
        Assert.Equal(0, LessonCompletion.BeraknaGrad(5, 0));
    }

    [Fact]
    public void BeraknaGrad_KlampasTill100()
    {
        Assert.Equal(100, LessonCompletion.BeraknaGrad(12, 10));
    }
}

public class CourseEnrollmentGenomforandeTests
{
    private readonly Guid _anstallId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();

    [Fact]
    public void UppdateraGenomforande_Noll_ForblirAnmald()
    {
        var e = CourseEnrollment.Anmala(_anstallId, _courseId);

        e.UppdateraGenomforande(0);

        Assert.Equal(EnrollmentProgress.Anmalad, e.Progress);
        Assert.Equal(0, e.GenomforandeGrad);
        Assert.Null(e.PaborjadVid);
    }

    [Fact]
    public void UppdateraGenomforande_Delvis_SatterPaborjad()
    {
        var e = CourseEnrollment.Anmala(_anstallId, _courseId);

        e.UppdateraGenomforande(50);

        Assert.Equal(EnrollmentProgress.Paborjad, e.Progress);
        Assert.Equal(50, e.GenomforandeGrad);
        Assert.NotNull(e.PaborjadVid);
        Assert.Null(e.GenomfordVid);
    }

    [Fact]
    public void UppdateraGenomforande_Full_SatterGenomfordOchGodkand()
    {
        var e = CourseEnrollment.Anmala(_anstallId, _courseId);

        e.UppdateraGenomforande(100);

        Assert.Equal(EnrollmentProgress.Genomford, e.Progress);
        Assert.Equal(100, e.GenomforandeGrad);
        Assert.True(e.Godkand);
        Assert.NotNull(e.GenomfordVid);
    }

    [Fact]
    public void UppdateraGenomforande_OgiltigProcent_KastarUndantag()
    {
        var e = CourseEnrollment.Anmala(_anstallId, _courseId);

        Assert.Throws<ArgumentOutOfRangeException>(() => e.UppdateraGenomforande(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.UppdateraGenomforande(101));
    }

    [Fact]
    public void UppdateraGenomforande_BevararTidigarePaborjadVid()
    {
        var e = CourseEnrollment.Anmala(_anstallId, _courseId);
        e.UppdateraGenomforande(30);
        var forsta = e.PaborjadVid;

        e.UppdateraGenomforande(60);

        Assert.Equal(forsta, e.PaborjadVid);
    }
}

public class ExternalCourseEnrollmentTests
{
    private readonly Guid _participantId = Guid.NewGuid();
    private readonly Guid _courseId = Guid.NewGuid();

    [Fact]
    public void Anmala_SatterAnmald()
    {
        var e = ExternalCourseEnrollment.Anmala(_participantId, _courseId);

        Assert.Equal(EnrollmentProgress.Anmalad, e.Progress);
        Assert.Equal(_participantId, e.ExternalParticipantId);
        Assert.Equal(_courseId, e.CourseId);
        Assert.Null(e.GenomforandeGrad);
        Assert.False(e.Godkand);
    }

    [Fact]
    public void UppdateraGenomforande_Full_GenomfordOchGodkand()
    {
        var e = ExternalCourseEnrollment.Anmala(_participantId, _courseId);

        e.UppdateraGenomforande(100);

        Assert.Equal(EnrollmentProgress.Genomford, e.Progress);
        Assert.True(e.Godkand);
        Assert.NotNull(e.GenomfordVid);
    }

    [Fact]
    public void UppdateraGenomforande_Delvis_Paborjad()
    {
        var e = ExternalCourseEnrollment.Anmala(_participantId, _courseId);

        e.UppdateraGenomforande(40);

        Assert.Equal(EnrollmentProgress.Paborjad, e.Progress);
        Assert.Equal(40, e.GenomforandeGrad);
        Assert.NotNull(e.PaborjadVid);
    }

    [Fact]
    public void Avbryt_SatterAvbruten()
    {
        var e = ExternalCourseEnrollment.Anmala(_participantId, _courseId);

        e.Avbryt();

        Assert.Equal(EnrollmentProgress.Avbruten, e.Progress);
    }

    [Fact]
    public void UppdateraGenomforande_OgiltigProcent_KastarUndantag()
    {
        var e = ExternalCourseEnrollment.Anmala(_participantId, _courseId);

        Assert.Throws<ArgumentOutOfRangeException>(() => e.UppdateraGenomforande(150));
    }
}
