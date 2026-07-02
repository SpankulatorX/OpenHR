using System.Globalization;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.SalaryReview.Services;

/// <summary>
/// Läser in löneförslag från en fil (CSV eller ett redan uppackat cell-rutnät från Excel)
/// och validerar varje rad enligt svensk lönepraxis: giltigt personnummer, positiv lön,
/// obligatorisk motivering och inga dubbletter i filen. Ren, databaslös logik så att
/// hela tolkningen är enhetstestbar; databasberoende kontroller (att den anställde finns
/// och inte redan har ett förslag i rundan) görs av det anropande Web-lagret.
///
/// Systemet är experten: tvetydiga rader (dubbletter, ogiltiga värden) avvisas här i stället
/// för att gissa fel — importen ska aldrig skapa ett felaktigt löneförslag i tysthet.
///
/// Förväntade kolumner (namngiven rubrik eller positionell ordning
/// Personnummer, Ny lön, Motivering):
///   Personnummer   – 10- eller 12-siffrigt svenskt personnummer (obligatoriskt)
///   Ny lön         – föreslagen ny månadslön i kronor, positiv (obligatoriskt)
///   Motivering     – textmotivering (obligatoriskt)
///   Anställnings-id – valfri GUID som pekar ut exakt anställning vid flera anställningar
/// </summary>
public sealed class SalaryProposalImportParser
{
    // Rubrikalias (normaliserade: gemener, utan mellanslag/bindestreck/understreck).
    private static readonly string[] PnrAlias =
        { "personnummer", "pnr", "personnr", "personid", "person" };
    private static readonly string[] AnstallningAlias =
        { "anstallningsid", "anstallning", "anstallningsnummer", "employmentid", "anstid" };
    private static readonly string[] LonAlias =
        { "nylon", "foreslagenlon", "foreslagenmanadslon", "manadslon", "lon", "belopp", "nymanadslon" };
    private static readonly string[] MotiveringAlias =
        { "motivering", "kommentar", "notering", "skal", "anledning", "motiv" };

    /// <summary>
    /// Tolkar rå CSV-text. Avgränsare (semikolon, tab eller komma) upptäcks automatiskt
    /// och citerade fält ("...") respekteras så att fritext med avgränsartecken fungerar.
    /// </summary>
    public SalaryImportParseResult ParseCsv(string innehall)
    {
        if (string.IsNullOrWhiteSpace(innehall))
            return SalaryImportParseResult.Tomt("Filen är tom.");

        var textrader = innehall
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(r => r.Trim().Length > 0)
            .ToList();

        if (textrader.Count == 0)
            return SalaryImportParseResult.Tomt("Filen innehåller inga rader.");

        var avgransare = UpptackAvgransare(textrader[0]);
        var rutnat = textrader
            .Select(r => (IReadOnlyList<string?>)SplittraCsvRad(r, avgransare))
            .ToList();

        return ParseRutnat(rutnat);
    }

    /// <summary>
    /// Tolkar ett redan uppackat cell-rutnät (rad → celler), t.ex. från ett Excel-blad.
    /// Delas av CSV- och Excel-vägen så att rubrikigenkänning och radvalidering är identiska.
    /// </summary>
    public SalaryImportParseResult ParseRutnat(IReadOnlyList<IReadOnlyList<string?>> rutnat)
    {
        ArgumentNullException.ThrowIfNull(rutnat);

        var ickeTomma = rutnat
            .Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
            .ToList();

        if (ickeTomma.Count == 0)
            return SalaryImportParseResult.Tomt("Filen innehåller inga rader.");

        var harRubrik = UpptackKolumner(ickeTomma[0], out var karta);
        if (!harRubrik)
            karta = PositionellKarta();

        var globalaFel = new List<string>();
        if (karta.Pnr < 0)
            globalaFel.Add("Kunde inte hitta en personnummerkolumn.");
        if (karta.Lon < 0)
            globalaFel.Add("Kunde inte hitta en kolumn för ny lön.");
        if (globalaFel.Count > 0)
            return new SalaryImportParseResult([], globalaFel);

        var dataRader = harRubrik ? ickeTomma.Skip(1).ToList() : ickeTomma;
        var rader = new List<SalaryImportRad>();
        var radNummer = 0;

        foreach (var celler in dataRader)
        {
            radNummer++;
            rader.Add(TolkaRad(radNummer, celler, karta));
        }

        MarkeraDubbletter(rader);
        return new SalaryImportParseResult(rader, []);
    }

