namespace RegionHR.Wellness.Domain;

public enum WellnessClaimStatus { Inskickad, Godkand, Avslagen }

/// <summary>
/// Friskvårdsbidragsansökan. Kopplad till verklig anställd via AnstallId.
/// Max 5000 kr/år per anställd (standard friskvårdsbidrag).
/// </summary>
public sealed class WellnessClaim
{
    /// <summary>Maximalt friskvårdsbidrag per anställd och kalenderår (Skatteverkets schablon).</summary>
    public const decimal MaxBeloppPerAr = 5000m;

    public Guid Id { get; private set; }
    public Guid AnstallId { get; private set; }
    public string Aktivitet { get; private set; } = default!;
    public decimal Belopp { get; private set; }
    public DateOnly Datum { get; private set; }
    public WellnessClaimStatus Status { get; private set; }
    public string? KvittoFilId { get; private set; }
    public DateTime SkapadVid { get; private set; }
    public Guid? GodkandAv { get; private set; }
    public DateTime? GodkandVid { get; private set; }
    public string? Kommentar { get; private set; }

    private WellnessClaim() { }

    /// <summary>
    /// Skapar en ny ansökan. <paramref name="tidigareGodkantUnderAret"/> är summan av årets
    /// redan godkända ansökningar för den anställde — ansökan avvisas om beloppet skulle
    /// spränga årstaket på <see cref="MaxBeloppPerAr"/> kr.
    /// </summary>
    public static WellnessClaim Skapa(Guid anstallId, string aktivitet, decimal belopp, DateOnly datum, string? kvittoFilId = null, decimal tidigareGodkantUnderAret = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aktivitet);
        if (anstallId == Guid.Empty) throw new ArgumentException("AnstallId krävs.", nameof(anstallId));
        if (belopp <= 0) throw new ArgumentOutOfRangeException(nameof(belopp));
        ValideraArstak(belopp, tidigareGodkantUnderAret);

        return new WellnessClaim
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Aktivitet = aktivitet,
            Belopp = belopp,
            Datum = datum,
            Status = WellnessClaimStatus.Inskickad,
            KvittoFilId = kvittoFilId,
            SkapadVid = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Godkänner ansökan. <paramref name="tidigareGodkantUnderAret"/> är summan av årets
    /// redan godkända ansökningar för den anställde (exklusive denna) — godkännandet
    /// avvisas om årstaket på <see cref="MaxBeloppPerAr"/> kr skulle överskridas.
    /// </summary>
    public void Godkann(Guid godkannare, string? kommentar = null, decimal tidigareGodkantUnderAret = 0)
    {
        if (Status != WellnessClaimStatus.Inskickad)
            throw new InvalidOperationException($"Kan bara godkänna inskickad ansökan. Nuvarande: {Status}");
        ValideraArstak(Belopp, tidigareGodkantUnderAret);
        Status = WellnessClaimStatus.Godkand;
        GodkandAv = godkannare;
        GodkandVid = DateTime.UtcNow;
        Kommentar = kommentar;
    }

    private static void ValideraArstak(decimal belopp, decimal tidigareGodkantUnderAret)
    {
        if (tidigareGodkantUnderAret < 0)
            throw new ArgumentOutOfRangeException(nameof(tidigareGodkantUnderAret));
        if (tidigareGodkantUnderAret + belopp > MaxBeloppPerAr)
            throw new InvalidOperationException(
                $"Friskvårdsbidraget är max {MaxBeloppPerAr:N0} kr per kalenderår. " +
                $"Redan godkänt i år: {tidigareGodkantUnderAret:N0} kr — kvar att nyttja: " +
                $"{Math.Max(0, MaxBeloppPerAr - tidigareGodkantUnderAret):N0} kr.");
    }

    public void Avvisa(Guid godkannare, string kommentar)
    {
        if (Status != WellnessClaimStatus.Inskickad)
            throw new InvalidOperationException($"Kan bara avvisa inskickad ansökan. Nuvarande: {Status}");
        ArgumentException.ThrowIfNullOrWhiteSpace(kommentar);
        Status = WellnessClaimStatus.Avslagen;
        GodkandAv = godkannare;
        GodkandVid = DateTime.UtcNow;
        Kommentar = kommentar;
    }
}
