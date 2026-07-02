namespace RegionHR.LMS.Domain;

/// <summary>
/// Livscykel för en extern kursdeltagare. <see cref="Inbjuden"/> = registrerad men
/// har ännu inte öppnat sin access-länk; <see cref="Aktiv"/> = har använt länken;
/// <see cref="Inaktiverad"/> = access återkallad.
/// </summary>
public enum ExternalParticipantStatus { Inbjuden, Aktiv, Inaktiverad }

/// <summary>
/// En extern kursdeltagare — en person UTAN anställning i regionen (t.ex. konsult,
/// entreprenör, förtroendevald, elev) som bjuds in till en kurs. RFI:n kräver att
/// utbildningsportalen hanterar "både interna och externa" deltagare (Grade-ersättning).
///
/// FÖRENKLAT: access sker via ett slumpat <see cref="AccessToken"/> (delas som länk
/// per e-post). Detta är en "enkel access"-modell, INTE en fullständig extern
/// identitetslösning — ingen federerad inloggning (BankID/eduID/IdP), inget lösenord,
/// ingen e-postverifiering. Token = bärartoken; i skarp drift bör länken vara
/// tidsbegränsad och e-postutskicket ske via NotificationEndpoints.
/// </summary>
public class ExternalParticipant
{
    public Guid Id { get; private set; }
    public string Epost { get; private set; } = "";
    public string Namn { get; private set; } = "";
    public string? Organisation { get; private set; }
    public string AccessToken { get; private set; } = "";
    public ExternalParticipantStatus Status { get; private set; }
    public DateTime InbjudenVid { get; private set; }
    public DateTime? SenastAktivVid { get; private set; }

    private ExternalParticipant() { }

    public static ExternalParticipant Bjudin(string epost, string namn, string? organisation = null)
    {
        if (!ArGiltigEpost(epost)) throw new ArgumentException("Ogiltig e-postadress", nameof(epost));
        if (string.IsNullOrWhiteSpace(namn)) throw new ArgumentException("Namn krävs", nameof(namn));

        return new ExternalParticipant
        {
            Id = Guid.NewGuid(),
            Epost = epost.Trim().ToLowerInvariant(),
            Namn = namn.Trim(),
            Organisation = string.IsNullOrWhiteSpace(organisation) ? null : organisation.Trim(),
            AccessToken = GeneraToken(),
            Status = ExternalParticipantStatus.Inbjuden,
            InbjudenVid = DateTime.UtcNow
        };
    }

    /// <summary>Registrerar att deltagaren använt sin access-länk (uppgraderar Inbjuden → Aktiv).</summary>
    public void RegistreraAktivitet()
    {
        SenastAktivVid = DateTime.UtcNow;
        if (Status == ExternalParticipantStatus.Inbjuden) Status = ExternalParticipantStatus.Aktiv;
    }

    /// <summary>Återkallar access. En inaktiverad deltagare kan inte längre nå kursen via sin token.</summary>
    public void Inaktivera() => Status = ExternalParticipantStatus.Inaktiverad;

    /// <summary>Återaktiverar en tidigare inaktiverad deltagare.</summary>
    public void Ateraktivera()
    {
        if (Status == ExternalParticipantStatus.Inaktiverad) Status = ExternalParticipantStatus.Inbjuden;
    }

    /// <summary>Roterar access-token (t.ex. vid misstänkt läckt länk). Gammal länk slutar fungera.</summary>
    public void NyttAccessToken() => AccessToken = GeneraToken();

    /// <summary>Kan deltagaren nå kurser via sin access-token just nu?</summary>
    public bool HarAktivAccess => Status != ExternalParticipantStatus.Inaktiverad;

    private static string GeneraToken() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static bool ArGiltigEpost(string epost)
    {
        if (string.IsNullOrWhiteSpace(epost)) return false;
        epost = epost.Trim();
        var at = epost.IndexOf('@');
        if (at <= 0 || at != epost.LastIndexOf('@')) return false;
        if (at == epost.Length - 1) return false;
        var doman = epost[(at + 1)..];
        return doman.Contains('.') && !doman.StartsWith('.') && !doman.EndsWith('.') && !doman.Contains(' ');
    }
}
