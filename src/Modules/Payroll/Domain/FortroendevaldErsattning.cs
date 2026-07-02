using System.Globalization;
using System.Text;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>Var i flödet förtroendevald-ersättningen befinner sig.</summary>
public enum FortroendevaldStatus
{
    Registrerad = 0,
    Godkand = 1,
    Utbetald = 2
}

/// <summary>
/// Ersättning till en förtroendevald/fritidspolitiker (t.ex. ledamot i regionfullmäktige
/// eller en nämnd). Detta är ett EGET ersättningsflöde skilt från anställdas lön och består av
/// sammanträdesarvode, ersättning för förlorad arbetsinkomst samt reseersättning.
///
/// Systemet är experten på skatte-/skattefrihetsgränser: sammanträdesarvode och förlorad
/// arbetsinkomst är skattepliktiga, medan reseersättning är skattefri upp till Skatteverkets
/// schablon (25 kr/mil = 2,50 kr/km). Den del av reseersättningen som överstiger schablonen
/// blir skattepliktig. Preliminärskatt beräknas på det skattepliktiga beloppet.
/// </summary>
public sealed class FortroendevaldErsattning
{
    /// <summary>Skattefri milersättning för egen bil enligt Skatteverkets schablon (kr/km).</summary>
    public const decimal SkattefriKmSchablon = 2.50m;

    /// <summary>Standard preliminärskattesats (30 % engångs-/sidoinkomstskatt).</summary>
    public const decimal StandardSkattesats = 0.30m;

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Den förtroendevaldes namn.</summary>
    public string Namn { get; private set; } = string.Empty;

    /// <summary>Den förtroendevaldes personnummer.</summary>
    public string Personnummer { get; private set; } = string.Empty;

    /// <summary>Uppdraget/rollen (t.ex. "Ledamot regionfullmäktige", "Ordförande hälso- och sjukvårdsnämnden").</summary>
    public string Uppdrag { get; private set; } = string.Empty;

    /// <summary>Organ/nämnd sammanträdet avser (frivilligt).</summary>
    public string? Organ { get; private set; }

    /// <summary>Sammanträdes-/förrättningsdatum.</summary>
    public DateOnly Sammantradesdatum { get; private set; }

    /// <summary>Sammanträdesarvode (skattepliktigt).</summary>
    public Money Sammantradesarvode { get; private set; } = Money.Zero;

    /// <summary>Ersättning för förlorad arbetsinkomst (skattepliktigt).</summary>
    public Money ForloradArbetsinkomst { get; private set; } = Money.Zero;

    /// <summary>Antal resta kilometer med egen bil.</summary>
    public decimal AntalKm { get; private set; }

    /// <summary>Tillämpad kilometersättning (kr/km); default = skattefri schablon.</summary>
    public decimal KmErsattning { get; private set; } = SkattefriKmSchablon;

    /// <summary>Preliminärskattesats som tillämpas på det skattepliktiga beloppet.</summary>
    public decimal Skattesats { get; private set; } = StandardSkattesats;

    public FortroendevaldStatus Status { get; private set; } = FortroendevaldStatus.Registrerad;

    public DateTime Registrerad { get; private set; } = DateTime.UtcNow;
    public string? RegistreradAv { get; private set; }

    private FortroendevaldErsattning() { } // EF Core

