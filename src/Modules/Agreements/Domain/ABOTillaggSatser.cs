using RegionHR.SharedKernel.Domain;

namespace RegionHR.Agreements.Domain;

/// <summary>
/// En årsversionerad uppsättning O-tilläggssatser (kr/tim) enligt
/// AB (Allmänna bestämmelser) § 21 mom. 1.
///
/// O-tilläggstiderna delas i fyra kategorier:
///   A = storhelg, B = helg, C = vardagsnatt, D = vardagskväll.
/// För kategori A och B höjs satsen under natt (kl. 22.00–06.00) mot helg/helgdag.
/// </summary>
public sealed record ABOTillaggSats(
    DateOnly GiltigFran,
    decimal StorhelgA,      // O-tilläggstid A (dagtid)
    decimal StorhelgANatt,  // O-tilläggstid A höjd kl. 22.00–06.00 natt mot helgdag
    decimal HelgB,          // O-tilläggstid B (dagtid)
    decimal HelgBNatt,      // O-tilläggstid B höjd kl. 22.00–06.00 natt mot lör/sön/helgdag
    decimal VardagNattC,    // O-tilläggstid C
    decimal VardagKvallD);  // O-tilläggstid D

/// <summary>
/// Kanoniska, årsversionerade O-tilläggssatser enligt AB § 21 mom. 1.
///
/// KÄLLA: SKR "Allmänna Bestämmelser (AB) 25 i lydelse 2025-04-01", § 21.
/// Satserna räknas upp per avtalsår och gäller fr.o.m. 1 april respektive år.
///
/// Detta är den enda auktoritativa källan för AB/HÖK O-tillägg i systemet.
/// Både <see cref="CollectiveAgreementType.AB"/> och <see cref="CollectiveAgreementType.HOK"/>
/// följer dessa satser eftersom HÖK inkorporerar AB § 21.
/// </summary>
public static class ABOTillaggSatser
{
    // Ordnad fallande på GiltigFran så att första posten med GiltigFran <= datum gäller.
    private static readonly ABOTillaggSats[] Tabell =
    [
        // AB § 21 mom. 1 — gäller fr.o.m. 2026-04-01
        new(new DateOnly(2026, 4, 1),
            StorhelgA: 130.70m, StorhelgANatt: 156.90m,
            HelgB: 68.10m, HelgBNatt: 78.30m,
            VardagNattC: 58.40m, VardagKvallD: 26.40m),

        // AB § 21 mom. 1 — gäller fr.o.m. 2025-04-01
        new(new DateOnly(2025, 4, 1),
            StorhelgA: 126.90m, StorhelgANatt: 152.30m,
            HelgB: 66.10m, HelgBNatt: 76.00m,
            VardagNattC: 56.70m, VardagKvallD: 25.60m),
    ];

    /// <summary>Äldsta kända avtalsversion (används som golv för datum före första posten).</summary>
    public static DateOnly ArligtGolvdatum => Tabell[^1].GiltigFran;

    /// <summary>Hämtar den O-tilläggstabell som gäller för ett givet datum.</summary>
    public static ABOTillaggSats ForDatum(DateOnly datum)
    {
        foreach (var sats in Tabell)
        {
            if (datum >= sats.GiltigFran)
                return sats;
        }
        // Datum före första kända avtalsversionen → använd äldsta kända tabellen.
        return Tabell[^1];
    }

    /// <summary>
    /// Grundsats (dagtid) i kr/tim för en O-tilläggskategori enligt AB § 21 mom. 1.
    /// Returnerar 0 för <see cref="OBCategory.Ingen"/>.
    /// </summary>
    public static decimal Grundsats(OBCategory kategori, DateOnly datum)
    {
        var s = ForDatum(datum);
        return kategori switch
        {
            OBCategory.VardagKvall => s.VardagKvallD, // O-tilläggstid D
            OBCategory.VardagNatt => s.VardagNattC,   // O-tilläggstid C
            OBCategory.Helg => s.HelgB,               // O-tilläggstid B
            OBCategory.Storhelg => s.StorhelgA,       // O-tilläggstid A
            _ => 0m
        };
    }

    /// <summary>
    /// Nattsats (kl. 22.00–06.00) i kr/tim. För kategori A (storhelg) och B (helg)
    /// höjs O-tillägget nattetid. Övriga kategorier saknar natthöjning och returnerar grundsatsen.
    /// </summary>
    public static decimal Nattsats(OBCategory kategori, DateOnly datum)
    {
        var s = ForDatum(datum);
        return kategori switch
        {
            OBCategory.Storhelg => s.StorhelgANatt,
            OBCategory.Helg => s.HelgBNatt,
            OBCategory.VardagNatt => s.VardagNattC,
            OBCategory.VardagKvall => s.VardagKvallD,
            _ => 0m
        };
    }

    /// <summary>
    /// Väljer korrekt sats för en kategori och en specifik timme på dygnet.
    /// Under kl. 22.00–06.00 tillämpas natthöjningen för storhelg (A) och helg (B).
    /// </summary>
    public static decimal SatsForTimme(OBCategory kategori, DateOnly datum, TimeOnly tid)
    {
        var arNatt = tid >= new TimeOnly(22, 0) || tid < new TimeOnly(6, 0);
        if (arNatt && kategori is OBCategory.Storhelg or OBCategory.Helg)
            return Nattsats(kategori, datum);
        return Grundsats(kategori, datum);
    }
}
