using System.ComponentModel.DataAnnotations.Schema;
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

    /// <summary>
    /// Intjänad komptid (övertid) i timmar — <b>brutto</b>, härlett ur stämplat övertidsunderlag.
    /// Detta är hur mycket som tjänats in, inte hur mycket som finns kvar att ta ut;
    /// dra bort <see cref="UttagnaKompTimmar"/> för nettosaldot (<see cref="TillgangligKomptidTimmar"/>).
    /// </summary>
    public decimal KompsaldoTimmar { get; set; }

    /// <summary>
    /// Ackumulerat, godkänt uttag av komptid i timmar (ledighet eller utbetalning).
    /// Persisterad huvudbok som INTE räknas om ur stämplingar — den växer när ett uttag
    /// godkänns och minskar bara om ett uttag återförs. Därför överlever den att
    /// bruttosaldot <see cref="KompsaldoTimmar"/> räknas om.
    /// </summary>
    public decimal UttagnaKompTimmar { get; set; }

    /// <summary>
    /// Tillgänglig komptid att ta ut = intjänat brutto − redan uttaget.
    /// Kan aldrig övertrasseras: <see cref="RegistreraKomputtag"/> vägrar uttag över detta.
    /// </summary>
    [NotMapped]
    public decimal TillgangligKomptidTimmar => Math.Round(KompsaldoTimmar - UttagnaKompTimmar, 2);

    /// <summary>Datum till och med vilket saldot är beräknat.</summary>
    public DateOnly BeraknadTom { get; set; }

    public DateTime UppdateradVid { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Registrerar ett godkänt uttag av komptid och drar det från det tillgängliga saldot.
    /// Systemet är experten: uttaget får aldrig vara noll/negativt och aldrig överstiga
    /// <see cref="TillgangligKomptidTimmar"/> — saldot kan alltså inte övertrasseras.
    /// </summary>
    /// <param name="timmar">Antal timmar som tas ut (positivt).</param>
    /// <exception cref="ArgumentOutOfRangeException">Om <paramref name="timmar"/> ≤ 0.</exception>
    /// <exception cref="InvalidOperationException">Om uttaget överstiger tillgänglig komptid.</exception>
    public void RegistreraKomputtag(decimal timmar)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timmar);

        if (timmar > TillgangligKomptidTimmar)
            throw new InvalidOperationException(
                $"Otillräcklig komptid. Tillgängligt: {TillgangligKomptidTimmar:0.##} h, begärt uttag: {timmar:0.##} h.");

        UttagnaKompTimmar = Math.Round(UttagnaKompTimmar + timmar, 2);
        UppdateradVid = DateTime.UtcNow;
    }

    /// <summary>
    /// Återför ett tidigare uttaget antal timmar till saldot (t.ex. när ett godkänt
    /// uttag återkallas). Går aldrig under noll uttaget.
    /// </summary>
    /// <param name="timmar">Antal timmar som återförs (positivt).</param>
    /// <exception cref="ArgumentOutOfRangeException">Om <paramref name="timmar"/> ≤ 0.</exception>
    public void AterforKomputtag(decimal timmar)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timmar);

        UttagnaKompTimmar = Math.Round(Math.Max(0m, UttagnaKompTimmar - timmar), 2);
        UppdateradVid = DateTime.UtcNow;
    }
}
