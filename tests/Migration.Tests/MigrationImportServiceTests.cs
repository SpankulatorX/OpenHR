using RegionHR.Migration.Adapters;
using RegionHR.Migration.Domain;
using RegionHR.Migration.Services;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Migration.Tests;

public class MigrationImportServiceTests
{
    // ------------------------------------------------------------------
    // En in-minnes-sink som fångar de operationer tjänsten skickar och
    // simulerar persistens (så idempotens kan verifieras över körningar).
    // ------------------------------------------------------------------
    private sealed class FakeSink : IEmployeeImportSink
    {
        public List<EmployeeImportOperation> Mottagna { get; } = [];
        public Dictionary<string, Guid> Befintliga { get; } = new(StringComparer.Ordinal);
        public string? TvingaGlobaltFel { get; set; }
        public HashSet<int> RaderSomFallerISink { get; } = [];

        public Task<IReadOnlyDictionary<string, Guid>> LaddaBefintligaAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, Guid>>(
                new Dictionary<string, Guid>(Befintliga, StringComparer.Ordinal));

        public Task<SinkExekveringsResultat> ExekveraAsync(
            IReadOnlyList<EmployeeImportOperation> operationer,
            MigrationImportContext kontext,
            CancellationToken ct = default)
        {
            Mottagna.AddRange(operationer);

            if (TvingaGlobaltFel is not null)
                return Task.FromResult(new SinkExekveringsResultat(Array.Empty<SinkRadUtfall>(), TvingaGlobaltFel));

            var utfall = new List<SinkRadUtfall>();
            foreach (var op in operationer)
            {
                if (RaderSomFallerISink.Contains(op.RadNummer))
                {
                    utfall.Add(new SinkRadUtfall(op.RadNummer, false, null, "simulerat domänfel"));
                    continue;
                }

                var id = Guid.NewGuid();
                // Simulera persistens → syns i nästa LaddaBefintligaAsync (idempotens).
                Befintliga[(string)op.Data.Personnummer] = id;
                utfall.Add(new SinkRadUtfall(op.RadNummer, true, id, null));
            }

            return Task.FromResult(new SinkExekveringsResultat(utfall, null));
        }
    }

    // ------------------------------------------------------------------
    // Hjälpare för testdata. Personnummer genereras via CreateValidated så
    // att kontrollsiffran alltid stämmer.
    // ------------------------------------------------------------------
    private static string GiltigtPnr(string elva) => (string)Personnummer.CreateValidated(elva);

    private static ParsedRecord EmployeeRad(
        string pnr, string fornamn, string efternamn,
        string? anstForm = "Tillsvidare", string? avtal = "AB",
        string? manlon = "35000", string? enhet = "VE001")
    {
        var fields = new Dictionary<string, string>
        {
            ["Personnummer"] = pnr,
            ["Fornamn"] = fornamn,
            ["Efternamn"] = efternamn,
        };
        if (anstForm is not null) fields["Anstallningsform"] = anstForm;
        if (avtal is not null) fields["Kollektivavtal"] = avtal;
        if (manlon is not null) fields["Manadslon"] = manlon;
        if (enhet is not null) fields["Enhetskod"] = enhet;

        return new ParsedRecord { EntityType = "Employee", Fields = fields };
    }

    private static ParsedMigrationData Data(params ParsedRecord[] records)
        => new() { Records = [.. records], TotalRows = records.Length };

    // ==================================================================
    // Skapande
    // ==================================================================

