namespace RegionHR.Agreements.Domain;

/// <summary>
/// Kanoniska övertidsregler enligt AB (Allmänna bestämmelser) § 20 mom. 3.
///
/// KÄLLA: SKR "Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01", § 20.
///
/// Övertidskompensation per timme:
///   - Enkel övertid: 180 % av (månadslönen / 165)
///   - Kvalificerad övertid: 240 % av (månadslönen / 165)
/// Kompensation kan i stället tas ut som ledighet (1,5 tim resp. 2 tim per övertidstimme).
/// </summary>
public static class ABOvertidSatser
{
    // AB § 20 mom. 3 — total kompensationsprocent (inkl. grundlönens 100 %).
    public const decimal EnkelOvertidProcent = 1.80m;        // 180 %
    public const decimal KvalificeradOvertidProcent = 2.40m; // 240 %

    // Tillägg utöver grundlönens 100 % (dvs. den del som läggs på timlönen då
    // grundlönen redan täcker basen). 80 % resp. 140 %.
    public const decimal EnkelOvertidTillaggFaktor = 0.80m;
    public const decimal KvalificeradOvertidTillaggFaktor = 1.40m;

    // AB § 20 mom. 3 — delaren för att räkna fram övertidsgrundande timlön.
    public const decimal Overtidsdelare = 165m; // timlön = månadslön / 165

    // AB § 20 mom. 2 — kompensationsledighet per övertidstimme.
    public const decimal EnkelOvertidLedighetFaktor = 1.5m; // 1,5 tim ledigt per tim enkel övertid
    public const decimal KvalificeradOvertidLedighetFaktor = 2.0m;

    // AB § 20 mom. 3 — max sparade övertidstimmar som ledighet.
    public const decimal MaxSparadeOvertidstimmar = 200m;

    // Arbetstidslagen (ATL) § 7–8 — gränser för allmän övertid.
    public const decimal MaxOvertidPerVecka = 48m;
    public const decimal MaxOvertidPerFyraVeckor = 48m;
    public const decimal MaxAllmanOvertidPerAr = 200m;

    /// <summary>Timlön för övertidsberäkning enligt AB § 20 mom. 3 (månadslön / 165).</summary>
    public static decimal Overtidstimlon(decimal manadslon) => manadslon / Overtidsdelare;

    /// <summary>
    /// Övertidsersättning (tillägg utöver grundlön) i kronor för ett antal timmar.
    /// </summary>
    public static decimal OvertidsTillagg(decimal manadslon, decimal timmar, bool kvalificerad)
    {
        var faktor = kvalificerad ? KvalificeradOvertidTillaggFaktor : EnkelOvertidTillaggFaktor;
        return Overtidstimlon(manadslon) * faktor * timmar;
    }
}
