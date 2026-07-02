using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using RegionHR.Migration.Adapters;
using RegionHR.Migration.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Migration.Services;

/// <summary>
/// Skapar riktiga anställda ur parsad migreringsdata.
///
/// Adaptrarna (t.ex. <see cref="RegionHR.Migration.Adapters.HeromaAdapter"/>) PARSAR en fil till
/// <see cref="ParsedMigrationData"/>, men skapar ingenting. Denna tjänst tar den parsade datan,
/// validerar och mappar varje rad till en typad <see cref="EmployeeImportData"/> och ber sedan
/// en <see cref="IEmployeeImportSink"/> att skapa/uppdatera Employee + Employment via kärndomänens
/// publika API (<c>Employee.Skapa</c> / <c>Employee.LaggTillAnstallning</c>) i EN transaktion.
///
/// Modulen refererar bara SharedKernel, så all värde­objekt-parsning (Personnummer, Money,
/// Percentage, EmploymentType, CollectiveAgreementType) sker här — själva domän-anropet och
/// persistensen ligger i Web-sidans sink som ser Core + DbContext. Det gör mappningslogiken
/// enhetstestbar utan databas.
///
/// Importen är idempotent: personnummer som redan finns (i filen eller i databasen) hoppas över
/// om inte <c>uppdateraDubbletter</c> är satt. En andra körning av samma fil skapar därför inga
/// dubbletter.
/// </summary>
public sealed class MigrationImportService
{
    private static readonly CultureInfo SvSE = CultureInfo.GetCultureInfo("sv-SE");

    private readonly IEmployeeImportSink _sink;

    public MigrationImportService(IEmployeeImportSink sink) => _sink = sink;

    /// <summary>
    /// Kör importen: validerar, dedupliceras och skapar/uppdaterar anställda.
    /// </summary>
    /// <param name="data">Parsad data från en adapter.</param>
    /// <param name="kalla">Källsystem (för historikpost).</param>
    /// <param name="filNamn">Ursprungligt filnamn (för historikpost).</param>
    /// <param name="skapadAv">Användare som kör importen.</param>
    /// <param name="uppdateraDubbletter">
    /// Om sant uppdateras befintliga anställda (kontaktuppgifter) i stället för att hoppas över.
    /// </param>
    public async Task<MigrationImportResult> ImporteraAsync(
        ParsedMigrationData data,
        SourceSystem kalla,
        string filNamn,
        string skapadAv,
        bool uppdateraDubbletter = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var befintliga = await _sink.LaddaBefintligaAsync(ct);
        var settIFilen = new HashSet<string>(StringComparer.Ordinal);
        var operationer = new List<EmployeeImportOperation>();
        var rader = new List<ImportRadResultat>();

        for (var i = 0; i < data.Records.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var radNummer = i + 1;
            var record = data.Records[i];

            if (!string.Equals(record.EntityType, "Employee", StringComparison.Ordinal))
            {
                rader.Add(new ImportRadResultat(radNummer, null, ImportRadStatus.Hoppad,
                    $"Posttyp '{record.EntityType}' hanteras inte av anställd-importen"));
                continue;
            }

            if (!TryMappa(record, out var mappad, out var felMeddelande))
            {
                rader.Add(new ImportRadResultat(radNummer, LasRatt(record), ImportRadStatus.Fel, felMeddelande));
                continue;
            }

            var nyckel = (string)mappad.Personnummer; // 12-siffrig normaliserad form

            if (!settIFilen.Add(nyckel))
            {
                rader.Add(new ImportRadResultat(radNummer, mappad.Personnummer.ToMaskedString(),
                    ImportRadStatus.Hoppad, "Dubblett — personnumret förekommer tidigare i filen"));
                continue;
            }

            if (befintliga.TryGetValue(nyckel, out var befintligtId))
            {
                if (uppdateraDubbletter)
                {
                    operationer.Add(new EmployeeImportOperation(
                        radNummer, ImportOperation.Uppdatera, befintligtId, mappad));
                }
                else
                {
                    rader.Add(new ImportRadResultat(radNummer, mappad.Personnummer.ToMaskedString(),
                        ImportRadStatus.Hoppad, "Anställd finns redan (hoppas över)"));
                }
                continue;
            }

            operationer.Add(new EmployeeImportOperation(radNummer, ImportOperation.Skapa, null, mappad));
        }

        var antalOgiltiga = rader.Count(r => r.Status == ImportRadStatus.Fel);
        var kontext = new MigrationImportContext(filNamn, skapadAv, kalla, data.Records.Count, antalOgiltiga);

        var exekvering = await _sink.ExekveraAsync(operationer, kontext, ct);

        if (exekvering.GlobaltFel is not null)
        {
            // Transaktionen rullades tillbaka — inga anställda skapades.
            foreach (var op in operationer)
            {
                rader.Add(new ImportRadResultat(op.RadNummer, op.Data.Personnummer.ToMaskedString(),
                    ImportRadStatus.Fel, "Ej sparad — import avbröts"));
            }
            rader.Sort((a, b) => a.RadNummer.CompareTo(b.RadNummer));
            return new MigrationImportResult(
                data.Records.Count, 0, 0,
                rader.Count(r => r.Status == ImportRadStatus.Hoppad),
                rader.Count(r => r.Status == ImportRadStatus.Fel),
                rader, exekvering.GlobaltFel);
        }

        var perRad = operationer.ToDictionary(o => o.RadNummer);
        foreach (var utfall in exekvering.Utfall)
        {
            if (!perRad.TryGetValue(utfall.RadNummer, out var op))
                continue;

            var maskerat = op.Data.Personnummer.ToMaskedString();
            if (utfall.Lyckades)
            {
                var status = op.Typ == ImportOperation.Skapa
                    ? ImportRadStatus.Skapad
                    : ImportRadStatus.Uppdaterad;
                rader.Add(new ImportRadResultat(utfall.RadNummer, maskerat, status, null));
            }
            else
            {
                rader.Add(new ImportRadResultat(utfall.RadNummer, maskerat, ImportRadStatus.Fel, utfall.Fel));
            }
        }

        rader.Sort((a, b) => a.RadNummer.CompareTo(b.RadNummer));

        return new MigrationImportResult(
            data.Records.Count,
            rader.Count(r => r.Status == ImportRadStatus.Skapad),
            rader.Count(r => r.Status == ImportRadStatus.Uppdaterad),
            rader.Count(r => r.Status == ImportRadStatus.Hoppad),
            rader.Count(r => r.Status == ImportRadStatus.Fel),
            rader,
            null);
    }

