using RegionHR.Core.Contracts;
using RegionHR.LAS.Domain;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.LAS.Tests;

/// <summary>
/// In-memory stub för ILASRepository att använda i tester.
/// </summary>
internal sealed class InMemoryLASRepository : ILASRepository
{
    private readonly List<LASAccumulation> _store = [];

    public Task<LASAccumulation?> GetByEmployeeAsync(EmployeeId id, CancellationToken ct)
        => Task.FromResult(_store.FirstOrDefault(a => a.AnstallId == id));

    public Task<IReadOnlyList<LASAccumulation>> GetAllaAktiva(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LASAccumulation>>(_store.ToList());

    public Task<IReadOnlyList<LASAccumulation>> GetByStatus(LASStatus status, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LASAccumulation>>(_store.Where(a => a.Status == status).ToList());

    public Task AddAsync(LASAccumulation acc, CancellationToken ct)
    {
        _store.Add(acc);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(LASAccumulation acc, CancellationToken ct)
        => Task.CompletedTask;
}

/// <summary>
/// Stub för ICoreHRModule.
/// </summary>
internal sealed class StubCoreHR : ICoreHRModule
{
    private readonly List<EmployeeDto> _employees = [];

    public void LaggTillAnstallda(params EmployeeDto[] anstallda) => _employees.AddRange(anstallda);

    public Task<EmployeeDto?> GetEmployeeAsync(EmployeeId id, CancellationToken ct = default)
        => Task.FromResult(_employees.FirstOrDefault(e => e.Id == id));

    public Task<EmploymentDto?> GetActiveEmploymentAsync(EmployeeId id, DateOnly date, CancellationToken ct = default)
        => Task.FromResult<EmploymentDto?>(null);

    public Task<IReadOnlyList<EmploymentDto>> GetActiveEmploymentsAsync(EmployeeId id, DateOnly date, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EmploymentDto>>(Array.Empty<EmploymentDto>());

    public Task<IReadOnlyList<EmployeeDto>> GetEmployeesByUnitAsync(OrganizationId unitId, DateOnly date, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EmployeeDto>>(_employees.ToList());

    public Task<OrganizationUnitDto?> GetOrganizationUnitAsync(OrganizationId id, CancellationToken ct = default)
        => Task.FromResult<OrganizationUnitDto?>(null);
}

public class LASServiceTests
{
    private readonly InMemoryLASRepository _repository = new();
    private readonly StubCoreHR _coreHR = new();
    private readonly LASService _service;

    public LASServiceTests()
    {
        _service = new LASService(_coreHR, _repository);
    }

    [Fact]
    public async Task DagligKontroll_HittarApproachingLimits()
    {
        // Arrange: skapa en ackumulering nära SAVA-gränsen
        var anstallId = EmployeeId.New();
        var acc = LASAccumulation.Skapa(anstallId, EmploymentType.SAVA);
        var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-310));
        var slut = DateOnly.FromDateTime(DateTime.Today);
        acc.LaggTillPeriod(start, slut);
        await _repository.AddAsync(acc, CancellationToken.None);

        // Act
        await _service.KorDagligKontrollAsync(CancellationToken.None);

        // Assert: borde vara NäraGräns efter omberäkning
        var uppdaterad = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(uppdaterad);
        Assert.True(uppdaterad.Status is LASStatus.NaraGrans or LASStatus.KritiskNara);
    }

    [Fact]
    public async Task DagligKontroll_OmberaknarAllaAktiva()
    {
        // Arrange: två anställda
        var id1 = EmployeeId.New();
        var id2 = EmployeeId.New();

        var acc1 = LASAccumulation.Skapa(id1, EmploymentType.SAVA);
        acc1.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-100)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-10)));
        await _repository.AddAsync(acc1, CancellationToken.None);

