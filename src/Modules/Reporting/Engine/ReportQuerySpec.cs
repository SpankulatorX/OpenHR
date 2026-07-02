using System.Text.Json;

namespace RegionHR.Reporting.Engine;

/// <summary>Ett enkelt likhetsfilter: kolumn = värde.</summary>
public sealed record ReportFilter(string Kolumn, string Varde);

/// <summary>
/// En parsad, körbar rapportspecifikation som byggts ur en sparad
/// <c>ReportDefinition</c> (datakälla + kolumn/filter/gruppering-JSON).
/// Ren domänlogik utan EF-beroende så att den kan enhetstestas.
/// </summary>
public sealed class ReportQuerySpec
{
    /// <summary>Logiskt datakällenamn, t.ex. "Anstallda" eller "Lonekorngar".</summary>
    public string Datakalla { get; }

    /// <summary>Valda kolumner i vald ordning.</summary>
    public IReadOnlyList<string> Kolumner { get; }

    /// <summary>Aktiva likhetsfilter (tomma/"Alla" har redan rensats bort).</summary>
    public IReadOnlyList<ReportFilter> Filter { get; }

    /// <summary>Kolumn att gruppera/aggregera på, eller null för radnivå.</summary>
    public string? Gruppering { get; }

    /// <summary>Visualiseringstyp (Table/Bar/Line/Pie) — påverkar inte query, endast presentation.</summary>
    public string VisualiseringsTyp { get; }

    public ReportQuerySpec(
        string datakalla,
        IReadOnlyList<string> kolumner,
        IReadOnlyList<ReportFilter> filter,
        string? gruppering,
        string visualiseringsTyp)
    {
        Datakalla = datakalla;
        Kolumner = kolumner;
        Filter = filter;
        Gruppering = string.IsNullOrWhiteSpace(gruppering) ? null : gruppering;
        VisualiseringsTyp = string.IsNullOrWhiteSpace(visualiseringsTyp) ? "Table" : visualiseringsTyp;
    }

    /// <summary>
    /// Bygger en spec ur de persisterade fälten på en ReportDefinition.
    /// <paramref name="kolumnerJson"/> förväntas vara en JSON-array av strängar.
    /// <paramref name="filterJson"/> förväntas vara ett JSON-objekt, t.ex.
    /// <c>{"Enhet":"IVA","Status":"Aktiv"}</c>. "Status" mappas till den interna
    /// filterkolumnen "Anstallningsstatus" för att inte krocka med datakällor som
    /// har en egen "Status"-kolumn.
    /// </summary>
    public static ReportQuerySpec FranDefinition(
        string? datakalla,
        string? kolumnerJson,
        string? filterJson,
        string? gruppering,
        string? visualiseringsTyp)
    {
        var kolumner = ParseKolumner(kolumnerJson);
        var filter = ParseFilter(filterJson);
        return new ReportQuerySpec(
            datakalla ?? "",
            kolumner,
            filter,
            gruppering,
            visualiseringsTyp ?? "Table");
    }

    private static List<string> ParseKolumner(string? json)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                }
            }
        }
        catch (JsonException) { /* korrupt JSON → tomma kolumner */ }
        return result;
    }

    private static List<ReportFilter> ParseFilter(string? json)
    {
        var result = new List<ReportFilter>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var varde = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(varde) ||
                    string.Equals(varde, "Alla", StringComparison.OrdinalIgnoreCase))
                    continue;

                // "Status" i byggarens filter avser anställningsstatus (Aktiv/Avslutad).
                var kolumn = prop.Name.Equals("Status", StringComparison.OrdinalIgnoreCase)
                    ? "Anstallningsstatus"
                    : prop.Name;

                result.Add(new ReportFilter(kolumn, varde));
            }
        }
        catch (JsonException) { /* korrupt JSON → inga filter */ }
        return result;
    }
}
