#pragma warning disable CS0618
using RegionHR.Positions.Domain;
using Xunit;

namespace RegionHR.Positions.Tests;

public class PositionTests
{
    [Fact]
    public void Skapa_SetsCorrectDefaults()
    {
        // Arrange
        var enhetId = Guid.NewGuid();

        // Act
        var position = Position.Skapa(enhetId, "Sjuksköterska", 35000m, 100m, "123", "ABC");

        // Assert
        Assert.NotEqual(Guid.Empty, position.Id);
        Assert.Equal(enhetId, position.EnhetId);
        Assert.Equal("Sjuksköterska", position.Titel);
        Assert.Equal("123", position.BESTAKod);
        Assert.Equal("ABC", position.AIDKod);
        Assert.Equal(PositionStatus.Vakant, position.Status);
        Assert.Equal(35000m, position.BudgeteradManadslon);
        Assert.Equal(100m, position.Sysselsattningsgrad);
        Assert.Null(position.InnehavareAnstallId);
    }

    [Fact]
    public void Tillsatt_SetsStatusToAktiv()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Läkare", 55000m, 100m);
        var anstallId = Guid.NewGuid();

        // Act
        position.Tillsatt(anstallId);

        // Assert
        Assert.Equal(PositionStatus.Aktiv, position.Status);
        Assert.Equal(anstallId, position.InnehavareAnstallId);
        Assert.Single(position.Historik);
    }

    [Fact]
    public void Tillsatt_ThrowsForAvvecklad()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Undersköterska", 28000m, 75m);
        position.Avveckla();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => position.Tillsatt(Guid.NewGuid()));
    }

    [Fact]
    public void Vakansatt_SetsStatusToVakant()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Läkare", 55000m, 100m);
        position.Tillsatt(Guid.NewGuid());

        // Act
        position.Vakansatt("Pension");

        // Assert
        Assert.Equal(PositionStatus.Vakant, position.Status);
        Assert.Null(position.InnehavareAnstallId);
        Assert.Equal(2, position.Historik.Count);
    }

    [Fact]
    public void Frys_SetsStatusToFrysta()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Administratör", 30000m, 100m);

        // Act
        position.Frys();

        // Assert
        Assert.Equal(PositionStatus.Frysta, position.Status);
    }

    [Fact]
    public void Avveckla_SetsStatusAndTimestamp()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Receptionist", 26000m, 50m);

        // Act
        position.Avveckla();

        // Assert
        Assert.Equal(PositionStatus.Avvecklad, position.Status);
        Assert.NotNull(position.AvveckladVid);
    }

    [Fact]
    public void HeadcountPlan_Skapa_SetsCorrectValues()
    {
        // Arrange
        var enhetId = Guid.NewGuid();

        // Act
        var plan = HeadcountPlan.Skapa(enhetId, 2026, 50, 45.5m, 2000000m);

        // Assert
        Assert.NotEqual(Guid.Empty, plan.Id);
        Assert.Equal(enhetId, plan.EnhetId);
        Assert.Equal(2026, plan.Ar);
        Assert.Equal(50, plan.BudgeteradePositioner);
        Assert.Equal(45.5m, plan.BudgeteradFTE);
        Assert.Equal(2000000m, plan.BudgeteradKostnad);
    }

    [Fact]
    public void HeadcountPlan_UppdateraFaktiskt_CalculatesAvvikelse()
    {
        // Arrange
        var plan = HeadcountPlan.Skapa(Guid.NewGuid(), 2026, 50, 45.5m, 2000000m);

        // Act
        plan.UppdateraFaktiskt(48, 44.0m, 2100000m);

        // Assert
        Assert.Equal(48, plan.FaktiskaPositioner);
        Assert.Equal(44.0m, plan.FaktiskFTE);
        Assert.Equal(2100000m, plan.FaktiskKostnad);
        Assert.Equal(100000m, plan.Avvikelse);
    }

    [Fact]
    public void LaggTillKompetenskrav_AddsDeduplicated()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Sjuksköterska", 35000m, 100m);

        // Act
        position.LaggTillKompetenskrav("HLR");
        position.LaggTillKompetenskrav("HLR"); // duplicate
        position.LaggTillKompetenskrav("Brandskydd");

        // Assert
        Assert.Equal(2, position.KravdaKompetenser.Count);
        Assert.Contains("HLR", position.KravdaKompetenser);
        Assert.Contains("Brandskydd", position.KravdaKompetenser);
    }

    [Fact]
    public void SattEftertrardare_SetsPlannedSuccessor()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Avdelningschef", 45000m, 100m);
        var successorId = Guid.NewGuid();

        // Act
        position.SattEftertrardare(successorId);

        // Assert
        Assert.Equal(successorId, position.EftertradarePlanerad);
    }

    [Fact]
    public void Tillsatt_FromFrysta_SetsAktivAndRecordsHistorik()
    {
        // En frusen position ska kunna tillsättas (webb-UI:t erbjuder Tillsätt för Vakant/Frusen).
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "IT-tekniker", 38000m, 100m);
        position.Frys();
        var anstallId = Guid.NewGuid();

        // Act
        position.Tillsatt(anstallId);

        // Assert
        Assert.Equal(PositionStatus.Aktiv, position.Status);
        Assert.Equal(anstallId, position.InnehavareAnstallId);
        Assert.Single(position.Historik);
    }

    [Fact]
    public void UppdateraBudget_SetsNewAmount()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Sjuksköterska", 34500m, 100m);

        // Act
        position.UppdateraBudget(36200m);

        // Assert
        Assert.Equal(36200m, position.BudgeteradManadslon);
    }

    [Fact]
    public void Vakansatt_RecordsAnledningInHistorik()
    {
        // Arrange
        var position = Position.Skapa(Guid.NewGuid(), "Läkare", 62000m, 100m);
        position.Tillsatt(Guid.NewGuid());

        // Act
        position.Vakansatt("Pensionsavgång");

        // Assert
        var senaste = position.Historik.OrderByDescending(h => h.AndradVid).First();
        Assert.Equal("Pensionsavgång", senaste.Anledning);
        Assert.Null(senaste.NyInnehavare);
    }
}