    /// <summary>Registrera en ersättning till en förtroendevald/fritidspolitiker.</summary>
    public static FortroendevaldErsattning Skapa(
        string namn,
        string personnummer,
        string uppdrag,
        DateOnly sammantradesdatum,
        Money? sammantradesarvode = null,
        Money? forloradArbetsinkomst = null,
        decimal antalKm = 0m,
        decimal? kmErsattning = null,
        decimal? skattesats = null,
        string? organ = null,
        string? registreradAv = null)
    {
        if (string.IsNullOrWhiteSpace(namn))
            throw new ArgumentException("Namn måste anges.", nameof(namn));
        if (string.IsNullOrWhiteSpace(personnummer))
            throw new ArgumentException("Personnummer måste anges.", nameof(personnummer));
        if (string.IsNullOrWhiteSpace(uppdrag))
            throw new ArgumentException("Uppdrag måste anges.", nameof(uppdrag));
        if (antalKm < 0m)
            throw new ArgumentException("Antal km kan inte vara negativt.", nameof(antalKm));

        var kmSats = kmErsattning ?? SkattefriKmSchablon;
        if (kmSats < 0m)
            throw new ArgumentException("Kilometersättning kan inte vara negativ.", nameof(kmErsattning));

        var sats = skattesats ?? StandardSkattesats;
        if (sats < 0m || sats > 1m)
            throw new ArgumentException("Skattesats måste vara mellan 0 och 1.", nameof(skattesats));

        var arvode = sammantradesarvode ?? Money.Zero;
        var forlorad = forloradArbetsinkomst ?? Money.Zero;
        if (arvode.Amount < 0m)
            throw new ArgumentException("Sammanträdesarvode kan inte vara negativt.", nameof(sammantradesarvode));
        if (forlorad.Amount < 0m)
            throw new ArgumentException("Förlorad arbetsinkomst kan inte vara negativ.", nameof(forloradArbetsinkomst));

        return new FortroendevaldErsattning
        {
            Namn = namn.Trim(),
            Personnummer = personnummer.Trim(),
            Uppdrag = uppdrag.Trim(),
            Organ = Rensa(organ),
            Sammantradesdatum = sammantradesdatum,
            Sammantradesarvode = arvode,
            ForloradArbetsinkomst = forlorad,
            AntalKm = antalKm,
            KmErsattning = kmSats,
            Skattesats = sats,
            RegistreradAv = Rensa(registreradAv)
        };
    }

    /// <summary>Total reseersättning (antal km × kilometersättning).</summary>
    public Money BeraknaReseersattning() =>
        Money.SEK(AntalKm * KmErsattning).RoundToOren();

    /// <summary>Skattefri del av reseersättningen (kapad vid Skatteverkets schablon).</summary>
    public Money BeraknaSkattefriResa()
    {
        var schablon = Math.Min(KmErsattning, SkattefriKmSchablon);
        return Money.SEK(AntalKm * schablon).RoundToOren();
    }

    /// <summary>Skattepliktig del av reseersättningen (det som överstiger schablonen).</summary>
    public Money BeraknaSkattepliktigResa() =>
        (BeraknaReseersattning() - BeraknaSkattefriResa()).RoundToOren();

    /// <summary>Skattepliktigt underlag: arvode + förlorad arbetsinkomst + skattepliktig reseandel.</summary>
    public Money BeraknaSkattepliktigt() =>
        (Sammantradesarvode + ForloradArbetsinkomst + BeraknaSkattepliktigResa()).RoundToOren();

    /// <summary>Bruttoersättning: arvode + förlorad arbetsinkomst + hela reseersättningen.</summary>
    public Money BeraknaBrutto() =>
        (Sammantradesarvode + ForloradArbetsinkomst + BeraknaReseersattning()).RoundToOren();

    /// <summary>Preliminärskatteavdrag på det skattepliktiga beloppet.</summary>
    public Money BeraknaSkatt() =>
        (BeraknaSkattepliktigt() * Skattesats).RoundToOren();

    /// <summary>Nettoersättning att betala ut: brutto − skatt.</summary>
    public Money BeraknaNetto() =>
        (BeraknaBrutto() - BeraknaSkatt()).RoundToOren();

    /// <summary>Godkänn ersättningen för utbetalning.</summary>
    public void Godkann()
    {
        if (Status == FortroendevaldStatus.Utbetald)
            throw new InvalidOperationException("Ersättningen är redan utbetald.");
        Status = FortroendevaldStatus.Godkand;
    }

