using RegionHR.Reporting.Domain;
using RegionHR.Reporting.Engine;
using Xunit;

namespace RegionHR.Reporting.Tests;

public class ReportQueryEngineTests
{
    private static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] kv)
        => kv.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    // ---------- Spec-parsning ----------

    [Fact]
    public void FranDefinition_ParsesColumns_AndFilters()
    {
        var spec = ReportQuerySpec.FranDefinition(
            "Anstallda",
            "[\"Fornamn\",\"Efternamn\",\"Enhet\"]",
            "{\"Enhet\":\"IVA\",\"Status\":\"Aktiv\"}",
            "Enhet",
            "Bar");

        Assert.Equal("Anstallda", spec.Datakalla);
        Assert.Equal(new[] { "Fornamn", "Efternamn", "Enhet" }, spec.Kolumner);
        Assert.Equal("Enhet", spec.Gruppering);
        Assert.Equal("Bar", spec.VisualiseringsTyp);

        // Enhet-filtret behålls, Status mappas till Anstallningsstatus.
        Assert.Contains(spec.Filter, f => f.Kolumn == "Enhet" && f.Varde == "IVA");
        Assert.Contains(spec.Filter, f => f.Kolumn == "Anstallningsstatus" && f.Varde == "Aktiv");
    }

    [Fact]
    public void FranDefinition_SkipsAllaAndNullFilters()
    {
        var spec = ReportQuerySpec.FranDefinition(
            "Anstallda", "[]", "{\"Enhet\":\"Alla\",\"Status\":null}", null, null);

        Assert.Empty(spec.Filter);
        Assert.Null(spec.Gruppering);
        Assert.Equal("Table", spec.VisualiseringsTyp); // default
    }

    [Fact]
    public void FranDefinition_CorruptJson_YieldsEmpty()
    {
        var spec = ReportQuerySpec.FranDefinition("X", "not json", "also not json", null, null);
        Assert.Empty(spec.Kolumner);
        Assert.Empty(spec.Filter);
    }

    // ---------- Projektion (radnivå) ----------

    [Fact]
    public void Execute_ProjectsSelectedColumns_InOrder()
    {
        var spec = new ReportQuerySpec("Anstallda", new[] { "Efternamn", "Fornamn" }, [], null, "Table");
        var rader = new[]
        {
            Row(("Fornamn", "Anna"), ("Efternamn", "Svensson"), ("Enhet", "IVA")),
            Row(("Fornamn", "Erik"), ("Efternamn", "Johansson"), ("Enhet", "Avd 32")),
        };

        var result = new ReportQueryEngine().Execute(spec, rader);

        Assert.False(result.ArGrupperad);
        Assert.Equal(new[] { "Efternamn", "Fornamn" }, result.Rubriker);
        Assert.Equal(2, result.AntalRader);
        Assert.Equal(new[] { "Svensson", "Anna" }, result.Rader[0]);
        Assert.Equal(new[] { "Johansson", "Erik" }, result.Rader[1]);
    }

    [Fact]
    public void Execute_MissingColumn_YieldsEmptyCell()
    {
        var spec = new ReportQuerySpec("X", new[] { "Fornamn", "Saknas" }, [], null, "Table");
        var result = new ReportQueryEngine().Execute(spec, new[] { Row(("Fornamn", "Anna")) });

        Assert.Equal(new[] { "Anna", "" }, result.Rader[0]);
    }

    // ---------- Filter ----------

    [Fact]
    public void Execute_AppliesEqualityFilter()
    {
        var spec = new ReportQuerySpec("Anstallda", new[] { "Fornamn", "Enhet" },
            new[] { new ReportFilter("Enhet", "IVA") }, null, "Table");
        var rader = new[]
        {
            Row(("Fornamn", "Anna"), ("Enhet", "IVA")),
            Row(("Fornamn", "Erik"), ("Enhet", "Avd 32")),
            Row(("Fornamn", "Sara"), ("Enhet", "IVA")),
        };

        var result = new ReportQueryEngine().Execute(spec, rader);

        Assert.Equal(2, result.AntalRader);
        Assert.All(result.Rader, r => Assert.Equal("IVA", r[1]));
    }

    [Fact]
    public void Execute_IgnoresFilterWhenColumnAbsentFromDataSource()
    {
        // Filtret gäller "Anstallningsstatus" men datakällan (Schema) saknar den kolumnen.
        var spec = new ReportQuerySpec("Schema", new[] { "Datum" },
            new[] { new ReportFilter("Anstallningsstatus", "Aktiv") }, null, "Table");
        var rader = new[]
        {
            Row(("Datum", new DateOnly(2026, 3, 1))),
            Row(("Datum", new DateOnly(2026, 3, 2))),
        };

        var result = new ReportQueryEngine().Execute(spec, rader);

        Assert.Equal(2, result.AntalRader); // inget filtrerades bort
    }

    // ---------- Gruppering / aggregering ----------

    [Fact]
    public void Execute_GroupsAndSumsNumericColumns()
    {
        var spec = new ReportQuerySpec("Lonekorngar", new[] { "Enhet", "Brutto" }, [], "Enhet", "Bar");
        var rader = new[]
        {
            Row(("Enhet", "IVA"), ("Brutto", 100m)),
            Row(("Enhet", "IVA"), ("Brutto", 50m)),
            Row(("Enhet", "Avd 32"), ("Brutto", 200m)),
        };

        var result = new ReportQueryEngine().Execute(spec, rader);

        Assert.True(result.ArGrupperad);
        Assert.Equal(new[] { "Enhet", "Antal", "Brutto" }, result.Rubriker);
        Assert.Equal(2, result.AntalRader);
        // Sorterat på gruppnyckel: "Avd 32" före "IVA".
        Assert.Equal(new[] { "Avd 32", "1", "200" }, result.Rader[0]);
        Assert.Equal(new[] { "IVA", "2", "150" }, result.Rader[1]);
    }

    [Fact]
    public void Execute_GroupingIgnoredWhenColumnMissing()
    {
        var spec = new ReportQuerySpec("X", new[] { "Fornamn" }, [], "Enhet", "Table");
        var result = new ReportQueryEngine().Execute(spec, new[] { Row(("Fornamn", "Anna")) });

        Assert.False(result.ArGrupperad); // "Enhet" saknas → faller tillbaka till radnivå
        Assert.Equal(1, result.AntalRader);
    }

    // ---------- Formatering ----------

    [Theory]
    [InlineData(null, "")]
    [InlineData(true, "Ja")]
    [InlineData(false, "Nej")]
    public void Formatera_HandlesPrimitives(object? input, string expected)
        => Assert.Equal(expected, ReportQueryEngine.Formatera(input));

    [Fact]
    public void Formatera_NumbersAndDates_AreCultureInvariant()
    {
        Assert.Equal("30000", ReportQueryEngine.Formatera(30000m));
        Assert.Equal("30000.5", ReportQueryEngine.Formatera(30000.5m));
        Assert.Equal("2026-03-01", ReportQueryEngine.Formatera(new DateOnly(2026, 3, 1)));
        Assert.Equal("2026-03-01", ReportQueryEngine.Formatera(new DateTime(2026, 3, 1, 12, 0, 0)));
    }

    // ---------- ReportDefinition.Datakalla ----------

    [Fact]
    public void SattRapportmall_PersistsDatakalla()
    {
        var def = ReportDefinition.Skapa("Egen rapport", "", ReportType.AdHoc);
        def.SattRapportmall("Anstallda", "[\"Fornamn\"]", "{}", "Enhet", "Bar");

        Assert.Equal("Anstallda", def.Datakalla);
        Assert.Equal("Enhet", def.Gruppering);
        Assert.Equal("Bar", def.VisualiseringsTyp);
    }
}
