using RegionHR.SharedKernel.Domain;

namespace RegionHR.Scheduling.Domain;

/// <summary>
/// Sparad ögonblicksbild av en anställds flex- och kompsaldo.
/// Beräknas ur stämplingar (faktisk tid) jämfört med schemalagd tid av <see cref="FlexCalculator"/>.
/// Saldot härleds alltid ur underlaget; denna entitet är en cache som uppdateras
/// när det räknas om, så att andra vyer slipper räkna om varje gång.
/// </summary>
public sealed class FlexBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EmployeeId AnstallId { get; set; }

    /// <summary>Aktuellt flexsaldo i timmar (kan vara negativt).</summary>
    public decimal SaldoTimmar { get; set; }

    /// <summary>Sparad komptid (övertid som tas ut i ledighet) i timmar.</summary>
    public decimal KompsaldoTimmar { get; set; }

    /// <summary>Datum till och med vilket saldot är beräknat.</summary>
    public DateOnly BeraknadTom { get; set; }

    public DateTime UppdateradVid { get; set; } = DateTime.UtcNow;
}
