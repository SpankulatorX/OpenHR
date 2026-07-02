using Xunit;
using RegionHR.SalaryReview.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.SalaryReview.Tests;

public class SalaryProposalImportParserTests
{
    private readonly SalaryProposalImportParser _parser = new();

    // Giltigt 12-siffrigt personnummer med korrekt kontrollsiffra (format YYYYMMDD-NNNN).
    private static string Pnr(string ymdPlus3) => Personnummer.CreateValidated(ymdPlus3).ToString();

    [Fact]
    public void ParseCsv_med_rubrik_tolkar_alla_giltiga_rader()
    {
        var a = Pnr("19850115123");
        var b = Pnr("19900320456");
        var csv = $"""
            Personnummer;Ny lön;Motivering
            {a};32000;Bra prestation
            {b};41200;Utökat ansvar
            """;

        var res = _parser.ParseCsv(csv);

        Assert.Empty(res.GlobalaFel);
        Assert.Equal(2, res.Rader.Count);
        Assert.Equal(2, res.AntalGiltiga);
        Assert.Equal(0, res.AntalFel);
        Assert.All(res.Rader, r => Assert.True(r.ArGiltig));
        Assert.Equal(32000m, res.Rader[0].NyLon);
        Assert.Equal("Bra prestation", res.Rader[0].Motivering);
        Assert.NotNull(res.Rader[0].Personnummer);
    }

    [Fact]
    public void ParseCsv_utan_rubrik_antar_positionell_ordning()
    {
        var a = Pnr("19850115123");
        var csv = $"{a};30500;Lönejustering";

        var res = _parser.ParseCsv(csv);

        Assert.Single(res.Rader);
        Assert.True(res.Rader[0].ArGiltig);
        Assert.Equal(30500m, res.Rader[0].NyLon);
        Assert.Equal("Lönejustering", res.Rader[0].Motivering);
    }

