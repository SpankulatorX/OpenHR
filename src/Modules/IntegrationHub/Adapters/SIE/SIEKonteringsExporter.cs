using System.Globalization;
using System.Text;
using RegionHR.IntegrationHub.Adapters.Raindance;
using RegionHR.Payroll.Domain;

namespace RegionHR.IntegrationHub.Adapters.SIE;

/// <summary>
/// Uppgiftslämnare/metadata för en SIE-export.
/// Ingen live-koppling till ekonomisystemet sker här — endast en fil genereras.
/// Den skarpa överföringen (SFTP/AP-drop till Raindance/Agresso/Visma) är konfigurationsklar
/// men avsiktligt frånkopplad i denna leverans.
/// </summary>
public sealed class SIEExportInput
{
    /// <summary>Organisationsnummer, t.ex. "232100-0016".</summary>
    public string OrganisationsNummer { get; init; } = string.Empty;

    /// <summary>Företagsnamn, t.ex. "Region Örebro län".</summary>
    public string Foretagsnamn { get; init; } = string.Empty;

    public string ProgramNamn { get; init; } = "OpenHR";

    public string ProgramVersion { get; init; } = "1.0";

    /// <summary>Datum då filen genereras (#GEN). Default = idag.</summary>
    public DateOnly GenereringsDatum { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Verifikationsserie (t.ex. "L" för lön).</summary>
    public string VerifikationsSerie { get; init; } = "L";

    /// <summary>Verifikationsnummer inom serien.</summary>
    public int VerifikationsNummer { get; init; } = 1;
}

/// <summary>
/// Exporterar en lönekörnings kontering som SIE typ 4 (SIE4E) — det öppna, dokumenterade
/// svenska formatet för bokföringsdata (https://sie.se). Filen innehåller en verifikation
/// för hela lönekörningen med en transaktion (#TRANS) per konto/kostnadsställe, dimensionerad
/// på kostnadsställe (SIE-dimension 1). Debet bokförs positivt, kredit negativt; summan av
/// alla transaktionsbelopp är noll (balanserad verifikation).
///
/// Återanvänder konteringslogiken i <see cref="RaindanceKonteringsGenerator"/> — samma
/// balanserade rader, bara ett annat filformat.
/// </summary>
public sealed class SIEKonteringsExporter
{
    /// <summary>SIE:s reserverade dimension 1 = kostnadsställe.</summary>
    private const int DimKostnadsstalle = 1;

    private const string Crlf = "\r\n";

    /// <summary>
    /// Kontonamn för de konton som <see cref="RaindanceKonteringsGenerator"/> använder.
    /// Endast konton som faktiskt förekommer i konteringen skrivs ut som #KONTO.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KontoNamn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["5010"] = "Löner",
        ["5020"] = "OB-tillägg",
        ["5030"] = "Övertidsersättning",
        ["5040"] = "Jour och beredskap",
        ["7510"] = "Arbetsgivaravgifter",
        ["7410"] = "Pensionsavgifter",
        ["2710"] = "Personalens källskatt",
        ["2730"] = "Avräkning lagstadgade sociala avgifter",
        ["2920"] = "Upplupna löner",
        ["7420"] = "Avsättning till pensioner",
    };

