namespace RegionHR.Infrastructure.Arbetsmiljo;

public enum IncidentAllvarlighetsgrad { Lag, Medel, Hog, Kritisk }
public enum IncidentTyp { Tillbud, Olycka, Arbetsskada }
public enum IncidentStatus { Rapporterad, UnderUtredning, AtgardVidtagen, Avslutad }

/// <summary>
/// Tillbud, olycka eller arbetsskada rapporterad i verksamheten.
/// </summary>
public class Incident
{
    public Guid Id { get; private set; }
    public DateTime Datum { get; private set; }

    /// <summary>Fritext — ingen koppling till Employee i v1.</summary>
    public string RapporterareNamn { get; private set; } = default!;

    /// <summary>Logisk referens till OrganizationUnit.Id.Value. Inget FK-constraint.</summary>
    public Guid EnhetId { get; private set; }

    public string Plats { get; private set; } = default!;
    public string Beskrivning { get; private set; } = default!;
    public IncidentAllvarlighetsgrad Allvarlighetsgrad { get; private set; }
    public IncidentTyp Typ { get; private set; }
    public IncidentStatus Status { get; private set; }
    public string? AtgardsForslag { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Incident() { }

    public static Incident Skapa(
        DateTime datum, string rapporterare, Guid enhetId, string plats,
        string beskrivning, IncidentAllvarlighetsgrad allvarlighetsgrad,
        IncidentTyp typ, string? atgardsForslag = null)
    {
        return new Incident
        {
            Id = Guid.NewGuid(),
            Datum = datum,
            RapporterareNamn = rapporterare,
            EnhetId = enhetId,
            Plats = plats,
            Beskrivning = beskrivning,
            Allvarlighetsgrad = allvarlighetsgrad,
            Typ = typ,
            Status = IncidentStatus.Rapporterad,
            AtgardsForslag = atgardsForslag,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Nästa status i incidentens livscykel
    /// (Rapporterad → Under utredning → Åtgärd vidtagen → Avslutad),
    /// eller null om ärendet redan är avslutat.
    /// </summary>
    public IncidentStatus? NastaStatus => Status switch
    {
        IncidentStatus.Rapporterad => IncidentStatus.UnderUtredning,
        IncidentStatus.UnderUtredning => IncidentStatus.AtgardVidtagen,
        IncidentStatus.AtgardVidtagen => IncidentStatus.Avslutad,
        _ => null
    };

    /// <summary>
    /// Flyttar incidenten ett steg framåt i livscykeln. Ett avslutat ärende
    /// påverkas inte (använd <see cref="Ateroppna"/> för att öppna igen).
    /// </summary>
    public void FlyttaFram()
    {
        if (NastaStatus is { } nasta)
            Status = nasta;
    }

    /// <summary>Återöppnar ett avslutat ärende för fortsatt utredning.</summary>
    public void Ateroppna()
    {
        if (Status == IncidentStatus.Avslutad)
            Status = IncidentStatus.UnderUtredning;
    }
}
