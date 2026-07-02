using System.Globalization;
using System.Text;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>Typ av tolk-/översättningsuppdrag.</summary>
public enum TolkuppdragTyp
{
    /// <summary>Kontakttolkning på plats.</summary>
    Kontakttolkning = 0,

    /// <summary>Telefontolkning.</summary>
    Telefontolkning = 1,

    /// <summary>Distans-/videotolkning.</summary>
    Distanstolkning = 2,

    /// <summary>Skriftlig översättning.</summary>
    SkriftligOversattning = 3
}

/// <summary>Var i flödet uppdraget/ersättningen befinner sig.</summary>
public enum TolkersattningStatus
{
    Registrerad = 0,
    Godkand = 1,
    Utbetald = 2
}

/// <summary>
/// Ett tolk-/översättningsuppdrag med tillhörande ersättning. Detta är ett eget
/// ersättningsflöde för externa tolkar/översättare (inte anställda): uppdrag → ersättning →
/// utbetalningsunderlag. Ersättningen består av arvode (timmar × timarvode + ev.
/// förberedelsearvode) plus skattefri reseersättning.
///
/// Systemet är experten: när tolken debiterar via egen firma med F-skatt görs INGET
/// preliminärskatteavdrag (betalas mot faktura), annars innehålls preliminärskatt på
/// arvodet enligt <see cref="Skattesats"/>. Reseersättningen är alltid skattefri.
/// </summary>
public sealed class Tolkersattning
{
    /// <summary>Standard preliminärskattesats på arvode utan F-skatt (30 % engångs-/sidoinkomstskatt).</summary>
    public const decimal StandardSkattesats = 0.30m;

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Tolkens/översättarens namn eller firmanamn.</summary>
    public string TolkNamn { get; private set; } = string.Empty;

    /// <summary>Tolkens personnummer (frivilligt; saknas ofta för firma med F-skatt).</summary>
    public string? TolkPersonnummer { get; private set; }

    /// <summary>Språk uppdraget avser (t.ex. "Arabiska", "Somaliska", "Ukrainska").</summary>
    public string Sprak { get; private set; } = string.Empty;

    public TolkuppdragTyp Typ { get; private set; }

    /// <summary>Datum uppdraget utfördes.</summary>
    public DateOnly Uppdragsdatum { get; private set; }

    /// <summary>Beställande enhet/verksamhet (frivillig).</summary>
    public string? BestallandeEnhet { get; private set; }

    /// <summary>Uppdrags-/beställningsreferens (frivillig).</summary>
    public string? Referens { get; private set; }

    /// <summary>Antal debiterbara timmar (för översättning kan detta uttrycka nedlagd tid).</summary>
    public decimal AntalTimmar { get; private set; }

    /// <summary>Timarvode (kr/timme).</summary>
    public Money Timarvode { get; private set; } = Money.Zero;

    /// <summary>Fast förberedelse-/framinställelsearvode (frivilligt).</summary>
    public Money Forberedelsearvode { get; private set; } = Money.Zero;

    /// <summary>Skattefri reseersättning för uppdraget.</summary>
    public Money Reseersattning { get; private set; } = Money.Zero;

    /// <summary>Sant om tolken fakturerar via egen firma med F-skatt (inget skatteavdrag).</summary>
    public bool HarFSkatt { get; private set; }

    /// <summary>Preliminärskattesats som tillämpas på arvodet när F-skatt saknas.</summary>
    public decimal Skattesats { get; private set; } = StandardSkattesats;

    public TolkersattningStatus Status { get; private set; } = TolkersattningStatus.Registrerad;

    public DateTime Registrerad { get; private set; } = DateTime.UtcNow;
    public string? RegistreradAv { get; private set; }

    private Tolkersattning() { } // EF Core

