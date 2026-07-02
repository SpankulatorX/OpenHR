using System.Globalization;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.IntegrationHub.Adapters.Skatteverket;

/// <summary>
/// Läser in folkbokföringsaviseringar från Skatteverket (Navet) och tolkar dem till
/// strukturerade ändringsposter som kan appliceras på medarbetarregistret (namn, adress,
/// dödsfall, skyddad identitet).
///
/// FILFORMAT — representativ Navet-avisering (ändringsposter):
/// Skatteverkets bastjänst Navet levererar löpande <em>aviseringar</em> med bara de
/// uppgifter som ändrats för en person. Detta är en dokumenterad, förenklad textrepresentation
/// av det formatet (en post per person, endast ändrade fält skickas):
/// <code>
///   # Kommentarrad (ignoreras)
///   #PERSON 19850101-1234
///   EFTERNAMN=Andersson
///   FORNAMN=Anna
///   MELLANNAMN=
///   GATUADRESS=Storgatan 1
///   POSTNUMMER=70210
///   POSTORT=Örebro
///   LAND=Sverige
///   AVLIDEN=
///   SEKRETESS=INGEN
///
///   #PERSON 19901231-2392
///   SEKRETESS=SKYDDAD_FOLKBOKFORING
/// </code>
/// Regler:
/// <list type="bullet">
///   <item>Ett personblock inleds med <c>#PERSON &lt;personnummer&gt;</c> (10 eller 12 siffror,
///     med eller utan bindestreck). Personnumret Luhn-valideras strikt — ogiltiga poster hoppas
///     över och rapporteras som fel.</item>
///   <item>Rader inom blocket är <c>NYCKEL=VÄRDE</c>. Tomt värde = uppgiften ingår inte i
///     aviseringen (ändras inte).</item>
///   <item>Rader som börjar med <c>#</c> (utom <c>#PERSON</c>) samt tomma rader ignoreras.</item>
///   <item>En adressändring kräver att GATUADRESS, POSTNUMMER och POSTORT alla finns; annars
///     varning och adressen tolkas som ofullständig (appliceras inte).</item>
///   <item>Är personen skyddad (sekretessmarkering / skyddad folkbokföring) utelämnas
///     adressuppgifter medvetet — de maskas bort ur aviseringen.</item>
/// </list>
///
/// VIKTIGT — ÄRLIG MÄRKNING: Denna importer tolkar en <em>fil</em>. Skarp, automatisk
/// hämtning från Navet kräver tecknat avtal med Skatteverket samt teknisk anslutning
/// (SSEK/webbtjänst). Filtolkningen är byggd; transporten är konfigklar men ej skarp.
/// </summary>
public sealed class FolkbokforingImporter
{
    /// <summary>Markering som visar att uppgifterna kommer från en inläst fil, inte skarp Navet-koppling.</summary>
    public const string Kalla = "FIL_EJ_SKARP_NAVET_KOPPLING";

    private const string PersonPrefix = "#PERSON";

