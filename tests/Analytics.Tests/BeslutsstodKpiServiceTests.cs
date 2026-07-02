using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Analytics;
using RegionHR.Leave.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Positions.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Analytics.Tests;

public class BeslutsstodKpiServiceTests
{
    private static readonly DateOnly Snapshot = new(2026, 6, 30);

    private static OrganizationUnit Enhet(string namn = "Akutmottagning", string kst = "KST100") =>
        OrganizationUnit.Skapa(namn, OrganizationUnitType.Avdelning, kst, new DateOnly(2020, 1, 1));

    private static Employee Anstalld() =>
        Employee.Skapa(Personnummer.CreateValidated("19900101234"), "Test", "Testsson");

    private static Employment Tillsvidare(Employee emp, OrganizationUnit enhet, decimal grad = 100m) =>
        emp.LaggTillAnstallning(
            enhet.Id, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(35000), new Percentage(grad), new DateOnly(2022, 1, 1),
            befattningstitel: "Sjuksköterska");

    private static BeslutsstodKpi ForEnhet(BeslutsstodResultat res, OrganizationUnit enhet) =>
        res.PerEnhet.Single(r => r.EnhetId == enhet.Id.Value.ToString());

    [Fact]
    public void Berakna_Turnover_LeaversOverActiveHeadcount()
    {
        var enhet = Enhet();

        var e1 = Anstalld();
        Tillsvidare(e1, enhet);
        var e2 = Anstalld();
        Tillsvidare(e2, enhet);

        // Avslutad anställning inom senaste 12 mån (ej aktiv på snapshot).
        var e3 = Anstalld();
        e3.LaggTillAnstallning(
            enhet.Id, EmploymentType.Vikariat, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), new DateOnly(2023, 1, 1),
            slutdatum: new DateOnly(2026, 3, 31), befattningstitel: "Vikarie");

        var res = BeslutsstodKpiService.Berakna([e1, e2, e3], [], [], [], [enhet], Snapshot);
        var kpi = ForEnhet(res, enhet);