    // ------------------------------------------------------------------
    // Mappning: ParsedRecord (strängar) → typad EmployeeImportData
    // ------------------------------------------------------------------

    private static bool TryMappa(
        ParsedRecord record,
        [NotNullWhen(true)] out EmployeeImportData? data,
        out string? fel)
    {
        data = null;
        fel = null;

        var rawPnr = HamtaFalt(record, "Personnummer", "PERSNR", "PERSONNUMMER");
        if (string.IsNullOrWhiteSpace(rawPnr))
        {
            fel = "Personnummer saknas";
            return false;
        }

        Personnummer personnummer;
        try
        {
            personnummer = new Personnummer(rawPnr);
        }
        catch (ArgumentException)
        {
            fel = $"Ogiltigt personnummer: '{rawPnr}'";
            return false;
        }

        var fornamn = HamtaFalt(record, "Fornamn", "FNAMN", "Förnamn");
        if (string.IsNullOrWhiteSpace(fornamn))
        {
            fel = "Förnamn saknas";
            return false;
        }

        var efternamn = HamtaFalt(record, "Efternamn", "ENAMN");
        if (string.IsNullOrWhiteSpace(efternamn))
        {
            fel = "Efternamn saknas";
            return false;
        }

        var epost = NullOmTom(HamtaFalt(record, "Epost", "EPOST", "Email", "E-post"));
        var telefon = NullOmTom(HamtaFalt(record, "Telefon", "TEL", "Phone", "Mobil"));

        EmployeeEmploymentData? anstallning = null;
        var enhetskod = HamtaFalt(record, "Enhetskod", "ENHET_KOD", "ENHETSKOD", "Enhet");
        if (!string.IsNullOrWhiteSpace(enhetskod))
        {
            if (!TryTolkaAnstallningsform(HamtaFalt(record, "Anstallningsform", "ANST_FORM"), out var form))
            {
                fel = $"Okänd anställningsform: '{HamtaFalt(record, "Anstallningsform", "ANST_FORM")}'";
                return false;
            }

            if (!TryTolkaAvtal(HamtaFalt(record, "Kollektivavtal", "KOL_AVTAL", "Avtal"), out var avtal))
            {
                fel = $"Okänt kollektivavtal: '{HamtaFalt(record, "Kollektivavtal", "KOL_AVTAL", "Avtal")}'";
                return false;
            }

            var lonRaw = HamtaFalt(record, "Manadslon", "MANLON", "Lon");
            var lon = 0m;
            if (!string.IsNullOrWhiteSpace(lonRaw) && !TryTolkaDecimal(lonRaw, out lon))
            {
                fel = $"Ogiltig månadslön: '{lonRaw}'";
                return false;
            }

            var gradRaw = HamtaFalt(record, "Sysselsattningsgrad", "SYSS", "Tjanstgoringsgrad", "Grad");
            var grad = 100m;
            if (!string.IsNullOrWhiteSpace(gradRaw) && !TryTolkaDecimal(gradRaw, out grad))
            {
                fel = $"Ogiltig sysselsättningsgrad: '{gradRaw}'";
                return false;
            }
            if (grad <= 0m || grad > 100m)
            {
                fel = $"Sysselsättningsgrad måste vara 0–100 %, var {grad.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            var startRaw = HamtaFalt(record, "Startdatum", "Anstallningsdatum", "START", "TILLTRADE");
            var startdatum = DateOnly.FromDateTime(DateTime.Today);
            if (!string.IsNullOrWhiteSpace(startRaw) && !TryTolkaDatum(startRaw, out startdatum))
            {
                fel = $"Ogiltigt startdatum: '{startRaw}'";
                return false;
            }

            var slutRaw = HamtaFalt(record, "Slutdatum", "SLUT", "AVGANG");
            DateOnly? slutdatum = null;
            if (!string.IsNullOrWhiteSpace(slutRaw))
            {
                if (!TryTolkaDatum(slutRaw, out var slut))
                {
                    fel = $"Ogiltigt slutdatum: '{slutRaw}'";
                    return false;
                }
                slutdatum = slut;
            }

            var befattning = NullOmTom(HamtaFalt(record, "Befattning", "Befattningstitel", "TITEL"));

            anstallning = new EmployeeEmploymentData(
                enhetskod.Trim(), form, avtal,
                Money.SEK(lon), new Percentage(grad),
                startdatum, slutdatum, befattning);
        }

        data = new EmployeeImportData(personnummer, fornamn.Trim(), efternamn.Trim(), epost, telefon, anstallning);
        return true;
    }

    private static string HamtaFalt(ParsedRecord record, params string[] nycklar)
    {
        foreach (var nyckel in nycklar)
        {
            if (record.Fields.TryGetValue(nyckel, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return string.Empty;
    }

    private static string? NullOmTom(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? LasRatt(ParsedRecord record)
    {
        var raw = HamtaFalt(record, "Personnummer", "PERSNR", "PERSONNUMMER");
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    private static bool TryTolkaAnstallningsform(string raw, out EmploymentType typ)
    {
        typ = EmploymentType.Tillsvidare;
        if (string.IsNullOrWhiteSpace(raw))
            return true; // rimlig standard för migrering: tillsvidare

        var v = raw.Trim();
        if (Enum.TryParse<EmploymentType>(v, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            typ = parsed;
            return true;
        }

        switch (v.ToUpperInvariant())
        {
            case "TV":
            case "TILLS":
            case "TILLSVIDAREANSTÄLLNING":
            case "PERMANENT":
                typ = EmploymentType.Tillsvidare;
                return true;
            case "VIK":
                typ = EmploymentType.Vikariat;
                return true;
            case "PROV":
            case "PROVANSTÄLLD":
                typ = EmploymentType.Provanstallning;
                return true;
            case "AVA":
            case "SÄVA":
            case "ALLMÄN VISSTID":
                typ = EmploymentType.SAVA;
                return true;
            case "TIM":
            case "TIMANSTÄLLD":
            case "TIMAVLÖNAD":
                typ = EmploymentType.Timavlonad;
                return true;
            case "SÄSONG":
            case "SASONG":
                typ = EmploymentType.Sasongsanstallning;
                return true;
            default:
                return false;
        }
    }

    private static bool TryTolkaAvtal(string raw, out CollectiveAgreementType avtal)
    {
        avtal = CollectiveAgreementType.None;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (Enum.TryParse<CollectiveAgreementType>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            avtal = parsed;
            return true;
        }
        return false;
    }

    private static bool TryTolkaDecimal(string raw, out decimal value)
    {
        // Strippa alla blanksteg (inkl. hardt mellanslag som sv-SE anvander som tusentalsavgransare).
        var clean = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return decimal.TryParse(clean, NumberStyles.Any, SvSE, out value)
            || decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryTolkaDatum(string raw, out DateOnly datum)
    {
        var clean = raw.Trim();
        string[] format = ["yyyy-MM-dd", "yyyyMMdd", "yy-MM-dd", "yyMMdd"];
        foreach (var f in format)
        {
            if (DateOnly.TryParseExact(clean, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out datum))
                return true;
        }
        if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            datum = DateOnly.FromDateTime(dt);
            return true;
        }
        datum = default;
        return false;
    }
}

// ======================================================================
// Kontrakt: typade DTO:er som binder ihop mappnings-tjänsten (SharedKernel)
// med Web-sidans sink (Core + DbContext).
// ======================================================================

/// <summary>
/// En färdigvaliderad, typad importrad — redo att bli en <c>Employee</c> och (valfritt) en
/// <c>Employment</c>. Alla värden är SharedKernel-värdeobjekt så att sinken bara behöver anropa
/// kärndomänen.
/// </summary>
public sealed record EmployeeImportData(
    Personnummer Personnummer,
    string Fornamn,
    string Efternamn,
    string? Epost,
    string? Telefon,
    EmployeeEmploymentData? Anstallning);

/// <summary>Anställningsdelen av en importrad (om filen innehåller anställningsuppgifter).</summary>
public sealed record EmployeeEmploymentData(
    string Enhetskod,
    EmploymentType Anstallningsform,
    CollectiveAgreementType Kollektivavtal,
    Money Manadslon,
    Percentage Sysselsattningsgrad,
    DateOnly Startdatum,
    DateOnly? Slutdatum,
    string? Befattning);

/// <summary>Typ av importoperation för en rad.</summary>
public enum ImportOperation
{
    Skapa,
    Uppdatera
}

/// <summary>En enskild import­operation som sinken ska utföra.</summary>
public sealed record EmployeeImportOperation(
    int RadNummer,
    ImportOperation Typ,
    Guid? BefintligtEmployeeId,
    EmployeeImportData Data);

/// <summary>Metadata om en importkörning (för historikpost i migreringslistan).</summary>
public sealed record MigrationImportContext(
    string FilNamn,
    string SkapadAv,
    SourceSystem Kalla,
    int TotaltAntalRader,
    int AntalOgiltiga);

/// <summary>Utfall för en enskild rad som sinken försökte skapa/uppdatera.</summary>
public sealed record SinkRadUtfall(
    int RadNummer,
    bool Lyckades,
    Guid? EmployeeId,
    string? Fel);

/// <summary>Sinkens sammanlagda resultat. <see cref="GlobaltFel"/> satt = hela transaktionen rullades tillbaka.</summary>
public sealed record SinkExekveringsResultat(
    IReadOnlyList<SinkRadUtfall> Utfall,
    string? GlobaltFel);

/// <summary>Status för en rad i importresultatet.</summary>
public enum ImportRadStatus
{
    Skapad,
    Uppdaterad,
    Hoppad,
    Fel
}

/// <summary>Resultatet för en enskild rad i importen (för UI-rapport).</summary>
public sealed record ImportRadResultat(
    int RadNummer,
    string? Personnummer,
    ImportRadStatus Status,
    string? Meddelande);

/// <summary>Sammanfattat resultat av en importkörning.</summary>
public sealed record MigrationImportResult(
    int Totalt,
    int Skapade,
    int Uppdaterade,
    int Hoppade,
    int Fel,
    IReadOnlyList<ImportRadResultat> Rader,
    string? GlobaltFel);

/// <summary>
/// Persistens-abstraktion för anställd-importen. Definieras i Migration-modulen (som bara ser
/// SharedKernel) och implementeras i Web-lagret där Core-domänen och DbContext är tillgängliga.
/// Låter mappnings-/valideringslogiken enhetstestas utan databas.
/// </summary>
public interface IEmployeeImportSink
{
    /// <summary>
    /// Laddar befintliga anställda: normaliserat 12-siffrigt personnummer → EmployeeId (Guid).
    /// Används för dubblettkontroll och idempotens.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> LaddaBefintligaAsync(CancellationToken ct = default);

    /// <summary>
    /// Skapar/uppdaterar samtliga operationer i EN transaktion (ett SaveChanges) och returnerar
    /// per-rad-utfall. Enheter slås upp på enhetskod och skapas om de saknas.
    /// </summary>
    Task<SinkExekveringsResultat> ExekveraAsync(
        IReadOnlyList<EmployeeImportOperation> operationer,
        MigrationImportContext kontext,
        CancellationToken ct = default);
}