    /// <summary>
    /// Tolkar filinnehållet till en lista av aviseringar. Returnerar alltid ett resultat;
    /// enskilda felaktiga block rapporteras i <see cref="FolkbokforingImportResult.Fel"/> men
    /// stoppar inte övriga poster.
    /// </summary>
    public FolkbokforingImportResult Parsa(string? filinnehall)
    {
        var aviseringar = new List<FolkbokforingAvisering>();
        var fel = new List<string>();
        var varningar = new List<string>();

        if (string.IsNullOrWhiteSpace(filinnehall))
        {
            varningar.Add("Filen var tom — inga aviseringar att tolka.");
            return new FolkbokforingImportResult(true, aviseringar, fel, varningar, Kalla);
        }

        // Dela upp i logiska rader; normalisera radslut.
        var rader = filinnehall.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        PersonBlock? aktuellt = null;
        var hopparBlock = false; // true när ett #PERSON-block har ogiltigt personnummer och dess fält ska ignoreras
        var radnr = 0;

        foreach (var raRad in rader)
        {
            radnr++;
            var rad = raRad.Trim();

            if (rad.Length == 0)
                continue;

            if (rad.StartsWith(PersonPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Avsluta föregående block.
                Slutfor(aktuellt, aviseringar, fel, varningar);
                aktuellt = null;
                hopparBlock = false;

                var pnrToken = rad[PersonPrefix.Length..].Trim();
                if (pnrToken.Length == 0)
                {
                    fel.Add($"Rad {radnr}: #PERSON saknar personnummer.");
                    hopparBlock = true;
                    continue;
                }

                Personnummer? pnr = null;
                try
                {
                    pnr = new Personnummer(pnrToken);
                }
                catch (ArgumentException)
                {
                    fel.Add($"Rad {radnr}: ogiltigt personnummer \"{pnrToken}\" (kunde inte valideras).");
                }

                if (pnr is null)
                    hopparBlock = true;
                else
                    aktuellt = new PersonBlock(pnr, radnr);
                continue;
            }

            // Övriga kommentarer.
            if (rad.StartsWith('#'))
                continue;

            var likhet = rad.IndexOf('=');
            if (likhet <= 0)
            {
                fel.Add($"Rad {radnr}: förväntade NYCKEL=VÄRDE men fick \"{rad}\".");
                continue;
            }

            if (aktuellt is null)
            {
                // Ligger fältet i ett block med ogiltigt personnummer ignoreras det tyst
                // (felet är redan rapporterat på #PERSON-raden).
                if (!hopparBlock)
                    fel.Add($"Rad {radnr}: uppgift \"{rad}\" ligger utanför ett #PERSON-block.");
                continue;
            }

            var nyckel = rad[..likhet].Trim().ToUpperInvariant();
            var varde = rad[(likhet + 1)..].Trim();
            aktuellt.SattFalt(nyckel, varde, radnr, varningar);
        }

        // Avsluta sista blocket.
        Slutfor(aktuellt, aviseringar, fel, varningar);

        if (aviseringar.Count == 0 && fel.Count == 0)
            varningar.Add("Filen innehöll inga giltiga personblock.");

        return new FolkbokforingImportResult(true, aviseringar, fel, varningar, Kalla);
    }

    private static void Slutfor(
        PersonBlock? block,
        List<FolkbokforingAvisering> aviseringar,
        List<string> fel,
        List<string> varningar)
    {
        if (block is null)
            return;

        var avisering = block.Bygg(varningar);
        if (avisering is null)
        {
            varningar.Add($"Personblock (rad {block.Radnr}) för {block.Person.ToString()} innehöll inga ändringar och hoppades över.");
            return;
        }

        aviseringar.Add(avisering);
    }

    /// <summary>Muterbart arbetsobjekt medan ett personblock byggs upp.</summary>
    private sealed class PersonBlock
    {
        public Personnummer Person { get; }
        public int Radnr { get; }

        private string? _efternamn;
        private string? _fornamn;
        private string? _mellannamn;
        private string? _gatuadress;
        private string? _postnummer;
        private string? _postort;
        private string? _land;
        private DateOnly? _avliden;
        private Sekretessmarkering _sekretess = Sekretessmarkering.Ingen;

        public PersonBlock(Personnummer person, int radnr)
        {
            Person = person;
            Radnr = radnr;
        }

        public void SattFalt(string nyckel, string varde, int radnr, List<string> varningar)
        {
            // Tomt värde = uppgiften ingår inte i aviseringen.
            var harVarde = varde.Length > 0;

            switch (nyckel)
            {
                case "EFTERNAMN": _efternamn = harVarde ? varde : _efternamn; break;
                case "FORNAMN": _fornamn = harVarde ? varde : _fornamn; break;
                case "MELLANNAMN": _mellannamn = harVarde ? varde : _mellannamn; break;
                case "GATUADRESS": _gatuadress = harVarde ? varde : _gatuadress; break;
                case "POSTNUMMER": _postnummer = harVarde ? NormaliseraPostnummer(varde) : _postnummer; break;
                case "POSTORT": _postort = harVarde ? varde : _postort; break;
                case "LAND": _land = harVarde ? varde : _land; break;
                case "AVLIDEN":
                    if (harVarde)
                    {
                        if (DateOnly.TryParse(varde, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            _avliden = d;
                        else
                            varningar.Add($"Rad {radnr}: kunde inte tolka avlidendatum \"{varde}\" (förväntat YYYY-MM-DD).");
                    }
                    break;
                case "SEKRETESS":
                    if (harVarde)
                        _sekretess = TolkaSekretess(varde, radnr, varningar);
                    break;
                default:
                    varningar.Add($"Rad {radnr}: okänd uppgift \"{nyckel}\" ignorerades.");
                    break;
            }
        }

        public FolkbokforingAvisering? Bygg(List<string> varningar)
        {
            var harNamn = _efternamn is not null || _fornamn is not null || _mellannamn is not null;

            var arSkyddad = _sekretess != Sekretessmarkering.Ingen;

            string? gata = _gatuadress, post = _postnummer, ort = _postort, land = _land;
            var harFullAdress = !string.IsNullOrWhiteSpace(gata)
                                && !string.IsNullOrWhiteSpace(post)
                                && !string.IsNullOrWhiteSpace(ort);
            // En adress som försökts (någon del angiven) men är ofullständig ska ändå ytas
            // som avisering — så operatören ser den problematiska posten, inte tyst tappar den.
            var adressForsokt = !string.IsNullOrWhiteSpace(_gatuadress)
                                 || !string.IsNullOrWhiteSpace(_postnummer)
                                 || !string.IsNullOrWhiteSpace(_postort);

            // Skyddad identitet: adressuppgifter ska aldrig exponeras/appliceras.
            if (arSkyddad && (gata is not null || post is not null || ort is not null))
            {
                varningar.Add($"{Person.ToString()}: skyddad identitet — adressuppgifter i aviseringen maskas bort och appliceras inte.");
                gata = post = ort = land = null;
                harFullAdress = false;
            }
            else if (!arSkyddad && (gata is not null || post is not null || ort is not null) && !harFullAdress)
            {
                varningar.Add($"{Person.ToString()}: ofullständig adress (kräver gatuadress, postnummer och postort) — adressen appliceras inte.");
            }

            var harAdress = harFullAdress; // redan false om skyddad eller ofullständig

            if (!harNamn && !harAdress && _avliden is null && !arSkyddad && !adressForsokt)
                return null;

            return new FolkbokforingAvisering(
                Person: Person,
                Efternamn: _efternamn,
                Fornamn: _fornamn,
                MellanNamn: _mellannamn,
                Gatuadress: harAdress ? gata : null,
                Postnummer: harAdress ? post : null,
                Postort: harAdress ? ort : null,
                Land: harAdress ? (string.IsNullOrWhiteSpace(land) ? "Sverige" : land) : null,
                HarAdressAndring: harAdress,
                AvlidenDatum: _avliden,
                Sekretess: _sekretess);
        }

        private static string NormaliseraPostnummer(string varde) =>
            new string(varde.Where(char.IsDigit).ToArray());

        private static Sekretessmarkering TolkaSekretess(string varde, int radnr, List<string> varningar)
        {
            switch (varde.ToUpperInvariant().Replace(" ", "_"))
            {
                case "INGEN":
                case "NEJ":
                case "0":
                    return Sekretessmarkering.Ingen;
                case "SEKRETESSMARKERING":
                case "SPARR":
                case "1":
                    return Sekretessmarkering.Sekretessmarkering;
                case "SKYDDAD_FOLKBOKFORING":
                case "KVARSKRIVNING":
                case "2":
                    return Sekretessmarkering.SkyddadFolkbokforing;
                default:
                    varningar.Add($"Rad {radnr}: okänt sekretessvärde \"{varde}\" tolkas som ingen sekretess.");
                    return Sekretessmarkering.Ingen;
            }
        }
    }
}

/// <summary>Skyddsstatus för folkbokförd person (Offentlighets- och sekretesslagen 22 kap.).</summary>
public enum Sekretessmarkering
{
    /// <summary>Ingen skyddad identitet.</summary>
    Ingen,

    /// <summary>Sekretessmarkering ("spärrmarkering") i folkbokföringen.</summary>
    Sekretessmarkering,

    /// <summary>Skyddad folkbokföring (tidigare kvarskrivning).</summary>
    SkyddadFolkbokforing
}

/// <summary>
/// En tolkad folkbokföringsavisering för en person. Endast fält som ingick i aviseringen
/// är satta; övriga är null och ska inte ändras.
/// </summary>
public sealed record FolkbokforingAvisering(
    Personnummer Person,
    string? Efternamn,
    string? Fornamn,
    string? MellanNamn,
    string? Gatuadress,
    string? Postnummer,
    string? Postort,
    string? Land,
    bool HarAdressAndring,
    DateOnly? AvlidenDatum,
    Sekretessmarkering Sekretess)
{
    /// <summary>Personnumret i 12-siffrig form (YYYYMMDDNNNN) för matchning mot register.</summary>
    public string Personnummer12 => Person;

    /// <summary>Personnumret i visningsform (YYYYMMDD-NNNN).</summary>
    public string PersonnummerFormaterat => Person.ToString();

    /// <summary>Innehåller aviseringen minst en namnuppgift?</summary>
    public bool HarNamnAndring => Efternamn is not null || Fornamn is not null || MellanNamn is not null;

    /// <summary>Är personen markerad som avliden?</summary>
    public bool ArAvliden => AvlidenDatum.HasValue;

    /// <summary>Har personen skyddad identitet?</summary>
    public bool ArSkyddad => Sekretess != Sekretessmarkering.Ingen;

    /// <summary>Läsbar beskrivning av sekretesstatus.</summary>
    public string SekretessBeskrivning => Sekretess switch
    {
        Sekretessmarkering.Sekretessmarkering => "Sekretessmarkering",
        Sekretessmarkering.SkyddadFolkbokforing => "Skyddad folkbokföring",
        _ => "Ingen"
    };
}

/// <summary>Resultatet av att tolka en folkbokföringsfil.</summary>
public sealed record FolkbokforingImportResult(
    bool Giltig,
    IReadOnlyList<FolkbokforingAvisering> Aviseringar,
    IReadOnlyList<string> Fel,
    IReadOnlyList<string> Varningar,
    string Kalla)
{
    /// <summary>Antal tolkade aviseringar.</summary>
    public int AntalPoster => Aviseringar.Count;
}
