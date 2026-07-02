namespace RegionHR.GDPR.Klassa;

/// <summary>
/// Svenska visningstexter för KLASSA-modellens värden. Håller UI-lager fritt från switchar och
/// gör benämningarna testbara (samma text i sammanställning, register och export).
/// </summary>
public static class KlassaText
{
    /// <summary>Nivåns benämning, t.ex. "3 – Betydande".</summary>
    public static string Niva(KonsekvensNiva niva) => niva switch
    {
        KonsekvensNiva.Forsumbar => "1 – Försumbar",
        KonsekvensNiva.Mattlig => "2 – Måttlig",
        KonsekvensNiva.Betydande => "3 – Betydande",
        KonsekvensNiva.Allvarlig => "4 – Allvarlig",
        _ => niva.ToString()
    };

    /// <summary>Kort beskrivning av konsekvensnivåns innebörd.</summary>
    public static string NivaBeskrivning(KonsekvensNiva niva) => niva switch
    {
        KonsekvensNiva.Forsumbar => "Ingen eller ringa konsekvens för verksamhet eller registrerad.",
        KonsekvensNiva.Mattlig => "Hanterbar, begränsad konsekvens.",
        KonsekvensNiva.Betydande => "Allvarlig konsekvens för verksamhet eller registrerad.",
        KonsekvensNiva.Allvarlig => "Mycket/synnerligen allvarlig, svårreparabel konsekvens.",
        _ => ""
    };

    public static string Aspekt(Skyddsaspekt aspekt) => aspekt switch
    {
        Skyddsaspekt.Konfidentialitet => "Konfidentialitet",
        Skyddsaspekt.Riktighet => "Riktighet",
        Skyddsaspekt.Tillganglighet => "Tillgänglighet",
        _ => aspekt.ToString()
    };

    public static string Kategori(InformationsKategori kategori) => kategori switch
    {
        InformationsKategori.Grunddata => "Personuppgift (grunddata)",
        InformationsKategori.Loneuppgift => "Löneuppgift",
        InformationsKategori.Bankuppgift => "Bankuppgift",
        InformationsKategori.Halsouppgift => "Hälsa/rehab (art. 9)",
        InformationsKategori.FackligTillhorighet => "Facklig tillhörighet (art. 9)",
        InformationsKategori.SkyddadIdentitet => "Skyddad identitet",
        InformationsKategori.Rekrytering => "Rekrytering",
        InformationsKategori.Arbetsmiljo => "Arbetsmiljö/tillbud",
        InformationsKategori.Kompetens => "Kompetens/utbildning",
        InformationsKategori.Systemteknisk => "Systemteknisk (loggar)",
        _ => kategori.ToString()
    };
}
