using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>Hur Kronofogdens utmätningsbelopp uttrycks i beslutet.</summary>
public enum UtmatningTyp
{
    /// <summary>Fast utmätningsbelopp per månad ur KFM-beslutet.</summary>
    FastBelopp = 0,

    /// <summary>Andel av nettolönen (0–1) som ska innehållas.</summary>
    AndelAvNetto = 1
}

/// <summary>
/// Aktiv löneutmätning (införsel i lön) för en anställd enligt Kronofogdemyndighetens beslut
/// (Utsökningsbalken 7 kap.). Arbetsgivaren är enligt lag skyldig att innehålla utmätningsbeloppet
/// ur lönen och betala det till KFM — men lönen efter skatt får ALDRIG understiga det av KFM
/// fastställda FÖRBEHÅLLSBELOPPET (existensminimum: normalbelopp + boendekostnad m.m.).
///
/// Systemet är experten: <see cref="BeraknaAvdrag"/> kapar alltid avdraget mot vad som finns
/// kvar över förbehållsbeloppet, så att lagkravet respekteras oavsett vad HR matar in.
/// </summary>
public sealed class Loneutmatning
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public EmployeeId AnstallId { get; private set; }

    /// <summary>KFM:s mål-/ärendenummer (referens till beslutet).</summary>
    public string Malnummer { get; private set; } = string.Empty;

    public UtmatningTyp Typ { get; private set; }

    /// <summary>Fastställt utmätningsbelopp per månad (används när <see cref="Typ"/> = FastBelopp).</summary>
    public Money Belopp { get; private set; } = Money.Zero;

    /// <summary>Andel av nettolönen 0–1 (används när <see cref="Typ"/> = AndelAvNetto).</summary>
    public decimal Andel { get; private set; }

    /// <summary>Förbehållsbelopp: den summa den anställde minst ska ha kvar efter skatt varje månad.</summary>
    public Money Forbehallsbelopp { get; private set; } = Money.Zero;

    /// <summary>Mottagare av det innehållna beloppet (normalt Kronofogdemyndigheten), ev. med bankgiro/referens.</summary>
    public string? Mottagare { get; private set; }

    public DateOnly Startdatum { get; private set; }
    public DateOnly? Slutdatum { get; private set; }

    public DateTime Registrerad { get; private set; } = DateTime.UtcNow;
    public string? RegistreradAv { get; private set; }
    public string? Avslutsorsak { get; private set; }

    private Loneutmatning() { } // EF Core

    /// <summary>Registrera en utmätning med fast månadsbelopp ur KFM-beslutet.</summary>
    public static Loneutmatning SkapaFastBelopp(
        EmployeeId anstallId,
        string malnummer,
        Money belopp,
        Money forbehallsbelopp,
        DateOnly startdatum,
        DateOnly? slutdatum = null,
        string? mottagare = null,
        string? registreradAv = null)
    {
        Validera(malnummer, forbehallsbelopp, startdatum, slutdatum);
        if (belopp.Amount <= 0m)
            throw new ArgumentException("Utmätningsbelopp måste vara större än 0 kr.", nameof(belopp));

        return new Loneutmatning
        {
            AnstallId = anstallId,
            Malnummer = malnummer.Trim(),
            Typ = UtmatningTyp.FastBelopp,
            Belopp = belopp,
            Forbehallsbelopp = forbehallsbelopp,
            Startdatum = startdatum,
            Slutdatum = slutdatum,
            Mottagare = Rensa(mottagare),
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Registrera en utmätning som en andel av nettolönen.</summary>
    public static Loneutmatning SkapaAndel(
        EmployeeId anstallId,
        string malnummer,
        decimal andel,
        Money forbehallsbelopp,
        DateOnly startdatum,
        DateOnly? slutdatum = null,
        string? mottagare = null,
        string? registreradAv = null)
    {
        Validera(malnummer, forbehallsbelopp, startdatum, slutdatum);
        if (andel <= 0m || andel > 1m)
            throw new ArgumentException("Andel måste vara större än 0 och högst 1 (100 %).", nameof(andel));

        return new Loneutmatning
        {
            AnstallId = anstallId,
            Malnummer = malnummer.Trim(),
            Typ = UtmatningTyp.AndelAvNetto,
            Andel = andel,
            Forbehallsbelopp = forbehallsbelopp,
            Startdatum = startdatum,
            Slutdatum = slutdatum,
            Mottagare = Rensa(mottagare),
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Är utmätningen aktiv någon gång under den angivna kalendermånaden?</summary>
    public bool ArAktivUnder(DateOnly manadensForstaDag, DateOnly manadensSistaDag) =>
        Startdatum <= manadensSistaDag && (Slutdatum is null || Slutdatum >= manadensForstaDag);

    /// <summary>
    /// Beräkna det belopp som ska innehållas givet nettolönen INNAN utmätning.
    /// Aldrig mer än vad som finns kvar över förbehållsbeloppet; aldrig negativt.
    /// </summary>
    public Money BeraknaAvdrag(Money nettoInnanUtmatning)
    {
        var tillgangligt = nettoInnanUtmatning - Forbehallsbelopp;
        if (tillgangligt.Amount <= 0m)
            return Money.Zero;

        var begart = Typ == UtmatningTyp.FastBelopp
            ? Belopp
            : (nettoInnanUtmatning * Andel).RoundToOren();

        return begart <= tillgangligt ? begart : tillgangligt;
    }

    /// <summary>Avsluta utmätningen (t.ex. när skulden är betald eller KFM återkallar beslutet).</summary>
    public void Avsluta(DateOnly slutdatum, string? orsak = null)
    {
        if (slutdatum < Startdatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.", nameof(slutdatum));
        Slutdatum = slutdatum;
        Avslutsorsak = Rensa(orsak);
    }

    private static void Validera(string malnummer, Money forbehallsbelopp, DateOnly startdatum, DateOnly? slutdatum)
    {
        if (string.IsNullOrWhiteSpace(malnummer))
            throw new ArgumentException("KFM mål-/ärendenummer måste anges.", nameof(malnummer));
        if (forbehallsbelopp.Amount < 0m)
            throw new ArgumentException("Förbehållsbelopp kan inte vara negativt.", nameof(forbehallsbelopp));
        if (slutdatum is { } slut && slut < startdatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.", nameof(slutdatum));
    }

    private static string? Rensa(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
