using System.Globalization;

namespace RegionHR.Reporting.Engine;

/// <summary>
/// Exekverar en <see cref="ReportQuerySpec"/> mot en redan materialiserad radmängd
/// (rad = ordbok kolumnnamn → värde). Ren LINQ-to-objects: filter → ev. gruppering
/// (aggregering) → kolumnprojektion → formaterade strängar. Inget EF-beroende, helt
/// enhetstestbart. Infrastrukturlagret ansvarar för att ladda raderna ur databasen.
/// </summary>
public sealed class ReportQueryEngine
{
    public const string AntalKolumnRubrik = "Antal";

    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public ReportResult Execute(
        ReportQuerySpec spec,
        IEnumerable<IReadOnlyDictionary<string, object?>> rader)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(rader);

        var rows = rader.ToList();

        // 1. Filter — ett filter tillämpas bara om kolumnen faktiskt finns i datakällan.
        foreach (var filter in spec.Filter)
        {
            var kolumnFinns = rows.Any(r => HarKolumn(r, filter.Kolumn));
            if (!kolumnFinns) continue; // irrelevant filter för denna datakälla → ignorera

            rows = rows.Where(r =>
                HamtaVarde(r, filter.Kolumn) is { } v &&
                string.Equals(Formatera(v), filter.Varde, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // 2. Gruppering (om satt OCH kolumnen finns i datakällan) → aggregering.
        if (spec.Gruppering is { } grp && rows.Any(r => HarKolumn(r, grp)))
        {
            return Gruppera(spec, rows, grp);
        }

        // 3. Radnivå — projicera valda kolumner (eller alla om inga valts).
        var kolumner = spec.Kolumner.Count > 0
            ? spec.Kolumner
            : rows.SelectMany(r => r.Keys).Distinct(KeyComparer).ToList();

        var utdata = new List<IReadOnlyList<string>>(rows.Count);
        foreach (var r in rows)
        {
            var cell = new List<string>(kolumner.Count);
            foreach (var k in kolumner)
                cell.Add(Formatera(HamtaVarde(r, k)));
            utdata.Add(cell);
        }

        return new ReportResult(kolumner.ToList(), utdata, arGrupperad: false);
    }

    private static ReportResult Gruppera(
        ReportQuerySpec spec,
        List<IReadOnlyDictionary<string, object?>> rows,
        string grupperingsKolumn)
    {
        // Numeriska valda kolumner summeras per grupp. Grupperingskolumnen själv summeras aldrig.
        var numeriskaKolumner = spec.Kolumner
            .Where(k => !string.Equals(k, grupperingsKolumn, StringComparison.OrdinalIgnoreCase))
            .Where(k => rows.Any(r => ArNumeriskt(HamtaVarde(r, k))))
            .ToList();

        var rubriker = new List<string> { grupperingsKolumn, AntalKolumnRubrik };
        rubriker.AddRange(numeriskaKolumner);

        var grupper = rows
            .GroupBy(r => Formatera(HamtaVarde(r, grupperingsKolumn)))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var utdata = new List<IReadOnlyList<string>>();
        foreach (var g in grupper)
        {
            var cell = new List<string> { g.Key, g.Count().ToString(CultureInfo.InvariantCulture) };
            foreach (var k in numeriskaKolumner)
            {
                var summa = g.Sum(r => TillDecimal(HamtaVarde(r, k)));
                cell.Add(FormateraTal(summa));
            }
            utdata.Add(cell);
        }

        return new ReportResult(rubriker, utdata, arGrupperad: true);
    }

    private static bool HarKolumn(IReadOnlyDictionary<string, object?> rad, string kolumn)
    {
        // Ordboken kan vara skiftlägeskänslig; testa både direkt och skiftlägesokänsligt.
        if (rad.ContainsKey(kolumn)) return true;
        return rad.Keys.Any(k => KeyComparer.Equals(k, kolumn));
    }

    private static object? HamtaVarde(IReadOnlyDictionary<string, object?> rad, string kolumn)
    {
        if (rad.TryGetValue(kolumn, out var v)) return v;
        foreach (var kvp in rad)
            if (KeyComparer.Equals(kvp.Key, kolumn)) return kvp.Value;
        return null;
    }

    private static bool ArNumeriskt(object? v) => v is decimal or double or float or int or long or short;

    private static decimal TillDecimal(object? v) => v switch
    {
        decimal d => d,
        double db => (decimal)db,
        float f => (decimal)f,
        int i => i,
        long l => l,
        short s => s,
        _ => 0m
    };

    /// <summary>Kulturoberoende cellformatering (deterministisk för både UI, export och tester).</summary>
    public static string Formatera(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "Ja" : "Nej",
        decimal or double or float or int or long or short => FormateraTal(TillDecimal(v)),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    private static string FormateraTal(decimal d) =>
        d == Math.Truncate(d)
            ? d.ToString("0", CultureInfo.InvariantCulture)
            : d.ToString("0.##", CultureInfo.InvariantCulture);
}
