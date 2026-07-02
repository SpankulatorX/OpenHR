namespace RegionHR.Scheduling.Domain;

/// <summary>Underlag för en dag: schemalagda timmar och (om instämplat) faktiska timmar.</summary>
public sealed record FlexDagsunderlag(DateOnly Datum, decimal PlaneradeTimmar, decimal? FaktiskaTimmar);

/// <summary>Beräknad flexpost för en enskild dag.</summary>
public sealed record FlexDagspost(
    DateOnly Datum,
    decimal PlaneradeTimmar,
    decimal FaktiskaTimmar,
    decimal FlexDelta,
    bool Kapad);

/// <summary>Resultat av en flexberäkning över en period.</summary>
public sealed record FlexBerakningsResultat(
    decimal IngaendeSaldo,
    decimal RaFlexforandring,
    decimal UtgaendeSaldo,
    bool NaddeOvreGrans,
    bool NaddeUndreGrans,
    IReadOnlyList<FlexDagspost> Dagsposter);

/// <summary>
/// Ren (sidoeffektfri) beräkning av flexsaldo ur stämplingsunderlag jämfört med schemalagd tid.
///
/// Regler:
/// - Flexförändring per dag = faktiska timmar − planerade timmar. Positivt = arbetat mer (flex+).
/// - Endast dagar med instämplad faktisk tid påverkar saldot; ostämplade (framtida/ej rapporterade) dagar hoppas över.
/// - Dagsförändringen kapas till ±<see cref="FlexInstallning.DagligFlexgransTimmar"/> (0 = ingen dagsgräns).
/// - Utgående saldo klampas till [<see cref="FlexInstallning.MaxMinusTimmar"/>, <see cref="FlexInstallning.MaxPlusTimmar"/>].
///   MaxPlus = 0 tolkas som "ingen övre gräns".
/// - Är flex inte aktiverad påverkas saldot inte alls.
/// </summary>
public static class FlexCalculator
{
    public static FlexBerakningsResultat Berakna(
        decimal ingaendeSaldo,
        IEnumerable<FlexDagsunderlag> dagar,
        FlexInstallning installning)
    {
        ArgumentNullException.ThrowIfNull(dagar);
        ArgumentNullException.ThrowIfNull(installning);

        var ingaende = Math.Round(ingaendeSaldo, 2);

        if (!installning.FlexAktiverad)
        {
            return new FlexBerakningsResultat(
                ingaende, 0m, ingaende, false, false, Array.Empty<FlexDagspost>());
        }

        var poster = new List<FlexDagspost>();
        var summa = 0m;
        var cap = installning.DagligFlexgransTimmar;

        foreach (var dag in dagar.Where(d => d.FaktiskaTimmar.HasValue).OrderBy(d => d.Datum))
        {
            var faktiska = dag.FaktiskaTimmar!.Value;
            var delta = faktiska - dag.PlaneradeTimmar;
            var kapad = false;

            if (cap > 0m)
            {
                if (delta > cap) { delta = cap; kapad = true; }
                else if (delta < -cap) { delta = -cap; kapad = true; }
            }

            delta = Math.Round(delta, 2);
            summa += delta;
            poster.Add(new FlexDagspost(dag.Datum, dag.PlaneradeTimmar, faktiska, delta, kapad));
        }

        summa = Math.Round(summa, 2);
        var raSaldo = ingaende + summa;
        var utgaende = raSaldo;
        var naddeOvre = false;
        var naddeUndre = false;

        if (installning.MaxPlusTimmar > 0m && utgaende > installning.MaxPlusTimmar)
        {
            utgaende = installning.MaxPlusTimmar;
            naddeOvre = true;
        }

        if (utgaende < installning.MaxMinusTimmar)
        {
            utgaende = installning.MaxMinusTimmar;
            naddeUndre = true;
        }

        return new FlexBerakningsResultat(
            ingaende,
            summa,
            Math.Round(utgaende, 2),
            naddeOvre,
            naddeUndre,
            poster);
    }
}
