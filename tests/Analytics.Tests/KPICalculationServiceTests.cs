using Microsoft.EntityFrameworkCore;
using RegionHR.Analytics.Domain;
using RegionHR.Competence.Domain;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Infrastructure.Services;
using RegionHR.LAS.Domain;
using RegionHR.Leave.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Positions.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Analytics.Tests;

public class KPICalculationServiceTests
{
    private static RegionHRDbContext CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RegionHRDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RegionHRDbContext(options);
    }

    [Fact]
    public void CalculateTrend_WithNullPrevious_ReturnsStable()
    {
        var trend = KPICalculationService.CalculateTrend(100m, null);
        Assert.Equal("Stable", trend);
    }

    [Fact]
    public void CalculateTrend_WithZeroPrevious_ReturnsStable()
    {
        var trend = KPICalculationService.CalculateTrend(100m, 0m);
        Assert.Equal("Stable", trend);
    }

    [Fact]
    public void CalculateTrend_WhenCurrentSignificantlyHigher_ReturnsUp()
    {
        // 110 > 100 * 1.05 = 105
        var trend = KPICalculationService.CalculateTrend(110m, 100m);
        Assert.Equal("Up", trend);
    }

    [Fact]
    public void CalculateTrend_WhenCurrentSignificantlyLower_ReturnsDown()
    {
        // 90 < 100 * 0.95 = 95
        var trend = KPICalculationService.CalculateTrend(90m, 100m);
        Assert.Equal("Down", trend);
    }

    [Fact]
    public void CalculateTrend_WhenWithinThreshold_ReturnsStable()
    {
        // 102 is between 95 and 105
        var trend = KPICalculationService.CalculateTrend(102m, 100m);
        Assert.Equal("Stable", trend);
    }

    [Fact]
    public void ParsePeriodDates_QuarterlyFormat_ParsesCorrectly()
    {
        var today = new DateOnly(2026, 3, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("2026-Q1", today);

        Assert.Equal(new DateOnly(2026, 1, 1), start);
        Assert.Equal(new DateOnly(2026, 3, 31), end);
    }

    [Fact]
    public void ParsePeriodDates_Q2_ParsesCorrectly()
    {
        var today = new DateOnly(2026, 6, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("2026-Q2", today);

        Assert.Equal(new DateOnly(2026, 4, 1), start);
        Assert.Equal(new DateOnly(2026, 6, 30), end);
    }

    [Fact]
    public void ParsePeriodDates_Q3_ParsesCorrectly()
    {
        var today = new DateOnly(2026, 9, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("2026-Q3", today);

        Assert.Equal(new DateOnly(2026, 7, 1), start);
        Assert.Equal(new DateOnly(2026, 9, 30), end);
    }

    [Fact]
    public void ParsePeriodDates_Q4_ParsesCorrectly()
    {
        var today = new DateOnly(2026, 12, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("2026-Q4", today);

        Assert.Equal(new DateOnly(2026, 10, 1), start);
        Assert.Equal(new DateOnly(2026, 12, 31), end);
    }

    [Fact]
    public void ParsePeriodDates_MonthlyFormat_ParsesCorrectly()
    {
        var today = new DateOnly(2026, 3, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("2026-03", today);

        Assert.Equal(new DateOnly(2026, 3, 1), start);
        Assert.Equal(new DateOnly(2026, 3, 31), end);
    }

    [Fact]
    public void ParsePeriodDates_YearFormat_ParsesCorrectly()
    {
        var today = new DateOnly(2026, 3, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("2026", today);

        Assert.Equal(new DateOnly(2026, 1, 1), start);
        Assert.Equal(new DateOnly(2026, 12, 31), end);
    }

    [Fact]
    public void ParsePeriodDates_EmptyString_FallsBackToLast3Months()
    {
        var today = new DateOnly(2026, 3, 15);
        var (start, end) = KPICalculationService.ParsePeriodDates("", today);

        Assert.Equal(today.AddMonths(-3), start);
        Assert.Equal(today, end);
    }

    [Fact]
    public async Task CalculateAllAsync_WithNoDefinitions_ReturnsEmptyList()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_WithNoDefinitions_ReturnsEmptyList));
        var service = new KPICalculationService(db);

        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task CalculateAllAsync_WithActiveDefinitions_CreatesSnapshots()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_WithActiveDefinitions_CreatesSnapshots));

        var kpiDef = KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50);
        db.KPIDefinitions.Add(kpiDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal("2026-Q1", result[0].Period);
        Assert.Equal(kpiDef.Id, result[0].KPIDefinitionId);
    }

    [Fact]
    public async Task CalculateAllAsync_WithPreviousSnapshot_SetsTrendAndJamforelseVarde()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_WithPreviousSnapshot_SetsTrendAndJamforelseVarde));

        var kpiDef = KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50);
        db.KPIDefinitions.Add(kpiDef);

        // Add previous period snapshot
        var prevSnapshot = KPISnapshot.Skapa(kpiDef.Id, "2025-Q4", 5m, null, "Stable");
        db.KPISnapshots.Add(prevSnapshot);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal(5m, result[0].JamforelseVarde);
    }

    [Fact]
    public async Task CalculateAllAsync_InactiveDefinitions_AreSkipped()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_InactiveDefinitions_AreSkipped));

        var activeDef = KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50);
        var inactiveDef = KPIDefinition.Skapa("Inactive", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50, arAktiv: false);
        db.KPIDefinitions.AddRange(activeDef, inactiveDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal(activeDef.Id, result[0].KPIDefinitionId);
    }

    [Fact]
    public async Task CalculateAllAsync_VacancyRate_CalculatesCorrectly()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_VacancyRate_CalculatesCorrectly));

        // Create positions: 2 active, 1 vacant
        var orgId = Guid.NewGuid();
        var pos1 = Position.Skapa(orgId, "Sjukskoterska", 35000, 100);
        pos1.Tillsatt(Guid.NewGuid());
        var pos2 = Position.Skapa(orgId, "Lakare", 55000, 100);
        // pos2 is default Vakant
        var pos3 = Position.Skapa(orgId, "Underskaterska", 28000, 100);
        pos3.Tillsatt(Guid.NewGuid());

        db.Positions_Table.AddRange(pos1, pos2, pos3);

        var kpiDef = KPIDefinition.Skapa("Vakansgrad", "Recruitment", "vacant/total*100", "percent", "LowerIsBetter", 5, 10, 15);
        db.KPIDefinitions.Add(kpiDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        // 1 vacant / 3 total * 100 = 33.33
        var expectedRate = 1m / 3m * 100m;
        Assert.Equal(expectedRate, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_LASRiskCount_CountsCorrectly()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_LASRiskCount_CountsCorrectly));

        // Create LAS accumulations
        var las1 = LASAccumulation.Skapa(EmployeeId.From(Guid.NewGuid()), EmploymentType.SAVA);
        las1.LaggTillPeriod(DateOnly.FromDateTime(DateTime.Today).AddDays(-320), DateOnly.FromDateTime(DateTime.Today));
        // This should have >= 305 days

        var las2 = LASAccumulation.Skapa(EmployeeId.From(Guid.NewGuid()), EmploymentType.SAVA);
        las2.LaggTillPeriod(DateOnly.FromDateTime(DateTime.Today).AddDays(-100), DateOnly.FromDateTime(DateTime.Today));
        // This should have < 305 days

        db.LASAccumulations.AddRange(las1, las2);

        var kpiDef = KPIDefinition.Skapa("LAS-riskantal", "Compliance", "COUNT(las>=305)", "count", "LowerIsBetter", 0, 3, 5);
        db.KPIDefinitions.Add(kpiDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal(1m, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_CertificationCoverage_CalculatesCorrectly()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_CertificationCoverage_CalculatesCorrectly));

        // Create mandatory trainings
        var mt1 = MandatoryTraining.Skapa("Sjukskoterska", "HLR", 24);
        var mt2 = MandatoryTraining.Skapa("Sjukskoterska", "Brandsakerhet", 12);
        db.MandatoryTrainings.AddRange(mt1, mt2);

        // Create certifications (1 valid, 1 expired)
        var cert1 = Certification.Skapa(Guid.NewGuid(), "HLR", CertificationType.ObligatoriskUtbildning,
            "Roda Korset", DateOnly.FromDateTime(DateTime.Today).AddYears(-1),
            DateOnly.FromDateTime(DateTime.Today).AddYears(1), true);
        var cert2 = Certification.Skapa(Guid.NewGuid(), "Brandsakerhet", CertificationType.ObligatoriskUtbildning,
            "MSB", DateOnly.FromDateTime(DateTime.Today).AddYears(-2),
            DateOnly.FromDateTime(DateTime.Today).AddDays(-30), true);
        db.Certifications.AddRange(cert1, cert2);

        var kpiDef = KPIDefinition.Skapa("Certifieringstackning", "Competence", "valid/required*100", "percent", "HigherIsBetter", 95, 85, 70);
        db.KPIDefinitions.Add(kpiDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        // 1 valid / 2 required * 100 = 50
        Assert.Equal(50m, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_CertificationCoverage_NoRequirements_Returns100()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_CertificationCoverage_NoRequirements_Returns100));

        var kpiDef = KPIDefinition.Skapa("Certifieringstackning", "Competence", "valid/required*100", "percent", "HigherIsBetter", 95, 85, 70);
        db.KPIDefinitions.Add(kpiDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal(100m, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_UnknownKPI_ReturnsZero()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_UnknownKPI_ReturnsZero));

        var kpiDef = KPIDefinition.Skapa("UnknownKPI", "Other", "n/a", "count", "HigherIsBetter", 10, 5, 1);
        db.KPIDefinitions.Add(kpiDef);
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal(0m, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_MultipleKPIs_CreatesAllSnapshots()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_MultipleKPIs_CreatesAllSnapshots));

        db.KPIDefinitions.AddRange(
            KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50),
            KPIDefinition.Skapa("Vakansgrad", "Recruitment", "vacant/total*100", "percent", "LowerIsBetter", 5, 10, 15),
            KPIDefinition.Skapa("LAS-riskantal", "Compliance", "COUNT(las>=305)", "count", "LowerIsBetter", 0, 3, 5)
        );
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Equal(3, result.Count);
        Assert.All(result, s => Assert.Equal("2026-Q1", s.Period));
    }

    [Fact]
    public async Task CalculateAllAsync_Headcount_CountsDistinctIndividualsAndExcludesFutureEmployments()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_Headcount_CountsDistinctIndividualsAndExcludesFutureEmployments));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var enhetId = OrganizationId.New();

        // En person med två parallella aktiva anställningar → räknas som EN individ.
        var dubbel = Employee.Skapa(Personnummer.CreateValidated("19900101234"), "Anna", "Andersson");
        dubbel.LaggTillAnstallning(enhetId, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(50), today.AddYears(-2));
        dubbel.LaggTillAnstallning(enhetId, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(50), today.AddYears(-1));

        // Framtida (ej påbörjad) anställning → räknas inte.
        var framtida = Employee.Skapa(Personnummer.CreateValidated("19850615237"), "Erik", "Eriksson");
        framtida.LaggTillAnstallning(enhetId, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), today.AddDays(30));

        db.Employees.AddRange(dubbel, framtida);
        db.KPIDefinitions.Add(KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50));
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        Assert.Equal(1m, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_SickLeave_ExcludesNonApprovedRequests()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_SickLeave_ExcludesNonApprovedRequests));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var enhetId = OrganizationId.New();
        var emp = Employee.Skapa(Personnummer.CreateValidated("19900101234"), "Anna", "Andersson");
        emp.LaggTillAnstallning(enhetId, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), today.AddYears(-2));
        db.Employees.Add(emp);

        // Godkänd sjukfrånvaro (räknas): mån–fre = 5 dagar.
        var godkand = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), null);
        godkand.SkickaIn();
        godkand.Godkann(Guid.NewGuid(), null);

        // Utkast (räknas inte).
        var utkast = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 16), null);

        // Avslagen (räknas inte).
        var avslagen = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6), null);
        avslagen.SkickaIn();
        avslagen.Avvisa(Guid.NewGuid(), "Avslag");

        db.LeaveRequests.AddRange(godkand, utkast, avslagen);
        db.KPIDefinitions.Add(KPIDefinition.Skapa("Sjukfranvaro %", "Health", "sick/possible*100", "percent", "LowerIsBetter", 3, 5, 8));
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        var result = await service.CalculateAllAsync("2026-Q1");

        Assert.Single(result);
        // 5 godkända dagar / (1 individ * 3 mån * 21 arbetsdagar) * 100
        Assert.Equal(5m / 63m * 100m, result[0].Varde);
    }

    [Fact]
    public async Task CalculateAllAsync_SnapshotsArePersistedToDb()
    {
        using var db = CreateInMemoryDb(nameof(CalculateAllAsync_SnapshotsArePersistedToDb));

        db.KPIDefinitions.Add(KPIDefinition.Skapa("Headcount", "Workforce", "COUNT(*)", "count", "HigherIsBetter", 100, 80, 50));
        await db.SaveChangesAsync();

        var service = new KPICalculationService(db);
        await service.CalculateAllAsync("2026-Q1");

        var savedSnapshots = await db.KPISnapshots.ToListAsync();
        Assert.Single(savedSnapshots);
        Assert.Equal("2026-Q1", savedSnapshots[0].Period);
    }
}
