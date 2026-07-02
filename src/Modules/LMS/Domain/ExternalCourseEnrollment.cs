namespace RegionHR.LMS.Domain;

/// <summary>
/// En extern deltagares anmälan till en kurs. Parallell till <see cref="CourseEnrollment"/>
/// (som kräver anställnings-id) men refererar en <see cref="ExternalParticipant"/> i stället.
/// Hålls avsiktligt separat så att intern/extern deltagande inte blandas i samma tabell
/// och den interna modellens publika API är oförändrat.
/// </summary>
public class ExternalCourseEnrollment
{
    public Guid Id { get; private set; }
    public Guid ExternalParticipantId { get; private set; }
    public Guid CourseId { get; private set; }
    public EnrollmentProgress Progress { get; private set; }

    /// <summary>Andel genomförda lektioner (0–100 %). Null innan kursen påbörjats.</summary>
    public int? GenomforandeGrad { get; private set; }
    public int? Resultat { get; private set; }
    public bool Godkand { get; private set; }
    public DateTime AnmalanVid { get; private set; }
    public DateTime? PaborjadVid { get; private set; }
    public DateTime? GenomfordVid { get; private set; }

    private ExternalCourseEnrollment() { }

    public static ExternalCourseEnrollment Anmala(Guid externalParticipantId, Guid courseId)
    {
        return new ExternalCourseEnrollment
        {
            Id = Guid.NewGuid(),
            ExternalParticipantId = externalParticipantId,
            CourseId = courseId,
            Progress = EnrollmentProgress.Anmalad,
            AnmalanVid = DateTime.UtcNow
        };
    }

    public void Paborja()
    {
        if (Progress == EnrollmentProgress.Anmalad) Progress = EnrollmentProgress.Paborjad;
        PaborjadVid ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Uppdaterar genomförandegrad utifrån kursspelaren. 0 → oförändrat anmäld,
    /// 1–99 → påbörjad, 100 → genomförd (och godkänd om inget separat provresultat krävs).
    /// </summary>
    public void UppdateraGenomforande(int procent)
    {
        if (procent < 0 || procent > 100) throw new ArgumentOutOfRangeException(nameof(procent));
        GenomforandeGrad = procent;

        if (procent > 0 && Progress == EnrollmentProgress.Anmalad)
        {
            Progress = EnrollmentProgress.Paborjad;
            PaborjadVid ??= DateTime.UtcNow;
        }

        if (procent >= 100)
        {
            Progress = EnrollmentProgress.Genomford;
            Godkand = true;
            GenomfordVid ??= DateTime.UtcNow;
        }
    }

    public void Avbryt() => Progress = EnrollmentProgress.Avbruten;
}
