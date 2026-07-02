using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>
/// Beräknar avgiftsbestämd tjänstepension enligt <b>AKAP-KR</b> (Avgiftsbestämd
/// KollektivAvtalad Pension – Kommuner och Regioner), det pensionsavtal som gäller
/// för anställda i kommun och region sedan 2023-01-01.
///
/// Premien som arbetsgivaren betalar in:
/// <list type="bullet">
///   <item><description><b>6,0 %</b> på pensionsgrundande lön <i>upp till</i> 7,5 inkomstbasbelopp (IBB).</description></item>
///   <item><description><b>31,5 %</b> på pensionsgrundande lön <i>över</i> 7,5 IBB, upp till taket 30 IBB.</description></item>
///   <item><description><b>0 %</b> på lön över 30 IBB (inget premiegrundande belopp därutöver).</description></item>
/// </list>
///
/// Verifierat mot Pensionsmyndigheten/SKR för inkomståret 2026:
/// IBB 2026 = 83 400 kr (SFS 2025:1002) → 7,5 IBB = 625 500 kr/år (52 125 kr/mån),
/// 30 IBB = 2 502 000 kr/år (208 500 kr/mån).
///
/// Klassen är en ren beräkningsmotor utan sidoeffekter. Själva inrapporteringen till
/// vald pensionsleverantör (t.ex. KPA eller Valcentralen/Pensionsvalet) sker via
/// <c>RegionHR.Infrastructure.Integrations.PensionFileGenerator</c> och kräver tecknat
/// avtal + leverantörens formatspecifikation för skarp filöverföring.
/// </summary>
public sealed class PensionsberakningsEngine
{
    /// <summary>Premiesats under brytpunkten (6,0 %).</summary>
    public const decimal PremiesatsUnderGrans = 0.06m;

    /// <summary>Premiesats över brytpunkten (31,5 %).</summary>
    public const decimal PremiesatsOverGrans = 0.315m;

    /// <summary>Brytpunkt mätt i inkomstbasbelopp (7,5 IBB).</summary>
    public const decimal GransIBB = 7.5m;

    /// <summary>Övre premietak mätt i inkomstbasbelopp (30 IBB).</summary>
    public const decimal TakIBB = 30m;

    /// <summary>
    /// Inkomstbasbelopp (IBB) per inkomstår. Värden fastställda av regeringen.
    /// Uppdatera årligen; okända framtida år faller tillbaka på senast kända värde.
    /// </summary>
    public static decimal Inkomstbasbelopp(int year) => year switch
    {
        <= 2024 => 76_200m,   // IBB 2024
        2025 => 80_600m,      // IBB 2025
        2026 => 83_400m,      // IBB 2026 (SFS 2025:1002)
        _ => 83_400m          // Framtida år: använd senast kända — bör uppdateras årligen
    };

    /// <summary>
    /// Beräkna årspremie utifrån pensionsgrundande <b>årslön</b>.
    /// Brytpunkt 7,5 IBB och tak 30 IBB tillämpas på helårsbasis.
    /// </summary>
    public PensionPremie BeraknaArspremie(Money pensionsgrundandeArslon, int year)
    {
        var ibb = Inkomstbasbelopp(year);
        return BeraknaPremie(pensionsgrundandeArslon, GransIBB * ibb, TakIBB * ibb, ibb);
    }

    /// <summary>
    /// Beräkna månadspremie utifrån pensionsgrundande lön <b>för en månad</b>.
    /// Brytpunkt (7,5 IBB) och tak (30 IBB) proportioneras till 1/12 så att en jämn
    /// månadslön ger samma årssumma som <see cref="BeraknaArspremie"/> på 12× lönen.
    /// Detta är den beräkning som används för månatlig pensionsredovisning.
    /// </summary>
    public PensionPremie BeraknaManadspremie(Money pensionsgrundandeManadslon, int year)
    {
        var ibb = Inkomstbasbelopp(year);
        return BeraknaPremie(pensionsgrundandeManadslon, GransIBB * ibb / 12m, TakIBB * ibb / 12m, ibb);
    }

    private static PensionPremie BeraknaPremie(Money underlag, decimal gransBelopp, decimal takBelopp, decimal ibb)
    {
        // Negativt underlag (t.ex. korrigeringsrad) ger ingen premie.
        var lon = underlag.Amount < 0m ? 0m : underlag.Amount;

        // Premie beräknas aldrig på lön över 30 IBB-taket.
        var premiegrundande = Math.Min(lon, takBelopp);

        decimal underGrans;
        decimal overGrans;
        if (premiegrundande <= gransBelopp)
        {
            underGrans = premiegrundande * PremiesatsUnderGrans;
            overGrans = 0m;
        }
        else
        {
            underGrans = gransBelopp * PremiesatsUnderGrans;
            overGrans = (premiegrundande - gransBelopp) * PremiesatsOverGrans;
        }

        var premieUnder = Money.SEK(underGrans).RoundToOren();
        var premieOver = Money.SEK(overGrans).RoundToOren();

        return new PensionPremie(
            PensionsgrundandeLon: underlag,
            PremiegrundandeBelopp: Money.SEK(premiegrundande),
            PremieUnderGrans: premieUnder,
            PremieOverGrans: premieOver,
            TotalPremie: premieUnder + premieOver,
            Inkomstbasbelopp: ibb,
            GransBelopp: Money.SEK(gransBelopp),
            TakBelopp: Money.SEK(takBelopp));
    }
}

/// <summary>
/// Resultatet av en AKAP-KR premieberäkning för en anställd och period.
/// Alla belopp i SEK (<see cref="Money"/>).
/// </summary>
/// <param name="PensionsgrundandeLon">Ingående pensionsgrundande lön (månad eller år).</param>
/// <param name="PremiegrundandeBelopp">Del av lönen som premie faktiskt beräknas på (kapat vid 30 IBB-taket).</param>
/// <param name="PremieUnderGrans">Premie på delen under 7,5 IBB (6,0 %).</param>
/// <param name="PremieOverGrans">Premie på delen över 7,5 IBB (31,5 %).</param>
/// <param name="TotalPremie">Summan av premie under och över brytpunkten.</param>
/// <param name="Inkomstbasbelopp">Använt inkomstbasbelopp för perioden.</param>
/// <param name="GransBelopp">Brytpunkten (7,5 IBB) i kronor för perioden.</param>
/// <param name="TakBelopp">Premietaket (30 IBB) i kronor för perioden.</param>
public sealed record PensionPremie(
    Money PensionsgrundandeLon,
    Money PremiegrundandeBelopp,
    Money PremieUnderGrans,
    Money PremieOverGrans,
    Money TotalPremie,
    decimal Inkomstbasbelopp,
    Money GransBelopp,
    Money TakBelopp)
{
    /// <summary>
    /// True om den pensionsgrundande lönen översteg 30 IBB-taket, dvs. premie
    /// beräknades bara på lön upp till taket.
    /// </summary>
    public bool OverstigerTak => PensionsgrundandeLon.Amount > TakBelopp.Amount;
}
