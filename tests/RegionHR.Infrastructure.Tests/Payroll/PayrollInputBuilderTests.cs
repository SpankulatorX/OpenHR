using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Payroll;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Leave.Domain;
using RegionHR.Scheduling.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Infrastructure.Tests.Payroll;

/// <summary>
/// Tester för löneunderlagsbyggandet (PayrollInputBuilder): schema → OB/övertid/jour/beredskap,
/// frånvaroregister → sjuk/semester/föräldraledighet, samt proportionering av arbetade dagar.
/// September 2026 saknar svenska helgdagar, vilket gör arbetsdagsräkningen deterministisk.
/// </summary>
public sealed class PayrollInputBuilderTests : IDisposable
{
    private const int Ar = 2026;
    private const int Manad = 9; // September 2026 — inga röda dagar
    private readonly RegionHRDbContext _db;
    private readonly PayrollInputBuilder _builder = new();

    public PayrollInputBuilderTests()
    {
        var options = new DbContextOptionsBuilder<RegionHRDbContext>()
            .UseInMemoryDatabase($"PayrollInput-{Guid.NewGuid()}")
            .Options;
        _db = new RegionHRDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // === Arbetsdagar / grundlön ===

    [Fact]
    public async Task BuildAsync_FullMonthEmployment_CountsAllWorkingDays_AndFullBase()
    {
        var (_, employment) = await SeedEmployeeAsync(start: new DateOnly(2020, 1, 1));

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        var forvantadeArbetsdagar = WorkingDays(new DateOnly(Ar, Manad, 1), new DateOnly(Ar, Manad, 30));
        Assert.Equal(forvantadeArbetsdagar, input.ArbetsdagarIManadens);
        Assert.Equal(forvantadeArbetsdagar, input.ArbetadeDagar); // hela månaden anställd → full grundlön
        Assert.Empty(input.OBTimmar);
        Assert.Equal(0m, input.OvertidTimmar);
        Assert.Equal(0, input.SjukdagarMedLon);
        Assert.Equal(0, input.SemesterdagarUttagna);
        Assert.Equal(Money.Zero, input.Loneutmatning);
        Assert.Equal(Money.Zero, input.Fackavgift);
        Assert.Equal(employment.EnhetId.Value.ToString(), input.Kostnadsstalle);
    }

    [Fact]
    public async Task BuildAsync_MidMonthStart_ProportionsWorkedDaysBelowFullMonth()
    {
        var (_, employment) = await SeedEmployeeAsync(start: new DateOnly(Ar, Manad, 15));

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.True(input.ArbetadeDagar > 0);
        Assert.True(input.ArbetadeDagar < input.ArbetsdagarIManadens,
            "Anställning som börjar mitt i månaden ska ge färre arbetade dagar än hela månaden.");
        Assert.Equal(
            WorkingDays(new DateOnly(Ar, Manad, 15), new DateOnly(Ar, Manad, 30)),
            input.ArbetadeDagar);
    }

    // === Schema: OB, övertid, jour, beredskap ===

    [Fact]
    public async Task BuildAsync_AggregatesEveningObAndQualifiedOvertime_FromShifts()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        // Två kvällspass (VardagKvall), 15–22 med 30 min rast = 6,5 h vardera.
        AddShift(emp.Id, new DateOnly(Ar, Manad, 2), ShiftType.Kvall, OBCategory.VardagKvall,
            new TimeOnly(15, 0), new TimeOnly(22, 0), rastMin: 30, faktisk: true);
        AddShift(emp.Id, new DateOnly(Ar, Manad, 3), ShiftType.Kvall, OBCategory.VardagKvall,
            new TimeOnly(15, 0), new TimeOnly(22, 0), rastMin: 30, faktisk: true, overtid: 3m);
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        var ob = Assert.Single(input.OBTimmar);
        Assert.Equal(OBCategory.VardagKvall, ob.Kategori);
        Assert.Equal(13.0m, ob.Timmar);
        Assert.Equal(3m, input.OvertidTimmar);
        Assert.True(input.KvalificeradOvertid); // > 2 h/dag
    }

