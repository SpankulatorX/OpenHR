using RegionHR.Analytics.Domain.BiExport;
using Xunit;

namespace RegionHR.Analytics.Tests;

public class BiExportGeneratorTests
{
    private static BiStjarnschema BuildSchema() => new(
        DimTid:
        [
            new BiDimTid("2026-01", 2026, 1, 1, "Januari"),
            new BiDimTid("2026-04", 2026, 2, 4, "April"),
        ],
        DimEnhet:
        [
            new BiDimEnhet("enhet-1", "Akutmottagning", "KST100", "Avdelning", null, "SE1234"),
            // Namn med komma → måste citeras i CSV.
            new BiDimEnhet("enhet-2", "Kirurgi, plan 3", "KST200", "Avdelning", "enhet-1", null),
        ],
        DimBefattning:
        [
            new BiDimBefattning("Sjuksköterska", "Sjuksköterska", "204010", "203"),
        ],
        DimKon:
        [
            new BiDimKon("K", "Kvinna"),
            new BiDimKon("M", "Man"),
        ],
        DimAlder:
        [
            new BiDimAlder("30-39", "30-39", 30, 39),
        ],
        FaktaAnstallning:
        [
            new BiFaktaAnstallning("2026-01", "enhet-1", "Sjuksköterska", "K", "30-39",
                AntalAnstallningar: 1, Sysselsattningsgrad: 80m, Fte: 0.8m,
                ManadslonSEK: 34500.50m, ArTillsvidare: 1, ArTidsbegransad: 0),
        ],
        FaktaLon:
        [
            new BiFaktaLon("2026-01", "enhet-1", "K",
                BruttoSEK: 34500.50m, SkattSEK: 10000m, NettoSEK: 24500.50m,
                ArbetsgivaravgifterSEK: 10839m, PensionsavgiftSEK: 1552m,
                TotalArbetskraftskostnadSEK: 45339.50m),
        ],
        FaktaFranvaro:
        [
            new BiFaktaFranvaro("2026-04", "enhet-1", "K", "Sjukfranvaro", AntalDagar: 3, AntalFall: 1),
        ],
        SnapshotDatum: new DateOnly(2026, 4, 30),
        GenereradVid: new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void GenereraCsvPaket_ContainsAllEightTables()
    {
        var paket = BiExportGenerator.GenereraCsvPaket(BuildSchema());

        Assert.Equal(8, paket.Count);
        Assert.Contains("dim_tid.csv", paket.Keys);
        Assert.Contains("dim_enhet.csv", paket.Keys);
        Assert.Contains("dim_befattning.csv", paket.Keys);
        Assert.Contains("dim_kon.csv", paket.Keys);
        Assert.Contains("dim_alder.csv", paket.Keys);
        Assert.Contains("fakta_anstallning.csv", paket.Keys);
        Assert.Contains("fakta_lon.csv", paket.Keys);
        Assert.Contains("fakta_franvaro.csv", paket.Keys);
    }

    [Fact]
    public void GenereraCsvPaket_FaktaAnstallning_HasHeaderAndDataRow()
    {
        var paket = BiExportGenerator.GenereraCsvPaket(BuildSchema());
        var csv = paket["fakta_anstallning.csv"];

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // rubrik + 1 datarad
        Assert.StartsWith("TidId,EnhetId,BefattningId,KonId,AlderId,AntalAnstallningar", lines[0]);
        Assert.Contains("2026-01,enhet-1,Sjuksköterska,K,30-39,1", lines[1]);
    }

    [Fact]
    public void GenereraCsv_UsesInvariantDecimalSeparator()
    {
        var paket = BiExportGenerator.GenereraCsvPaket(BuildSchema());
        var csv = paket["fakta_lon.csv"];

        // Punkt (invariant), aldrig svenskt decimalkomma — annars spräcks kolumnerna.
        Assert.Contains("34500.5", csv);
        Assert.DoesNotContain("34500,5", csv);
    }

    [Fact]
    public void GenereraCsv_QuotesFieldsContainingComma()
    {
        var paket = BiExportGenerator.GenereraCsvPaket(BuildSchema());
        var csv = paket["dim_enhet.csv"];

        // "Kirurgi, plan 3" måste citeras så kommat inte tolkas som kolumnavgränsare.
        Assert.Contains("\"Kirurgi, plan 3\"", csv);
    }

    [Fact]
    public void EscapeCsv_DoublesEmbeddedQuotes()
    {
        var escaped = BiExportGenerator.EscapeCsv("Enhet \"Norr\"");
        Assert.Equal("\"Enhet \"\"Norr\"\"\"", escaped);
    }

    [Fact]
    public void EscapeCsv_LeavesPlainFieldUnquoted()
    {
        Assert.Equal("Akutmottagning", BiExportGenerator.EscapeCsv("Akutmottagning"));
    }

    [Fact]
    public void GenereraPlattAnstallningCsv_InlinesDimensionAttributes()
    {
        var csv = BiExportGenerator.GenereraPlattAnstallningCsv(BuildSchema());
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Ar,Kvartal,Manad,EnhetNamn,Kostnadsstalle,Befattning,Kon,Aldersintervall", lines[0]);
        // Dimensionsvärden ska vara utskrivna, inte nycklar.
        Assert.Contains("Akutmottagning", lines[1]);
        Assert.Contains("Kvinna", lines[1]);
        Assert.Contains("Sjuksköterska", lines[1]);
        Assert.Contains("2026", lines[1]);
    }

    [Fact]
    public void GenereraJson_RoundTripsSchema()
    {
        var json = BiExportGenerator.GenereraJson(BuildSchema());

        Assert.Contains("\"DimTid\"", json);
        Assert.Contains("\"FaktaAnstallning\"", json);
        // Svenska tecken i klartext (UnsafeRelaxedJsonEscaping), inte ä.
        Assert.Contains("Sjuksköterska", json);
        // JSON-tal använder alltid invariant punkt.
        Assert.Contains("34500.5", json);
    }

    [Fact]
    public void GenereraJsonBytes_HasNoBom()
    {
        var bytes = BiExportGenerator.GenereraJsonBytes(BuildSchema());

        // UTF-8 BOM = EF BB BF. Får inte finnas.
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public void AntalFaktarader_SumsAllFactTables()
    {
        var schema = BuildSchema();
        Assert.Equal(3, schema.AntalFaktarader);      // 1 + 1 + 1
        Assert.Equal(8, schema.AntalDimensionsrader); // tid 2 + enhet 2 + befattning 1 + kon 2 + alder 1
    }

    [Fact]
    public void GenereraCsv_RoundsDecimalsToTwoPlaces()
    {
        var schema = BuildSchema() with
        {
            FaktaLon =
            [
                new BiFaktaLon("2026-01", "enhet-1", "K",
                    BruttoSEK: 100.126m, SkattSEK: 0m, NettoSEK: 0m,
                    ArbetsgivaravgifterSEK: 0m, PensionsavgiftSEK: 0m, TotalArbetskraftskostnadSEK: 0m),
            ]
        };

        var csv = BiExportGenerator.GenereraCsvPaket(schema)["fakta_lon.csv"];
        Assert.Contains("100.13", csv); // avrundas till 2 decimaler (banker's rounding)
        Assert.DoesNotContain("100.126", csv);
    }
}
