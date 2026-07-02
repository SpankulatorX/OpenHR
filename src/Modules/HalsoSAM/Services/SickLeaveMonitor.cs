using RegionHR.SharedKernel.Domain;
using RegionHR.HalsoSAM.Domain;

namespace RegionHR.HalsoSAM.Services;

/// <summary>
/// En detekterad rehab-signal: vilken trigger som slog till OCH vilket datum som är
/// sjukfallets dag 1. Dag 1 driver milstolpe-beräkningen i <see cref="RehabCase"/>.
/// </summary>
public sealed record RehabSignal(RehabTrigger Trigger, DateOnly SjukfallDag1);

/// <summary>
/// Bevakar sjukfrånvaromönster och triggar rehabiliteringsärenden.
/// Trösklar är årsversionerade i <see cref="Rehabkedja"/>.
/// </summary>
public sealed class SickLeaveMonitor
{
    private static readonly int MAX_TILLFALLEN_12_MANADER = Rehabkedja.AutoTriggerAntalTillfallen;
    private static readonly int MAX_SAMMANHANGANDE_DAGAR = Rehabkedja.AutoTriggerSammanhangandeDagar;
    private static readonly int FONSTER_MANADER = Rehabkedja.AutoTriggerTillfallenFonsterManader;

    public RehabTrigger? Analysera(IReadOnlyList<SjukfranvaroPeriod> perioder)
    {
        if (perioder.Count == 0) return null;

        var senasteTolvManader = perioder
            .Where(p => p.StartDatum >= DateOnly.FromDateTime(DateTime.Today.AddMonths(-FONSTER_MANADER)))
            .ToList();

        // Kontrollera 6+ tillfällen
        if (senasteTolvManader.Count >= MAX_TILLFALLEN_12_MANADER)
            return RehabTrigger.SexTillfallenTolvManader;

        // Kontrollera 14+ sammanhängande dagar
        var langstaPeriod = perioder.Max(p => p.AntalDagar);
        if (langstaPeriod >= MAX_SAMMANHANGANDE_DAGAR)
            return RehabTrigger.FjortonSammanhangandeDagar;

        // Mönsterdetektering (förenklad: >50% av sjukfrånvaron samma veckodag).
        // Kräver ett minsta antal tillfällen för att över huvud taget vara ett "mönster" —
        // annars slår regeln till redan vid ett enstaka sjuktillfälle.
        const int MIN_TILLFALLEN_FOR_MONSTER = 4;
        if (senasteTolvManader.Count >= MIN_TILLFALLEN_FOR_MONSTER)
        {
            var dagarPerVeckodag = senasteTolvManader
                .GroupBy(p => p.StartDatum.DayOfWeek)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (dagarPerVeckodag is not null && dagarPerVeckodag.Count() > senasteTolvManader.Count * 0.5)
                return RehabTrigger.MonsterDetekterat;
        }

        return null;
    }

    /// <summary>
    /// Som <see cref="Analysera"/>, men returnerar även sjukfallets dag 1 så att
    /// rehabkedjans milstolpar kan förankras rätt. Dag 1 väljs så att den blir
    /// rättsligt försvarbar:
    ///   • Sammanhängande långtidsfrånvaro → första dagen i den kvalificerande perioden.
    ///   • Upprepade tillfällen / mönster → senaste sjuktillfällets startdatum inom fönstret
    ///     (den tidpunkt då utredningsbehovet utlöstes).
    /// </summary>
    public RehabSignal? AnalyseraSignal(IReadOnlyList<SjukfranvaroPeriod> perioder)
    {
        var trigger = Analysera(perioder);
        if (trigger is null) return null;
        return new RehabSignal(trigger.Value, BestamSjukfallDag1(perioder, trigger.Value));
    }

    private static DateOnly BestamSjukfallDag1(IReadOnlyList<SjukfranvaroPeriod> perioder, RehabTrigger trigger)
    {
        if (trigger == RehabTrigger.FjortonSammanhangandeDagar)
        {
            // Första dagen i den längsta (kvalificerande) sammanhängande perioden.
            return perioder
                .Where(p => p.AntalDagar >= MAX_SAMMANHANGANDE_DAGAR)
                .OrderByDescending(p => p.AntalDagar)
                .ThenBy(p => p.StartDatum)
                .First()
                .StartDatum;
        }

        // Upprepade tillfällen eller mönster: senaste tillfället inom 12-månadersfönstret.
        var granodatum = DateOnly.FromDateTime(DateTime.Today.AddMonths(-FONSTER_MANADER));
        return perioder
            .Where(p => p.StartDatum >= granodatum)
            .OrderByDescending(p => p.StartDatum)
            .First()
            .StartDatum;
    }
}

public sealed class SjukfranvaroPeriod
{
    public DateOnly StartDatum { get; set; }
    public DateOnly SlutDatum { get; set; }
    public int AntalDagar => SlutDatum.DayNumber - StartDatum.DayNumber + 1;
}