    /// <summary>Registrera ett tolk-/översättningsuppdrag med ersättning.</summary>
    public static Tolkersattning Skapa(
        string tolkNamn,
        string sprak,
        TolkuppdragTyp typ,
        DateOnly uppdragsdatum,
        decimal antalTimmar,
        Money timarvode,
        Money? forberedelsearvode = null,
        Money? reseersattning = null,
        bool harFSkatt = false,
        decimal? skattesats = null,
        string? tolkPersonnummer = null,
        string? bestallandeEnhet = null,
        string? referens = null,
        string? registreradAv = null)
    {
        if (string.IsNullOrWhiteSpace(tolkNamn))
            throw new ArgumentException("Tolkens namn måste anges.", nameof(tolkNamn));
        if (string.IsNullOrWhiteSpace(sprak))
            throw new ArgumentException("Språk måste anges.", nameof(sprak));
        if (antalTimmar < 0m)
            throw new ArgumentException("Antal timmar kan inte vara negativt.", nameof(antalTimmar));
        if (timarvode.Amount < 0m)
            throw new ArgumentException("Timarvode kan inte vara negativt.", nameof(timarvode));

        var sats = skattesats ?? StandardSkattesats;
        if (sats < 0m || sats > 1m)
            throw new ArgumentException("Skattesats måste vara mellan 0 och 1.", nameof(skattesats));

        return new Tolkersattning
        {
            TolkNamn = tolkNamn.Trim(),
            Sprak = sprak.Trim(),
            Typ = typ,
            Uppdragsdatum = uppdragsdatum,
            AntalTimmar = antalTimmar,
            Timarvode = timarvode,
            Forberedelsearvode = forberedelsearvode ?? Money.Zero,
            Reseersattning = reseersattning ?? Money.Zero,
            HarFSkatt = harFSkatt,
            Skattesats = sats,
            TolkPersonnummer = Rensa(tolkPersonnummer),
            BestallandeEnhet = Rensa(bestallandeEnhet),
            Referens = Rensa(referens),
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Skattepliktigt arvode: timmar × timarvode + förberedelsearvode.</summary>
    public Money BeraknaArvode() =>
        ((Timarvode * AntalTimmar) + Forberedelsearvode).RoundToOren();

    /// <summary>Preliminärskatteavdrag på arvodet (0 kr när tolken har F-skatt).</summary>
    public Money BeraknaSkatt() =>
        HarFSkatt ? Money.Zero : (BeraknaArvode() * Skattesats).RoundToOren();

    /// <summary>Bruttoersättning: arvode + skattefri reseersättning.</summary>
    public Money BeraknaBrutto() =>
        (BeraknaArvode() + Reseersattning).RoundToOren();

    /// <summary>Nettoersättning att betala ut: brutto − skatt.</summary>
    public Money BeraknaNetto() =>
        (BeraknaBrutto() - BeraknaSkatt()).RoundToOren();

    /// <summary>Godkänn uppdraget för utbetalning.</summary>
    public void Godkann()
    {
        if (Status == TolkersattningStatus.Utbetald)
            throw new InvalidOperationException("Ersättningen är redan utbetald.");
        Status = TolkersattningStatus.Godkand;
    }

    /// <summary>Markera ersättningen som utbetald (efter att underlag skapats).</summary>
    public void MarkeraUtbetald()
    {
        if (Status == TolkersattningStatus.Registrerad)
            throw new InvalidOperationException("Ersättningen måste godkännas innan den kan markeras utbetald.");
        Status = TolkersattningStatus.Utbetald;
    }

    private static string? Rensa(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Indata till <see cref="TolkersattningUnderlagGenerator"/>.</summary>
public sealed record TolkersattningUnderlagInput(
    string Organisationsnummer,
    string ArbetsgivareNamn,
    string Period,
    DateOnly Utbetalningsdatum,
    IReadOnlyList<Tolkersattning> Poster);

/// <summary>Genererat utbetalningsunderlag för tolkersättning.</summary>
public sealed record TolkersattningUnderlagFil(string FileName, byte[] Content, string MimeType);

/// <summary>
/// Self-contained generator som bygger ett utbetalningsunderlag (semikolonseparerad CSV,
/// ISO-8859-1) för tolk-/översättningsersättningar under en period. Underlaget innehåller
/// en rad per uppdrag med arvode, reseersättning, skatt och netto, samt en summeringsrad.
/// Alla tal skrivs med invariant kultur (punkt som decimaltecken, två decimaler).
/// </summary>
public sealed class TolkersattningUnderlagGenerator
{
    private const char Sep = ';';

    public TolkersattningUnderlagFil Generera(TolkersattningUnderlagInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Organisationsnummer))
            throw new ArgumentException("Organisationsnummer måste anges.", nameof(input));

        var innehall = ByggInnehall(input);
        var bytes = Encoding.Latin1.GetBytes(innehall);
        var period = (input.Period ?? string.Empty).Replace("-", string.Empty);
        var fileName = $"TOLKERSATTNING_{Rensa(input.Organisationsnummer)}_{period}.csv";
        return new TolkersattningUnderlagFil(fileName, bytes, "text/csv");
    }

    /// <summary>Bygger textinnehållet (exponerat för test).</summary>
    internal string ByggInnehall(TolkersattningUnderlagInput input)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append("#H").Append(Sep)
          .Append(Rensa(input.Organisationsnummer)).Append(Sep)
          .Append(F(input.ArbetsgivareNamn)).Append(Sep)
          .Append(F(input.Period)).Append(Sep)
          .Append(input.Utbetalningsdatum.ToString("yyyy-MM-dd", ci)).Append(Sep)
          .Append(input.Poster.Count.ToString(ci))
          .Append('\n');

        sb.Append("Uppdragsdatum").Append(Sep)
          .Append("Tolk").Append(Sep)
          .Append("Personnummer").Append(Sep)
          .Append("Sprak").Append(Sep)
          .Append("Uppdragstyp").Append(Sep)
          .Append("Timmar").Append(Sep)
          .Append("Arvode").Append(Sep)
          .Append("Reseersattning").Append(Sep)
          .Append("Brutto").Append(Sep)
          .Append("Skatt").Append(Sep)
          .Append("Netto").Append(Sep)
          .Append("FSkatt")
          .Append('\n');

        decimal totBrutto = 0m, totSkatt = 0m, totNetto = 0m;
        foreach (var p in input.Poster)
        {
            var brutto = p.BeraknaBrutto().Amount;
            var skatt = p.BeraknaSkatt().Amount;
            var netto = p.BeraknaNetto().Amount;
            totBrutto += brutto;
            totSkatt += skatt;
            totNetto += netto;

            sb.Append(p.Uppdragsdatum.ToString("yyyy-MM-dd", ci)).Append(Sep)
              .Append(F(p.TolkNamn)).Append(Sep)
              .Append(F(p.TolkPersonnummer)).Append(Sep)
              .Append(F(p.Sprak)).Append(Sep)
              .Append(TypText(p.Typ)).Append(Sep)
              .Append(p.AntalTimmar.ToString("0.##", ci)).Append(Sep)
              .Append(p.BeraknaArvode().Amount.ToString("0.00", ci)).Append(Sep)
              .Append(p.Reseersattning.Amount.ToString("0.00", ci)).Append(Sep)
              .Append(brutto.ToString("0.00", ci)).Append(Sep)
              .Append(skatt.ToString("0.00", ci)).Append(Sep)
              .Append(netto.ToString("0.00", ci)).Append(Sep)
              .Append(p.HarFSkatt ? "JA" : "NEJ")
              .Append('\n');
        }

        sb.Append("#S").Append(Sep)
          .Append(input.Poster.Count.ToString(ci)).Append(Sep)
          .Append(totBrutto.ToString("0.00", ci)).Append(Sep)
          .Append(totSkatt.ToString("0.00", ci)).Append(Sep)
          .Append(totNetto.ToString("0.00", ci))
          .Append('\n');

        return sb.ToString();
    }

    private static string TypText(TolkuppdragTyp typ) => typ switch
    {
        TolkuppdragTyp.Kontakttolkning => "KONTAKTTOLKNING",
        TolkuppdragTyp.Telefontolkning => "TELEFONTOLKNING",
        TolkuppdragTyp.Distanstolkning => "DISTANSTOLKNING",
        TolkuppdragTyp.SkriftligOversattning => "OVERSATTNING",
        _ => "KONTAKTTOLKNING"
    };

    private static string F(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Rensa(string? s) => (s ?? string.Empty).Replace(" ", string.Empty).Trim();
}