    private static SalaryImportRad TolkaRad(int radNummer, IReadOnlyList<string?> celler, KolumnKarta karta)
    {
        var pnrRaw = Cell(celler, karta.Pnr);
        var lonRaw = Cell(celler, karta.Lon);
        var motivering = Cell(celler, karta.Motivering)?.Trim();
        var anstRaw = Cell(celler, karta.Anstallning);

        var fel = new List<string>();

        Personnummer? pnr = null;
        if (string.IsNullOrWhiteSpace(pnrRaw))
        {
            fel.Add("Personnummer saknas.");
        }
        else
        {
            try { pnr = new Personnummer(pnrRaw.Trim()); }
            catch (ArgumentException) { fel.Add($"Ogiltigt personnummer: \"{pnrRaw.Trim()}\"."); }
        }

        EmploymentId? anstallning = null;
        if (!string.IsNullOrWhiteSpace(anstRaw))
        {
            if (Guid.TryParse(anstRaw.Trim(), out var guid))
                anstallning = new EmploymentId(guid);
            else
                fel.Add($"Ogiltigt anställnings-id: \"{anstRaw.Trim()}\".");
        }

        decimal? nyLon = null;
        if (string.IsNullOrWhiteSpace(lonRaw))
        {
            fel.Add("Ny lön saknas.");
        }
        else
        {
            var belopp = ParseBelopp(lonRaw);
            if (belopp is null)
                fel.Add($"Ogiltig lön: \"{lonRaw.Trim()}\".");
            else if (belopp <= 0)
                fel.Add($"Lön måste vara positiv: \"{lonRaw.Trim()}\".");
            else
                nyLon = belopp;
        }

        if (string.IsNullOrWhiteSpace(motivering))
            fel.Add("Motivering saknas.");

        return new SalaryImportRad(
            radNummer,
            pnrRaw?.Trim(),
            anstRaw?.Trim(),
            lonRaw?.Trim(),
            motivering,
            pnr,
            anstallning,
            nyLon,
            fel);
    }

    /// <summary>
    /// Flaggar rader där samma personnummer förekommer flera gånger. Alla förekomster
    /// markeras som fel så att användaren tvingas rätta dubbletten i stället för att
    /// systemet gissar vilken rad som gäller.
    /// </summary>
    private static void MarkeraDubbletter(List<SalaryImportRad> rader)
    {
        var grupper = rader
            .Where(r => r.Personnummer is not null)
            .GroupBy(r => (string)r.Personnummer!)
            .Where(g => g.Count() > 1);

        foreach (var grupp in grupper)
        {
            var radnr = string.Join(", ", grupp.Select(r => r.RadNummer));
            foreach (var rad in grupp)
                rad.Fel.Add($"Dubblett: personnummer förekommer på flera rader ({radnr}).");
        }
    }

