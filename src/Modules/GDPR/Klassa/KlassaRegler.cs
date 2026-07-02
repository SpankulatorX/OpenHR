namespace RegionHR.GDPR.Klassa;

/// <summary>
/// KLASSA-rekommendation: den lägsta konsekvensnivå per skyddsaspekt som en informationsmängd
/// av en viss kategori normalt bör klassas till. Används som stödregel — en klassning får sättas
/// högre, men understiger den kravet flaggas den som avvikande i sammanställningen.
/// </summary>
public sealed record Klassningskrav(
    KonsekvensNiva MinKonfidentialitet,
    KonsekvensNiva MinRiktighet,
    KonsekvensNiva MinTillganglighet);

/// <summary>
/// Normativt regelverk för KLASSA i OpenHR: kopplar informationskategori till dels om den utgör
/// en känslig personuppgift (GDPR art. 9), dels rekommenderade miniminivåer för K/R/T.
/// Reglerna är rena funktioner utan sidoeffekter — direkt enhetstestbara.
/// </summary>
public static class KlassaRegler
{
    /// <summary>
    /// Är kategorin en känslig personuppgift enligt GDPR art. 9? Hälsouppgifter och uppgift om
    /// facklig tillhörighet är särskilda kategorier och kräver därmed hög konfidentialitet.
    /// </summary>
    public static bool ArKansligPersonuppgift(InformationsKategori kategori) => kategori switch
    {
        InformationsKategori.Halsouppgift => true,
        InformationsKategori.FackligTillhorighet => true,
        _ => false
    };

    /// <summary>
    /// Rekommenderade miniminivåer (K/R/T) för en kategori enligt KLASSA-vägledningen anpassad
    /// för HR-domänen. Känsliga personuppgifter och skyddad identitet ligger på högsta
    /// konfidentialitetsnivån; löne-/bankdata betonar riktighet (felaktig utbetalning) och
    /// tillgänglighet (utbetalning i tid).
    /// </summary>
    public static Klassningskrav RekommenderatKrav(InformationsKategori kategori) => kategori switch
    {
        InformationsKategori.Halsouppgift =>
            new Klassningskrav(KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig),
        InformationsKategori.FackligTillhorighet =>
            new Klassningskrav(KonsekvensNiva.Allvarlig, KonsekvensNiva.Mattlig, KonsekvensNiva.Forsumbar),
        InformationsKategori.SkyddadIdentitet =>
            new Klassningskrav(KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig),
        InformationsKategori.Loneuppgift =>
            new Klassningskrav(KonsekvensNiva.Betydande, KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande),
        InformationsKategori.Bankuppgift =>
            new Klassningskrav(KonsekvensNiva.Betydande, KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande),
        InformationsKategori.Grunddata =>
            new Klassningskrav(KonsekvensNiva.Mattlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig),
        InformationsKategori.Rekrytering =>
            new Klassningskrav(KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig),
        InformationsKategori.Arbetsmiljo =>
            new Klassningskrav(KonsekvensNiva.Betydande, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig),
        InformationsKategori.Kompetens =>
            new Klassningskrav(KonsekvensNiva.Forsumbar, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig),
        InformationsKategori.Systemteknisk =>
            new Klassningskrav(KonsekvensNiva.Mattlig, KonsekvensNiva.Betydande, KonsekvensNiva.Betydande),
        _ => new Klassningskrav(KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig)
    };

    /// <summary>
    /// Uppfyller den satta klassningen kategorins rekommenderade miniminivåer för samtliga aspekter?
    /// </summary>
    public static bool UppfyllerKrav(
        InformationsKategori kategori,
        KonsekvensNiva konfidentialitet,
        KonsekvensNiva riktighet,
        KonsekvensNiva tillganglighet)
    {
        var krav = RekommenderatKrav(kategori);
        return konfidentialitet >= krav.MinKonfidentialitet
            && riktighet >= krav.MinRiktighet
            && tillganglighet >= krav.MinTillganglighet;
    }

    /// <summary>
    /// Den högsta av tre konsekvensnivåer — informationsmängdens sammanvägda skyddsnivå.
    /// </summary>
    public static KonsekvensNiva HogstaNiva(
        KonsekvensNiva konfidentialitet,
        KonsekvensNiva riktighet,
        KonsekvensNiva tillganglighet)
    {
        var hogst = (int)konfidentialitet;
        if ((int)riktighet > hogst) hogst = (int)riktighet;
        if ((int)tillganglighet > hogst) hogst = (int)tillganglighet;
        return (KonsekvensNiva)hogst;
    }
}
