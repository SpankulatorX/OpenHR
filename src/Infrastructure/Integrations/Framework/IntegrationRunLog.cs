using RegionHR.IntegrationHub.Framework;

namespace RegionHR.Infrastructure.Integrations.Framework;

/// <summary>
/// EF Core-entitet som persisterar en integrationskörning (schema:
/// integration_hub, tabell: integration_run_log). Kompletterar
/// <c>OutboxMessage</c>: run-loggen är en revisionslogg per körning (vad
/// genererades, hur många poster, vart det levererades, ev. fel), medan outboxen
/// är den pålitliga leveranskön.
/// </summary>
public sealed class IntegrationRunLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IntegrationKey { get; set; } = string.Empty;
    public string IntegrationNamn { get; set; } = string.Empty;
    public DateTime StartadUtc { get; set; }
    public DateTime AvslutadUtc { get; set; }
    public IntegrationRunStatus Status { get; set; }
    public int AntalPoster { get; set; }
    public string? Filnamn { get; set; }
    public string? Plats { get; set; }
    public string TransportNamn { get; set; } = string.Empty;
    public bool SkarpTransport { get; set; }
    public string UtlostAv { get; set; } = string.Empty;
    public string? Fel { get; set; }
    public string? Anmarkning { get; set; }

    /// <summary>Bygger en entitet ur ramverkets immutabla resultat-DTO.</summary>
    public static IntegrationRunLog Fran(IntegrationKorningsResultat r) => new()
    {
        Id = r.Id,
        IntegrationKey = r.IntegrationKey,
        IntegrationNamn = r.IntegrationNamn,
        StartadUtc = r.StartadUtc,
        AvslutadUtc = r.AvslutadUtc,
        Status = r.Status,
        AntalPoster = r.AntalPoster,
        Filnamn = r.Filnamn,
        Plats = r.Plats,
        TransportNamn = r.TransportNamn,
        SkarpTransport = r.SkarpTransport,
        UtlostAv = r.UtlostAv,
        Fel = r.Fel,
        Anmarkning = r.Anmarkning
    };

    /// <summary>Projicerar entiteten till ramverkets DTO.</summary>
    public IntegrationKorningsResultat TillResultat() => new(
        Id, IntegrationKey, IntegrationNamn, StartadUtc, AvslutadUtc, Status,
        AntalPoster, Filnamn, Plats, TransportNamn, SkarpTransport, UtlostAv, Fel, Anmarkning);
}
