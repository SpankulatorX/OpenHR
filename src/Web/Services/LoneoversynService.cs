using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegionHR.Core.Domain;
using RegionHR.Infrastructure.Payroll;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SalaryReview.Domain;
using RegionHR.SalaryReview.Services;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Orkestrerar löneöversynens genomförandeflöde för Blazor-sidorna:
/// förslag → facklig avstämning → godkännande → genomförande (applicerar ny lön + retro).
///
/// Använder <see cref="IDbContextFactory{TContext}"/> direkt (samma mönster som
/// övriga Web-tjänster) och kör den rena <see cref="SalaryReviewExecutionEngine"/>
/// för själva löneappliceringen. Kan registreras i DI eller instansieras av sidan.
/// </summary>
public sealed class LoneoversynService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly SalaryReviewExecutionEngine _engine = new();
    private readonly PayrollBatchService? _payrollBatch;

    /// <param name="dbFactory">Databasfabrik (samma mönster som övriga Web-tjänster).</param>
    /// <param name="payrollBatch">
    /// Lönekörningstjänsten som skapar den retroaktiva lönekörningen vid genomförande.
    /// Valfri: utan den appliceras ny lön fortfarande, men ingen retro-körning skapas
    /// (t.ex. i importflödet som aldrig genomför rundan).
    /// </param>
    public LoneoversynService(
        IDbContextFactory<RegionHRDbContext> dbFactory,
        PayrollBatchService? payrollBatch = null)
    {
        _dbFactory = dbFactory;
        _payrollBatch = payrollBatch;
    }

    public async Task<List<SalaryReviewRound>> HamtaRundorAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SalaryReviewRounds
            .Include(r => r.Forslag)
            .OrderByDescending(r => r.Ar)
            .ToListAsync(ct);
    }

    public async Task<SalaryReviewRound?> HamtaRundaAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.SalaryReviewRounds
            .Include(r => r.Forslag)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Anställda med en aktiv anställning inom rundans avtalsområde, med nuvarande lön
    /// och rätt anställnings-id — underlag för att lägga till löneförslag.
    /// </summary>
    public async Task<List<LoneKandidat>> HamtaKandidaterAsync(
        CollectiveAgreementType avtal, DateOnly peildatum, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var anstallda = await db.Employees
            .Include(e => e.Anstallningar)
            .OrderBy(e => e.Efternamn)
            .ToListAsync(ct);

        var kandidater = new List<LoneKandidat>();
        foreach (var e in anstallda)
        {
            var anstallning = e.AktivaAnstallningar(peildatum)
                .FirstOrDefault(a => a.Kollektivavtal == avtal)
                ?? e.AktivaAnstallningar(peildatum).FirstOrDefault();
            if (anstallning is null) continue;

            kandidater.Add(new LoneKandidat(
                e.Id,
                anstallning.Id,
                e.FulltNamn,
                anstallning.Befattningstitel ?? "-",
                anstallning.Manadslon));
        }
        return kandidater;
    }

    public async Task LaggTillForslagAsync(
        Guid rundaId, EmployeeId anstallId, EmploymentId anstallningId,
        decimal foreslagenLon, string motivering, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);

        var employee = await db.Employees
            .Include(e => e.Anstallningar)
            .FirstOrDefaultAsync(e => e.Id == anstallId, ct)
            ?? throw new InvalidOperationException("Anställd hittades inte.");
        var anstallning = employee.Anstallningar.FirstOrDefault(a => a.Id == anstallningId)
            ?? throw new InvalidOperationException("Anställning hittades inte på den anställde.");

        runda.LaggTillForslag(anstallId, anstallning.Manadslon, Money.SEK(foreslagenLon), motivering, anstallningId);
        await db.SaveChangesAsync(ct);
    }

    public async Task GodkannForslagAsync(Guid rundaId, Guid forslagId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.GodkannForslag(forslagId);
        await db.SaveChangesAsync(ct);
    }

    public async Task AvvisaForslagAsync(Guid rundaId, Guid forslagId, string anledning, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.AvvisaForslag(forslagId, anledning);
        await db.SaveChangesAsync(ct);
    }

    public async Task SkickaFackligAvstemningAsync(Guid rundaId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.SkickaFackligAvstemning();
        await db.SaveChangesAsync(ct);
    }

    public async Task GodkannFackligAsync(Guid rundaId, string fackligRepresentant, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        runda.GodkannFacklig(fackligRepresentant);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Genomför rundan: applicerar varje godkänt förslag som ny lön på anställningen,
    /// beräknar retroaktivt belopp och sparar allt i samma transaktion. Därefter skapas
    /// en retroaktiv lönekörning per månad i fönstret [ikraftträdande, genomförande) så
    /// att differensen faktiskt betalas ut — inte bara visas i UI:t.
    /// </summary>
    public async Task<LoneoversynGenomforandeResultat> GenomforAsync(
        Guid rundaId, string genomfordAv, DateOnly? genomforandeDatum = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);

        var anstalldIds = runda.Forslag
            .Where(f => f.Status == SalaryProposalStatus.Godkand)
            .Select(f => f.AnstallId)
            .Distinct()
            .ToList();

        var anstallda = new Dictionary<EmployeeId, Employee>();
        foreach (var id in anstalldIds)
        {
            var employee = await db.Employees
                .Include(e => e.Anstallningar)
                .FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new InvalidOperationException($"Anställd {id} hittades inte.");
            anstallda[id] = employee;
        }

        var datum = genomforandeDatum ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resultat = _engine.Genomfor(runda, anstallda, datum, genomfordAv);

        // En SaveChanges persisterar både rundans status/retro och de nya lönerna
        // (alla aggregat spåras av samma DbContext).
        await db.SaveChangesAsync(ct);

        // Skapa retroaktiva lönekörningar för varje månad mellan ikraftträdande och
        // genomförande. Körs EFTER SaveChanges så att omräkningen ser de nya lönerna.
        // Fel per period (t.ex. ingen ursprunglig körning för månaden) stoppar inte
        // genomförandet — lönerna är redan applicerade — utan rapporteras till anroparen.
        var retroKorningar = new List<string>();
        var retroFel = new List<string>();
        if (_payrollBatch is not null)
        {
            var period = new DateOnly(runda.IkrafttradandeDatum.Year, runda.IkrafttradandeDatum.Month, 1);
            var genomforandeManad = new DateOnly(datum.Year, datum.Month, 1);
            while (period < genomforandeManad)
            {
                var retroPeriod = $"{period.Year:D4}-{period.Month:D2}";
                try
                {
                    await _payrollBatch.ExecuteRetroactiveRunAsync(
                        datum.Year, datum.Month, retroPeriod, genomfordAv, ct);
                    retroKorningar.Add(retroPeriod);
                }
                catch (Exception ex)
                {
                    retroFel.Add($"{retroPeriod}: {ex.Message}");
                }
                period = period.AddMonths(1);
            }
        }

        return new LoneoversynGenomforandeResultat(resultat, retroKorningar, retroFel);
    }

    /// <summary>Namnuppslag EmployeeId → fullständigt namn för att visa förslag/ändringar.</summary>
    public async Task<Dictionary<EmployeeId, string>> HamtaAnstalldNamnAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var anstallda = await db.Employees.ToListAsync(ct);
        return anstallda.ToDictionary(e => e.Id, e => e.FulltNamn);
    }

    // ─────────────────────────── Filimport av löneförslag ───────────────────────────

    private readonly SalaryProposalImportParser _importParser = new();

    /// <summary>
    /// Läser en uppladdad CSV- eller Excel-fil, tolkar löneförslagen och matchar varje rad
    /// mot en anställd + aktiv anställning i rundan. Committar ingenting — resultatet är en
    /// förhandsgranskning med giltiga rader och tydliga fel per rad, som sidan visar innan
    /// användaren bekräftar. Systemet är experten: rader som skulle spränga budget, sakna
    /// anställd eller dubbleras avvisas redan här.
    /// </summary>
    public async Task<SalaryImportForhandsvisning> FortolkaFilAsync(
        Guid rundaId, Stream fil, string filnamn, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        if (runda.Status != SalaryReviewStatus.Planering)
            throw new InvalidOperationException("Import kan bara göras när rundan är i planeringsfasen.");

        var parseResultat = await TolkaFilAsync(fil, filnamn, ct);
        if (!parseResultat.HarRader)
            return new SalaryImportForhandsvisning(rundaId, [], parseResultat.GlobalaFel, 0, 0);

        var anstallda = await db.Employees.Include(e => e.Anstallningar).ToListAsync(ct);
        // Gruppera först: om registret mot förmodan har dubblett-pnr ska uppslaget inte krascha.
        var perPnr = anstallda
            .GroupBy(e => (string)e.Personnummer)
            .ToDictionary(g => g.Key, g => g.First());

        var rader = new List<SalaryImportForslagRad>();
        var sedanAnstallningar = new HashSet<EmploymentId>();
        var lopandeFordelad = runda.FordeladBudget;

        foreach (var parsad in parseResultat.Rader)
        {
            var fel = new List<string>(parsad.Fel);

            Employee? anstalld = null;
            Employment? anstallning = null;

            if (parsad.ArGiltig && parsad.Personnummer is { } pnr)
            {
                if (!perPnr.TryGetValue((string)pnr, out anstalld))
                {
                    fel.Add($"Ingen anställd med personnummer {pnr.ToMaskedString()} i registret.");
                }
                else
                {
                    anstallning = ValjAnstallning(anstalld, runda, parsad, fel);
                    if (anstallning is not null)
                    {
                        if (!sedanAnstallningar.Add(anstallning.Id))
                            fel.Add("Dubblett: anställningen förekommer redan i importen.");
                        else if (runda.Forslag.Any(f =>
                                     f.AnstallningId == anstallning.Id && f.Status != SalaryProposalStatus.Avslagen))
                            fel.Add("Anställningen har redan ett löneförslag i rundan.");
                    }
                }
            }

            decimal? nuvarande = anstallning?.Manadslon.Amount;
            decimal? okning = (anstallning is not null && parsad.NyLon is { } ny) ? ny - anstallning.Manadslon.Amount : null;

            // Speglar aggregatets spärr: lönesänkning avvisas redan i förhandsgranskningen.
            if (okning is < 0)
                fel.Add("Föreslagen lön är lägre än nuvarande lön — lönesänkning kan inte importeras.");

            // Budgetkontroll i filordning, speglar aggregatets sekventiella kontroll.
            if (fel.Count == 0 && okning is { } o)
            {
                var nyFordelad = lopandeFordelad + Money.SEK(o);
                if (nyFordelad > runda.TotalBudget)
                    fel.Add($"Överskrider rundans budget (återstår {runda.AterstaendeBudget.Amount:N0} kr).");
                else
                    lopandeFordelad = nyFordelad;
            }

            rader.Add(new SalaryImportForslagRad(
                parsad.RadNummer,
                parsad.PersonnummerRaw ?? "-",
                anstalld?.FulltNamn,
                anstallning?.Befattningstitel,
                nuvarande,
                parsad.NyLon,
                okning,
                parsad.Motivering ?? string.Empty,
                anstalld?.Id,
                anstallning?.Id,
                fel));
        }

        return new SalaryImportForhandsvisning(
            rundaId,
            rader,
            parseResultat.GlobalaFel,
            rader.Count(r => r.ArGiltig),
            rader.Count(r => !r.ArGiltig));
    }

    /// <summary>
    /// Committar de giltiga, förhandsgranskade raderna till rundan som löneförslag i en
    /// transaktion. Nuvarande lön läses om från databasen (litar aldrig på klientvärdet)
    /// och varje rad läggs till via aggregatet så att alla domäninvarianter gäller.
    /// </summary>
    public async Task<SalaryImportResultat> CommittaImportAsync(
        Guid rundaId, IReadOnlyList<SalaryImportForslagRad> giltigaRader, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var runda = await LaddaRundaAsync(db, rundaId, ct);
        if (runda.Status != SalaryReviewStatus.Planering)
            throw new InvalidOperationException("Import kan bara göras när rundan är i planeringsfasen.");

        var anstalldIds = giltigaRader
            .Where(r => r.AnstallId is not null)
            .Select(r => r.AnstallId!.Value)
            .ToHashSet();

        // Ladda alla och filtrera i minnet — undviker EF-översättning av Contains över
        // en värdekonverterad nyckel (samma säkra mönster som GenomforAsync använder).
        var anstallda = (await db.Employees
                .Include(e => e.Anstallningar)
                .ToListAsync(ct))
            .Where(e => anstalldIds.Contains(e.Id))
            .ToDictionary(e => e.Id);

        var skapade = 0;
        var avvisade = 0;
        var meddelanden = new List<string>();

        foreach (var rad in giltigaRader)
        {
            if (rad.AnstallId is not { } anstallId || rad.AnstallningId is not { } anstallningId
                || rad.NyLon is not { } nyLon)
            {
                avvisade++;
                meddelanden.Add($"Rad {rad.RadNummer}: ofullständig rad, hoppades över.");
                continue;
            }

            if (!anstallda.TryGetValue(anstallId, out var anstalld)
                || anstalld.Anstallningar.FirstOrDefault(a => a.Id == anstallningId) is not { } anstallning)
            {
                avvisade++;
                meddelanden.Add($"Rad {rad.RadNummer}: anställningen hittades inte längre.");
                continue;
            }

            try
            {
                runda.LaggTillForslag(
                    anstallId, anstallning.Manadslon, Money.SEK(nyLon),
                    string.IsNullOrWhiteSpace(rad.Motivering) ? "Importerad" : rad.Motivering,
                    anstallningId);
                skapade++;
            }
            catch (Exception ex)
            {
                avvisade++;
                meddelanden.Add($"Rad {rad.RadNummer}: {ex.Message}");
            }
        }

        if (skapade > 0)
            await db.SaveChangesAsync(ct);

        return new SalaryImportResultat(skapade, avvisade, meddelanden);
    }

    /// <summary>
    /// Väljer vilken anställning ett importerat förslag ska gälla: explicit angivet
    /// anställnings-id vinner, annars den aktiva anställningen på ikraftträdandedatumet
    /// (i första hand inom rundans avtalsområde).
    /// </summary>
    private static Employment? ValjAnstallning(
        Employee anstalld, SalaryReviewRound runda, SalaryImportRad parsad, List<string> fel)
    {
        if (parsad.AnstallningId is { } explicitId)
        {
            var traff = anstalld.Anstallningar.FirstOrDefault(a => a.Id == explicitId);
            if (traff is null)
                fel.Add("Angivet anställnings-id finns inte på den anställde.");
            return traff;
        }

        var aktiva = anstalld.AktivaAnstallningar(runda.IkrafttradandeDatum);
        if (aktiva.Count == 0)
        {
            fel.Add($"Ingen aktiv anställning {runda.IkrafttradandeDatum:yyyy-MM-dd}.");
            return null;
        }

        var iAvtal = aktiva.Where(a => a.Kollektivavtal == runda.Avtalsomrade).ToList();
        var val = iAvtal.Count > 0 ? iAvtal : aktiva.ToList();
        if (val.Count > 1)
        {
            fel.Add("Flera aktiva anställningar — ange anställnings-id i filen.");
            return null;
        }
        return val[0];
    }

    private async Task<SalaryImportParseResult> TolkaFilAsync(Stream fil, string filnamn, CancellationToken ct)
    {
        var ext = Path.GetExtension(filnamn).ToLowerInvariant();
        if (ext is ".xlsx" or ".xlsm" or ".xls")
        {
            using var ms = new MemoryStream();
            await fil.CopyToAsync(ms, ct);
            ms.Position = 0;
            return _importParser.ParseRutnat(LasExcelRutnat(ms));
        }

        using var reader = new StreamReader(fil, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        return _importParser.ParseCsv(text);
    }

    private static IReadOnlyList<IReadOnlyList<string?>> LasExcelRutnat(Stream fil)
    {
        using var wb = new XLWorkbook(fil);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws is null)
            return [];

        var sistaRad = ws.LastRowUsed()?.RowNumber() ?? 0;
        var sistaKol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var rutnat = new List<IReadOnlyList<string?>>(sistaRad);

        for (var r = 1; r <= sistaRad; r++)
        {
            var celler = new string?[sistaKol];
            for (var c = 1; c <= sistaKol; c++)
                celler[c - 1] = ws.Cell(r, c).GetString();
            rutnat.Add(celler);
        }
        return rutnat;
    }

    private static async Task<SalaryReviewRound> LaddaRundaAsync(
        RegionHRDbContext db, Guid rundaId, CancellationToken ct) =>
        await db.SalaryReviewRounds.Include(r => r.Forslag).FirstOrDefaultAsync(r => r.Id == rundaId, ct)
        ?? throw new InvalidOperationException($"Löneöversynsrunda {rundaId} hittades inte.");
}

