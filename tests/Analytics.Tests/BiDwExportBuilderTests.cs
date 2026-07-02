using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Reporting;
using RegionHR.Leave.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Analytics.Tests;

public class BiDwExportBuilderTests
{
    private static readonly DateOnly Snapshot = new(2026, 6, 30);

    private static OrganizationUnit Enhet(string namn, string kst) =>
        OrganizationUnit.Skapa(namn, OrganizationUnitType.Avdelning, kst, new DateOnly(2020, 1, 1));

    // Gender styrs av näst sista siffran: jämn = Kvinna, udda = Man.
    private static Employee Kvinna(string namn = "Anna") =>
        Employee.Skapa(Personnummer.CreateValidated("19900101234"), namn, "Andersson"); // 3:e birth-siffran = 4 (jämn)

    private static Employee Man(string namn = "Erik") =>
        Employee.Skapa(Personnummer.CreateValidated("19850615237"), namn, "Eriksson"); // 3:e birth-siffran = 7 (udda)

    private static Employment AnstallningTillsvidare(Employee emp, OrganizationUnit enhet, decimal grad, string titel) =>
        emp.LaggTillAnstallning(
            enhet.Id, EmploymentType.Tillsvidare, CollectiveAgreementType.AB,
            Money.SEK(35000), new Percentage(grad), new DateOnly(2022, 1, 1),
            befattningstitel: titel);

    private static PayrollResult Lon(Employee emp, Employment anst, int year, int month, decimal brutto, decimal aga)
    {
        var r = PayrollResult.Skapa(
            PayrollRunId.New(), emp.Id, anst.Id, year, month,
            Money.SEK(brutto), 100m, CollectiveAgreementType.AB);
        r.Brutto = Money.SEK(brutto);
        r.Arbetsgivaravgifter = Money.SEK(aga);
        r.Skatt = Money.SEK(brutto * 0.3m);
        r.Netto = Money.SEK(brutto * 0.7m);
        r.Pensionsavgift = Money.SEK(brutto * 0.045m);
        return r;
    }

    [Fact]
    public void Bygg_ActiveEmployment_ProducesOneAnstallningFact()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Kvinna();
        AnstallningTillsvidare(emp, enhet, 80m, "Sjuksköterska");

        var schema = BiDwExportBuilder.Bygg([emp], [], [], [enhet], Snapshot);