        Assert.Equal(2, kpi.Headcount);                    // e1, e2 aktiva
        Assert.Equal(50.0m, kpi.PersonalomsattningProcent); // 1 avslut / 2 aktiva
    }

    /// <summary>Skapar en godkänd sjukfrånvaro (Utkast → Inskickad → Godkänd).</summary>
    private static LeaveRequest GodkandSjukfranvaro(Employee emp, DateOnly from, DateOnly to)
    {
        var leave = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro, from, to, null);
        leave.SkickaIn();
        leave.Godkann(Guid.NewGuid(), null);
        return leave;
    }

    [Fact]
    public void Berakna_SickLeavePercent_OverPossibleWorkdays()
    {
        var enhet = Enhet();
        var emp = Anstalld();
        Tillsvidare(emp, enhet);

        // 5 arbetsdagar godkänd sjukfrånvaro (mån–fre).
        var leave = GodkandSjukfranvaro(emp, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
        Assert.Equal(5, leave.AntalDagar);

        var res = BeslutsstodKpiService.Berakna([emp], [], [leave], [], [enhet], Snapshot);
        var kpi = ForEnhet(res, enhet);

        // 5 / (1 headcount * 12 mån * 21 dagar) * 100 = 1.984 → 2.0
        Assert.Equal(2.0m, kpi.SjukfranvaroProcent);
    }

    [Fact]
    public void Berakna_SickLeaveOutsideWindow_IsIgnored()
    {
        var enhet = Enhet();
        var emp = Anstalld();
        Tillsvidare(emp, enhet);

        // Godkänd sjukfrånvaro för > 12 mån sedan (utanför fönstret).
        var leave = GodkandSjukfranvaro(emp, new DateOnly(2024, 1, 5), new DateOnly(2024, 1, 12));

        var res = BeslutsstodKpiService.Berakna([emp], [], [leave], [], [enhet], Snapshot);
        Assert.Equal(0m, ForEnhet(res, enhet).SjukfranvaroProcent);
    }

    [Fact]
    public void Berakna_NonApprovedSickLeave_IsExcluded()
    {
        var enhet = Enhet();
        var emp = Anstalld();
        Tillsvidare(emp, enhet);

        // Utkast — räknas inte.
        var utkast = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), null);

        // Avslagen — räknas inte.
        var avslagen = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6), null);
        avslagen.SkickaIn();
        avslagen.Avvisa(Guid.NewGuid(), "Avslag");

        // Återkallad — räknas inte.
        var aterkallad = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), null);
        aterkallad.Aterkalla();

        var res = BeslutsstodKpiService.Berakna(
            [emp], [], [utkast, avslagen, aterkallad], [], [enhet], Snapshot);
        Assert.Equal(0m, ForEnhet(res, enhet).SjukfranvaroProcent);
    }

    [Fact]
    public void Berakna_Bemanningsgrad_FilledOverStaffablePositions()
    {
        var enhet = Enhet();
        var g = enhet.Id.Value;

        var p1 = Position.Skapa(g, "Sjuksköterska", 35000, 100);
        p1.Tillsatt(Guid.NewGuid());
        var p2 = Position.Skapa(g, "Sjuksköterska", 35000, 100);
        p2.Tillsatt(Guid.NewGuid());
        var p3 = Position.Skapa(g, "Undersköterska", 30000, 100); // Vakant (default)
        var p4 = Position.Skapa(g, "Gammal", 30000, 100);
        p4.Avveckla(); // exkluderas

        var res = BeslutsstodKpiService.Berakna([], [], [], [p1, p2, p3, p4], [enhet], Snapshot);
        var kpi = ForEnhet(res, enhet);

        Assert.Equal(3, kpi.AntalPositioner);       // 2 aktiva + 1 vakant (avvecklad exkluderad)
        Assert.Equal(2, kpi.TillsattaPositioner);
        Assert.Equal(66.7m, kpi.Bemanningsgrad);    // 2/3 * 100
    }

    [Fact]
    public void Berakna_LasRisk_CountsTimeLimitedNearThreshold()
    {
        var enhet = Enhet();

        // SAVA aktiv, startad > 305 dagar sedan → risk.
        var sava = Anstalld();
        sava.LaggTillAnstallning(
            enhet.Id, EmploymentType.SAVA, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), new DateOnly(2025, 6, 1),
            slutdatum: new DateOnly(2026, 12, 31), befattningstitel: "Vikarie");

        // Vikariat aktiv, startad > 640 dagar sedan → risk.
        var vik = Anstalld();
        vik.LaggTillAnstallning(
            enhet.Id, EmploymentType.Vikariat, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), new DateOnly(2024, 6, 1),
            slutdatum: new DateOnly(2026, 12, 31), befattningstitel: "Vikarie");

        // Tillsvidare → aldrig LAS-risk.
        var fast = Anstalld();
        Tillsvidare(fast, enhet);

        var res = BeslutsstodKpiService.Berakna([sava, vik, fast], [], [], [], [enhet], Snapshot);
        Assert.Equal(2, ForEnhet(res, enhet).LasRiskAntal);
    }

    [Fact]
    public void Berakna_RecentTimeLimited_NotYetLasRisk()
    {
        var enhet = Enhet();

        // SAVA startad nyligen (< 305 dagar) → ingen risk ännu.
        var sava = Anstalld();
        sava.LaggTillAnstallning(
            enhet.Id, EmploymentType.SAVA, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), new DateOnly(2026, 5, 1),
            slutdatum: new DateOnly(2026, 12, 31), befattningstitel: "Vikarie");

        var res = BeslutsstodKpiService.Berakna([sava], [], [], [], [enhet], Snapshot);
        Assert.Equal(0, ForEnhet(res, enhet).LasRiskAntal);
    }

    [Fact]
    public void Berakna_LonekostnadPerManad_AveragesOverDistinctPeriods()
    {
        var enhet = Enhet();
        var emp = Anstalld();
        var anst = Tillsvidare(emp, enhet); // 100 % → FTE 1

        var lon1 = Payroll(emp, anst, 2026, 1, 30000, 9000);
        var lon2 = Payroll(emp, anst, 2026, 2, 30000, 9000);

        var res = BeslutsstodKpiService.Berakna([emp], [lon1, lon2], [], [], [enhet], Snapshot);
        var kpi = ForEnhet(res, enhet);

        // (39000 + 39000) / 2 perioder = 39000; per FTE = 39000 / 1
        Assert.Equal(39000m, kpi.LonekostnadPerManadSEK);
        Assert.Equal(39000m, kpi.LonekostnadPerFteSEK);
    }

    [Fact]
    public void Berakna_Oversikt_AggregatesAcrossUnits()
    {
        var enhetA = Enhet("Akut", "KSTA");
        var enhetB = Enhet("Kirurgi", "KSTB");

        var a = Anstalld();
        Tillsvidare(a, enhetA);
        var b = Anstalld();
        Tillsvidare(b, enhetB);

        var res = BeslutsstodKpiService.Berakna([a, b], [], [], [], [enhetA, enhetB], Snapshot);

        Assert.Equal(BeslutsstodKpiService.OversiktEnhetId, res.Oversikt.EnhetId);
        Assert.Equal(2, res.Oversikt.Headcount);
        Assert.Equal(2, res.PerEnhet.Count);
    }

    [Fact]
    public void Berakna_EmptyInputs_ReturnsZeroedOverviewWithoutThrowing()
    {
        var res = BeslutsstodKpiService.Berakna([], [], [], [], [], Snapshot);

        Assert.Equal(0, res.Oversikt.Headcount);
        Assert.Equal(0m, res.Oversikt.PersonalomsattningProcent);
        Assert.Empty(res.PerEnhet);
    }

    private static PayrollResult Payroll(Employee emp, Employment anst, int year, int month, decimal brutto, decimal aga)
    {
        var r = PayrollResult.Skapa(
            PayrollRunId.New(), emp.Id, anst.Id, year, month,
            Money.SEK(brutto), 100m, CollectiveAgreementType.AB);
        r.Brutto = Money.SEK(brutto);
        r.Arbetsgivaravgifter = Money.SEK(aga);
        return r;
    }
}
