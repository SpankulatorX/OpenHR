using RegionHR.LAS.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.LAS.Services;

/// <summary>
/// Larmnivå för LAS-bevakning, i stigande allvarlighetsgrad.
/// Tröskelvärdena uttrycks som "dagar kvar till konverteringsgränsen" och ger,
/// för SAVA (gräns 365), exakt de dokumenterade dagtrösklarna 300/330/350/360.
/// För vikariat (gräns 730) skalas samma "dagar kvar"-logik automatiskt.
/// </summary>
public enum LASAlertNiva
{
    Ingen = 0,
    Varning = 1,        // SAVA: 300 dagar (65 kvar)
    Kritisk = 2,        // SAVA: 330 dagar (35 kvar)
    MycketKritisk = 3,  // SAVA: 350 dagar (15 kvar)
    Konvertering = 4    // SAVA: 360 dagar (5 kvar) eller gräns nådd
}

/// <summary>Resultat av en LAS-larmbedömning.</summary>
public readonly record struct LASAlertBedomning(
    LASAlertNiva Niva,
    int GransDagar,
    int TroskelDagar,
    int DagarKvar);

/// <summary>
/// Ren (sidoeffektsfri) regelmotor för LAS-larm. Håller tröskel- och mottagarlogik
/// testbar utan databas. Används av LASAlertService (bakgrundsjobb).
/// </summary>
public static class LASAlertRegler
{
    // Dagar kvar till gränsen då respektive larmnivå triggas.
    public const int VarningDagarKvar = 65;         // SAVA -> 300
    public const int KritiskDagarKvar = 35;         // SAVA -> 330
    public const int MycketKritiskDagarKvar = 15;   // SAVA -> 350
    public const int KonverteringDagarKvar = 5;     // SAVA -> 360

    /// <summary>Konverteringsgränsen (max ackumulerade dagar) för en anställningsform.</summary>
    public static int GransFor(EmploymentType form) => form switch
    {
        EmploymentType.SAVA => LASAccumulation.SAVA_MAX_DAGAR_5AR,        // 365
        EmploymentType.Vikariat => LASAccumulation.VIKARIAT_MAX_DAGAR_5AR, // 730
        _ => int.MaxValue
    };

    /// <summary>
    /// Bedöm vilken larmnivå en ackumulering ligger på givet anställningsform och ackumulerade dagar.
    /// TroskelDagar är den korsade dagtröskeln (300/330/350/360 för SAVA) och används som dedup-nyckel.
    /// </summary>
    public static LASAlertBedomning Bedom(EmploymentType form, int ackumuleradeDagar)
    {
        var grans = GransFor(form);
        if (grans == int.MaxValue)
            return new LASAlertBedomning(LASAlertNiva.Ingen, 0, 0, int.MaxValue);

        var kvar = grans - ackumuleradeDagar;

        if (kvar <= KonverteringDagarKvar)
            return new LASAlertBedomning(LASAlertNiva.Konvertering, grans, grans - KonverteringDagarKvar, kvar);
        if (kvar <= MycketKritiskDagarKvar)
            return new LASAlertBedomning(LASAlertNiva.MycketKritisk, grans, grans - MycketKritiskDagarKvar, kvar);
        if (kvar <= KritiskDagarKvar)
            return new LASAlertBedomning(LASAlertNiva.Kritisk, grans, grans - KritiskDagarKvar, kvar);
        if (kvar <= VarningDagarKvar)
            return new LASAlertBedomning(LASAlertNiva.Varning, grans, grans - VarningDagarKvar, kvar);

        return new LASAlertBedomning(LASAlertNiva.Ingen, grans, 0, kvar);
    }

    /// <summary>
    /// Välj mottagare för ett LAS-larm: HR-funktionen och (om satt) den anställdes chef.
    /// Den anställde själv exkluderas ALLTID — larmet ska aldrig gå till den bevakade personen.
    /// </summary>
    public static IReadOnlySet<Guid> ValjMottagare(IEnumerable<Guid> hrMottagare, Guid? chefId, Guid anstalldId)
    {
        var set = new HashSet<Guid>(hrMottagare);
        if (chefId is { } chef)
            set.Add(chef);
        set.Remove(anstalldId);
        return set;
    }
}
