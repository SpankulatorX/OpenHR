using System.Text;
using RegionHR.Infrastructure.Integrations.Framework;
using RegionHR.IntegrationHub.Framework;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Integrations.Framework;

/// <summary>
/// Tester för <see cref="IntegrationJobRunner"/> + <see cref="LocalFileDropSftpTransport"/>
/// + <see cref="HealthConnectManifestJob"/> — integrationsramverkets fundament.
/// Kör end-to-end mot en riktig fil-drop i en temporär katalog; run-loggen ersätts
/// av en minnesimplementation så testerna inte kräver databas.
/// </summary>
public sealed class IntegrationJobRunnerTests : IDisposable
{
    private readonly string _tempRot =
        Path.Combine(Path.GetTempPath(), "openhr-itest-" + Guid.NewGuid().ToString("N"));

    private LocalFileDropSftpTransport Transport() =>
        new(new SftpTransportOptions { LokalDropKatalog = _tempRot });

    // ── Jobbrunner: lyckad körning ────────────────────────────────────────────

    [Fact]
    public async Task KorAsync_MedRegistreratJobb_GenererarFilOchLoggarLyckad()
    {
        var store = new InMemoryRunLogStore();
        var job = new HealthConnectManifestJob();
        var runner = new IntegrationJobRunner(new IIntegrationJob[] { job }, Transport(), store);

        var r = await runner.KorAsync(HealthConnectManifestJob.Nyckel, "Test");

        Assert.Equal(IntegrationRunStatus.Lyckad, r.Status);
        Assert.Equal(IntegrationRegistry.Alla.Count, r.AntalPoster);
        Assert.NotNull(r.Plats);
        Assert.True(File.Exists(r.Plats));
        Assert.Contains("hc-manifest", r.Plats); // levererad under integrationens nyckelkatalog
        Assert.False(r.SkarpTransport);

        // Run-loggen fick posten.
        var loggad = Assert.Single(store.Poster);
        Assert.Equal(r.Id, loggad.Id);
        Assert.Equal("hc-manifest", loggad.IntegrationKey);
    }

    [Fact]
    public async Task KorAsync_LevererarFaktisktInnehallMedKorrektPersonnummer()
    {
        // Fake-jobb som skriver ett personnummer skapat med CreateValidated (giltig Luhn).
        var pnr = (string)Personnummer.CreateValidated("198012120000");
        var innehall = Encoding.UTF8.GetBytes($"pnr;belopp\n{pnr};25000\n");
        var job = new FakeJob("skatteverket-agi", new IntegrationJobResultat("agi.txt", innehall, 1));

        var store = new InMemoryRunLogStore();
        var runner = new IntegrationJobRunner(new IIntegrationJob[] { job }, Transport(), store);

        var r = await runner.KorAsync("skatteverket-agi", "Test");

        Assert.Equal(IntegrationRunStatus.Lyckad, r.Status);
        Assert.Contains("skatteverket-agi", r.Plats!);
        var pafil = await File.ReadAllTextAsync(r.Plats!);
        Assert.Contains(pnr, pafil);
    }

    // ── Jobbrunner: saknat jobb ───────────────────────────────────────────────

    [Fact]
    public async Task KorAsync_UtanRegistreratJobb_LoggarSaknarJobb()
    {
        var store = new InMemoryRunLogStore();
        // Inga jobb registrerade — men nyckeln finns i registret.
        var runner = new IntegrationJobRunner(Array.Empty<IIntegrationJob>(), Transport(), store);

        var r = await runner.KorAsync("nordea-pain001", "Test");

        Assert.Equal(IntegrationRunStatus.SaknarJobb, r.Status);
        Assert.Null(r.Filnamn);
        Assert.Null(r.Plats);
        Assert.NotNull(r.Anmarkning);
        Assert.Single(store.Poster);
        Assert.False(runner.HarKorbartJobb("nordea-pain001"));
    }

    // ── Jobbrunner: fel ───────────────────────────────────────────────────────

    [Fact]
    public async Task KorAsync_NarJobbetKastar_LoggarMisslyckadMedFel()
    {
        var store = new InMemoryRunLogStore();
        var job = new FakeJob("scb-klr", _ => throw new InvalidOperationException("byggfel"));
        var runner = new IntegrationJobRunner(new IIntegrationJob[] { job }, Transport(), store);

        var r = await runner.KorAsync("scb-klr", "Test");

        Assert.Equal(IntegrationRunStatus.Misslyckad, r.Status);
        Assert.Equal("byggfel", r.Fel);
        Assert.Equal(0, r.AntalPoster);
        Assert.Single(store.Poster);
    }

