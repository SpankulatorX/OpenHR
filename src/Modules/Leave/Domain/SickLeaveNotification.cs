namespace RegionHR.Leave.Domain;

/// <summary>
/// Sjukfrnvaroanmlan med svensk sjukskrivningsuppfljning.
/// Lkarintyg krvs frn dag 8, Frskringskassan ska anmlas frn dag 15.
/// </summary>
public sealed class SickLeaveNotification
{
    public Guid Id { get; private set; }
    public Guid AnstallId { get; private set; }
    public DateOnly StartDatum { get; private set; }
    public DateOnly? SlutDatum { get; private set; }
    public int SjukDag { get; private set; }

    /// <summary>
    /// True om sjukdag >= 8: lkarintyg krvs.
    /// </summary>
    public bool LakarintygKravs { get; private set; }

    /// <summary>
    /// True om sjukdag >= 15: anmlan till Frskringskassan krvs.
    /// </summary>
    public bool FKAnmalanKravs { get; private set; }

    public bool LakarintygInlamnat { get; private set; }
    public bool FKAnmalanGjord { get; private set; }

    private SickLeaveNotification() { } // EF Core

    /// <summary>
    /// Skapar en ny sjukanmlan med dag 1.
    /// </summary>
    public static SickLeaveNotification Skapa(Guid anstallId, DateOnly start)
    {
        return new SickLeaveNotification
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            StartDatum = start,
            SlutDatum = null,
            SjukDag = 1,
            LakarintygKravs = false,
            FKAnmalanKravs = false,
            LakarintygInlamnat = false,
            FKAnmalanGjord = false
        };
    }

    /// <summary>
    /// Uppdaterar sjukdagnummer och stter flaggor fr lkarintyg och FK-anmlan.
    /// </summary>
    public void UppdateraDag(int dagNr)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dagNr, 1);

        SjukDag = dagNr;
        LakarintygKravs = dagNr >= 8;
        FKAnmalanKravs = dagNr >= 15;
    }

    /// <summary>
    /// Berknar aktuellt sjukdagnummer utifrn startdatum: dag 1 = startdatum.
    /// Avslutade sjukfall rknas t.o.m. slutdatum, pgende t.o.m. angivet datum.
    /// </summary>
    public int BeraknaSjukDag(DateOnly idag)
    {
        var till = SlutDatum.HasValue && SlutDatum.Value < idag ? SlutDatum.Value : idag;
        if (till < StartDatum) return 1;
        return till.DayNumber - StartDatum.DayNumber + 1;
    }

    /// <summary>
    /// Synkroniserar <see cref="SjukDag"/> och lagkravsflaggorna (lkarintyg dag 8,
    /// FK-anmlan dag 15) mot angivet datum. Anropas dagligen av bakgrundsjobbet
    /// fr ppna sjukfall samt vid registrering av sjukanmlan med retroaktiv start.
    /// </summary>
    public void SynkroniseraDag(DateOnly idag) => UppdateraDag(BeraknaSjukDag(idag));

    /// <summary>True om sjukfallet r avslutat (friskanmlt).</summary>
    public bool ArAvslutad => SlutDatum.HasValue;

    /// <summary>
    /// Friskanmlan: avslutar sjukfallet med angiven sista sjukdag och fryser
    /// dagrkningen dr, s att pminnelser och auto-rehab slutar rkna vidare.
    /// </summary>
    public void Avsluta(DateOnly sistaSjukdag)
    {
        if (SlutDatum.HasValue)
            throw new InvalidOperationException("Sjukfallet r redan avslutat.");
        if (sistaSjukdag < StartDatum)
            throw new ArgumentException("Sista sjukdagen kan inte vara fre startdatum.", nameof(sistaSjukdag));

        SlutDatum = sistaSjukdag;
        UppdateraDag(BeraknaSjukDag(sistaSjukdag));
    }
}
