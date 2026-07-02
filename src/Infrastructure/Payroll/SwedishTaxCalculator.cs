using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using AgAvgift = RegionHR.Payroll.Domain.Arbetsgivaravgift;

namespace RegionHR.Infrastructure.Payroll;

/// <summary>
/// Förenklad svensk skatte- och arbetsgivaravgiftskalkyl för översikts- och
/// kostnadsrapporter (t.ex. bemanningsrapporten). Den EXAKTA preliminärskatten per
/// anställd beräknas i lönemotorn via Skatteverkets skattetabell; denna klass ger en
/// snabb approximation för aggregerade kostnadskalkyler.
///
/// Satser verifierade mot Skatteverket 2026:
///  - Kommunalskatt: hämtas per kommun ur <see cref="KommunSkattesatser"/> (default Örebro 33,65 %).
///  - Statlig inkomstskatt: 20 % på beskattningsbar förvärvsinkomst över skiktgränsen 643 000 kr/år.
///  - Arbetsgivaravgift: full 31,42 %, äldre 10,21 %, född 1937 eller tidigare 0 % — via domänen.
/// </summary>
public class SwedishTaxCalculator
{
    /// <summary>Skiktgräns för statlig inkomstskatt 2026 (beskattningsbar förvärvsinkomst per år).</summary>
    public const decimal Skiktgrans2026Arlig = 643000m;

    /// <summary>
    /// Total kommunalskatt (kommun + region) som används som standard. Default = Örebro 2026
    /// (33,65 %). Kommunalskatten är INTE platt utan konfigurerbar per kommun — ange kommun
    /// i <see cref="Berakna"/> eller sätt denna sats för en annan standardkommun.
    /// </summary>
    public decimal KommunalSkatt { get; set; } = KommunSkattesatser.OrebroTotal2026;

    /// <summary>Månadsgräns för statlig inkomstskatt (skiktgräns / 12 ≈ 53 583 kr/mån för 2026).</summary>
    public decimal StatligSkattGrans { get; set; } = Skiktgrans2026Arlig / 12m;

    /// <summary>Statlig inkomstskattesats över skiktgränsen (20 %).</summary>
    public decimal StatligSkattSats { get; set; } = 0.20m;

    /// <summary>Full arbetsgivaravgift 2026 (31,42 %).</summary>
    public decimal Arbetsgivaravgift { get; set; } = AgAvgift.FullSats2026;

    /// <summary>Reducerad avgift för äldre (endast ålderspensionsavgift, 10,21 %).</summary>
    public decimal ReduceradAvgift { get; set; } = AgAvgift.EndastAlderspensionsavgift;

    /// <summary>Total kommunalskatt för en specifik kommun ur den årsversionerade tabellen.</summary>
    public static decimal KommunalSkattForKommun(string? kommun, int ar = 2026)
        => KommunSkattesatser.ForKommun(kommun, ar);

    /// <summary>
    /// Beräkna en förenklad brutto-till-netto samt arbetsgivarkostnad.
    /// </summary>
    /// <param name="brutto">Bruttolön per månad.</param>
    /// <param name="fodelsear">Födelseår (styr arbetsgivaravgift för äldre/ungdom).</param>
    /// <param name="kommun">Kommun för kommunalskatt; null = standardkommunen (<see cref="KommunalSkatt"/>).</param>
    public LoneBerakning Berakna(decimal brutto, int fodelsear = 1985, string? kommun = null)
    {
        var idag = DateTime.Today;
        var kommunalSats = kommun is null ? KommunalSkatt : KommunSkattesatser.ForKommun(kommun, idag.Year);

        var kommunalSkatt = Math.Round(brutto * kommunalSats, 0);
        var statligSkatt = brutto > StatligSkattGrans
            ? Math.Round((brutto - StatligSkattGrans) * StatligSkattSats, 0)
            : 0m;
        var totalSkatt = kommunalSkatt + statligSkatt;
        var netto = brutto - totalSkatt;

        // Arbetsgivaravgift: satsen beror på födelseår + inkomstår/månad (hanterar äldre/ungdom/1937).
        var arbetsgivaravgift = Math.Round(
            AgAvgift.Belopp(Money.SEK(brutto), fodelsear, idag.Year, idag.Month).Amount, 0);
        var semesterTillagg = Math.Round(brutto * 0.0043m * 12, 0); // 0.43% per månad (AB)

        return new LoneBerakning(
            Brutto: brutto,
            KommunalSkatt: kommunalSkatt,
            StatligSkatt: statligSkatt,
            TotalSkatt: totalSkatt,
            Netto: netto,
            Arbetsgivaravgift: arbetsgivaravgift,
            TotalKostnad: brutto + arbetsgivaravgift,
            SemesterTillagg: semesterTillagg,
            Skattesats: Math.Round((totalSkatt / brutto) * 100, 1)
        );
    }
}

public record LoneBerakning(
    decimal Brutto, decimal KommunalSkatt, decimal StatligSkatt,
    decimal TotalSkatt, decimal Netto, decimal Arbetsgivaravgift,
    decimal TotalKostnad, decimal SemesterTillagg, decimal Skattesats);