    /// <summary>Markera ersättningen som utbetald (efter att underlag skapats).</summary>
    public void MarkeraUtbetald()
    {
        if (Status == FortroendevaldStatus.Registrerad)
            throw new InvalidOperationException("Ersättningen måste godkännas innan den kan markeras utbetald.");
        Status = FortroendevaldStatus.Utbetald;
    }

    private static string? Rensa(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Indata till <see cref="FortroendevaldUnderlagGenerator"/>.</summary>
public sealed record FortroendevaldUnderlagInput(
    string Organisationsnummer,
    string ArbetsgivareNamn,
    string Period,
    DateOnly Utbetalningsdatum,
    IReadOnlyList<FortroendevaldErsattning> Poster);

/// <summary>Genererat utbetalningsunderlag för förtroendevald-ersättningar.</summary>
public sealed record FortroendevaldUnderlagFil(string FileName, byte[] Content, string MimeType);

/// <summary>
/// Self-contained generator som bygger ett utbetalningsunderlag (semikolonseparerad CSV,
/// ISO-8859-1) för ersättningar till förtroendevalda/fritidspolitiker under en period.
/// Underlaget särredovisar arvode, förlorad arbetsinkomst, skattefri/skattepliktig
/// reseersättning, skatt och netto per post, samt en summeringsrad. Tal skrivs med
/// invariant kultur (två decimaler).
/// </summary>
public sealed class FortroendevaldUnderlagGenerator
{
    private const char Sep = ';';

    public FortroendevaldUnderlagFil Generera(FortroendevaldUnderlagInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Organisationsnummer))
            throw new ArgumentException("Organisationsnummer måste anges.", nameof(input));

        var innehall = ByggInnehall(input);
        var bytes = Encoding.Latin1.GetBytes(innehall);
        var period = (input.Period ?? string.Empty).Replace("-", string.Empty);
        var fileName = $"FORTROENDEVALDA_{Rensa(input.Organisationsnummer)}_{period}.csv";
        return new FortroendevaldUnderlagFil(fileName, bytes, "text/csv");
    }

    /// <summary>Bygger textinnehållet (exponerat för test).</summary>
    internal string ByggInnehall(FortroendevaldUnderlagInput input)
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

        sb.Append("Datum").Append(Sep)
          .Append("Namn").Append(Sep)
          .Append("Personnummer").Append(Sep)
          .Append("Uppdrag").Append(Sep)
          .Append("Organ").Append(Sep)
          .Append("Arvode").Append(Sep)
          .Append("ForloradArbetsinkomst").Append(Sep)
          .Append("ResaSkattefri").Append(Sep)
          .Append("ResaSkattepliktig").Append(Sep)
          .Append("Skattepliktigt").Append(Sep)
          .Append("Skatt").Append(Sep)
          .Append("Brutto").Append(Sep)
          .Append("Netto")
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

            sb.Append(p.Sammantradesdatum.ToString("yyyy-MM-dd", ci)).Append(Sep)
              .Append(F(p.Namn)).Append(Sep)
              .Append(F(p.Personnummer)).Append(Sep)
              .Append(F(p.Uppdrag)).Append(Sep)
              .Append(F(p.Organ)).Append(Sep)
              .Append(p.Sammantradesarvode.Amount.ToString("0.00", ci)).Append(Sep)
              .Append(p.ForloradArbetsinkomst.Amount.ToString("0.00", ci)).Append(Sep)
              .Append(p.BeraknaSkattefriResa().Amount.ToString("0.00", ci)).Append(Sep)
              .Append(p.BeraknaSkattepliktigResa().Amount.ToString("0.00", ci)).Append(Sep)
              .Append(p.BeraknaSkattepliktigt().Amount.ToString("0.00", ci)).Append(Sep)
              .Append(skatt.ToString("0.00", ci)).Append(Sep)
              .Append(brutto.ToString("0.00", ci)).Append(Sep)
              .Append(netto.ToString("0.00", ci))
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

    private static string F(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Rensa(string? s) => (s ?? string.Empty).Replace(" ", string.Empty).Trim();
}
