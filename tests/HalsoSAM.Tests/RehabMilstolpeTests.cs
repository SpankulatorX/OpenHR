using RegionHR.HalsoSAM.Domain;
using RegionHR.SharedKernel.Domain;
using Xunit;

namespace RegionHR.HalsoSAM.Tests;

/// <summary>
/// Låser fast att rehabkedjans milstolpar (dag 14/90/180/365) räknas från sjukfallets
/// FAKTISKA dag 1 — inte från när ärendet råkade skapas i systemet.
/// </summary>
public class RehabMilstolpeTests
{
    private static DateTime Ankare(DateOnly dag1) =>
        DateTime.SpecifyKind(dag1.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    [Fact]
    public void Skapa_MedDag1_SatterMilstolparRelativtDag1()
    {
        var dag1 = new DateOnly(2026, 1, 10);

        var rc = RehabCase.Skapa(EmployeeId.New(), RehabTrigger.FjortonSammanhangandeDagar, dag1);

        Assert.Equal(dag1, rc.SjukfallDag1);
        Assert.Equal(Ankare(dag1).AddDays(14), rc.Uppfoljning14Dagar);
        Assert.Equal(Ankare(dag1).AddDays(90), rc.Uppfoljning90Dagar);
        Assert.Equal(Ankare(dag1).AddDays(180), rc.Uppfoljning180Dagar);
        Assert.Equal(Ankare(dag1).AddDays(365), rc.Uppfoljning365Dagar);
    }

    [Fact]
    public void Milstolpar_HarExaktKedjeAvstand()
    {
        var dag1 = new DateOnly(2026, 3, 1);
        var rc = RehabCase.Skapa(EmployeeId.New(), RehabTrigger.ChefInitierat, dag1);

        // Dag 1 + 14/90/180/365 exakt
        Assert.Equal(14, (rc.Uppfoljning14Dagar!.Value - Ankare(dag1)).Days);
        Assert.Equal(90, (rc.Uppfoljning90Dagar!.Value - Ankare(dag1)).Days);
        Assert.Equal(180, (rc.Uppfoljning180Dagar!.Value - Ankare(dag1)).Days);
        Assert.Equal(365, (rc.Uppfoljning365Dagar!.Value - Ankare(dag1)).Days);
    }

    [Fact]
    public void Dag1_ILangtFornflutna_GerPasseradeMilstolpar_InteRaknatFranSkapandet()
    {
        // Anställd sjuk sedan 100 dagar tillbaka, men ärendet skapas idag.
        var dag1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-100));

        var rc = RehabCase.Skapa(EmployeeId.New(), RehabTrigger.FjortonSammanhangandeDagar, dag1);

        // Dag 14 och dag 90 ska redan vara passerade (räknat från dag 1)...
        Assert.True(rc.Uppfoljning14Dagar < DateTime.UtcNow, "dag 14 borde ligga i det förflutna");
        Assert.True(rc.Uppfoljning90Dagar < DateTime.UtcNow, "dag 90 borde ligga i det förflutna");
        // ...medan om de felaktigt räknats från skapandedatumet skulle dag 90 ligga ~90 dagar framåt.
        Assert.True(rc.Uppfoljning180Dagar > DateTime.UtcNow, "dag 180 borde ligga i framtiden");
    }

    [Fact]
    public void SattSjukfallDag1_RaknarOmSamtligaMilstolpar()
    {
        // Skapas via 2-arg (ankras vid skapandet)...
        var rc = RehabCase.Skapa(EmployeeId.New(), RehabTrigger.MedarbetareInitierat);
        var fore = rc.Uppfoljning14Dagar;

        // ...och korrigeras sedan till verklig dag 1.
        var dag1 = new DateOnly(2025, 6, 1);
        rc.SattSjukfallDag1(dag1);

        Assert.Equal(dag1, rc.SjukfallDag1);
        Assert.Equal(Ankare(dag1).AddDays(14), rc.Uppfoljning14Dagar);
        Assert.Equal(Ankare(dag1).AddDays(365), rc.Uppfoljning365Dagar);
        Assert.NotEqual(fore, rc.Uppfoljning14Dagar);
    }

    [Fact]
    public void Skapa_UtanDag1_SatterDag1TillIdag()
    {
        var fore = DateOnly.FromDateTime(DateTime.UtcNow);
        var rc = RehabCase.Skapa(EmployeeId.New(), RehabTrigger.SexTillfallenTolvManader);
        var efter = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.NotNull(rc.SjukfallDag1);
        Assert.InRange(rc.SjukfallDag1!.Value, fore, efter);
    }

    [Fact]
    public void ArUppfoljningRegistrerad_ReflekterarRegistrering()
    {
        var dag1 = new DateOnly(2026, 2, 1);
        var rc = RehabCase.Skapa(EmployeeId.New(), RehabTrigger.FjortonSammanhangandeDagar, dag1);

        Assert.False(rc.ArUppfoljningRegistrerad(14));
        rc.RegistreraUppfoljning(14, "Genomförd", EmployeeId.New());
        Assert.True(rc.ArUppfoljningRegistrerad(14));
        Assert.False(rc.ArUppfoljningRegistrerad(90));
    }

    [Fact]
    public void Rehabkedja_Tabell_HarVerifieradeVarden()
    {
        Assert.Equal(2026, Rehabkedja.Version);
        int[] forvantadeDagar = [14, 90, 180, 365];
        Assert.Equal(forvantadeDagar, Rehabkedja.Milstolpar.Select(m => m.DagNr).ToArray());
        Assert.Equal(8, Rehabkedja.LakarintygFranDag);                    // Sjuklönelagen 8 §
        Assert.Equal(15, Rehabkedja.ForsakringskassanAnmalanFranDag);     // Sjuklönelagen 12 §
        Assert.Equal(30, Rehabkedja.PlanForAtergangSenastDag);            // SFB 30 kap 6 §
        Assert.Equal(60, Rehabkedja.PlanAntasPagaLangreAnDagar);         // SFB 30 kap 6 §
    }
}
