namespace RegionHR.Documents.Domain;

/// <summary>
/// Arkiv- och gallringsklass enligt arkivlagen (1990:782) och regionens
/// dokumenthanteringsplan. Klassen styr om en allmän handling ska
/// <em>bevaras</em> (aldrig gallras) eller gallras efter en fastställd frist.
/// </summary>
public enum ArchiveClass
{
    /// <summary>Bevaras för all framtid — får aldrig gallras (t.ex. styrande dokument, avtal).</summary>
    Bevaras,

    /// <summary>Gallras 2 år efter arkivering (t.ex. läkarintyg, betyg).</summary>
    Gallras2Ar,

    /// <summary>Gallras 5 år efter arkivering (standard för övriga handlingar).</summary>
    Gallras5Ar,

    /// <summary>Gallras 7 år efter arkivering (räkenskapsinformation, BFL 7 kap. 2 §).</summary>
    Gallras7Ar,

    /// <summary>Gallras 10 år efter arkivering (legitimationer, vårdrelaterade krav).</summary>
    Gallras10Ar
}

/// <summary>
/// Livscykelstatus för en arkiverad handling i e-arkivet.
/// </summary>
public enum ArchiveStatus
{
    /// <summary>Handlingen är arkiverad och oföränderlig.</summary>
    Arkiverad,

    /// <summary>Handlingen har gallrats (gallringsfrist passerad, inget hinder) och är borttagen ur arkivet.</summary>
    Gallrad
}