    [Fact]
    public void ParseCsv_ogiltigt_personnummer_flaggas()
    {
        var csv = """
            Personnummer;Ny lön;Motivering
            12345;32000;Motivering
            """;

        var res = _parser.ParseCsv(csv);

        Assert.Single(res.Rader);
        Assert.False(res.Rader[0].ArGiltig);
        Assert.Contains(res.Rader[0].Fel, f => f.Contains("personnummer", System.StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-500")]
    [InlineData("abc")]
    [InlineData("")]
    public void ParseCsv_ogiltig_eller_ickepositiv_lon_flaggas(string lon)
    {
        var a = Pnr("19850115123");
        var csv = $"Personnummer;Ny lön;Motivering\n{a};{lon};Motivering";

        var res = _parser.ParseCsv(csv);

        Assert.Single(res.Rader);
        Assert.False(res.Rader[0].ArGiltig);
        Assert.Null(res.Rader[0].NyLon);
    }

    [Fact]
    public void ParseCsv_saknad_motivering_flaggas()
    {
        var a = Pnr("19850115123");
        var csv = $"Personnummer;Ny lön;Motivering\n{a};32000;";

        var res = _parser.ParseCsv(csv);

        Assert.False(res.Rader[0].ArGiltig);
        Assert.Contains(res.Rader[0].Fel, f => f.Contains("Motivering"));
    }

    [Fact]
    public void ParseCsv_dubblett_personnummer_flaggar_alla_forekomster()
    {
        var a = Pnr("19850115123");
        var csv = $"""
            Personnummer;Ny lön;Motivering
            {a};32000;Första
            {a};33000;Andra
            """;

        var res = _parser.ParseCsv(csv);

        Assert.Equal(2, res.Rader.Count);
        Assert.Equal(0, res.AntalGiltiga);
        Assert.All(res.Rader, r => Assert.Contains(r.Fel, f => f.Contains("Dubblett")));
    }

    [Theory]
    [InlineData("45000", 45000)]
    [InlineData("45 000", 45000)]     // mellanslag som tusentalsavgränsare
    [InlineData("45000,50", 45000.50)] // svensk decimal
    [InlineData("45.000", 45000)]      // punkt som tusentalsavgränsare
    [InlineData("45 000,50", 45000.50)]
    [InlineData("45000 kr", 45000)]    // valutasuffix
    public void ParseCsv_tolkar_olika_talformat(string lon, decimal forvantad)
    {
        var a = Pnr("19850115123");
        var csv = $"Personnummer;Ny lön;Motivering\n{a};{lon};Motivering";

        var res = _parser.ParseCsv(csv);

        Assert.True(res.Rader[0].ArGiltig, string.Join(" ", res.Rader[0].Fel));
        Assert.Equal(forvantad, res.Rader[0].NyLon);
    }

    [Fact]
    public void ParseCsv_upptacker_kommaavgransare()
    {
        var a = Pnr("19850115123");
        // Komma som avgränsare, lön utan decimaler så komma inte tolkas som decimal.
        var csv = $"Personnummer,Ny lön,Motivering\n{a},32000,Bra jobb";

        var res = _parser.ParseCsv(csv);

        Assert.True(res.Rader[0].ArGiltig, string.Join(" ", res.Rader[0].Fel));
        Assert.Equal(32000m, res.Rader[0].NyLon);
        Assert.Equal("Bra jobb", res.Rader[0].Motivering);
    }

    [Fact]
    public void ParseCsv_respekterar_citerade_falt_med_avgransartecken()
    {
        var a = Pnr("19850115123");
        var csv = $"Personnummer;Ny lön;Motivering\n{a};32000;\"Utökat ansvar; ny roll\"";

        var res = _parser.ParseCsv(csv);

        Assert.True(res.Rader[0].ArGiltig);
        Assert.Equal("Utökat ansvar; ny roll", res.Rader[0].Motivering);
    }

    [Fact]
    public void ParseCsv_tom_fil_ger_globalt_fel()
    {
        var res = _parser.ParseCsv("");

        Assert.False(res.HarRader);
        Assert.NotEmpty(res.GlobalaFel);
    }

    [Fact]
    public void ParseCsv_saknad_lonekolumn_ger_globalt_fel()
    {
        var a = Pnr("19850115123");
        var csv = $"Personnummer;Motivering\n{a};Motivering";

        var res = _parser.ParseCsv(csv);

        Assert.Empty(res.Rader);
        Assert.Contains(res.GlobalaFel, f => f.Contains("ny lön", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseCsv_tolkar_valfri_anstallningsid_kolumn()
    {
        var a = Pnr("19850115123");
        var id = System.Guid.NewGuid();
        var csv = $"""
            Personnummer;Anställnings-id;Ny lön;Motivering
            {a};{id};32000;Motivering
            """;

        var res = _parser.ParseCsv(csv);

        Assert.True(res.Rader[0].ArGiltig, string.Join(" ", res.Rader[0].Fel));
        Assert.Equal(new EmploymentId(id), res.Rader[0].AnstallningId);
    }

    [Fact]
    public void ParseRutnat_fran_excelliknande_celler_tolkar_rader()
    {
        var a = Pnr("19850115123");
        var rutnat = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Personnummer", "Ny lön", "Motivering" },
            new string?[] { a, "32000", "Bra prestation" },
            new string?[] { null, null, null }, // tom rad ska ignoreras
        };

        var res = _parser.ParseRutnat(rutnat);

        Assert.Single(res.Rader);
        Assert.True(res.Rader[0].ArGiltig);
        Assert.Equal(32000m, res.Rader[0].NyLon);
    }

    [Fact]
    public void ParseCsv_blandar_giltiga_och_felaktiga_rader_med_ratt_rakning()
    {
        var a = Pnr("19850115123");
        var b = Pnr("19900320456");
        var csv = $"""
            Personnummer;Ny lön;Motivering
            {a};32000;Giltig rad
            OGILTIG;33000;Fel pnr
            {b};-1;Negativ lön
            """;

        var res = _parser.ParseCsv(csv);

        Assert.Equal(3, res.Rader.Count);
        Assert.Equal(1, res.AntalGiltiga);
        Assert.Equal(2, res.AntalFel);
    }
}
