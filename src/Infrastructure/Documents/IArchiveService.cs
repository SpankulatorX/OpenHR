using RegionHR.Documents.Domain;

namespace RegionHR.Infrastructure.Documents;

/// <summary>
/// Tjänst för e-arkivet enligt arkivlagen: arkivering (oföränderlig kopia med
/// integritetshash + arkivmetadata), gallringsspärr (legal hold), gallring och
/// integritetsverifiering. Skarp anslutning mot externt slutarkiv (t.ex. FGS-paket
/// till kommunalt e-arkiv) är konfigklar men körs i demoläge här.
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Arkiverar ett befintligt <see cref="Document"/>: låser källdokumentet, beräknar
    /// integritetshash, sätter gallringsfrist enligt arkivklass och loggar i granskningsloggen.
    /// </summary>
    Task<ArchivedDocument> ArkiveraAsync(
        Guid documentId,
        string diarienummer,
        string ansvarig,
        string arkiveratAv,
        ArchiveClass? arkivklass = null,
        CancellationToken ct = default);

    /// <summary>Sätter gallringsspärr (legal hold) på en arkiverad handling.</summary>
    Task SattGallringsSparrAsync(Guid archivedDocumentId, string orsak, string av, CancellationToken ct = default);

    /// <summary>Häver gallringsspärren på en arkiverad handling.</summary>
    Task TaBortGallringsSparrAsync(Guid archivedDocumentId, string av, CancellationToken ct = default);

    /// <summary>Gallrar en handling vars gallringsfrist löpt ut och som saknar spärr/bevarandekrav.</summary>
    Task GallraAsync(Guid archivedDocumentId, string av, DateTime? asOf = null, CancellationToken ct = default);

    /// <summary>Handlingar som får gallras vid angiven tidpunkt (frist passerad, ingen spärr, bevaras ej).</summary>
    Task<IReadOnlyList<ArchivedDocument>> GetGallringsbaraAsync(DateTime asOf, CancellationToken ct = default);

    /// <summary>Arkiverade handlingar (senaste först), valfritt inklusive redan gallrade.</summary>
    Task<IReadOnlyList<ArchivedDocument>> GetArkiveradeAsync(bool inkluderaGallrade = false, int take = 200, CancellationToken ct = default);

    /// <summary>
    /// Verifierar integriteten mot lagrad hash. Returnerar <c>null</c> om det fysiska
    /// innehållet inte är åtkomligt (kan då inte kontrolleras).
    /// </summary>
    Task<bool?> VerifieraIntegritetAsync(Guid archivedDocumentId, CancellationToken ct = default);
}
