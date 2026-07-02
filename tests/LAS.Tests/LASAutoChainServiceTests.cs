using RegionHR.LAS.Domain;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.LAS.Tests;

/// <summary>
/// Stub för <see cref="IEmploymentLookup"/> — matar auto-kedjan med anställningsdata
/// utan databas. Nycklas på EmploymentId (readonly record struct = värdejämlikhet).
/// </summary>
internal sealed class StubEmploymentLookup : IEmploymentLookup
{
    private readonly Dictionary<EmploymentId, AnstallningsPeriod> _map = new();

    public EmploymentId Lagg(EmployeeId anstallId, EmploymentType form, DateOnly start, DateOnly? slut)
    {
        var id = EmploymentId.New();
        _map[id] = new AnstallningsPeriod(anstallId, form, start, slut);
        return id;
    }

    public Task<AnstallningsPeriod?> GetEmploymentAsync(EmploymentId employmentId, CancellationToken ct = default)
        => Task.FromResult(_map.TryGetValue(employmentId, out var p) ? p : null);
}

/// <summary>
/// Tester för LAS auto-kedjan: anställningshändelser ska driva LAS-ackumuleringen
/// utan manuell HR-registrering.
/// </summary>
public class LASAutoChainServiceTests
{
    private readonly InMemoryLASRepository _repository = new();
    private readonly StubEmploymentLookup _lookup = new();
    private readonly LASAutoChainService _service;

    private static DateOnly Idag => DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly DagarSedan(int dagar) => Idag.AddDays(-dagar);

    public LASAutoChainServiceTests()
    {
        _service = new LASAutoChainService(_repository, _lookup);
    }

    [Fact]
    public async Task AnstallningSkapad_ForSAVA_SkaparAckumuleringMedPeriod()
    {
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(100), Idag);

        var result = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Registrerad, result.Status);
        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.Equal(EmploymentType.SAVA, acc.Anstallningsform);
        Assert.True(acc.AckumuleradeDagar > 0);
        // Perioden ska taggas med anställnings-id (spårbarhet + idempotens).
        Assert.Single(acc.Perioder);
        Assert.Equal(employmentId.Value.ToString(), acc.Perioder[0].AnstallningsId);
    }

    [Fact]
    public async Task AnstallningSkapad_ForVikariat_SkaparAckumulering()
    {
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.Vikariat, DagarSedan(200), Idag);

        var result = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Registrerad, result.Status);
        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.Equal(EmploymentType.Vikariat, acc.Anstallningsform);
        Assert.True(acc.AckumuleradeDagar > 0);
    }

    [Fact]
    public async Task AnstallningSkapad_ForTillsvidare_Ignoreras()
    {
        var anstallId = EmployeeId.New();
        // Tillsvidare har inget slutdatum och ackumuleras aldrig.
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.Tillsvidare, DagarSedan(30), null);

        var result = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Ignorerad, result.Status);
        Assert.Null(await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None));
    }

    [Fact]
    public async Task AnstallningSkapad_ForSasong_Ignoreras()
    {
        // Säsongsanställning konverteras inte enligt §5a och saknar stöd i
        // ackumuleringsmodellen — auto-kedjan hoppar över den (dokumenterad begränsning).
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.Sasongsanstallning, DagarSedan(60), Idag);

        var result = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Ignorerad, result.Status);
        Assert.Null(await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None));
    }

    [Fact]
    public async Task AnstallningSkapad_OkandAnstallning_Ignoreras()
    {
        // Inget upplägg i stubben → lookup returnerar null.
        var result = await _service.RegistreraFranAnstallningAsync(EmploymentId.New(), CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Ignorerad, result.Status);
    }

    [Fact]
    public async Task AnstallningSkapad_Idempotent_RegistrerarInteDubblett()
    {
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(100), Idag);

        var forsta = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);
        var andra = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Registrerad, forsta.Status);
        Assert.Equal(LASAutoChainStatus.RedanRegistrerad, andra.Status);

        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.Single(acc.Perioder); // ingen dubblettperiod
    }

    [Fact]
    public async Task AnstallningSkapad_OverGrans_NoterarKonvertering()
    {
        // SAVA över 365 dagar → domänen noterar konvertering till tillsvidare.
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(400), Idag);

        var result = await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Registrerad, result.Status);
        Assert.True(result.KonverteringNoterad);
        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);
        Assert.NotNull(acc.KonverteringsDatum);
    }

    [Fact]
    public async Task AnstallningSkapad_ExisterandeAckumulering_OkarDagar()
    {
        // Två på varandra följande visstidsperioder för samma anställd summeras.
        var anstallId = EmployeeId.New();
        var forstaId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(100), DagarSedan(50)); // 51 dagar
        var andraId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(40), Idag);             // 41 dagar

        await _service.RegistreraFranAnstallningAsync(forstaId, CancellationToken.None);
        await _service.RegistreraFranAnstallningAsync(andraId, CancellationToken.None);

        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.Equal(2, acc.Perioder.Count);
        Assert.Equal(51 + 41, acc.AckumuleradeDagar);
    }

    [Fact]
    public async Task AnstallningAvslutad_Berattigad_SatterForetradesratt()
    {
        // SAVA med > 274 dagar (~9 mån) → företrädesrätt vid avslut.
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(290), DagarSedan(10));
        await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        var slutDatum = DagarSedan(10);
        var result = await _service.AvslutaFranAnstallningAsync(anstallId, slutDatum, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Avslutad, result.Status);
        Assert.True(result.ForetradesrattNoterad);
        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.True(acc.HarForetradesratt);
        Assert.Equal(slutDatum.AddMonths(9), acc.ForetradesrattUtgar);
    }

    [Fact]
    public async Task AnstallningAvslutad_EjBerattigad_IngenForetradesratt()
    {
        // Kort SAVA (< 274 dagar) → ingen företrädesrätt.
        var anstallId = EmployeeId.New();
        var employmentId = _lookup.Lagg(anstallId, EmploymentType.SAVA, DagarSedan(100), DagarSedan(10));
        await _service.RegistreraFranAnstallningAsync(employmentId, CancellationToken.None);

        var result = await _service.AvslutaFranAnstallningAsync(anstallId, DagarSedan(10), CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Avslutad, result.Status);
        Assert.False(result.ForetradesrattNoterad);
        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.False(acc.HarForetradesratt);
    }

    [Fact]
    public async Task AnstallningAvslutad_UtanAckumulering_Ignoreras()
    {
        var result = await _service.AvslutaFranAnstallningAsync(EmployeeId.New(), Idag, CancellationToken.None);

        Assert.Equal(LASAutoChainStatus.Ignorerad, result.Status);
    }
}
