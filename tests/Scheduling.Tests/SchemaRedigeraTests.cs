using RegionHR.Scheduling.Domain;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.Scheduling.Tests;

/// <summary>
/// Tester för schemadomänens grundschema-skapande, borttagning av pass, samt den
/// ATL-validering som schemagriden (Redigera.razor) kör när ett pass läggs till manuellt.
/// </summary>
public class SchemaRedigeraTests
{
    private readonly EmployeeId _anstallId = EmployeeId.New();
    private readonly OrganizationId _enhet = OrganizationId.New();

    #region Grundschema

    [Fact]
    public void SkapaGrundschema_SatterTypOchOandligPeriod()
    {
        var schema = Schedule.SkapaGrundschema(_enhet, "Vårdavd grundschema", new DateOnly(2026, 1, 5), 4);

        Assert.Equal(ScheduleType.Grundschema, schema.Typ);
        Assert.Equal(4, schema.CykelLangdVeckor);
        Assert.Equal(new DateOnly(2026, 1, 5), schema.Period.Start);
        Assert.True(schema.Period.IsOpenEnded);
        Assert.Equal(ScheduleStatus.Utkast, schema.Status);
    }

    [Fact]
    public void SkapaPeriodschema_HarStartOchSlut()
    {
        var schema = Schedule.SkapaPeriodschema(_enhet, "Mars", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        Assert.Equal(ScheduleType.Periodschema, schema.Typ);
        Assert.Equal(new DateOnly(2026, 3, 31), schema.Period.End);
    }

    #endregion

    #region Lägg till / ta bort pass

    [Fact]
    public void LaggTillPass_LaggsISchemat()
    {
        var schema = Schedule.SkapaGrundschema(_enhet, "G", new DateOnly(2026, 1, 5), 4);

        var pass = schema.LaggTillPass(_anstallId, new DateOnly(2026, 1, 5),
            ShiftType.Dag, new TimeOnly(7, 0), new TimeOnly(16, 0), TimeSpan.FromMinutes(60));

        Assert.Single(schema.Pass);
        Assert.Equal(pass.Id, schema.Pass[0].Id);
        Assert.Equal(ShiftStatus.Planerad, pass.Status);
    }

    [Fact]
    public void TaBortPass_ExisterandePass_TasBort()
    {
        var schema = Schedule.SkapaGrundschema(_enhet, "G", new DateOnly(2026, 1, 5), 4);
        var pass = schema.LaggTillPass(_anstallId, new DateOnly(2026, 1, 5),
            ShiftType.Dag, new TimeOnly(7, 0), new TimeOnly(16, 0), TimeSpan.FromMinutes(60));

        var borttaget = schema.TaBortPass(pass.Id);

        Assert.True(borttaget);
        Assert.Empty(schema.Pass);
    }

    [Fact]
    public void TaBortPass_OkantId_ReturnerarFalse()
    {
        var schema = Schedule.SkapaGrundschema(_enhet, "G", new DateOnly(2026, 1, 5), 4);
        schema.LaggTillPass(_anstallId, new DateOnly(2026, 1, 5),
            ShiftType.Dag, new TimeOnly(7, 0), new TimeOnly(16, 0), TimeSpan.FromMinutes(60));

        var borttaget = schema.TaBortPass(Guid.NewGuid());

        Assert.False(borttaget);
        Assert.Single(schema.Pass);
    }

    #endregion

    #region ValidateNyttPass (används av schemagriden vid manuell inläggning)

    [Fact]
    public void ValidateNyttPass_9hVila_GerDygnsvilaBrott()
    {
        // Befintligt pass slutar 23:00, nytt pass börjar 08:00 dagen efter = 9h vila.
        var validator = new ArbetstidslagenValidator(arSjukvard: false);
        var befintliga = new List<ScheduledShift>
        {
            SkapaPass(new DateOnly(2026, 3, 16), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };
        var nytt = SkapaPass(new DateOnly(2026, 3, 17), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var res = validator.ValidateNyttPass(nytt, befintliga);

        Assert.False(res.ArGiltigt);
        Assert.Contains(res.Overtraldelser, v => v.Regel.Contains("Dygnsvila"));
    }

    [Fact]
    public void ValidateNyttPass_9hVila_MedSjukvardsundantag_Godkant()
    {
        var validator = new ArbetstidslagenValidator(arSjukvard: true);
        var befintliga = new List<ScheduledShift>
        {
            SkapaPass(new DateOnly(2026, 3, 16), new TimeOnly(14, 0), new TimeOnly(23, 0))
        };
        var nytt = SkapaPass(new DateOnly(2026, 3, 17), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var res = validator.ValidateNyttPass(nytt, befintliga);

        Assert.True(res.ArGiltigt);
    }

    [Fact]
    public void ValidateNyttPass_TillrackligVila_Godkant()
    {
        var validator = new ArbetstidslagenValidator();
        var befintliga = new List<ScheduledShift>
        {
            SkapaPass(new DateOnly(2026, 3, 16), new TimeOnly(7, 0), new TimeOnly(16, 0))
        };
        // Nästa dag 07:00 → 15h vila
        var nytt = SkapaPass(new DateOnly(2026, 3, 17), new TimeOnly(7, 0), new TimeOnly(16, 0));

        var res = validator.ValidateNyttPass(nytt, befintliga);

        Assert.True(res.ArGiltigt);
    }

    [Fact]
    public void ValidateNyttPass_SjundeDagenEfterSexIRad_GerVeckovilaBrott()
    {
        // 6 dagar i rad (mån-lör) → söndagens pass bryter mot 36h veckovila.
        var validator = new ArbetstidslagenValidator();
        var befintliga = new List<ScheduledShift>();
        for (int i = 0; i < 6; i++)
        {
            befintliga.Add(SkapaPass(new DateOnly(2026, 3, 16).AddDays(i), new TimeOnly(8, 0), new TimeOnly(16, 0)));
        }
        var nytt = SkapaPass(new DateOnly(2026, 3, 22), new TimeOnly(8, 0), new TimeOnly(16, 0)); // söndag

        var res = validator.ValidateNyttPass(nytt, befintliga);

        Assert.False(res.ArGiltigt);
        Assert.Contains(res.Overtraldelser, v => v.Regel.Contains("Veckovila"));
    }

    [Fact]
    public void ValidateNyttPass_IgnorerarAnnanAnstallldsPass()
    {
        // Pass för en annan anställd ska inte påverka valideringen av den aktuella.
        var validator = new ArbetstidslagenValidator();
        var annan = EmployeeId.New();
        var befintliga = new List<ScheduledShift>
        {
            SkapaPass(new DateOnly(2026, 3, 16), new TimeOnly(14, 0), new TimeOnly(23, 0), anstallId: annan)
        };
        var nytt = SkapaPass(new DateOnly(2026, 3, 17), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var res = validator.ValidateNyttPass(nytt, befintliga);

        Assert.True(res.ArGiltigt);
    }

    [Fact]
    public void TillShiftAssignment_MapparPlaneradTid()
    {
        var pass = SkapaPass(new DateOnly(2026, 3, 17), new TimeOnly(7, 0), new TimeOnly(16, 0), rast: TimeSpan.FromMinutes(60));

        var a = ArbetstidslagenValidator.TillShiftAssignment(pass);

        Assert.Equal(_anstallId, a.AnstallId);
        Assert.Equal(new DateOnly(2026, 3, 17), a.Datum);
        Assert.Equal(new TimeOnly(7, 0), a.Start);
        Assert.Equal(new TimeOnly(16, 0), a.Slut);
        Assert.Equal(TimeSpan.FromMinutes(60), a.Rast);
        Assert.Equal(8m, a.PlaneradeTimmar); // 9h brutto - 1h rast
    }

    #endregion

    private ScheduledShift SkapaPass(
        DateOnly datum, TimeOnly start, TimeOnly slut,
        TimeSpan? rast = null, EmployeeId? anstallId = null) => new()
    {
        Id = Guid.NewGuid(),
        SchemaId = ScheduleId.New(),
        AnstallId = anstallId ?? _anstallId,
        Datum = datum,
        PassTyp = start >= new TimeOnly(21, 0) ? ShiftType.Natt : start >= new TimeOnly(14, 0) ? ShiftType.Kvall : ShiftType.Dag,
        PlaneradStart = start,
        PlaneradSlut = slut,
        Rast = rast ?? TimeSpan.FromMinutes(30),
        Status = ShiftStatus.Planerad
    };
}
