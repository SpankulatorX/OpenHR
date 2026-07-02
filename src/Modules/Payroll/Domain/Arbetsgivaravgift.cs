namespace RegionHR.Payroll.Domain;

using RegionHR.SharedKernel.Domain;

/// <summary>
/// Svenska arbetsgivaravgifter enligt socialavgiftslagen (2000:980), årsversionerat.
/// Systemet är experten: satsen väljs utifrån den anställdes FÖDELSEÅR och
/// aktuellt inkomstår/månad — inte utifrån en fritextinmatning.
///
/// Källa: Skatteverket "Arbetsgivaravgifter" (verifierad 2026-07):
///  - Full avgift 2026: 31,42 %.
///  - Personer födda 1937 eller tidigare: INGA arbetsgivaravgifter (fast kohort i lagen).
///  - Äldre: endast ålderspensionsavgift 10,21 %. Fr.o.m. 2026 gäller detta den
///    som vid årets ingång fyllt 67 år (t.o.m. 2025: fyllt 66 år) → födda 1958
///    eller tidigare för både 2025 och 2026.
///  - Ungdom (temporär nedsättning 2026-04-01 – 2027-09-30): 20,81 % på ersättning
///    upp till 25 000 kr/månad för den som vid årets ingång fyllt 18 men inte 23 år
///    (födda 2003–2007 för inkomstår 2026). Den tidigare 15–18-årsnedsättningen är
///    slopad fr.o.m. 2026.
///
/// OBS: uppdatera satser och åldersgränser årligen mot Skatteverkets publikation.
/// </summary>
public static class Arbetsgivaravgift
{
    /// <summary>Full arbetsgivaravgift 2026 (31,42 %).</summary>
    public const decimal FullSats2026 = 0.3142m;

    /// <summary>Full arbetsgivaravgift 2025 (31,42 %).</summary>
    public const decimal FullSats2025 = 0.3142m;

    /// <summary>Endast ålderspensionsavgift (10,21 %) — reducerad avgift för äldre.</summary>
    public const decimal EndastAlderspensionsavgift = 0.1021m;

    /// <summary>Temporär ungdomsnedsättning (20,81 %) = ålderspensionsavgift + halva övriga avgifter.</summary>
    public const decimal UngdomsSats = 0.2081m;

    /// <summary>Lönetak per månad för ungdomsnedsättningen (25 000 kr). Överskjutande del = full avgift.</summary>
    public const decimal UngdomsLonTakPerManad = 25000m;

    /// <summary>Personer födda detta år eller tidigare betalar inga arbetsgivaravgifter alls.</summary>
    public const int IngenAvgiftFodelseArTom = 1937;

    /// <summary>Full arbetsgivaravgift för angivet inkomstår.</summary>
    public static decimal FullSats(int inkomstAr) => inkomstAr switch
    {
        <= 2025 => FullSats2025,
        _ => FullSats2026
    };

    /// <summary>
    /// Senaste födelseår som ger reducerad äldreavgift (endast ålderspensionsavgift).
    /// T.o.m. 2025: "fyllt 66 vid årets ingång" → född inkomstår-67.
    /// Fr.o.m. 2026: "fyllt 67 vid årets ingång" → född inkomstår-68.
    /// Ger 1958 för både 2025 och 2026.
    /// </summary>
    public static int AldreFodelseArTom(int inkomstAr) => inkomstAr >= 2026 ? inkomstAr - 68 : inkomstAr - 67;

    /// <summary>Sant om personen inte omfattas av några arbetsgivaravgifter (född 1937 eller tidigare).</summary>
    public static bool ArIngenAvgift(int fodelseAr) => fodelseAr <= IngenAvgiftFodelseArTom;

    /// <summary>Sant om personen ger endast ålderspensionsavgift (äldre, men efter 1937).</summary>
    public static bool ArAldreMedReduceradAvgift(int fodelseAr, int inkomstAr)
        => fodelseAr > IngenAvgiftFodelseArTom && fodelseAr <= AldreFodelseArTom(inkomstAr);

    /// <summary>
    /// Är den temporära ungdomsnedsättningen i kraft för (inkomstår, månad)?
    /// Gäller ersättning utbetald 2026-04-01 – 2027-09-30. Månaden tolkas som löneperiodens
    /// månad (proxy för utbetalningsmånad).
    /// </summary>
    public static bool UngdomsnedsattningGaller(int inkomstAr, int manad) => inkomstAr switch
    {
        2026 => manad >= 4,
        2027 => manad <= 9,
        _ => false
    };

    /// <summary>
    /// Sant om personen omfattas av ungdomsnedsättningen: vid årets ingång fyllt 18 men inte 23 år
    /// (ålder vid årets ingång 19–23, dvs. födda inkomstår-23 .. inkomstår-19) OCH inom giltighetsfönstret.
    /// </summary>
    public static bool ArUngMedReduceradAvgift(int fodelseAr, int inkomstAr, int manad)
    {
        if (!UngdomsnedsattningGaller(inkomstAr, manad))
            return false;
        var alderVidArsingang = inkomstAr - fodelseAr;
        return alderVidArsingang is >= 19 and <= 23;
    }

    /// <summary>
    /// Primär avgiftssats för en person. För ungdom gäller satsen upp till lönetaket;
    /// använd <see cref="Belopp"/> för korrekt belopp med tak.
    /// </summary>
    public static decimal Sats(int fodelseAr, int inkomstAr, int manad)
    {
        if (ArIngenAvgift(fodelseAr))
            return 0m;
        if (ArAldreMedReduceradAvgift(fodelseAr, inkomstAr))
            return EndastAlderspensionsavgift;
        if (ArUngMedReduceradAvgift(fodelseAr, inkomstAr, manad))
            return UngdomsSats;
        return FullSats(inkomstAr);
    }

    /// <summary>
    /// Faktiskt avgiftsbelopp för en månad. Hanterar ungdomsrabattens lönetak:
    /// reducerad sats upp till 25 000 kr, full sats på överskjutande del.
    /// </summary>
    public static Money Belopp(Money bruttoManad, int fodelseAr, int inkomstAr, int manad)
    {
        if (ArIngenAvgift(fodelseAr))
            return Money.Zero;
        if (ArAldreMedReduceradAvgift(fodelseAr, inkomstAr))
            return bruttoManad * EndastAlderspensionsavgift;
        if (ArUngMedReduceradAvgift(fodelseAr, inkomstAr, manad))
        {
            var underTak = Math.Min(bruttoManad.Amount, UngdomsLonTakPerManad);
            var overTak = bruttoManad.Amount - underTak;
            return Money.SEK(underTak * UngdomsSats + overTak * FullSats(inkomstAr));
        }
        return bruttoManad * FullSats(inkomstAr);
    }
}
