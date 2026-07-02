namespace RegionHR.LMS.Domain;

public enum EnrollmentProgress { Anmalad, Paborjad, Genomford, Underkand, Avbruten }

public class CourseEnrollment
{
    public Guid Id { get; private set; }
    public Guid AnstallId { get; private set; }
    public Guid CourseId { get; private set; }
    public EnrollmentProgress Progress { get; private set; }
    public int? Resultat { get; private set; } // 0-100 score
    public int? GenomforandeGrad { get; private set; } // 0-100 % genomförda lektioner (kursspelare)
    public bool Godkand { get; private set; }
    public DateTime AnmalanVid { get; private set; }
    public DateTime? PaborjadVid { get; private set; }
    public DateTime? GenomfordVid { get; private set; }
    public DateOnly? GiltigTill { get; private set; }

    private CourseEnrollment() { }

    public static CourseEnrollment Anmala(Guid anstallId, Guid courseId)
    {
        return new CourseEnrollment
        {
            Id = Guid.NewGuid(), AnstallId = anstallId, CourseId = courseId,
            Progress = EnrollmentProgress.Anmalad, AnmalanVid = DateTime.UtcNow
        };
    }

    public void Paborja() { Progress = EnrollmentProgress.Paborjad; PaborjadVid = DateTime.UtcNow; }

    public void Genomfor(int resultat, int? giltighetManader = null)
    {
        if (resultat < 0 || resultat > 100) throw new ArgumentOutOfRangeException(nameof(resultat));
        Progress = resultat >= 70 ? EnrollmentProgress.Genomford : EnrollmentProgress.Underkand;
        Resultat = resultat;
        Godkand = resultat >= 70;
        GenomfordVid = DateTime.UtcNow;
        if (giltighetManader.HasValue)
            GiltigTill = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(giltighetManader.Value));
    }

    public void Avbryt() { Progress = EnrollmentProgress.Avbruten; }

    /// <summary>
    /// Uppdaterar genomförandegrad från kursspelaren (andel genomförda lektioner, 0–100 %).
    /// 1–99 % markerar kursen som Påbörjad; 100 % markerar den som Genomförd och godkänd.
    /// Detta är den lektionsbaserade genomförandespårningen (skiljd från <see cref="Genomfor"/>
    /// som sätter ett numeriskt provresultat).
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
}
