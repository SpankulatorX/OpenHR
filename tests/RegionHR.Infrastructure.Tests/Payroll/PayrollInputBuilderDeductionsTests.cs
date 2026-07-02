using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Payroll;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Payroll;

/// <summary>
/// Tester för att PayrollInputBuilder läser löneutmätnings- och fackavgiftsregistren och fyller
/// input.Loneutmatning/input.Fackavgift korrekt per anställd och månad — inklusive kapning mot
/// förbehållsbeloppet. September 2026 saknar svenska helgdagar → deterministisk arbetsdagsräkning.
/// </summary>
public sealed class PayrollInputBuilderDeductionsTests : IDisposable
{
    private const int Ar = 2026;
    private const int Manad = 9; // September 2026 — inga röda dagar, hel månad anställd
    private const decimal Manadslon = 35000m;
    private const decimal Kommunalskatt = 30m; // → estimerad netto = 35000 * 0.70 = 24500

    private readonly RegionHRDbContext _db;
    private readonly PayrollInputBuilder _builder = new();

    public PayrollInputBuilderDeductionsTests()
    {
        var options = new DbContextOptionsBuilder<RegionHRDbContext>()
            .UseInMemoryDatabase($"PayrollDeductions-{Guid.NewGuid()}")
            .Options;
        _db = new RegionHRDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task BuildAsync_NoRegisters_DeductionsZero()
    {
        var employment = await SeedEmployeeAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(Money.Zero, input.Loneutmatning);
        Assert.Equal(Money.Zero, input.Fackavgift);
    }

    [Fact]
    public async Task BuildAsync_FixedGarnishmentWithinForbehall_DeductsFullAmount()
    {
        var employment = await SeedEmployeeAsync();
        _db.Set<Loneutmatning>().Add(Loneutmatning.SkapaFastBelopp(
            employment.AnstallId, "U-1000-26", Money.SEK(3000m), Money.SEK(12000m), new DateOnly(2026, 1, 1)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        // Estimerad netto 24500, förbehåll 12000 → 12500 tillgängligt → hela 3000 dras.
        Assert.Equal(3000m, input.Loneutmatning.Amount);
    }

    [Fact]
    public async Task BuildAsync_FixedGarnishmentExceedsAvailable_CappedByForbehall()
    {
        var employment = await SeedEmployeeAsync();
        _db.Set<Loneutmatning>().Add(Loneutmatning.SkapaFastBelopp(
            employment.AnstallId, "U-2000-26", Money.SEK(5000m), Money.SEK(22000m), new DateOnly(2026, 1, 1)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        // Estimerad netto 24500, förbehåll 22000 → endast 2500 finns över → kapas till 2500.
        Assert.Equal(2500m, input.Loneutmatning.Amount);
    }

    [Fact]
    public async Task BuildAsync_ForbehallAboveNet_NoGarnishment()
    {
        var employment = await SeedEmployeeAsync();
        _db.Set<Loneutmatning>().Add(Loneutmatning.SkapaFastBelopp(
            employment.AnstallId, "U-3000-26", Money.SEK(5000m), Money.SEK(25000m), new DateOnly(2026, 1, 1)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        // Estimerad netto 24500 < förbehåll 25000 → inget dras.
        Assert.Equal(Money.Zero, input.Loneutmatning);
    }

    [Fact]
    public async Task BuildAsync_EndedGarnishmentBeforePeriod_NotCounted()
    {
        var employment = await SeedEmployeeAsync();
        _db.Set<Loneutmatning>().Add(Loneutmatning.SkapaFastBelopp(
            employment.AnstallId, "U-4000-26", Money.SEK(3000m), Money.SEK(12000m),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(Money.Zero, input.Loneutmatning);
    }

    [Fact]
    public async Task BuildAsync_MultipleGarnishments_TotalCappedByHighestForbehall()
    {
        var employment = await SeedEmployeeAsync();
        // Två aktiva beslut, förbehåll 20000 (högsta) → 4500 tillgängligt av netto 24500.
        _db.Set<Loneutmatning>().Add(Loneutmatning.SkapaFastBelopp(
            employment.AnstallId, "U-A", Money.SEK(3000m), Money.SEK(20000m), new DateOnly(2026, 1, 1)));
        _db.Set<Loneutmatning>().Add(Loneutmatning.SkapaFastBelopp(
            employment.AnstallId, "U-B", Money.SEK(3000m), Money.SEK(15000m), new DateOnly(2026, 2, 1)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        // Första beslutet tar 3000, andra kapas mot återstående 1500 → totalt 4500.
        Assert.Equal(4500m, input.Loneutmatning.Amount);
    }

    [Fact]
    public async Task BuildAsync_UnionFeeFixed_UsesAmount()
    {
        var employment = await SeedEmployeeAsync();
        _db.Set<Fackavgift>().Add(Fackavgift.SkapaFastBelopp(
            employment.AnstallId, "Vision", Money.SEK(295m), new DateOnly(2026, 1, 1)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(295m, input.Fackavgift.Amount);
    }

    [Fact]
    public async Task BuildAsync_UnionFeePercent_UsesGrossTimesPercent()
    {
        var employment = await SeedEmployeeAsync();
        _db.Set<Fackavgift>().Add(Fackavgift.SkapaProcent(
            employment.AnstallId, "Kommunal", 0.01m, new DateOnly(2026, 1, 1)));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        // 1 % av proportionerad brutto (hel månad = 35000) = 350.
        Assert.Equal(350m, input.Fackavgift.Amount);
    }

    private async Task<Employment> SeedEmployeeAsync()
    {
        var employee = Employee.Skapa(Personnummer.CreateValidated("19850101123"), "Test", "Testsson");
        employee.UppdateraSkatteuppgifter(34, 1, "Örebro", Kommunalskatt, harKyrkoavgift: false, kyrkoavgiftssats: null);

        var enhet = new OrganizationId(Guid.NewGuid());
        var employment = employee.LaggTillAnstallning(
            enhet, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(Manadslon), Percentage.FullTime, new DateOnly(2020, 1, 1));

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return employment;
    }
}
