namespace RegionHR.Reporting.Engine;

/// <summary>
/// Resultatet av en exekverad rapportdefinition: kolumnrubriker + rader (som strängar,
/// redan formaterade och grupperade enligt <see cref="ReportQuerySpec"/>).
/// Rent presentationsobjekt — ingen EF- eller UI-koppling.
/// </summary>
public sealed class ReportResult
{
    /// <summary>Kolumnrubriker i utdataordning.</summary>
    public IReadOnlyList<string> Rubriker { get; }

    /// <summary>Datarader; varje rad har lika många celler som <see cref="Rubriker"/>.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rader { get; }

    /// <summary>Antal rader i utdatan (efter filter och ev. gruppering).</summary>
    public int AntalRader => Rader.Count;

    /// <summary>True om resultatet grupperats (aggregerats) enligt definitionens gruppering.</summary>
    public bool ArGrupperad { get; }

    public ReportResult(
        IReadOnlyList<string> rubriker,
        IReadOnlyList<IReadOnlyList<string>> rader,
        bool arGrupperad)
    {
        Rubriker = rubriker;
        Rader = rader;
        ArGrupperad = arGrupperad;
    }

    public static ReportResult Tom(IReadOnlyList<string>? rubriker = null) =>
        new(rubriker ?? [], [], false);
}
