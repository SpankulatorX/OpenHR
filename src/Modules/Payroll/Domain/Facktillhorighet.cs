using System.Globalization;
using System.Text;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>Den anställdes roll/status i fackförbundet.</summary>
public enum FacktillhorighetRoll
{
    /// <summary>Vanlig medlem.</summary>
    Medlem = 0,

    /// <summary>Förtroendevald (t.ex. sektionsordförande, klubbstyrelse).</summary>
    Fortroendevald = 1,

    /// <summary>Skyddsombud.</summary>
    Skyddsombud = 2,

    /// <summary>Huvudskyddsombud.</summary>
    Huvudskyddsombud = 3
}

/// <summary>
/// Registrerad facktillhörighet (fackförbunds-medlemskap) för en anställd. Detta är
/// SJÄLVA medlemskapet — vilket förbund den anställde tillhör och i vilken roll — till
/// skillnad från <see cref="Fackavgift"/> som är avdraget på lönen. Facktillhörigheten
/// används dels internt (partsförhållanden, MBL, skyddsombud) och dels för att periodiskt
/// rapportera medlemsstocken till respektive fackförbund via en uppdateringsfil.
/// </summary>
public sealed class Facktillhorighet
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public EmployeeId AnstallId { get; private set; }

    /// <summary>Fackförbundets namn (t.ex. Kommunal, Vision, Vårdförbundet, Sveriges läkarförbund).</summary>
    public string Fackforbund { get; private set; } = string.Empty;

    /// <summary>Förbundets avtalsparts-/organisationskod som används i uppdateringsfilen (frivillig).</summary>
    public string? FackforbundKod { get; private set; }

    /// <summary>Medlemsnummer hos förbundet (frivilligt).</summary>
    public string? Medlemsnummer { get; private set; }

    public FacktillhorighetRoll Roll { get; private set; }

    /// <summary>Avtalsområde medlemskapet knyts till (t.ex. AB, HÖK, PAN). Frivilligt.</summary>
    public string? Avtalsomrade { get; private set; }

    public DateOnly Startdatum { get; private set; }
    public DateOnly? Slutdatum { get; private set; }

    public DateTime Registrerad { get; private set; } = DateTime.UtcNow;
    public string? RegistreradAv { get; private set; }

    private Facktillhorighet() { } // EF Core

    /// <summary>Registrera en facktillhörighet (medlemskap) för en anställd.</summary>
    public static Facktillhorighet Skapa(
        EmployeeId anstallId,
        string fackforbund,
        DateOnly startdatum,
        FacktillhorighetRoll roll = FacktillhorighetRoll.Medlem,
        string? fackforbundKod = null,
        string? medlemsnummer = null,
        string? avtalsomrade = null,
        DateOnly? slutdatum = null,
        string? registreradAv = null)
    {
        if (string.IsNullOrWhiteSpace(fackforbund))
            throw new ArgumentException("Fackförbund måste anges.", nameof(fackforbund));
        if (slutdatum is { } slut && slut < startdatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.", nameof(slutdatum));

        return new Facktillhorighet
        {
            AnstallId = anstallId,
            Fackforbund = fackforbund.Trim(),
            FackforbundKod = Rensa(fackforbundKod),
            Medlemsnummer = Rensa(medlemsnummer),
            Roll = roll,
            Avtalsomrade = Rensa(avtalsomrade),
            Startdatum = startdatum,
            Slutdatum = slutdatum,
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Är medlemskapet aktivt någon gång under den angivna kalendermånaden?</summary>
    public bool ArAktivUnder(DateOnly manadensForstaDag, DateOnly manadensSistaDag) =>
        Startdatum <= manadensSistaDag && (Slutdatum is null || Slutdatum >= manadensForstaDag);

    /// <summary>Är medlemskapet aktivt (ej avslutat) per angivet datum?</summary>
    public bool ArAktivPer(DateOnly datum) =>
        Startdatum <= datum && (Slutdatum is null || Slutdatum >= datum);

    /// <summary>Avsluta medlemskapet från och med angivet datum (utträde).</summary>
    public void Avsluta(DateOnly slutdatum)
    {
        if (slutdatum < Startdatum)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.", nameof(slutdatum));
        Slutdatum = slutdatum;
    }

    /// <summary>Byt roll (t.ex. utses till skyddsombud).</summary>
    public void SattRoll(FacktillhorighetRoll roll) => Roll = roll;

    private static string? Rensa(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>En rad i uppdateringsfilen till fackförbundet.</summary>
public sealed record FacktillhorighetRad(
    string Personnummer,
    string Namn,
    string Fackforbund,
    string? FackforbundKod,
    string? Medlemsnummer,
    FacktillhorighetRoll Roll,
    bool Aktiv,
    DateOnly Startdatum,
    DateOnly? Slutdatum);

/// <summary>Indata till <see cref="FacktillhorighetFilGenerator"/>.</summary>
public sealed record FacktillhorighetFilInput(
    string Organisationsnummer,
    string ArbetsgivareNamn,
    DateOnly Uppdateringsdatum,
    IReadOnlyList<FacktillhorighetRad> Rader);

/// <summary>Genererad fil för uppdatering av facktillhörighet till fackförbund.</summary>
public sealed record FacktillhorighetFil(string FileName, byte[] Content, string MimeType);

/// <summary>
/// Self-contained generator som bygger en fil för uppdatering av facktillhörighet till
/// fackförbund. Formatet är en semikolonseparerad textfil (fältseparator ';') med en
/// kommentarsheader, en kolumnrubrik, en rad per medlem och en avslutande summeringsrad.
/// Filen kodas i ISO-8859-1 (Latin1) — den teckenkodning som svenska förbunds
/// medlemsregister-importer typiskt förväntar sig — och alla datum/tal skrivs med
/// invariant kultur (ISO-datum yyyy-MM-dd).
/// </summary>
public sealed class FacktillhorighetFilGenerator
{
    private const char Sep = ';';

    public FacktillhorighetFil Generera(FacktillhorighetFilInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Organisationsnummer))
            throw new ArgumentException("Organisationsnummer måste anges.", nameof(input));

        var innehall = ByggInnehall(input);
        var bytes = Encoding.Latin1.GetBytes(innehall);
        var datum = input.Uppdateringsdatum.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var fileName = $"FACKTILLHORIGHET_{Rensa(input.Organisationsnummer)}_{datum}.csv";
        return new FacktillhorighetFil(fileName, bytes, "text/csv");
    }

    /// <summary>Bygger textinnehållet (exponerat för test).</summary>
    internal string ByggInnehall(FacktillhorighetFilInput input)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        // Kommentarsheader: #H;orgnr;arbetsgivare;uppdateringsdatum;antal
        sb.Append("#H").Append(Sep)
          .Append(Rensa(input.Organisationsnummer)).Append(Sep)
          .Append(F(input.ArbetsgivareNamn)).Append(Sep)
          .Append(input.Uppdateringsdatum.ToString("yyyy-MM-dd", ci)).Append(Sep)
          .Append(input.Rader.Count.ToString(ci))
          .Append('\n');

        // Kolumnrubrik
        sb.Append("Personnummer").Append(Sep)
          .Append("Namn").Append(Sep)
          .Append("Forbund").Append(Sep)
          .Append("ForbundKod").Append(Sep)
          .Append("Medlemsnummer").Append(Sep)
          .Append("Roll").Append(Sep)
          .Append("Status").Append(Sep)
          .Append("Startdatum").Append(Sep)
          .Append("Slutdatum")
          .Append('\n');

        foreach (var r in input.Rader)
        {
            sb.Append(F(r.Personnummer)).Append(Sep)
              .Append(F(r.Namn)).Append(Sep)
              .Append(F(r.Fackforbund)).Append(Sep)
              .Append(F(r.FackforbundKod)).Append(Sep)
              .Append(F(r.Medlemsnummer)).Append(Sep)
              .Append(RollText(r.Roll)).Append(Sep)
              .Append(r.Aktiv ? "AKTIV" : "AVSLUTAD").Append(Sep)
              .Append(r.Startdatum.ToString("yyyy-MM-dd", ci)).Append(Sep)
              .Append(r.Slutdatum?.ToString("yyyy-MM-dd", ci) ?? string.Empty)
              .Append('\n');
        }

        // Avslutande summeringsrad
        sb.Append("#S").Append(Sep).Append(input.Rader.Count.ToString(ci)).Append('\n');
        return sb.ToString();
    }

    private static string RollText(FacktillhorighetRoll roll) => roll switch
    {
        FacktillhorighetRoll.Medlem => "MEDLEM",
        FacktillhorighetRoll.Fortroendevald => "FORTROENDEVALD",
        FacktillhorighetRoll.Skyddsombud => "SKYDDSOMBUD",
        FacktillhorighetRoll.Huvudskyddsombud => "HUVUDSKYDDSOMBUD",
        _ => "MEDLEM"
    };

    // Tar bort fältseparator och radbrytningar ur fritext så filstrukturen inte bryts.
    private static string F(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Rensa(string? s) => (s ?? string.Empty).Replace(" ", string.Empty).Trim();
}