    [Fact]
    public async Task BuildAsync_JourAndBeredskap_AggregatedSeparately_NotAsOb()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        // Jourpass 17–07 (över midnatt), 14 h. Beredskap 08–18, 10 h.
        AddShift(emp.Id, new DateOnly(Ar, Manad, 4), ShiftType.Jour, OBCategory.Ingen,
            new TimeOnly(17, 0), new TimeOnly(7, 0), rastMin: 0, faktisk: true);
        AddShift(emp.Id, new DateOnly(Ar, Manad, 5), ShiftType.Beredskap, OBCategory.Ingen,
            new TimeOnly(8, 0), new TimeOnly(18, 0), rastMin: 0, faktisk: true);
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(14m, input.JourTimmar);
        Assert.Equal(10m, input.BeredskapsTimmar);
        Assert.Empty(input.OBTimmar);
    }

    [Fact]
    public async Task BuildAsync_PlannedOnlyShift_UsesPlannedHoursForOb()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        // Ej instämplat pass (Planerad) → faktiska timmar saknas, planerade används.
        AddShift(emp.Id, new DateOnly(Ar, Manad, 2), ShiftType.Kvall, OBCategory.VardagKvall,
            new TimeOnly(15, 0), new TimeOnly(22, 0), rastMin: 30, faktisk: false);
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        var ob = Assert.Single(input.OBTimmar);
        Assert.Equal(6.5m, ob.Timmar);
    }

    [Fact]
    public async Task BuildAsync_CancelledShiftsExcluded()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        AddShift(emp.Id, new DateOnly(Ar, Manad, 2), ShiftType.Kvall, OBCategory.VardagKvall,
            new TimeOnly(15, 0), new TimeOnly(22, 0), rastMin: 30, faktisk: true,
            status: ShiftStatus.Avbokad);
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Empty(input.OBTimmar);
    }

    [Fact]
    public async Task BuildAsync_NoShiftOvertime_FallsBackToTimesheetOvertime()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        var timesheet = Timesheet.Skapa(emp.Id.Value, Ar, Manad, 160m);
        timesheet.RegistreraTimmar(165m, overtid: 5m);
        _db.Timesheets.Add(timesheet);
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(5m, input.OvertidTimmar);
        Assert.True(input.KvalificeradOvertid);
    }

    // === Frånvaro ===

    [Fact]
    public async Task BuildAsync_ApprovedLeave_CountsWorkingDaysPerType_IgnoresUnapproved()
    {
        var (emp, employment) = await SeedEmployeeAsync();

        AddApprovedLeave(emp.Id.Value, LeaveType.Semester, new DateOnly(Ar, Manad, 7), new DateOnly(Ar, Manad, 9));
        AddApprovedLeave(emp.Id.Value, LeaveType.Sjukfranvaro, new DateOnly(Ar, Manad, 10), new DateOnly(Ar, Manad, 11));
        AddApprovedLeave(emp.Id.Value, LeaveType.Foraldraledighet, new DateOnly(Ar, Manad, 14), new DateOnly(Ar, Manad, 18));

        // Ej godkänd (endast inskickad) semester ska INTE räknas.
        var pending = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Semester,
            new DateOnly(Ar, Manad, 21), new DateOnly(Ar, Manad, 25), "väntar");
        pending.SkickaIn();
        _db.LeaveRequests.Add(pending);

        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(WorkingDays(new DateOnly(Ar, Manad, 7), new DateOnly(Ar, Manad, 9)), input.SemesterdagarUttagna);
        Assert.Equal(WorkingDays(new DateOnly(Ar, Manad, 10), new DateOnly(Ar, Manad, 11)), input.SjukdagarMedLon);
        Assert.Equal(WorkingDays(new DateOnly(Ar, Manad, 14), new DateOnly(Ar, Manad, 18)), input.ForaldraledigaDagar);
    }

    [Fact]
    public async Task BuildAsync_SickLeaveLongerThan14Days_CappedAt14()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        AddApprovedLeave(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(Ar, Manad, 1), new DateOnly(Ar, Manad, 30));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.True(WorkingDays(new DateOnly(Ar, Manad, 1), new DateOnly(Ar, Manad, 30)) > 14);
        Assert.Equal(14, input.SjukdagarMedLon);
    }

    [Fact]
    public async Task BuildAsync_LeavePartlyOutsidePeriod_OnlyIntersectionCounts()
    {
        var (emp, employment) = await SeedEmployeeAsync();
        // Semester som spänner över månadsgränsen: 28 aug – 4 sep. Endast sep-delen ska räknas.
        AddApprovedLeave(emp.Id.Value, LeaveType.Semester,
            new DateOnly(Ar, 8, 28), new DateOnly(Ar, Manad, 4));
        await _db.SaveChangesAsync();

        var input = await _builder.BuildAsync(_db, employment, Ar, Manad);

        Assert.Equal(WorkingDays(new DateOnly(Ar, Manad, 1), new DateOnly(Ar, Manad, 4)), input.SemesterdagarUttagna);
    }

    // === Hjälpmetoder ===

    private async Task<(Employee Employee, Employment Employment)> SeedEmployeeAsync(DateOnly? start = null)
    {
        var employee = Employee.Skapa(new Personnummer("198112289874"), "Test", "Testsson");
        var enhet = new OrganizationId(Guid.NewGuid());
        var employment = employee.LaggTillAnstallning(
            enhet, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(35000m), Percentage.FullTime, start ?? new DateOnly(2020, 1, 1));

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return (employee, employment);
    }

    private void AddShift(
        EmployeeId anstallId, DateOnly datum, ShiftType typ, OBCategory obKategori,
        TimeOnly start, TimeOnly slut, int rastMin, bool faktisk,
        decimal? overtid = null, ShiftStatus status = ShiftStatus.Avslutad)
    {
        _db.ScheduledShifts.Add(new ScheduledShift
        {
            Id = Guid.NewGuid(),
            SchemaId = new ScheduleId(Guid.NewGuid()),
            AnstallId = anstallId,
            Datum = datum,
            PassTyp = typ,
            OBKategori = obKategori,
            PlaneradStart = start,
            PlaneradSlut = slut,
            Rast = TimeSpan.FromMinutes(rastMin),
            FaktiskStart = faktisk ? start : null,
            FaktiskSlut = faktisk ? slut : null,
            Status = status,
            OvertidTimmar = overtid
        });
    }

    private void AddApprovedLeave(Guid anstallId, LeaveType typ, DateOnly from, DateOnly to)
    {
        var leave = LeaveRequest.Skapa(anstallId, typ, from, to, "test");
        leave.SkickaIn();
        leave.Godkann(Guid.NewGuid(), "godkänd");
        _db.LeaveRequests.Add(leave);
    }

    /// <summary>Oberoende referensräkning: mån–fre i intervallet (september 2026 saknar helgdagar).</summary>
    private static int WorkingDays(DateOnly from, DateOnly to)
    {
        var antal = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                antal++;
        }
        return antal;
    }
}