    [Fact]
    public async Task ImporteraAsync_GiltigRad_SkaparAnstalldMedAnstallning()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson")),
            SourceSystem.HEROMA, "heroma.csv", "test");

        Assert.Equal(1, result.Skapade);
        Assert.Equal(0, result.Hoppade);
        Assert.Equal(0, result.Fel);
        Assert.Null(result.GlobaltFel);

        var op = Assert.Single(sink.Mottagna);
        Assert.Equal(ImportOperation.Skapa, op.Typ);
        Assert.Equal("Anna", op.Data.Fornamn);
        Assert.Equal("Svensson", op.Data.Efternamn);
        Assert.NotNull(op.Data.Anstallning);
        Assert.Equal(EmploymentType.Tillsvidare, op.Data.Anstallning!.Anstallningsform);
        Assert.Equal(CollectiveAgreementType.AB, op.Data.Anstallning.Kollektivavtal);
        Assert.Equal(35000m, op.Data.Anstallning.Manadslon.Amount);
        Assert.Equal("VE001", op.Data.Anstallning.Enhetskod);
    }

    [Fact]
    public async Task ImporteraAsync_UtanAnstallningsuppgifter_SkaparBaraAnstalld()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("199003221567");

        // Ingen enhetskod → ingen anställning skapas.
        var record = EmployeeRad(pnr, "Erik", "Johansson", enhet: null, manlon: null, anstForm: null, avtal: null);

        var result = await service.ImporteraAsync(Data(record), SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(1, result.Skapade);
        var op = Assert.Single(sink.Mottagna);
        Assert.Null(op.Data.Anstallning);
    }

    [Fact]
    public async Task ImporteraAsync_DefaultSysselsattningsgrad_Ar100()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198712301890");

        await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Maria", "Lindberg")),
            SourceSystem.HEROMA, "f.csv", "test");

        var op = Assert.Single(sink.Mottagna);
        Assert.Equal(100m, op.Data.Anstallning!.Sysselsattningsgrad.Value);
    }

    [Fact]
    public async Task ImporteraAsync_SvensktDecimalformatPaLon_TolkasKorrekt()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson", manlon: "35 250,50")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(1, result.Skapade);
        var op = Assert.Single(sink.Mottagna);
        Assert.Equal(35250.50m, op.Data.Anstallning!.Manadslon.Amount);
    }

    // ==================================================================
    // Validering
    // ==================================================================

    [Fact]
    public async Task ImporteraAsync_OgiltigtPersonnummer_RapporterasSomFel_OchIngenOperation()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);

        // Ogiltig kontrollsiffra (12 siffror men fel Luhn).
        var result = await service.ImporteraAsync(
            Data(EmployeeRad("198501151235", "Anna", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(0, result.Skapade);
        Assert.Equal(1, result.Fel);
        Assert.Empty(sink.Mottagna);
        var rad = Assert.Single(result.Rader);
        Assert.Equal(ImportRadStatus.Fel, rad.Status);
    }

    [Fact]
    public async Task ImporteraAsync_SaknatFornamn_RapporterasSomFel()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(1, result.Fel);
        Assert.Empty(sink.Mottagna);
    }

    [Fact]
    public async Task ImporteraAsync_OkandAnstallningsform_RapporterasSomFel()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson", anstForm: "HeltRandomKod")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(1, result.Fel);
        Assert.Empty(sink.Mottagna);
    }

    [Fact]
    public async Task ImporteraAsync_IckeEmployeePost_Hoppas()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);

        var record = new ParsedRecord
        {
            EntityType = "PayrollRecord",
            Fields = new Dictionary<string, string> { ["Belopp"] = "100" }
        };

        var result = await service.ImporteraAsync(Data(record), SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(1, result.Hoppade);
        Assert.Empty(sink.Mottagna);
    }

    // ==================================================================
    // Dubbletter & idempotens
    // ==================================================================

    [Fact]
    public async Task ImporteraAsync_DubblettMotBefintlig_HoppasOver()
    {
        var sink = new FakeSink();
        var pnr = GiltigtPnr("198501151234");
        sink.Befintliga[pnr] = Guid.NewGuid(); // finns redan i DB

        var service = new MigrationImportService(sink);

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(0, result.Skapade);
        Assert.Equal(1, result.Hoppade);
        Assert.Empty(sink.Mottagna);
    }

    [Fact]
    public async Task ImporteraAsync_DubblettIFilen_AndraRadenHoppas()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(
                EmployeeRad(pnr, "Anna", "Svensson"),
                EmployeeRad(pnr, "Anna", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(1, result.Skapade);
        Assert.Equal(1, result.Hoppade);
        Assert.Single(sink.Mottagna);
    }

    [Fact]
    public async Task ImporteraAsync_KorSammaFilTvaGanger_ArIdempotent()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);
        var data = Data(
            EmployeeRad(GiltigtPnr("198501151234"), "Anna", "Svensson"),
            EmployeeRad(GiltigtPnr("198712301890"), "Maria", "Lindberg"));

        var forsta = await service.ImporteraAsync(data, SourceSystem.HEROMA, "f.csv", "test");
        Assert.Equal(2, forsta.Skapade);

        var andra = await service.ImporteraAsync(data, SourceSystem.HEROMA, "f.csv", "test");
        Assert.Equal(0, andra.Skapade);
        Assert.Equal(2, andra.Hoppade);
        Assert.Equal(2, sink.Befintliga.Count); // inga nya
    }

    [Fact]
    public async Task ImporteraAsync_UppdateraDubbletter_SkickarUppdateraOperation()
    {
        var sink = new FakeSink();
        var pnr = GiltigtPnr("198501151234");
        var befintligtId = Guid.NewGuid();
        sink.Befintliga[pnr] = befintligtId;

        var service = new MigrationImportService(sink);

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test", uppdateraDubbletter: true);

        Assert.Equal(1, result.Uppdaterade);
        Assert.Equal(0, result.Hoppade);
        var op = Assert.Single(sink.Mottagna);
        Assert.Equal(ImportOperation.Uppdatera, op.Typ);
        Assert.Equal(befintligtId, op.BefintligtEmployeeId);
    }

    // ==================================================================
    // Fel från sink
    // ==================================================================

    [Fact]
    public async Task ImporteraAsync_GlobaltDatabasfel_RapporterasOchNollSkapade()
    {
        var sink = new FakeSink { TvingaGlobaltFel = "Databasfel: anslutning bruten" };
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(0, result.Skapade);
        Assert.NotNull(result.GlobaltFel);
        Assert.Equal(1, result.Fel);
    }

    [Fact]
    public async Task ImporteraAsync_RadFallerISink_RapporterasSomFel()
    {
        var sink = new FakeSink();
        sink.RaderSomFallerISink.Add(1);
        var service = new MigrationImportService(sink);
        var pnr = GiltigtPnr("198501151234");

        var result = await service.ImporteraAsync(
            Data(EmployeeRad(pnr, "Anna", "Svensson")),
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(0, result.Skapade);
        Assert.Equal(1, result.Fel);
        var rad = Assert.Single(result.Rader);
        Assert.Equal(ImportRadStatus.Fel, rad.Status);
        Assert.Equal("simulerat domänfel", rad.Meddelande);
    }

    [Fact]
    public async Task ImporteraAsync_BlandadeRader_RaknarKorrekt()
    {
        var sink = new FakeSink();
        var befintligt = GiltigtPnr("197001011234");
        sink.Befintliga[befintligt] = Guid.NewGuid();

        var service = new MigrationImportService(sink);

        var result = await service.ImporteraAsync(
            Data(
                EmployeeRad(GiltigtPnr("198501151234"), "Anna", "Svensson"),   // skapas
                EmployeeRad(befintligt, "Bo", "Ek"),                            // hoppas (finns)
                EmployeeRad("198501151235", "Fel", "Person"),                   // fel (ogiltigt pnr)
                EmployeeRad(GiltigtPnr("198712301890"), "Maria", "Lindberg")),  // skapas
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Equal(2, result.Skapade);
        Assert.Equal(1, result.Hoppade);
        Assert.Equal(1, result.Fel);
        Assert.Equal(4, result.Totalt);
        Assert.Equal(4, result.Rader.Count);
    }

    [Fact]
    public async Task ImporteraAsync_ResultatRaderSorteradePaRadnummer()
    {
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);

        var result = await service.ImporteraAsync(
            Data(
                EmployeeRad("198501151235", "Fel", "Person"),                   // rad 1: fel
                EmployeeRad(GiltigtPnr("198501151234"), "Anna", "Svensson")),   // rad 2: skapad
            SourceSystem.HEROMA, "f.csv", "test");

        Assert.Collection(result.Rader,
            r => Assert.Equal(1, r.RadNummer),
            r => Assert.Equal(2, r.RadNummer));
    }

    // ==================================================================
    // Integration med den riktiga HeromaAdapter-parsern
    // ==================================================================

    [Fact]
    public async Task ImporteraAsync_ParsadHeromaFil_KopplarIhopParsningOchImport()
    {
        using var stream = File.OpenRead(Path.Combine("TestData", "sample-heroma.csv"));
        var parsed = await new HeromaAdapter().ParseAsync(stream);

        var sink = new FakeSink();
        var service = new MigrationImportService(sink);

        var result = await service.ImporteraAsync(parsed, SourceSystem.HEROMA, "sample-heroma.csv", "test");

        // Sample-filens personnummer är påhittade testdata (kontrollsiffran stämmer inte) — poängen med
        // testet är att den riktiga adaptern hänger ihop med importtjänsten och att varje rad får ett utfall.
        Assert.Equal(parsed.Records.Count, result.Totalt);
        Assert.Equal(result.Totalt, result.Rader.Count);
        Assert.Equal(result.Skapade + result.Uppdaterade + result.Hoppade + result.Fel, result.Totalt);
        Assert.Equal(result.Skapade + result.Uppdaterade, sink.Mottagna.Count);
        // Varje rad i filen har enhetskod → de operationer som ändå skickas bär med sig anställningsdata.
        Assert.All(sink.Mottagna, op => Assert.NotNull(op.Data.Anstallning));
    }

    [Fact]
    public async Task ImporteraAsync_HeromaFaltMedGiltigaPnr_SkaparAllaTre()
    {
        // Samma kolumner som HEROMA-adaptern producerar, men med Luhn-giltiga personnummer.
        var sink = new FakeSink();
        var service = new MigrationImportService(sink);

        var result = await service.ImporteraAsync(
            Data(
                EmployeeRad(GiltigtPnr("198501151234"), "Anna", "Svensson", "Tillsvidare", "AB", "35000", "VE001"),
                EmployeeRad(GiltigtPnr("199003221567"), "Erik", "Johansson", "Vikariat", "HOK", "55000", "VE002"),
                EmployeeRad(GiltigtPnr("198712301890"), "Maria", "Lindberg", "Tillsvidare", "AB", "42000", "VE001")),
            SourceSystem.HEROMA, "heroma.csv", "test");

        Assert.Equal(3, result.Skapade);
        Assert.Equal(0, result.Fel);
        Assert.Equal(3, sink.Mottagna.Count);
    }
}