    [Fact]
    public async Task KorAsync_OkandNyckel_KastarArgumentException()
    {
        var runner = new IntegrationJobRunner(Array.Empty<IIntegrationJob>(), Transport(), new InMemoryRunLogStore());
        await Assert.ThrowsAsync<ArgumentException>(() => runner.KorAsync("finns-inte", "Test"));
    }

    // ── Lokal fil-drop-transport ──────────────────────────────────────────────

    [Fact]
    public async Task LocalFileDrop_SkriverFilUnderBaskatalog()
    {
        var transport = Transport();
        var data = Encoding.UTF8.GetBytes("hej");

        var res = await transport.LaddaUppAsync("skatteverket-agi/fil.xml", data);

        Assert.True(res.Lyckades);
        Assert.Equal(3L, res.Bytes);
        Assert.True(File.Exists(res.Plats));
        Assert.StartsWith(_tempRot, res.Plats);
    }

    [Fact]
    public async Task LocalFileDrop_SaniterarPathTraversal()
    {
        var transport = Transport();
        var res = await transport.LaddaUppAsync("../../../etc/passwd", Encoding.UTF8.GetBytes("x"));

        Assert.True(res.Lyckades);
        // Filen hamnar UNDER baskatalogen, aldrig utanför.
        var full = Path.GetFullPath(res.Plats);
        Assert.StartsWith(Path.GetFullPath(_tempRot), full);
    }

    [Fact]
    public async Task LocalFileDrop_ArAldrigSkarp_OchArNabar()
    {
        var transport = Transport();
        Assert.False(transport.ArSkarp);
        Assert.True(await transport.TestaAnslutningAsync());
    }

    // ── Manifest-jobbet ───────────────────────────────────────────────────────

    [Fact]
    public async Task ManifestJob_ProducerarCsvMedAllaIntegrationer()
    {
        var job = new HealthConnectManifestJob();
        var res = await job.KorAsync(new IntegrationJobKontext(DateTime.UtcNow, "Test"));

        var csv = Encoding.UTF8.GetString(res.Innehall);
        Assert.Equal(IntegrationRegistry.Alla.Count, res.AntalPoster);
        Assert.Contains("integrationsmanifest", csv);
        Assert.Contains("Nyckel;Namn;Riktning;Transport;Motpart;Frekvens;Format;ViaHealthConnect", csv);
        Assert.Contains("skatteverket-agi", csv);
        Assert.EndsWith(".csv", res.Filnamn);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRot)) Directory.Delete(_tempRot, recursive: true); }
        catch { /* temp-städning är best-effort */ }
    }

    // ── Testdubbler ───────────────────────────────────────────────────────────

    private sealed class InMemoryRunLogStore : IIntegrationRunLogStore
    {
        public List<IntegrationKorningsResultat> Poster { get; } = [];

        public Task SparaAsync(IntegrationKorningsResultat resultat, CancellationToken ct = default)
        {
            Poster.Add(resultat);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IntegrationKorningsResultat>> HamtaSenastePerIntegrationAsync(
            CancellationToken ct = default)
        {
            IReadOnlyList<IntegrationKorningsResultat> senaste = Poster
                .GroupBy(p => p.IntegrationKey)
                .Select(g => g.OrderByDescending(p => p.StartadUtc).First())
                .ToList();
            return Task.FromResult(senaste);
        }

        public Task<IReadOnlyList<IntegrationKorningsResultat>> HamtaHistorikAsync(
            string integrationKey, int antal = 20, CancellationToken ct = default)
        {
            IReadOnlyList<IntegrationKorningsResultat> h = Poster
                .Where(p => p.IntegrationKey == integrationKey)
                .OrderByDescending(p => p.StartadUtc)
                .Take(antal)
                .ToList();
            return Task.FromResult(h);
        }
    }

    private sealed class FakeJob : IIntegrationJob
    {
        private readonly Func<IntegrationJobKontext, IntegrationJobResultat> _bygg;

        public FakeJob(string key, IntegrationJobResultat resultat)
        {
            IntegrationKey = key;
            _bygg = _ => resultat;
        }

        public FakeJob(string key, Func<IntegrationJobKontext, IntegrationJobResultat> bygg)
        {
            IntegrationKey = key;
            _bygg = bygg;
        }

        public string IntegrationKey { get; }

        public Task<IntegrationJobResultat> KorAsync(IntegrationJobKontext kontext, CancellationToken ct = default) =>
            Task.FromResult(_bygg(kontext));
    }
}