        var acc2 = LASAccumulation.Skapa(id2, EmploymentType.Vikariat);
        acc2.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-200)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-5)));
        await _repository.AddAsync(acc2, CancellationToken.None);

        // Act
        await _service.KorDagligKontrollAsync(CancellationToken.None);

        // Assert: alla ackumuleringar ska ha korrekt beräkning
        var result1 = await _repository.GetByEmployeeAsync(id1, CancellationToken.None);
        var result2 = await _repository.GetByEmployeeAsync(id2, CancellationToken.None);
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.True(result1.AckumuleradeDagar > 0);
        Assert.True(result2.AckumuleradeDagar > 0);
    }

    [Fact]
    public async Task Dashboard_ReturnerarKorrektaRakningar()
    {
        // Arrange: skapa ackumuleringar i olika statusar
        // 1. Under gräns
        var id1 = EmployeeId.New();
        var acc1 = LASAccumulation.Skapa(id1, EmploymentType.SAVA);
        acc1.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-50)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        await _repository.AddAsync(acc1, CancellationToken.None);

        // 2. Nära gräns
        var id2 = EmployeeId.New();
        var acc2 = LASAccumulation.Skapa(id2, EmploymentType.SAVA);
        acc2.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-310)),
            DateOnly.FromDateTime(DateTime.Today));
        await _repository.AddAsync(acc2, CancellationToken.None);

        // 3. Kritisk nära
        var id3 = EmployeeId.New();
        var acc3 = LASAccumulation.Skapa(id3, EmploymentType.SAVA);
        acc3.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-340)),
            DateOnly.FromDateTime(DateTime.Today));
        await _repository.AddAsync(acc3, CancellationToken.None);

        // 4. Konverterad
        var id4 = EmployeeId.New();
        var acc4 = LASAccumulation.Skapa(id4, EmploymentType.SAVA);
        acc4.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-370)),
            DateOnly.FromDateTime(DateTime.Today));
        await _repository.AddAsync(acc4, CancellationToken.None);

        // Act
        var dashboard = await _service.HamtaDashboardAsync(CancellationToken.None);

        // Assert
        Assert.Equal(4, dashboard.TotaltAktiva);
        Assert.Equal(1, dashboard.UnderGrans);
        Assert.Equal(1, dashboard.NaraGrans);
        Assert.Equal(1, dashboard.KritiskNara);
        Assert.Equal(1, dashboard.Konverterade);
        Assert.True(dashboard.TopNarmastKonvertering.Count <= 10);
        // Top-lista ska inte innehålla konverterade
        Assert.DoesNotContain(dashboard.TopNarmastKonvertering,
            a => a.Status == LASStatus.KonverteradTillTillsvidare);
    }

    [Fact]
    public async Task AvslutaAnstallning_SatterForetradesratt_NarBerattigad()
    {
        // Arrange: SAVA-anställd med 280+ dagar (> 274 dagar = 9 månader = berättigad)
        var anstallId = EmployeeId.New();
        var acc = LASAccumulation.Skapa(anstallId, EmploymentType.SAVA);
        acc.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-290)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-10)));
        await _repository.AddAsync(acc, CancellationToken.None);

        var slutDatum = DateOnly.FromDateTime(DateTime.Today);

        // Act
        await _service.AvslutaAnstallningAsync(anstallId, slutDatum, CancellationToken.None);

        // Assert
        var uppdaterad = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(uppdaterad);
        Assert.True(uppdaterad.HarForetradesratt);
        Assert.Equal(slutDatum.AddMonths(9), uppdaterad.ForetradesrattUtgar);
    }

    [Fact]
    public async Task AvslutaAnstallning_SatterInteForetradesratt_NarEjBerattigad()
    {
        // Arrange: anställd med < 180 dagar
        var anstallId = EmployeeId.New();
        var acc = LASAccumulation.Skapa(anstallId, EmploymentType.SAVA);
        acc.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-100)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-10)));
        await _repository.AddAsync(acc, CancellationToken.None);

        // Act
        await _service.AvslutaAnstallningAsync(anstallId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

        // Assert
        var uppdaterad = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(uppdaterad);
        Assert.False(uppdaterad.HarForetradesratt);
    }

    [Fact]
    public async Task Turordningslista_SorterasKorrekt_SistInForstUt()
    {
        // Arrange
        var enhetId = OrganizationId.New();
        var id1 = EmployeeId.New();
        var id2 = EmployeeId.New();
        var id3 = EmployeeId.New();

        _coreHR.LaggTillAnstallda(
            new EmployeeDto(id1, "Anna", "Andersson", "199001****", null, null, null, null, null, false, null, false, null),
            new EmployeeDto(id2, "Bertil", "Berg", "198501****", null, null, null, null, null, false, null, false, null),
            new EmployeeDto(id3, "Cecilia", "Carlsson", "197501****", null, null, null, null, null, false, null, false, null));

        // Anna: 50 dagar (senast anställd)
        var acc1 = LASAccumulation.Skapa(id1, EmploymentType.SAVA);
        acc1.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-50)),
            DateOnly.FromDateTime(DateTime.Today));
        await _repository.AddAsync(acc1, CancellationToken.None);

        // Bertil: 200 dagar
        var acc2 = LASAccumulation.Skapa(id2, EmploymentType.SAVA);
        acc2.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-200)),
            DateOnly.FromDateTime(DateTime.Today));
        await _repository.AddAsync(acc2, CancellationToken.None);

        // Cecilia: 300 dagar (längst anställd)
        var acc3 = LASAccumulation.Skapa(id3, EmploymentType.SAVA);
        acc3.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-300)),
            DateOnly.FromDateTime(DateTime.Today));
        await _repository.AddAsync(acc3, CancellationToken.None);

        // Act
        var turordning = await _service.GenereraTurordningslistaAsync(
            enhetId, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);

        // Assert: sist in först ut = lägst dagar först
        Assert.Equal(3, turordning.Count);
        Assert.Equal(id1, turordning[0].AnstallId); // Anna - minst dagar, sägs upp först
        Assert.Equal(id2, turordning[1].AnstallId); // Bertil
        Assert.Equal(id3, turordning[2].AnstallId); // Cecilia - flest dagar, sägs upp sist
    }

    [Fact]
    public async Task RegistreraPeriod_MedAnnanForm_AckumulerarSeparat()
    {
        // En anställd med både SAVA- och vikariatsperioder: dagarna ska räknas
        // separat mot respektive gräns (365 resp. 730) — inte blandas ihop.
        var anstallId = EmployeeId.New();

        await _service.RegistreraPeriodAsync(
            anstallId, EmploymentType.SAVA,
            DateOnly.FromDateTime(DateTime.Today.AddDays(-400)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-201))); // 200 SAVA-dagar

        await _service.RegistreraPeriodAsync(
            anstallId, EmploymentType.Vikariat,
            DateOnly.FromDateTime(DateTime.Today.AddDays(-200)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));   // 200 vikariatsdagar

        var acc = await _repository.GetByEmployeeAsync(anstallId, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.Equal(200, acc.AckumuleradeSavaDagar);
        Assert.Equal(200, acc.AckumuleradeVikariatDagar);
        Assert.Equal(400, acc.AckumuleradeDagar);
        // 400 blandade dagar passerar INTE SAVA-gränsen 365 — ingen felaktig konvertering.
        Assert.NotEqual(LASStatus.KonverteradTillTillsvidare, acc.Status);
    }

    [Fact]
    public async Task HamtaForetradesrattsinnehavare_ReturnerarAktiva()
    {
        // Arrange: SAVA med 280 dagar (~9+ månader) → berättigad till företrädesrätt
        var anstallId = EmployeeId.New();
        var acc = LASAccumulation.Skapa(anstallId, EmploymentType.SAVA);
        acc.LaggTillPeriod(
            DateOnly.FromDateTime(DateTime.Today.AddDays(-290)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(-10)));
        acc.SattForetradesratt(DateOnly.FromDateTime(DateTime.Today.AddDays(-10)));
        await _repository.AddAsync(acc, CancellationToken.None);

        // Act
        var foretradesratt = await _service.HamtaForetradesrattsinnehavareAsync(CancellationToken.None);

        // Assert
        Assert.Single(foretradesratt);
        Assert.Equal(anstallId, foretradesratt[0].AnstallId);
    }
}