    private static decimal? ParseBelopp(string raw)
    {
        // Ta bort all whitespace (mellanslag, NBSP, smalt NBSP, tab) som tusentalsavgränsare.
        var s = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Ta bort valutasuffix/prefix.
        s = s.Replace("kr", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace("sek", string.Empty, StringComparison.OrdinalIgnoreCase)
             .Replace(":-", string.Empty, StringComparison.Ordinal);

        if (s.Length == 0)
            return null;

        var harPunkt = s.Contains('.', StringComparison.Ordinal);
        var harKomma = s.Contains(',', StringComparison.Ordinal);

        if (harPunkt && harKomma)
        {
            // Sista separatorn är decimaltecken; den andra är tusentalsavgränsare.
            if (s.LastIndexOf(',') > s.LastIndexOf('.'))
                s = s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
            else
                s = s.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (harKomma)
        {
            s = NormaliseraEnkelSeparator(s, ',');
        }
        else if (harPunkt)
        {
            s = NormaliseraEnkelSeparator(s, '.');
        }

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    /// <summary>
    /// Ett enda separatortecken är tvetydigt: exakt tre siffror efter = tusentalsavgränsare
    /// ("45 000" → 45000), annars decimaltecken ("45000,50" → 45000.50).
    /// </summary>
    private static string NormaliseraEnkelSeparator(string s, char sep)
    {
        var sista = s.LastIndexOf(sep);
        var efter = s.Length - sista - 1;
        var baraSiffrorEfter = s[(sista + 1)..].All(char.IsDigit);
        var flera = s.Count(c => c == sep) > 1;

        if (baraSiffrorEfter && (efter == 3 || flera))
            return s.Replace(sep.ToString(), string.Empty); // tusental

        return sep == '.' ? s : s.Replace(sep, '.'); // decimal
    }

    private static bool UpptackKolumner(IReadOnlyList<string?> rubrik, out KolumnKarta karta)
    {
        int pnr = -1, anst = -1, lon = -1, mot = -1;
        var traffar = 0;

        for (var i = 0; i < rubrik.Count; i++)
        {
            var namn = NormaliseraRubrik(rubrik[i]);
            if (namn.Length == 0) continue;

            if (pnr < 0 && PnrAlias.Contains(namn)) { pnr = i; traffar++; }
            else if (anst < 0 && AnstallningAlias.Contains(namn)) { anst = i; traffar++; }
            else if (lon < 0 && LonAlias.Contains(namn)) { lon = i; traffar++; }
            else if (mot < 0 && MotiveringAlias.Contains(namn)) { mot = i; traffar++; }
        }

        karta = new KolumnKarta(pnr, anst, lon, mot);
        // Betrakta det som en rubrikrad bara om minst två kolumner kändes igen.
        return traffar >= 2;
    }

    private static KolumnKarta PositionellKarta() => new(Pnr: 0, Anstallning: -1, Lon: 1, Motivering: 2);

    private static string NormaliseraRubrik(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return string.Empty;
        return new string(cell
            .Trim()
            .ToLowerInvariant()
            .Where(c => c is not (' ' or '-' or '_' or '.' or ':'))
            .ToArray())
            .Replace("ö", "o", StringComparison.Ordinal)
            .Replace("ä", "a", StringComparison.Ordinal)
            .Replace("å", "a", StringComparison.Ordinal);
    }

    private static string? Cell(IReadOnlyList<string?> celler, int index) =>
        index >= 0 && index < celler.Count ? celler[index] : null;

    private static char UpptackAvgransare(string rad)
    {
        var semikolon = rad.Count(c => c == ';');
        var tab = rad.Count(c => c == '\t');
        var komma = rad.Count(c => c == ',');

        if (semikolon >= tab && semikolon >= komma && semikolon > 0) return ';';
        if (tab >= komma && tab > 0) return '\t';
        if (komma > 0) return ',';
        return ';';
    }

    private static string?[] SplittraCsvRad(string rad, char avgransare)
    {
        var falt = new List<string?>();
        var aktuell = new System.Text.StringBuilder();
        var inomCitat = false;

        for (var i = 0; i < rad.Length; i++)
        {
            var c = rad[i];
            if (inomCitat)
            {
                if (c == '"')
                {
                    if (i + 1 < rad.Length && rad[i + 1] == '"') { aktuell.Append('"'); i++; }
                    else inomCitat = false;
                }
                else aktuell.Append(c);
            }
            else if (c == '"')
            {
                inomCitat = true;
            }
            else if (c == avgransare)
            {
                falt.Add(aktuell.ToString());
                aktuell.Clear();
            }
            else
            {
                aktuell.Append(c);
            }
        }
        falt.Add(aktuell.ToString());
        return falt.ToArray();
    }

    private readonly record struct KolumnKarta(int Pnr, int Anstallning, int Lon, int Motivering);
}

/// <summary>En tolkad rad från importfilen med validerade värden och eventuella fel.</summary>
public sealed record SalaryImportRad(
    int RadNummer,
    string? PersonnummerRaw,
    string? AnstallningsIdRaw,
    string? NyLonRaw,
    string? Motivering,
    Personnummer? Personnummer,
    EmploymentId? AnstallningId,
    decimal? NyLon,
    List<string> Fel)
{
    /// <summary>Raden klarade all filnivåvalidering (databaskontroller görs separat).</summary>
    public bool ArGiltig => Fel.Count == 0;
}

/// <summary>Resultatet av att tolka en hel importfil.</summary>
public sealed record SalaryImportParseResult(
    IReadOnlyList<SalaryImportRad> Rader,
    IReadOnlyList<string> GlobalaFel)
{
    public int AntalGiltiga => Rader.Count(r => r.ArGiltig);
    public int AntalFel => Rader.Count(r => !r.ArGiltig);
    public bool HarRader => Rader.Count > 0;

    public static SalaryImportParseResult Tomt(string fel) => new([], [fel]);
}