/// <summary>
/// Utfallet av ett genomförande: själva löneappliceringen plus vilka retroaktiva
/// lönekörningar som skapades (en per månad i retrofönstret) respektive misslyckades.
/// </summary>
public sealed record LoneoversynGenomforandeResultat(
    SalaryReviewExecutionResult Genomforande,
    IReadOnlyList<string> RetroaktivaKorningar,
    IReadOnlyList<string> RetroaktivaFel)
{
    public int AntalAnstallda => Genomforande.AntalAnstallda;
    public Money TotalRetroaktivt => Genomforande.TotalRetroaktivt;
}

/// <summary>En anställd som kan få ett löneförslag i en runda.</summary>
public sealed record LoneKandidat(
    EmployeeId AnstallId,
    EmploymentId AnstallningId,
    string Namn,
    string Befattning,
    Money NuvarandeLon);

/// <summary>
/// Förhandsgranskning av en importfil: en rad per tolkad datarad, redo att visas för
/// användaren innan commit. Innehåller både giltiga rader och rader med tydliga fel.
/// </summary>
public sealed record SalaryImportForhandsvisning(
    Guid RundaId,
    IReadOnlyList<SalaryImportForslagRad> Rader,
    IReadOnlyList<string> GlobalaFel,
    int AntalGiltiga,
    int AntalFel)
{
    public bool HarRader => Rader.Count > 0;
    public IReadOnlyList<SalaryImportForslagRad> GiltigaRader => Rader.Where(r => r.ArGiltig).ToList();
}

/// <summary>En förhandsgranskad importrad, matchad mot anställd/anställning där möjligt.</summary>
public sealed record SalaryImportForslagRad(
    int RadNummer,
    string PersonnummerRaw,
    string? Namn,
    string? Befattning,
    decimal? NuvarandeLon,
    decimal? NyLon,
    decimal? Okning,
    string Motivering,
    EmployeeId? AnstallId,
    EmploymentId? AnstallningId,
    IReadOnlyList<string> Fel)
{
    /// <summary>Raden är fullständigt validerad och kan committas till rundan.</summary>
    public bool ArGiltig =>
        Fel.Count == 0 && AnstallId is not null && AnstallningId is not null && NyLon is > 0;
}

/// <summary>Utfallet av en committad import: hur många förslag som skapades respektive avvisades.</summary>
public sealed record SalaryImportResultat(
    int AntalSkapade,
    int AntalAvvisade,
    IReadOnlyList<string> Meddelanden);
