namespace RegionHR.Documents.Domain;

/// <summary>
/// Regelverk för arkiv- och gallringsklassning. Beräknar gallringsfrist utifrån
/// <see cref="ArchiveClass"/> och föreslår arkivklass utifrån dokumentkategori.
///
/// Klassningen bygger vidare på <see cref="RetentionPolicy"/> (GDPR-gallring av
/// personuppgifter) men uttrycker det arkivrättsliga perspektivet: vissa allmänna
/// handlingar ska <em>bevaras</em> (styrande dokument, anställningsavtal) medan andra
/// gallras efter fastställd frist. Där arkivlagen kräver bevarande väger den tyngre
/// än GDPR:s rätt till radering för allmänna handlingar.
/// </summary>
public static class ArchiveClassificationPolicy
{
    /// <summary>
    /// Beräknar gallringsfrist från arkivklass och arkiveringsdatum.
    /// Returnerar <c>null</c> för <see cref="ArchiveClass.Bevaras"/> (ingen frist — bevaras).
    /// </summary>
    public static DateTime? BeraknaGallringsfrist(ArchiveClass klass, DateTime arkiveringsdatum) => klass switch
    {
        ArchiveClass.Bevaras => null,
        ArchiveClass.Gallras2Ar => arkiveringsdatum.AddYears(2),
        ArchiveClass.Gallras5Ar => arkiveringsdatum.AddYears(5),
        ArchiveClass.Gallras7Ar => arkiveringsdatum.AddYears(7),
        ArchiveClass.Gallras10Ar => arkiveringsdatum.AddYears(10),
        _ => arkiveringsdatum.AddYears(5)
    };

    /// <summary>Antal år i gallringsfristen för en klass (0 = bevaras).</summary>
    public static int GallringsfristAr(ArchiveClass klass) => klass switch
    {
        ArchiveClass.Bevaras => 0,
        ArchiveClass.Gallras2Ar => 2,
        ArchiveClass.Gallras5Ar => 5,
        ArchiveClass.Gallras7Ar => 7,
        ArchiveClass.Gallras10Ar => 10,
        _ => 5
    };

    /// <summary>
    /// Föreslår arkivklass utifrån dokumentkategori. Bevarande föreslås för
    /// styrande dokument och anställningsavtal (allmänna handlingar som normalt
    /// bevaras); övriga får gallringsfrister som speglar <see cref="RetentionPolicy"/>.
    /// </summary>
    public static ArchiveClass ForeslaArkivklass(DocumentCategory kategori) => kategori switch
    {
        DocumentCategory.Anstallningsavtal => ArchiveClass.Bevaras,
        DocumentCategory.Policy => ArchiveClass.Bevaras,
        DocumentCategory.Legitimation => ArchiveClass.Gallras10Ar,
        DocumentCategory.Lonespecifikation => ArchiveClass.Gallras7Ar,
        DocumentCategory.Lakarintyg => ArchiveClass.Gallras2Ar,
        DocumentCategory.Betyg => ArchiveClass.Gallras2Ar,
        DocumentCategory.Tjanstgoringsbevis => ArchiveClass.Gallras5Ar,
        DocumentCategory.Ovrigt => ArchiveClass.Gallras5Ar,
        _ => ArchiveClass.Gallras5Ar
    };

    /// <summary>Läsbar svensk etikett för en arkivklass.</summary>
    public static string Etikett(ArchiveClass klass) => klass switch
    {
        ArchiveClass.Bevaras => "Bevaras",
        ArchiveClass.Gallras2Ar => "Gallras efter 2 år",
        ArchiveClass.Gallras5Ar => "Gallras efter 5 år",
        ArchiveClass.Gallras7Ar => "Gallras efter 7 år",
        ArchiveClass.Gallras10Ar => "Gallras efter 10 år",
        _ => klass.ToString()
    };
}
