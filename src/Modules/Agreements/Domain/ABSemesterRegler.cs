namespace RegionHR.Agreements.Domain;

/// <summary>
/// Kanoniska semesterregler enligt AB (Allmänna bestämmelser) § 27.
///
/// KÄLLA: SKR "Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01", § 27.
///
/// Central faktakälla för semesterrätt i systemet. Både löneberäkningen och
/// den (legacy) KollektivavtalEngine ska räkna semester via dessa metoder i
/// stället för magiska konstanter.
///
/// Viktiga AB-specifika avvikelser från semesterlagens standard:
///  - Intjänandeåret SAMMANFALLER med semesteråret och är löpande KALENDERÅR
///    (AB § 27 mom. 2), inte 1 april–31 mars som semesterlagens default.
///  - Semesterrätten trappas med ålder som uppnås under intjänandeåret
///    (AB § 27 mom. 5): 25 / 31 / 32 dagar.
/// </summary>
public static class ABSemesterRegler
{
    // AB § 27 mom. 5 — årlig semesterrätt (antal semesterdagar) per ålder.
    public const int BasDagar = 25;      // t.o.m. det intjänandeår man fyller 39
    public const int DagarFran40 = 31;   // fr.o.m. det intjänandeår man fyller 40
    public const int DagarFran50 = 32;   // fr.o.m. det intjänandeår man fyller 50

    // AB § 27 mom. 15 — semesterdagstillägg per uttagen betald semesterdag,
    // beräknat som procent av den fasta kontanta (månads-)lönen.
    public const decimal SemesterdagstillaggProcent = 0.605m;

    // AB § 27 mom. 16 — procentregel för rörlig lön (semesterlön av semesterlöneunderlaget).
    public const decimal RorligLonProcent25 = 12.00m;   // vid 25 semesterdagar
    public const decimal RorligLonProcent31 = 14.88m;   // vid 31 semesterdagar
    public const decimal RorligLonProcent32 = 15.36m;   // vid 32 semesterdagar

    // Semesterlagen (SemL) § 18 / AB § 27 mom. 17–18 — sparande av semesterdagar.
    // Endast betalda dagar som överstiger 20 per år får sparas.
    public const int MinBetaldaDagarForSparande = 20;
    public const int MaxSparadeAr = 5;

    /// <summary>
    /// Årlig semesterrätt enligt AB § 27 mom. 5, utifrån den ålder arbetstagaren
    /// UPPNÅR under intjänandeåret (= kalenderåret).
    /// </summary>
    public static int ArligSemesterratt(int alderUnderIntjanandear) => alderUnderIntjanandear switch
    {
        >= 50 => DagarFran50,
        >= 40 => DagarFran40,
        _ => BasDagar
    };

    /// <summary>
    /// Årlig semesterrätt utifrån födelseår och intjänandeår (kalenderår).
    /// Åldern som avses är den man fyller under året, dvs intjänandeår − födelseår.
    /// </summary>
    public static int ArligSemesterrattForFodelsear(int fodelsear, int intjanandear)
        => ArligSemesterratt(intjanandear - fodelsear);

    /// <summary>
    /// Procentsats för rörlig lön (AB § 27 mom. 16) beroende på årlig semesterrätt.
    /// </summary>
    public static decimal RorligLonProcent(int arligSemesterratt) => arligSemesterratt switch
    {
        >= 32 => RorligLonProcent32,
        >= 31 => RorligLonProcent31,
        _ => RorligLonProcent25
    };

    /// <summary>
    /// Antal BETALDA semesterdagar i proportion till anställd del av intjänandeåret
    /// (AB § 27 mom. 6 / semesterlagen § 7). Antalet avrundas uppåt till hel dag.
    ///
    /// Detta är rättelsen av den tidigare buggen där anställningsmånader ignorerades
    /// och full årsrätt gavs oavsett hur stor del av året man varit anställd.
    /// </summary>
    /// <param name="arligSemesterratt">Årlig semesterrätt (t.ex. 25/31/32).</param>
    /// <param name="anstallningsmanaderUnderIntjanandear">Antal månader anställd under intjänandeåret (0–12).</param>
    public static int BetaldaDagar(int arligSemesterratt, int anstallningsmanaderUnderIntjanandear)
    {
        var manader = Math.Clamp(anstallningsmanaderUnderIntjanandear, 0, 12);
        if (manader == 0)
            return 0;
        var betalda = (decimal)arligSemesterratt * manader / 12m;
        return (int)Math.Ceiling(betalda);
    }

    /// <summary>
    /// Hur många dagar som får sparas ett givet år (SemL § 18): endast betalda dagar
    /// som överstiger 20 per år. Returnerar 0 om årets betalda dagar ≤ 20.
    /// </summary>
    public static int MaxSparbaraDagar(int betaldaDagarIAr)
        => Math.Max(0, betaldaDagarIAr - MinBetaldaDagarForSparande);
}