        var fakta = Assert.Single(schema.FaktaAnstallning);
        Assert.Equal(enhet.Id.Value.ToString(), fakta.EnhetId);
        Assert.Equal("Sjuksköterska", fakta.BefattningId);
        Assert.Equal("K", fakta.KonId);
        Assert.Equal(0.8m, fakta.Fte);
        Assert.Equal(1, fakta.ArTillsvidare);
        Assert.Equal(0, fakta.ArTidsbegransad);
    }

    [Fact]
    public void Bygg_EndedEmploymentBeforeSnapshot_IsExcluded()
    {
        var enhet = Enhet("Kirurgi", "KST200");
        var emp = Kvinna();
        emp.LaggTillAnstallning(
            enhet.Id, EmploymentType.Vikariat, CollectiveAgreementType.AB,
            Money.SEK(30000), new Percentage(100), new DateOnly(2021, 1, 1),
            slutdatum: new DateOnly(2021, 12, 31), befattningstitel: "Vikarie");

        var schema = BiDwExportBuilder.Bygg([emp], [], [], [enhet], Snapshot);

        Assert.Empty(schema.FaktaAnstallning);
    }

    [Fact]
    public void Bygg_Payroll_ResolvesUnitAndComputesTotalLaborCost()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Kvinna();
        var anst = AnstallningTillsvidare(emp, enhet, 100m, "Sjuksköterska");
        var lon = Lon(emp, anst, 2026, 1, brutto: 35000m, aga: 11000m);

        var schema = BiDwExportBuilder.Bygg([emp], [lon], [], [enhet], Snapshot);

        var fakta = Assert.Single(schema.FaktaLon);
        Assert.Equal("2026-01", fakta.TidId);
        Assert.Equal(enhet.Id.Value.ToString(), fakta.EnhetId);
        Assert.Equal("K", fakta.KonId);
        Assert.Equal(46000m, fakta.TotalArbetskraftskostnadSEK);
    }

    [Fact]
    public void Bygg_PayrollForUnknownEmployment_MapsToOkandUnit()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Kvinna();
        AnstallningTillsvidare(emp, enhet, 100m, "Sjuksköterska");

        // Löneresultat med en anställnings-id som inte finns bland de laddade anställda.
        var orphan = PayrollResult.Skapa(
            PayrollRunId.New(), EmployeeId.New(), EmploymentId.New(),
            2026, 2, Money.SEK(20000), 100m, CollectiveAgreementType.AB);
        orphan.Brutto = Money.SEK(20000);
        orphan.Arbetsgivaravgifter = Money.SEK(6000);

        var schema = BiDwExportBuilder.Bygg([emp], [orphan], [], [enhet], Snapshot);

        var fakta = Assert.Single(schema.FaktaLon);
        Assert.Equal("OKÄND", fakta.EnhetId);
        Assert.Contains(schema.DimEnhet, e => e.EnhetId == "OKÄND");
    }

    [Fact]
    public void Bygg_SickLeave_ProducesAbsenceFactAttributedToUnit()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Kvinna();
        AnstallningTillsvidare(emp, enhet, 100m, "Sjuksköterska");
        var leave = LeaveRequest.Skapa(emp.Id.Value, LeaveType.Sjukfranvaro,
            new DateOnly(2026, 4, 6), new DateOnly(2026, 4, 8), "Influensa");

        var schema = BiDwExportBuilder.Bygg([emp], [], [leave], [enhet], Snapshot);

        var fakta = Assert.Single(schema.FaktaFranvaro);
        Assert.Equal("2026-04", fakta.TidId);
        Assert.Equal(enhet.Id.Value.ToString(), fakta.EnhetId);
        Assert.Equal("Sjukfranvaro", fakta.FranvaroTyp);
        Assert.Equal(1, fakta.AntalFall);
        Assert.Equal(leave.AntalDagar, fakta.AntalDagar);
    }

    [Fact]
    public void Bygg_MalePersonnummer_MapsToKonM()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Man();
        AnstallningTillsvidare(emp, enhet, 100m, "Läkare");

        var schema = BiDwExportBuilder.Bygg([emp], [], [], [enhet], Snapshot);

        Assert.Equal("M", Assert.Single(schema.FaktaAnstallning).KonId);
        Assert.Contains(schema.DimKon, k => k is { KonId: "M", Beteckning: "Man" });
    }

    [Fact]
    public void Bygg_AgeDimension_BandsEmployeeCorrectly()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Kvinna(); // född 1990 → 36 år vid 2026-06-30
        AnstallningTillsvidare(emp, enhet, 100m, "Sjuksköterska");

        var schema = BiDwExportBuilder.Bygg([emp], [], [], [enhet], Snapshot);

        Assert.Equal("30-39", Assert.Single(schema.FaktaAnstallning).AlderId);
        Assert.Contains(schema.DimAlder, a => a is { AlderId: "30-39", MinAlder: 30, MaxAlder: 39 });
    }

    [Fact]
    public void Bygg_TimeDimension_CoversSnapshotAndPayrollPeriods()
    {
        var enhet = Enhet("Akutmottagning", "KST100");
        var emp = Kvinna();
        var anst = AnstallningTillsvidare(emp, enhet, 100m, "Sjuksköterska");
        var lon = Lon(emp, anst, 2026, 1, 35000m, 11000m);

        var schema = BiDwExportBuilder.Bygg([emp], [lon], [], [enhet], Snapshot);

        Assert.Contains(schema.DimTid, t => t.TidId == "2026-06"); // snapshot-period för anställning
        Assert.Contains(schema.DimTid, t => t is { TidId: "2026-01", Ar: 2026, Kvartal: 1, Manad: 1 });
    }

    [Fact]
    public void Bygg_EnhetDimension_AlwaysIncludesProvidedUnits()
    {
        var enhet1 = Enhet("Akutmottagning", "KST100");
        var enhet2 = Enhet("Kirurgi", "KST200");

        // Inga anställda alls — dimensionen ska ändå innehålla alla enheter.
        var schema = BiDwExportBuilder.Bygg([], [], [], [enhet1, enhet2], Snapshot);

        Assert.Equal(2, schema.DimEnhet.Count);
        Assert.Contains(schema.DimEnhet, e => e.Namn == "Akutmottagning" && e.Kostnadsstalle == "KST100");
    }
}
