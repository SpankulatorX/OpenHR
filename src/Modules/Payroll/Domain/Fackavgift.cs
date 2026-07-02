using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>Hur fackavgiften beräknas.</summary>
public enum FackavgiftTyp
{
    /// <summary>Fast avgift per månad (kr).</summary>
    FastBelopp = 0,

    /// <summary>Procent av bruttolönen (0–1).</summary>
    ProcentAvLon = 1
}

/// <summary>
/// Registrerad fackföreningsavgift för en anställd. Avgiften dras som ett nettolöneavdrag
/// (efter skatt) och betalas vidare till fackförbundet. Kan anges antingen som ett fast
/// månadsbelopp eller som en procent av bruttolönen (t.ex. Kommunal ~1 %).
/// Kräver den anställdes medgivande; till skillnad från löneutmätning är den frivillig och
/// är efterställd en ev. utmätning.
/// </summary>
public sealed class Fackavgift
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public EmployeeId AnstallId { get; private set; }

    /// <summary>Fackförbundets namn (t.ex. Kommunal, Vision, Vårdförbundet, SSR).</summary>
    public string Fackforbund { get; private set; } = string.Empty;

    public FackavgiftTyp Typ { get; private set; }

    /// <summary>Fast avgift per månad (används när <see cref="Typ"/> = FastBelopp).</summary>
    public Money Belopp { get; private set; } = Money.Zero;

    /// <summary>Andel av bruttolönen 0–1 (används när <see cref="Typ"/> = ProcentAvLon).</summary>
    public decimal Procent { get; private set; }

    /// <summary>Medlemsnummer hos förbundet (frivilligt).</summary>
    public string? Medlemsnummer { get; private set; }

    public DateOnly Startdatum { get; private set; }
    public DateOnly? Slutdatum { get; private set; }

    public DateTime Registrerad { get; private set; } = DateTime.UtcNow;
    public string? RegistreradAv { get; private set; }

    private Fackavgift() { } // EF Core

    /// <summary>Registrera en fackavgift med fast månadsbelopp.</summary>
    public static Fackavgift SkapaFastBelopp(
        EmployeeId anstallId,
        string fackforbund,
        Money belopp,
        DateOnly startdatum,
        DateOnly? slutdatum = null,
        string? medlemsnummer = null,
        string? registreradAv = null)
    {
        Validera(fackforbund, startdatum, slutdatum);
        if (belopp.Amount <= 0m)
            throw new ArgumentException("Fackavgift måste vara större än 0 kr.", nameof(belopp));

        return new Fackavgift
        {
            AnstallId = anstallId,
            Fackforbund = fackforbund.Trim(),
            Typ = FackavgiftTyp.FastBelopp,
            Belopp = belopp,
            Startdatum = startdatum,
            Slutdatum = slutdatum,
            Medlemsnummer = Rensa(medlemsnummer),
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Registrera en fackavgift som en procent av bruttolönen.</summary>
    public static Fackavgift SkapaProcent(
        EmployeeId anstallId,
        string fackforbund,
        decimal procent,
        DateOnly startdatum,
        DateOnly? slutdatum = null,
        string? medlemsnummer = null,
        string? registreradAv = null)
    {
        Validera(fackforbund, startdatum, slutdatum);
        if (procent <= 0m || procent > 1m)
            throw new ArgumentException("Procentandel måste vara större än 0 och högst 1 (100 %).", nameof(procent));

        return new Fackavgift
        {
            AnstallId = anstallId,
            Fackforbund = fackforbund.Trim(),
            Typ = FackavgiftTyp.ProcentAvLon,
            Procent = procent,
            Startdatum = startdatum,
            Slutdatum = slutdatum,
            Medlemsnummer = Rensa(medlemsnummer),
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Är avgiften aktiv någon gång under den angivna kalendermånaden?</summary>
    public bool ArAktivUnder(DateOnly manadensForstaDag, DateOnly manadensSistaDag) =>
        Startdatum <= manadensSistaDag && (Slutdatum is null || Slutdatum >= manadensForstaDag);

    /// <summary>Beräkna avgiften för månaden givet bruttolönen för perioden.</summary>
    public Money BeraknaAvgift(Money bruttoForManaden) =>
        Typ == FackavgiftTyp.FastBelopp
            ? Belopp
            : (bruttoForManaden * Procent).RoundToOren();

    /// <summary>Avsluta medlemskapet/avgiften från och med angivet datum.</summary>
    public void Avsluta(DateOnly slutdatum)
    {
        if (slutdatum < Startdatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.", nameof(slutdatum));
        Slutdatum = slutdatum;
    }

    private static void Validera(string fackforbund, DateOnly startdatum, DateOnly? slutdatum)
    {
        if (string.IsNullOrWhiteSpace(fackforbund))
            throw new ArgumentException("Fackförbund måste anges.", nameof(fackforbund));
        if (slutdatum is { } slut && slut < startdatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.", nameof(slutdatum));
    }

    private static string? Rensa(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
