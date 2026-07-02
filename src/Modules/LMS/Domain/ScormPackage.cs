namespace RegionHR.LMS.Domain;

/// <summary>
/// SCORM-standardens huvudversioner. <see cref="Okand"/> = manifest kunde inte
/// entydigt versionsbestämmas (paketet accepteras ändå men flaggas i UI).
/// </summary>
public enum ScormVersion { Scorm12, Scorm2004, Okand }

/// <summary>
/// Metadata extraherad ur <c>imsmanifest.xml</c> av <see cref="ScormManifestParser"/>.
/// Rena tolkningsresultat — persisteras via <see cref="ScormPackage"/>.
/// </summary>
public sealed record ScormManifestInfo(
    string Identifier,
    string Titel,
    ScormVersion Version,
    string LaunchUrl,
    decimal? MasteryScore);

/// <summary>
/// Ett uppladdat SCORM-paket kopplat till en kurs. Zip-filen lagras via
/// IFileStorageService (<see cref="StoragePath"/>); denna entitet håller
/// manifest-metadatan så att kursspelaren kan visa/starta innehållet.
///
/// FÖRENKLAT: vi lagrar och katalogiserar paketet + parsar imsmanifest.xml
/// (identifier, titel, version, launch-URL, mastery score). En fullskalig
/// SCORM-runtime (JS SCORM-API 1.2/2004, cmi-datamodell, uppackning och
/// statisk servering av innehållet, sekvensering) ingår INTE — genomförande
/// spåras manuellt via <see cref="LessonCompletion"/> i kursspelaren.
/// </summary>
public class ScormPackage
{
    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public string Titel { get; private set; } = "";
    public string Identifier { get; private set; } = "";
    public ScormVersion Version { get; private set; }

    /// <summary>Relativ launch-URL inuti paketet (t.ex. <c>index.html</c> eller <c>shared/launchpage.html</c>).</summary>
    public string LaunchUrl { get; private set; } = "";

    /// <summary>Lagringsväg till zip-paketet i IFileStorageService.</summary>
    public string StoragePath { get; private set; } = "";
    public string OriginalFilNamn { get; private set; } = "";
    public long StorlekBytes { get; private set; }
    public decimal? MasteryScore { get; private set; }
    public DateTime UppladdadVid { get; private set; }

    private ScormPackage() { }

    public static ScormPackage Skapa(Guid courseId, ScormManifestInfo manifest, string storagePath, string originalFilNamn, long storlekBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("Lagringsväg krävs", nameof(storagePath));
        return new ScormPackage
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            Titel = string.IsNullOrWhiteSpace(manifest.Titel) ? originalFilNamn : manifest.Titel,
            Identifier = manifest.Identifier,
            Version = manifest.Version,
            LaunchUrl = manifest.LaunchUrl,
            StoragePath = storagePath,
            OriginalFilNamn = originalFilNamn,
            StorlekBytes = storlekBytes,
            MasteryScore = manifest.MasteryScore,
            UppladdadVid = DateTime.UtcNow
        };
    }

    public string VersionText => Version switch
    {
        ScormVersion.Scorm12 => "SCORM 1.2",
        ScormVersion.Scorm2004 => "SCORM 2004",
        _ => "Okänd SCORM-version"
    };
}
