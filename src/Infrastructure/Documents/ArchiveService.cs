using Microsoft.EntityFrameworkCore;
using RegionHR.Audit.Domain;
using RegionHR.Documents.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Infrastructure.Storage;

namespace RegionHR.Infrastructure.Documents;

/// <summary>
/// Standardimplementation av <see cref="IArchiveService"/> mot PostgreSQL + lokal fillagring.
/// Integritetshashen räknas över den fysiska filen när den är åtkomlig, annars över ett
/// deterministiskt metadata-fingeravtryck (ärligt märkt demoläge). Varje åtgärd loggas i
/// granskningsloggen (<see cref="AuditEntry"/>) attribuerad till den som utför den.
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly IFileStorageService _storage;

    public ArchiveService(IDbContextFactory<RegionHRDbContext> dbFactory, IFileStorageService storage)
    {
        _dbFactory = dbFactory;
        _storage = storage;
    }

    public async Task<ArchivedDocument> ArkiveraAsync(
        Guid documentId,
        string diarienummer,
        string ansvarig,
        string arkiveratAv,
        ArchiveClass? arkivklass = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new InvalidOperationException($"Dokument {documentId} hittades inte.");

        var redanArkiverad = await db.Set<ArchivedDocument>()
            .AnyAsync(a => a.SourceDocumentId == documentId, ct);
        if (redanArkiverad)
            throw new InvalidOperationException("Dokumentet är redan arkiverat i e-arkivet.");

        var klass = arkivklass ?? ArchiveClassificationPolicy.ForeslaArkivklass(doc.Kategori);
        var hash = await BeraknaHashAsync(doc, ct);

        var arkiverad = ArchivedDocument.Arkivera(
            sourceDocumentId: doc.Id,
            anstallId: doc.AnstallId,
            diarienummer: diarienummer,
            titel: doc.FileName,
            kategori: doc.Kategori,
            arkivklass: klass,
            ansvarig: ansvarig,
            arkiveratAv: arkiveratAv,
            integritetsHash: hash,
            storagePath: doc.StoragePath,
            contentType: doc.ContentType,
            fileSizeBytes: doc.FileSizeBytes);

        // Lås källdokumentet (oföränderligt efter arkivering) och spegla gallringsfristen.
        doc.Archive();
        if (arkiverad.GallringsFrist is { } frist)
            doc.SetRetention(frist);

        db.Set<ArchivedDocument>().Add(arkiverad);

        LoggaAudit(db, arkiverad.Id, AuditAction.Create, arkiveratAv,
            $"Arkiverad: diarienr {arkiverad.Diarienummer}, klass {arkiverad.Arkivklass}, " +
            $"frist {(arkiverad.GallringsFrist?.ToString("yyyy-MM-dd") ?? "bevaras")}, hash {arkiverad.IntegritetsHash}");

        await db.SaveChangesAsync(ct);
        return arkiverad;
    }

    public async Task SattGallringsSparrAsync(Guid archivedDocumentId, string orsak, string av, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var arkiverad = await HamtaAsync(db, archivedDocumentId, ct);
        arkiverad.SattGallringsSparr(orsak, av);
        LoggaAudit(db, arkiverad.Id, AuditAction.Update, av, $"Gallringsspärr satt: {orsak}");
        await db.SaveChangesAsync(ct);
    }

    public async Task TaBortGallringsSparrAsync(Guid archivedDocumentId, string av, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var arkiverad = await HamtaAsync(db, archivedDocumentId, ct);
        arkiverad.TaBortGallringsSparr(av);
        LoggaAudit(db, arkiverad.Id, AuditAction.Update, av, "Gallringsspärr hävd");
        await db.SaveChangesAsync(ct);
    }

    public async Task GallraAsync(Guid archivedDocumentId, string av, DateTime? asOf = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var arkiverad = await HamtaAsync(db, archivedDocumentId, ct);
        var tidpunkt = asOf ?? DateTime.UtcNow;
        arkiverad.Gallra(av, tidpunkt);
        LoggaAudit(db, arkiverad.Id, AuditAction.Delete, av,
            $"Gallrad {tidpunkt:yyyy-MM-dd}: diarienr {arkiverad.Diarienummer}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ArchivedDocument>> GetGallringsbaraAsync(DateTime asOf, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Set<ArchivedDocument>()
            .Where(a => a.Status == ArchiveStatus.Arkiverad
                        && !a.Bevaras
                        && !a.GallringsSparr
                        && a.GallringsFrist != null
                        && a.GallringsFrist <= asOf)
            .OrderBy(a => a.GallringsFrist)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ArchivedDocument>> GetArkiveradeAsync(bool inkluderaGallrade = false, int take = 200, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Set<ArchivedDocument>().AsQueryable();
        if (!inkluderaGallrade)
            query = query.Where(a => a.Status == ArchiveStatus.Arkiverad);
        return await query
            .OrderByDescending(a => a.ArkiveratVid)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<bool?> VerifieraIntegritetAsync(Guid archivedDocumentId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var arkiverad = await HamtaAsync(db, archivedDocumentId, ct);

        var innehall = await LaddaInnehallAsync(arkiverad.StoragePath, ct);
        if (innehall is null)
            return null; // fysiskt innehåll ej åtkomligt → kan ej verifieras

        return arkiverad.VerifieraIntegritet(innehall);
    }

    private static async Task<ArchivedDocument> HamtaAsync(RegionHRDbContext db, Guid id, CancellationToken ct) =>
        await db.Set<ArchivedDocument>().FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new InvalidOperationException($"Arkiverad handling {id} hittades inte.");

    private async Task<string> BeraknaHashAsync(Document doc, CancellationToken ct)
    {
        var innehall = await LaddaInnehallAsync(doc.StoragePath, ct);
        return innehall is not null
            ? ArchiveIntegrity.Hash(innehall)
            : ArchiveIntegrity.MetadataFingerprint(doc.StoragePath, doc.FileName, doc.FileSizeBytes, doc.ContentType);
    }

    private async Task<byte[]?> LaddaInnehallAsync(string storagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;
        try
        {
            await using var stream = await _storage.DownloadAsync(storagePath, ct);
            if (stream is null)
                return null;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void LoggaAudit(RegionHRDbContext db, Guid archivedId, AuditAction action, string userId, string beskrivning)
    {
        db.AuditEntries.Add(AuditEntry.Create(
            entityType: nameof(ArchivedDocument),
            entityId: archivedId.ToString(),
            action: action,
            oldValues: null,
            newValues: beskrivning,
            userId: userId,
            userName: userId,
            ipAddress: null));
    }
}
