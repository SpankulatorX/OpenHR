using RegionHR.LAS.Domain;
using RegionHR.LAS.Services;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.LAS.Tests;

/// <summary>
/// Tester för HR:s skrivväg: korrigering av perioder samt manuell konvertering
/// (formbyte visstid → tillsvidare) och attestkontroll (ingen självattest).
/// Återanvänder <c>InMemoryLASRepository</c> och <c>StubCoreHR</c> från LASServiceTests.
/// </summary>
public class LASKorrigeringKonverteringTests
{
    private static DateOnly Dag(int offset) => DateOnly.FromDateTime(DateTime.Today.AddDays(offset));

    // ── Domän: perioder ─────────────────────────────────────────────

    [Fact]
    public void TaBortPeriod_MinskarAckumulering_OchMatcharExakt()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-100), Dag(-11)); // 90 dagar

        var borttagen = acc.TaBortPeriod(Dag(-100), Dag(-11));

        Assert.True(borttagen);
        Assert.Equal(0, acc.AckumuleradeDagar);
    }

    [Fact]
    public void TaBortPeriod_ReturnerarFalse_NarPeriodenInteFinns()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-100), Dag(-11));

        Assert.False(acc.TaBortPeriod(Dag(-50), Dag(-1)));
        Assert.Equal(90, acc.AckumuleradeDagar);
    }

    [Fact]
    public void AndraPeriod_OmberaknarDagar()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-100), Dag(-11)); // 90 dagar

        var andrad = acc.AndraPeriod(Dag(-100), Dag(-11), Dag(-100), Dag(-61)); // nu 40 dagar

        Assert.True(andrad);
        Assert.Equal(40, acc.AckumuleradeDagar);
    }

    [Fact]
    public void AndraPeriod_KastarException_NarSlutForeStart()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-100), Dag(-11));

        Assert.Throws<ArgumentException>(() => acc.AndraPeriod(Dag(-100), Dag(-11), Dag(-10), Dag(-50)));
    }

    [Fact]
    public void TaBortPeriod_AterstallerKonvertering_NarDagarFallerUnderGrans()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-370), Dag(0)); // > 365 → auto-konvertering
        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);

        var borttagen = acc.TaBortPeriod(Dag(-370), Dag(0));

        Assert.True(borttagen);
        Assert.Equal(0, acc.AckumuleradeDagar);
        Assert.NotEqual(LASStatus.KonverteradTillTillsvidare, acc.Status);
        Assert.Null(acc.KonverteringsDatum);
    }

    // ── Domän: manuell konvertering ─────────────────────────────────

    [Fact]
    public void KonverteraTillTillsvidare_SatterStatusDatumOchBeslutsfattare()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-120), Dag(0)); // under gräns

        var konverterad = acc.KonverteraTillTillsvidare(Dag(0), "Karl Berg");

        Assert.True(konverterad);
        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc.Status);
        Assert.Equal(Dag(0), acc.KonverteringsDatum);
        Assert.Contains(acc.Handelser, h => h.Typ == LASEventTyp.Konvertering && h.Beskrivning.Contains("Karl Berg"));
    }

    [Fact]
    public void KonverteraTillTillsvidare_ArIdempotent()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-120), Dag(0));
        Assert.True(acc.KonverteraTillTillsvidare(Dag(0), "HR"));

        Assert.False(acc.KonverteraTillTillsvidare(Dag(0), "HR"));
    }

    [Fact]
    public void KonverteraTillTillsvidare_KraverBeslutsfattare()
    {
        var acc = LASAccumulation.Skapa(EmployeeId.New(), EmploymentType.SAVA);
        acc.LaggTillPeriod(Dag(-120), Dag(0));

        Assert.Throws<ArgumentException>(() => acc.KonverteraTillTillsvidare(Dag(0), "  "));
    }

    // ── Service: skrivväg + attestkontroll ──────────────────────────

    private static LASService NyService(out InMemoryLASRepository repo)
    {
        repo = new InMemoryLASRepository();
        return new LASService(new StubCoreHR(), repo);
    }

    [Fact]
    public async Task RegistreraPeriodMedAttest_SkaparAckumulering()
    {
        var service = NyService(out var repo);
        var anstalld = EmployeeId.New();
        var hrAktor = Guid.NewGuid();

        await service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-90), Dag(0), hrAktor, "Karl Berg");

        var acc = await repo.GetByEmployeeAsync(anstalld, CancellationToken.None);
        Assert.NotNull(acc);
        Assert.True(acc!.AckumuleradeDagar > 0);
    }

    [Fact]
    public async Task RegistreraPeriodMedAttest_KastarVidSjalvattest()
    {
        var service = NyService(out _);
        var anstalld = EmployeeId.New();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-90), Dag(0), anstalld.Value, "Själv"));
    }

    [Fact]
    public async Task KorrigeraPeriodAsync_UppdaterarPeriod()
    {
        var service = NyService(out var repo);
        var anstalld = EmployeeId.New();
        var hrAktor = Guid.NewGuid();
        await service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-100), Dag(-11), hrAktor, "HR"); // 90 d

        var ok = await service.KorrigeraPeriodAsync(anstalld, Dag(-100), Dag(-11), Dag(-100), Dag(-61), hrAktor, "HR"); // 40 d

        Assert.True(ok);
        var acc = await repo.GetByEmployeeAsync(anstalld, CancellationToken.None);
        Assert.Equal(40, acc!.AckumuleradeDagar);
    }

    [Fact]
    public async Task TaBortPeriodAsync_TarBortPeriod()
    {
        var service = NyService(out var repo);
        var anstalld = EmployeeId.New();
        var hrAktor = Guid.NewGuid();
        await service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-100), Dag(-11), hrAktor, "HR");

        var ok = await service.TaBortPeriodAsync(anstalld, Dag(-100), Dag(-11), hrAktor, "HR");

        Assert.True(ok);
        var acc = await repo.GetByEmployeeAsync(anstalld, CancellationToken.None);
        Assert.Equal(0, acc!.AckumuleradeDagar);
    }

    [Fact]
    public async Task KonverteraTillTillsvidareAsync_Konverterar_OchArIdempotent()
    {
        var service = NyService(out var repo);
        var anstalld = EmployeeId.New();
        var hrAktor = Guid.NewGuid();
        await service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-120), Dag(0), hrAktor, "HR");

        Assert.True(await service.KonverteraTillTillsvidareAsync(anstalld, Dag(0), hrAktor, "HR"));
        Assert.False(await service.KonverteraTillTillsvidareAsync(anstalld, Dag(0), hrAktor, "HR"));

        var acc = await repo.GetByEmployeeAsync(anstalld, CancellationToken.None);
        Assert.Equal(LASStatus.KonverteradTillTillsvidare, acc!.Status);
    }

    [Fact]
    public async Task KonverteraTillTillsvidareAsync_KastarVidSjalvattest()
    {
        var service = NyService(out _);
        var anstalld = EmployeeId.New();
        await service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-120), Dag(0), Guid.NewGuid(), "HR");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.KonverteraTillTillsvidareAsync(anstalld, Dag(0), anstalld.Value, "Själv"));
    }

    [Fact]
    public async Task BeviljaForetradesrattAsync_GerForetradesratt_NarBerattigad()
    {
        var service = NyService(out var repo);
        var anstalld = EmployeeId.New();
        var hrAktor = Guid.NewGuid();
        // SAVA ≥ 274 dagar i 3-årsperioden → berättigad
        await service.RegistreraPeriodAsync(anstalld, EmploymentType.SAVA, Dag(-290), Dag(-10), hrAktor, "HR");

        var har = await service.BeviljaForetradesrattAsync(anstalld, Dag(-10), hrAktor, "HR");

        Assert.True(har);
        var acc = await repo.GetByEmployeeAsync(anstalld, CancellationToken.None);
        Assert.True(acc!.HarForetradesratt);
    }
}
