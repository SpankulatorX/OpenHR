using RegionHR.HalsoSAM.Domain;

namespace RegionHR.HalsoSAM.Services;

/// <summary>
/// Domänbedömning av var ett sjukfall befinner sig i förhållande till
/// Försäkringskasse-anmälan (dag 15), sjuklöneperioden (14 dagar) och
/// läkarintygskravet (dag 8).
///
/// Ren funktion utan sidoeffekter: driver HälsoSAM-UI:t (om/när knappen
/// "Generera FK-anmälan" ska visas och vilka statusmarkeringar som gäller) och
/// testas utan databas. Trösklarna hämtas från <see cref="Rehabkedja"/> så att
/// det finns en enda källa till sanning för regelvärdena.
/// </summary>
public sealed record FKAnmalanBedomning(
    DateOnly ForstaSjukdag,
    DateOnly ReferensDatum,
    int AntalKalenderdagar,
    DateOnly SjuklonePeriodFran,
    DateOnly SjuklonePeriodTill,
    DateOnly ForsakringskassanFranDatum,
    DateOnly LakarintygKravsFranDatum,
    bool ArFKAnmalanAktuell,
    bool LakarintygKravs,
    bool LakarintygSaknasVarning)
{
    /// <summary>
    /// Bedömer ett sjukfall utifrån första sjukdagen. <paramref name="sistaSjukdag"/>
    /// null = pågående (referensdatum blir <paramref name="idag"/>).
    /// <paramref name="lakarintygFinns"/> styr om läkarintygsvarningen ska flaggas.
    /// </summary>
    public static FKAnmalanBedomning For(
        DateOnly forstaSjukdag,
        DateOnly? sistaSjukdag,
        DateOnly idag,
        bool lakarintygFinns = false)
    {
        var referens = sistaSjukdag ?? idag;
        if (referens < forstaSjukdag) referens = forstaSjukdag;

        var antalDagar = referens.DayNumber - forstaSjukdag.DayNumber + 1;

        // Sjuklöneperioden = dag 1 t.o.m. dag 14; FK tar vid från dag 15.
        var sjuklonFran = forstaSjukdag;
        var sjuklonTill = forstaSjukdag.AddDays(Rehabkedja.ForsakringskassanAnmalanFranDag - 2); // dag 14
        var fkFran = forstaSjukdag.AddDays(Rehabkedja.ForsakringskassanAnmalanFranDag - 1);        // dag 15
        var lakarintygKravsFran = forstaSjukdag.AddDays(Rehabkedja.LakarintygFranDag - 1);         // dag 8

        var arFKAktuell = antalDagar >= Rehabkedja.ForsakringskassanAnmalanFranDag;
        var lakarintygKravs = antalDagar >= Rehabkedja.LakarintygFranDag;

        return new FKAnmalanBedomning(
            ForstaSjukdag: forstaSjukdag,
            ReferensDatum: referens,
            AntalKalenderdagar: antalDagar,
            SjuklonePeriodFran: sjuklonFran,
            SjuklonePeriodTill: sjuklonTill,
            ForsakringskassanFranDatum: fkFran,
            LakarintygKravsFranDatum: lakarintygKravsFran,
            ArFKAnmalanAktuell: arFKAktuell,
            LakarintygKravs: lakarintygKravs,
            LakarintygSaknasVarning: lakarintygKravs && !lakarintygFinns);
    }

    /// <summary>
    /// Bekvämlighetsöverlagring som bedömer utifrån ett rehabärendes
    /// <see cref="RehabCase.SjukfallDag1"/>. Returnerar null om dag 1 saknas.
    /// </summary>
    public static FKAnmalanBedomning? ForRehabCase(
        RehabCase rehabCase, DateOnly idag, bool lakarintygFinns = false)
    {
        ArgumentNullException.ThrowIfNull(rehabCase);
        return rehabCase.SjukfallDag1 is { } dag1
            ? For(dag1, sistaSjukdag: null, idag, lakarintygFinns)
            : null;
    }
}
