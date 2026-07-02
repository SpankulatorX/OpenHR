namespace RegionHR.Payroll.Domain;

/// <summary>
/// Totala kommunala skattesatser (kommunalskatt + regionskatt, exkl. begravnings- och
/// kyrkoavgift), årsversionerat per kommun. Kommunalskatten är INTE platt utan hämtas
/// ur denna tabell — default är Örebro (Region Örebro län).
///
/// Källa: SCB/Skatteverket "Totala kommunala skattesatser 2026, kommunvis" (verifierad 2026-07).
/// Samtliga kommuner nedan ligger i Region Örebro län och betalar regionskatt 12,30 %.
///
/// Notera: den fulla inkomstskatteberäkningen i lönemotorn använder Skatteverkets
/// skattetabell (som redan väger in kommunalskatt, grundavdrag, jobbskatteavdrag och
/// statlig skatt). Dessa satser används för (a) att härleda rätt tabellnummer och
/// (b) den förenklade kostnadskalkylen i <c>SwedishTaxCalculator</c>.
/// </summary>
public static class KommunSkattesatser
{
    /// <summary>Standardkommun när inget annat anges.</summary>
    public const string DefaultKommun = "Örebro";

    /// <summary>Regionskatt Region Örebro län 2026 (12,30 %).</summary>
    public const decimal RegionOrebroLan2026 = 0.1230m;

    /// <summary>Örebro kommun total skattesats 2026 (kommun 21,35 % + region 12,30 % = 33,65 %).</summary>
    public const decimal OrebroTotal2026 = 0.3365m;

    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, decimal>> Tabell =
        new Dictionary<int, IReadOnlyDictionary<string, decimal>>
        {
            [2026] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Örebro"] = 0.3365m,      // kommun 21,35 + region 12,30
                ["Kumla"] = 0.3384m,
                ["Hallsberg"] = 0.3385m,
                ["Askersund"] = 0.3415m,
                ["Karlskoga"] = 0.3430m,
                ["Lindesberg"] = 0.3460m,
            },
        };

    /// <summary>
    /// Total kommunal skattesats (fraktion, t.ex. 0,3365) för angiven kommun och år.
    /// Okänd kommun faller tillbaka på <see cref="DefaultKommun"/>.
    /// </summary>
    public static decimal ForKommun(string? kommun, int ar)
    {
        var yearTable = NarmasteAr(ar);
        if (kommun is not null && yearTable.TryGetValue(kommun, out var sats))
            return sats;
        return yearTable[DefaultKommun];
    }

    /// <summary>
    /// Skatteverkets tabellnummer = total kommunalskatt avrundad till hel procent.
    /// Örebro 33,65 % → tabell 34.
    /// </summary>
    public static int Tabellnummer(string? kommun, int ar)
        => (int)Math.Round(ForKommun(kommun, ar) * 100m, MidpointRounding.AwayFromZero);

    /// <summary>Alla kommuner som finns i tabellen för angivet år (närmaste kända år).</summary>
    public static IReadOnlyCollection<string> Kommuner(int ar) => NarmasteAr(ar).Keys.ToList();

    private static IReadOnlyDictionary<string, decimal> NarmasteAr(int ar)
    {
        if (Tabell.TryGetValue(ar, out var t))
            return t;
        var senaste = Tabell.Keys.Where(y => y <= ar).DefaultIfEmpty(Tabell.Keys.Min()).Max();
        return Tabell[senaste];
    }
}
