namespace RegionHR.GDPR.Klassa;

/// <summary>
/// KLASSA/MSB:s konsekvensnivåer (1–4). Skalan speglar hur allvarlig konsekvensen blir
/// för verksamheten eller en registrerad om skyddet för informationens konfidentialitet,
/// riktighet eller tillgänglighet brister. Nivå 4 är SKR:s översta nivå (införd utöver
/// MSB:s ursprungliga matris för att fånga den mest allvarliga konsekvensen).
/// Heltalsvärdena är signifikanta: de används för jämförelse och aggregering (högsta nivå).
/// </summary>
public enum KonsekvensNiva
{
    /// <summary>Nivå 1 – Försumbar. Ingen eller ringa skada.</summary>
    Forsumbar = 1,

    /// <summary>Nivå 2 – Måttlig. Hanterbar, begränsad skada.</summary>
    Mattlig = 2,

    /// <summary>Nivå 3 – Betydande. Allvarlig skada för verksamhet eller registrerad.</summary>
    Betydande = 3,

    /// <summary>Nivå 4 – Allvarlig. Mycket/synnerligen allvarlig, svårreparabel skada.</summary>
    Allvarlig = 4
}

/// <summary>
/// De tre skyddsaspekter (säkerhetsdimensioner) som KLASSA värderar per informationsmängd.
/// </summary>
public enum Skyddsaspekt
{
    /// <summary>Åtkomst begränsas till behöriga (röjande = konsekvens).</summary>
    Konfidentialitet,

    /// <summary>Informationen är korrekt och ej manipulerad/förstörd.</summary>
    Riktighet,

    /// <summary>Informationen kan nyttjas efter behov av behörig.</summary>
    Tillganglighet
}

/// <summary>
/// Kategori av informationsmängd i OpenHR. Kategorin styr KLASSA-regelverkets
/// rekommenderade miniminivåer (se <see cref="KlassaRegler"/>) och avgör om mängden är
/// en känslig personuppgift enligt GDPR art. 9.
/// </summary>
public enum InformationsKategori
{
    /// <summary>Allmänna personuppgifter (namn, kontakt, anställning).</summary>
    Grunddata,

    /// <summary>Löne- och ersättningsuppgifter.</summary>
    Loneuppgift,

    /// <summary>Bank-/utbetalningsuppgifter.</summary>
    Bankuppgift,

    /// <summary>Hälsa/rehab/sjukfrånvaro — känslig personuppgift (GDPR art. 9).</summary>
    Halsouppgift,

    /// <summary>Facklig tillhörighet — känslig personuppgift (GDPR art. 9).</summary>
    FackligTillhorighet,

    /// <summary>Skyddad identitet/personuppgift — kräver högsta konfidentialitet.</summary>
    SkyddadIdentitet,

    /// <summary>Rekryterings- och kandidatuppgifter.</summary>
    Rekrytering,

    /// <summary>Arbetsmiljö, tillbud och arbetsskador.</summary>
    Arbetsmiljo,

    /// <summary>Kompetens-, utbildnings- och certifieringsuppgifter.</summary>
    Kompetens,

    /// <summary>Systemtekniska data (loggar, spårbarhet).</summary>
    Systemteknisk
}
