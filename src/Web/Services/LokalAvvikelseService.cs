using Microsoft.EntityFrameworkCore;
using RegionHR.Agreements.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Web.Services;

/// <summary>
/// Indata för att skapa/uppdatera en lokal avtalsavvikelse. Håller samman de fält som
/// HR fyller i så att UI:t inte behöver skicka ett gäng lösa parametrar.
/// </summary>
public sealed record LokalAvvikelseIndata(
    OrganizationId EnhetId,
    LokalAvvikelseTyp Typ,
    string Namn,
    LokalBerakningsTyp BerakningsTyp,
    LokalBeloppsEnhet Enhet,
    decimal Varde,
    DateOnly GiltigFran,
    DateOnly? GiltigTill,
    OBCategory? ObKategori,
    CollectiveAgreementId? AvtalsId,
    string? Beskrivning);

/// <summary>
/// DB-backad tjänst för lokala avtalsavvikelser / lokala förmåner per organisationsenhet.
///
/// Tjänsten är dels CRUD-hjälp för HR-sidan <c>/admin/avtal/lokala</c>, dels den
/// EXPONERADE uppslagningen som lönemotorn skulle kunna konsultera: den laddar de
/// relevanta posterna och delegerar själva regeln till den rena
/// <see cref="LokalAvvikelseResolver"/> (som är fritt återanvändbar utan EF).
///
/// Använder <c>db.Set&lt;LokalAvtalsAvvikelse&gt;()</c> så att den fungerar oavsett om
/// entiteten registrerats via en DbSet-property eller enbart via en EF-konfiguration.
/// </summary>
public sealed class LokalAvvikelseService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;

    public LokalAvvikelseService(IDbContextFactory<RegionHRDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>Alla lokala avvikelser (för admin-listan).</summary>
    public async Task<List<LokalAvtalsAvvikelse>> HamtaAllaAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<LokalAvtalsAvvikelse>()
            .AsNoTracking()
            .OrderBy(a => a.GiltigFran)
            .ToListAsync(ct);
    }

    /// <summary>Alla lokala avvikelser för en specifik enhet.</summary>
    public async Task<List<LokalAvtalsAvvikelse>> HamtaForEnhetAsync(
        OrganizationId enhetId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<LokalAvtalsAvvikelse>()
            .AsNoTracking()
            .Where(a => a.EnhetId == enhetId)
            .OrderBy(a => a.GiltigFran)
            .ToListAsync(ct);
    }

    /// <summary>
    /// De lokala avvikelser som GÄLLER för en enhet vid ett datum (aktiva + inom
    /// giltighetsfönster + ev. avtalsmatchning). Detta är kärnuppslaget "gäller lokal
    /// avvikelse för enhet X vid datum Y".
    /// </summary>
    public async Task<IReadOnlyList<LokalAvtalsAvvikelse>> HamtaGallandeAsync(
        OrganizationId enhetId,
        DateOnly datum,
        CollectiveAgreementId? avtalsId = null,
        CancellationToken ct = default)
    {
        // Grovfiltrera i databasen på enhet + aktiv; finmatcha period/avtal i resolvern.
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var kandidater = await db.Set<LokalAvtalsAvvikelse>()
            .AsNoTracking()
            .Where(a => a.EnhetId == enhetId && a.Aktiv)
            .ToListAsync(ct);

        return LokalAvvikelseResolver.GallandeAvvikelser(kandidater, enhetId, datum, avtalsId);
    }

    /// <summary>
    /// Effektiv OB-sats för en enhet efter lokala OB-påslag ovanpå den centrala satsen.
    /// Detta är den metod lönemotorn skulle anropa efter att ha hämtat den centrala
    /// AB/HÖK-satsen (se integrationsnoteringen om inkoppling i motorn).
    /// </summary>
    public async Task<decimal> EffektivObSatsAsync(
        decimal centralObSats,
        OrganizationId enhetId,
        OBCategory kategori,
        DateOnly datum,
        CollectiveAgreementId? avtalsId = null,
        CancellationToken ct = default)
    {
        var gallande = await HamtaGallandeAsync(enhetId, datum, avtalsId, ct);
        return LokalAvvikelseResolver.EffektivObSats(centralObSats, gallande, enhetId, kategori, datum, avtalsId);
    }

    public async Task<LokalAvtalsAvvikelse?> HamtaAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<LokalAvtalsAvvikelse>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<LokalAvtalsAvvikelse> LaggTillAsync(
        LokalAvvikelseIndata data, string? anvandare = null, CancellationToken ct = default)
    {
        var avvikelse = LokalAvtalsAvvikelse.Skapa(
            data.EnhetId, data.Typ, data.Namn, data.BerakningsTyp, data.Enhet, data.Varde,
            data.GiltigFran, data.GiltigTill, data.ObKategori, data.AvtalsId, data.Beskrivning, anvandare);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Set<LokalAvtalsAvvikelse>().Add(avvikelse);
        await db.SaveChangesAsync(ct);
        return avvikelse;
    }

    public async Task<bool> UppdateraAsync(
        Guid id, LokalAvvikelseIndata data, string? anvandare = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var avvikelse = await db.Set<LokalAvtalsAvvikelse>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (avvikelse is null)
            return false;

        avvikelse.Uppdatera(
            data.EnhetId, data.Typ, data.Namn, data.BerakningsTyp, data.Enhet, data.Varde,
            data.GiltigFran, data.GiltigTill, data.ObKategori, data.AvtalsId, data.Beskrivning, anvandare);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SattAktivAsync(
        Guid id, bool aktiv, string? anvandare = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var avvikelse = await db.Set<LokalAvtalsAvvikelse>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (avvikelse is null)
            return false;

        if (aktiv)
            avvikelse.Aktivera(anvandare);
        else
            avvikelse.Inaktivera(anvandare);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> TaBortAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var avvikelse = await db.Set<LokalAvtalsAvvikelse>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (avvikelse is null)
            return false;

        db.Set<LokalAvtalsAvvikelse>().Remove(avvikelse);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