    /// <summary>
    /// Genererar SIE typ 4-innehållet som text (radbrytning CRLF enligt SIE-standarden).
    /// </summary>
    public string GenerateSie(PayrollRun run, SIEExportInput input, IReadOnlyList<KonteringsRad> rader)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rader);

        var sb = new StringBuilder();

        // ── Filhuvud ──
        sb.Append("#FLAGGA 0").Append(Crlf);
        sb.Append("#PROGRAM ").Append(Quote(input.ProgramNamn)).Append(' ').Append(Quote(input.ProgramVersion)).Append(Crlf);
        sb.Append("#FORMAT PC8").Append(Crlf);
        sb.Append("#GEN ").Append(FormatDate(input.GenereringsDatum)).Append(Crlf);
        sb.Append("#SIETYP 4").Append(Crlf);
        sb.Append("#ORGNR ").Append(SanitizeToken(input.OrganisationsNummer)).Append(Crlf);
        sb.Append("#FNAMN ").Append(Quote(input.Foretagsnamn)).Append(Crlf);

        // Räkenskapsår 0 = innevarande, härlett ur lönekörningens år.
        var arStart = new DateOnly(run.Year, 1, 1);
        var arSlut = new DateOnly(run.Year, 12, 31);
        sb.Append("#RAR 0 ").Append(FormatDate(arStart)).Append(' ').Append(FormatDate(arSlut)).Append(Crlf);

        // ── Kontoplan (endast konton som används) ──
        var kontonSomAnvands = rader
            .Select(r => r.Konto)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        foreach (var konto in kontonSomAnvands)
        {
            var namn = KontoNamn.TryGetValue(konto, out var n) ? n : "Konto " + konto;
            sb.Append("#KONTO ").Append(konto).Append(' ').Append(Quote(namn)).Append(Crlf);
        }

        // ── Dimension + objekt (kostnadsställen) ──
        var kostnadsstallen = rader
            .Select(r => r.Kostnadsstalle)
            .Where(ks => !string.IsNullOrWhiteSpace(ks))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ks => ks, StringComparer.Ordinal)
            .ToList();

        if (kostnadsstallen.Count > 0)
        {
            sb.Append("#DIM ").Append(DimKostnadsstalle).Append(' ').Append(Quote("Kostnadsställe")).Append(Crlf);
            foreach (var ks in kostnadsstallen)
            {
                sb.Append("#OBJEKT ").Append(DimKostnadsstalle).Append(' ')
                  .Append(Quote(ks)).Append(' ').Append(Quote("Kostnadsställe " + ks)).Append(Crlf);
            }
        }

        // ── Verifikation för hela lönekörningen ──
        if (rader.Count > 0)
        {
            var verDatum = new DateOnly(run.Year, run.Month, DateTime.DaysInMonth(run.Year, run.Month));
            var verText = run.ArRetroaktiv && !string.IsNullOrWhiteSpace(run.RetroaktivtForPeriod)
                ? $"Lönekörning {run.Period} (retroaktiv, avser {run.RetroaktivtForPeriod})"
                : $"Lönekörning {run.Period}";

            sb.Append("#VER ")
              .Append(Quote(input.VerifikationsSerie)).Append(' ')
              .Append(Quote(input.VerifikationsNummer.ToString(CultureInfo.InvariantCulture))).Append(' ')
              .Append(FormatDate(verDatum)).Append(' ')
              .Append(Quote(verText)).Append(' ')
              .Append(FormatDate(input.GenereringsDatum)).Append(Crlf);

            sb.Append('{').Append(Crlf);
            foreach (var rad in rader)
            {
                // Debet positivt, kredit negativt.
                var belopp = rad.Debet.Amount - rad.Kredit.Amount;
                var objekt = string.IsNullOrWhiteSpace(rad.Kostnadsstalle)
                    ? "{}"
                    : "{" + DimKostnadsstalle.ToString(CultureInfo.InvariantCulture) + " " + Quote(rad.Kostnadsstalle) + "}";

                sb.Append("   #TRANS ")
                  .Append(rad.Konto).Append(' ')
                  .Append(objekt).Append(' ')
                  .Append(belopp.ToString("F2", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(FormatDate(verDatum)).Append(' ')
                  .Append(Quote(rad.Text)).Append(Crlf);
            }
            sb.Append('}').Append(Crlf);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Genererar SIE typ 4-filen som byte-array kodad i ISO-8859-1 (Latin-1), vilket är
    /// den teckenuppsättning OpenHR:s egen SIE-importer läser och som svenska ekonomisystem
    /// accepterar för svenska tecken (å/ä/ö).
    /// </summary>
    public byte[] GenerateSieBytes(PayrollRun run, SIEExportInput input, IReadOnlyList<KonteringsRad> rader)
        => Encoding.Latin1.GetBytes(GenerateSie(run, input, rader));

    private static string FormatDate(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Omsluter ett fält med citattecken och neutraliserar inbäddade citattecken/radbrytningar.</summary>
    private static string Quote(string? value)
    {
        var v = value ?? string.Empty;
        var cleaned = v.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
        return "\"" + cleaned + "\"";
    }

    /// <summary>Tar bort blanksteg/citattecken ur ett ociterat token (t.ex. orgnr).</summary>
    private static string SanitizeToken(string? value)
    {
        var v = value ?? string.Empty;
        return new string(v.Where(c => c is not ('"' or ' ' or '\r' or '\n')).ToArray());
    }
}
