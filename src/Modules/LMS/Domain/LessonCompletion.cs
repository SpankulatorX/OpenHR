namespace RegionHR.LMS.Domain;

/// <summary>
/// Spårar att en deltagare genomfört en enskild lektion. Fungerar för både interna
/// anställda och externa deltagare: <see cref="DeltagareId"/> är antingen anställnings-/
/// employee-id (intern) eller <see cref="ExternalParticipant"/>.Id (extern), och
/// <see cref="ArExtern"/> särskiljer dem. Kursspelaren räknar genomförandegrad =
/// antal completions / antal lektioner i kursen.
/// </summary>
public class LessonCompletion
{
    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid DeltagareId { get; private set; }
    public bool ArExtern { get; private set; }
    public DateTime GenomfordVid { get; private set; }

    private LessonCompletion() { }

    public static LessonCompletion Markera(Guid courseId, Guid lessonId, Guid deltagareId, bool arExtern)
    {
        return new LessonCompletion
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            LessonId = lessonId,
            DeltagareId = deltagareId,
            ArExtern = arExtern,
            GenomfordVid = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Beräknar genomförandegrad (0–100 %) utifrån antal genomförda och totalt antal lektioner.
    /// Returnerar 0 om kursen saknar lektioner.
    /// </summary>
    public static int BeraknaGrad(int antalGenomforda, int totaltAntalLektioner)
    {
        if (totaltAntalLektioner <= 0) return 0;
        if (antalGenomforda <= 0) return 0;
        var grad = (int)Math.Round(antalGenomforda / (double)totaltAntalLektioner * 100, MidpointRounding.AwayFromZero);
        return Math.Clamp(grad, 0, 100);
    }
}
