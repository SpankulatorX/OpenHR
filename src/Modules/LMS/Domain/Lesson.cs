namespace RegionHR.LMS.Domain;

/// <summary>
/// Typ av lektionsinnehåll i en kurs. En lektion är antingen ren text, en video
/// (extern URL/inbäddning), en uppladdad fil (PDF/bild via IFileStorageService)
/// eller ett SCORM-paket (metadata via <see cref="ScormPackage"/>).
/// </summary>
public enum LessonType { Text, Video, Fil, Scorm }

/// <summary>
/// En lektion (innehållsblock) i en kurs. Lektioner visas i stigande
/// <see cref="Ordning"/> i kursspelaren. Kursinnehåll saknades tidigare helt i LMS —
/// Course/CourseEnrollment fanns men inget faktiskt material.
/// </summary>
public class Lesson
{
    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public int Ordning { get; private set; }
    public string Titel { get; private set; } = "";
    public LessonType Typ { get; private set; }

    /// <summary>Fritext-innehåll (markdown/plain) för <see cref="LessonType.Text"/>.</summary>
    public string? TextInnehall { get; private set; }

    /// <summary>Video-URL (YouTube/Vimeo/mp4) för <see cref="LessonType.Video"/>.</summary>
    public string? MediaUrl { get; private set; }

    /// <summary>Lagringsväg (IFileStorageService) för <see cref="LessonType.Fil"/>.</summary>
    public string? FilStoragePath { get; private set; }
    public string? FilNamn { get; private set; }

    /// <summary>Koppling till uppladdat SCORM-paket för <see cref="LessonType.Scorm"/>.</summary>
    public Guid? ScormPackageId { get; private set; }

    public int LangdMinuter { get; private set; }
    public DateTime SkapadVid { get; private set; }

    private Lesson() { }

    private static Lesson Bas(Guid courseId, int ordning, string titel, LessonType typ, int langdMinuter)
    {
        if (string.IsNullOrWhiteSpace(titel)) throw new ArgumentException("Titel krävs", nameof(titel));
        if (ordning < 0) throw new ArgumentOutOfRangeException(nameof(ordning));
        if (langdMinuter < 0) throw new ArgumentOutOfRangeException(nameof(langdMinuter));
        return new Lesson
        {
            Id = Guid.NewGuid(), CourseId = courseId, Ordning = ordning,
            Titel = titel.Trim(), Typ = typ, LangdMinuter = langdMinuter,
            SkapadVid = DateTime.UtcNow
        };
    }

    public static Lesson SkapaText(Guid courseId, int ordning, string titel, string textInnehall, int langdMinuter = 0)
    {
        var l = Bas(courseId, ordning, titel, LessonType.Text, langdMinuter);
        l.TextInnehall = textInnehall;
        return l;
    }

    public static Lesson SkapaVideo(Guid courseId, int ordning, string titel, string mediaUrl, int langdMinuter = 0)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl)) throw new ArgumentException("Video-URL krävs", nameof(mediaUrl));
        var l = Bas(courseId, ordning, titel, LessonType.Video, langdMinuter);
        l.MediaUrl = mediaUrl.Trim();
        return l;
    }

    public static Lesson SkapaFil(Guid courseId, int ordning, string titel, string storagePath, string filNamn, int langdMinuter = 0)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("Lagringsväg krävs", nameof(storagePath));
        var l = Bas(courseId, ordning, titel, LessonType.Fil, langdMinuter);
        l.FilStoragePath = storagePath;
        l.FilNamn = filNamn;
        return l;
    }

    public static Lesson SkapaScorm(Guid courseId, int ordning, string titel, Guid scormPackageId, int langdMinuter = 0)
    {
        var l = Bas(courseId, ordning, titel, LessonType.Scorm, langdMinuter);
        l.ScormPackageId = scormPackageId;
        return l;
    }

    public void AndraOrdning(int ordning)
    {
        if (ordning < 0) throw new ArgumentOutOfRangeException(nameof(ordning));
        Ordning = ordning;
    }

    /// <summary>Uppdaterar redigerbara fält (titel, text, video, längd). Typ och kopplingar är oföränderliga.</summary>
    public void Uppdatera(string titel, string? textInnehall, string? mediaUrl, int langdMinuter)
    {
        if (string.IsNullOrWhiteSpace(titel)) throw new ArgumentException("Titel krävs", nameof(titel));
        if (langdMinuter < 0) throw new ArgumentOutOfRangeException(nameof(langdMinuter));
        Titel = titel.Trim();
        if (Typ == LessonType.Text) TextInnehall = textInnehall;
        if (Typ == LessonType.Video && !string.IsNullOrWhiteSpace(mediaUrl)) MediaUrl = mediaUrl.Trim();
        LangdMinuter = langdMinuter;
    }
}
