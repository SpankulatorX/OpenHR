namespace RegionHR.Documents.Domain;

/// <summary>
/// En handling som förts över till e-arkivet enligt arkivlagen (1990:782).
///
/// <para><b>Oföränderlighet.</b> När en handling arkiverats är dess innehåll och
/// arkivmetadata (diarienummer, arkivklass, gallringsfrist, integritetshash) låsta.
/// Klassen exponerar inga metoder för att ändra dessa fält — de sätts en gång i
/// <see cref="Arkivera"/>. De enda tillåtna livscykelåtgärderna efteråt är arkivrättsliga:
/// sätta/ta bort gallringsspärr (legal hold) samt gallra när fristen löpt ut.
/// <see cref="VerifieraIntegritet(ReadOnlySpan{byte})"/> upptäcker manipulation av det
/// fysiska innehållet genom att jämföra mot den hash som togs vid arkiveringen.</para>
///
/// <para><b>Arkivlagen &gt; GDPR.</b> För allmänna handlingar väger arkivlagens bevarandekrav
/// tyngre än GDPR:s rätt till radering (artikel 17.3 b/d — undantag för rättslig förpliktelse
/// och arkivändamål av allmänt intresse). En GDPR-begäran får därför inte radera en
/// arkivpliktig handling; se <see cref="FarRaderasEnligtGdpr"/>.</para>
/// </summary>
public sealed class ArchivedDocument
{
    public Guid Id { get; private set; }

    /// <summary>Ursprungsdokumentet (<see cref="Document"/>) som arkiverades.</summary>
    public Guid SourceDocumentId { get; private set; }

    /// <summary>Berörd anställd (för korsreferens och registerutdrag).</summary>
    public Guid? AnstallId { get; private set; }

    /// <summary>Diarienummer / registreringsnummer för handlingen.</summary>
    public string Diarienummer { get; private set; } = string.Empty;

    /// <summary>Handlingens titel.</summary>
    public string Titel { get; private set; } = string.Empty;

    public DocumentCategory Kategori { get; private set; }

    /// <summary>Arkiv- och gallringsklass.</summary>
    public ArchiveClass Arkivklass { get; private set; }

    /// <summary>True om handlingen ska bevaras för all framtid (<see cref="ArchiveClass.Bevaras"/>).</summary>
    public bool Bevaras { get; private set; }

    /// <summary>Datum då gallring tidigast får ske; <c>null</c> när handlingen bevaras.</summary>
    public DateTime? GallringsFrist { get; private set; }

    /// <summary>Arkivansvarig (person eller funktion).</summary>
    public string Ansvarig { get; private set; } = string.Empty;

    public DateTime ArkiveratVid { get; private set; }
    public string ArkiveratAv { get; private set; } = string.Empty;

    /// <summary>Integritetshash (SHA-256, hex) beräknad vid arkivering.</summary>
    public string IntegritetsHash { get; private set; } = string.Empty;

    /// <summary>Namn på hashalgoritmen (spårbarhet).</summary>
    public string HashAlgoritm { get; private set; } = ArchiveIntegrity.Algoritm;

    /// <summary>Lagringsväg till den arkiverade (oföränderliga) filkopian.</summary>
    public string StoragePath { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }

    public ArchiveStatus Status { get; private set; }
    public DateTime? GallradVid { get; private set; }
    public string? GallradAv { get; private set; }

    // ── Gallringsspärr (legal hold) ────────────────────────────────────────────
    /// <summary>True om handlingen är spärrad mot gallring (t.ex. pågående rättstvist/utredning).</summary>
    public bool GallringsSparr { get; private set; }
    public string? GallringsSparrOrsak { get; private set; }
    public string? GallringsSparrAv { get; private set; }
    public DateTime? GallringsSparrSatt { get; private set; }

    private ArchivedDocument() { } // EF Core

    /// <summary>
    /// Arkiverar en handling: skapar en oföränderlig arkivpost med arkivmetadata och
    /// beräknar gallringsfrist från arkivklassen. Anropas av arkivtjänsten efter att
    /// integritetshashen räknats fram över filinnehållet.
    /// </summary>
    public static ArchivedDocument Arkivera(
        Guid sourceDocumentId,
        Guid? anstallId,
        string diarienummer,
        string titel,
        DocumentCategory kategori,
        ArchiveClass arkivklass,
        string ansvarig,
        string arkiveratAv,
        string integritetsHash,
        string storagePath,
        string contentType,
        long fileSizeBytes,
        DateTime? arkiveratVid = null)
    {
        if (string.IsNullOrWhiteSpace(diarienummer))
            throw new ArgumentException("Diarienummer krävs för arkivering.", nameof(diarienummer));
        if (string.IsNullOrWhiteSpace(titel))
            throw new ArgumentException("Titel krävs för arkivering.", nameof(titel));
        if (string.IsNullOrWhiteSpace(ansvarig))
            throw new ArgumentException("Arkivansvarig krävs för arkivering.", nameof(ansvarig));
        if (string.IsNullOrWhiteSpace(arkiveratAv))
            throw new ArgumentException("Uppgift om vem som arkiverar krävs.", nameof(arkiveratAv));
        if (string.IsNullOrWhiteSpace(integritetsHash))
            throw new ArgumentException("Integritetshash krävs för arkivering.", nameof(integritetsHash));

        var tidpunkt = arkiveratVid ?? DateTime.UtcNow;

        return new ArchivedDocument
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = sourceDocumentId,
            AnstallId = anstallId,
            Diarienummer = diarienummer.Trim(),
            Titel = titel.Trim(),
            Kategori = kategori,
            Arkivklass = arkivklass,
            Bevaras = arkivklass == ArchiveClass.Bevaras,
            GallringsFrist = ArchiveClassificationPolicy.BeraknaGallringsfrist(arkivklass, tidpunkt),
            Ansvarig = ansvarig.Trim(),
            ArkiveratVid = tidpunkt,
            ArkiveratAv = arkiveratAv.Trim(),
            IntegritetsHash = integritetsHash,
            HashAlgoritm = ArchiveIntegrity.Algoritm,
            StoragePath = storagePath ?? string.Empty,
            ContentType = contentType ?? string.Empty,
            FileSizeBytes = fileSizeBytes,
            Status = ArchiveStatus.Arkiverad
        };
    }

    /// <summary>
    /// Sätter gallringsspärr (legal hold). Handlingen kan då inte gallras förrän spärren
    /// hävs, oavsett gallringsfrist.
    /// </summary>
    public void SattGallringsSparr(string orsak, string av)
    {
        if (Status == ArchiveStatus.Gallrad)
            throw new InvalidOperationException("En gallrad handling kan inte spärras.");
        if (string.IsNullOrWhiteSpace(orsak))
            throw new ArgumentException("Orsak till gallringsspärr krävs.", nameof(orsak));
        if (string.IsNullOrWhiteSpace(av))
            throw new ArgumentException("Uppgift om vem som spärrar krävs.", nameof(av));

        GallringsSparr = true;
        GallringsSparrOrsak = orsak.Trim();
        GallringsSparrAv = av.Trim();
        GallringsSparrSatt = DateTime.UtcNow;
    }

    /// <summary>Häver gallringsspärren.</summary>
    public void TaBortGallringsSparr(string av)
    {
        if (!GallringsSparr)
            throw new InvalidOperationException("Handlingen har ingen gallringsspärr.");
        if (string.IsNullOrWhiteSpace(av))
            throw new ArgumentException("Uppgift om vem som häver spärren krävs.", nameof(av));

        GallringsSparr = false;
        GallringsSparrOrsak = null;
        GallringsSparrAv = null;
        GallringsSparrSatt = null;
    }

    /// <summary>
    /// Anger om handlingen får gallras vid en given tidpunkt: den får aldrig gallras om
    /// den bevaras, är spärrad, redan gallrad, saknar frist eller om fristen inte löpt ut.
    /// </summary>
    public bool KanGallras(DateTime asOf)
    {
        if (Status == ArchiveStatus.Gallrad) return false;
        if (Bevaras) return false;
        if (GallringsSparr) return false;
        if (GallringsFrist is null) return false;
        return GallringsFrist.Value <= asOf;
    }

    /// <summary>
    /// Arkivrättslig prövning av GDPR-radering. Arkivlagen väger tyngre än GDPR för
    /// allmänna handlingar, så radering på GDPR-grund tillåts endast när handlingen
    /// ändå får gallras (frist passerad, ingen spärr, inte bevaras).
    /// </summary>
    public bool FarRaderasEnligtGdpr(DateTime asOf) => KanGallras(asOf);

    /// <summary>
    /// Gallrar handlingen. Kastar om gallring inte är tillåten (bevaras/spärr/frist ej passerad).
    /// </summary>
    public void Gallra(string av, DateTime asOf)
    {
        if (string.IsNullOrWhiteSpace(av))
            throw new ArgumentException("Uppgift om vem som gallrar krävs.", nameof(av));
        if (Status == ArchiveStatus.Gallrad)
            throw new InvalidOperationException("Handlingen är redan gallrad.");
        if (Bevaras)
            throw new InvalidOperationException("Handlingen ska bevaras och får inte gallras.");
        if (GallringsSparr)
            throw new InvalidOperationException("Handlingen har gallringsspärr och får inte gallras.");
        if (GallringsFrist is null || GallringsFrist.Value > asOf)
            throw new InvalidOperationException("Gallringsfristen har inte löpt ut.");

        Status = ArchiveStatus.Gallrad;
        GallradVid = asOf;
        GallradAv = av.Trim();
    }

    /// <summary>Verifierar integriteten genom att räkna om hashen över aktuellt innehåll.</summary>
    public bool VerifieraIntegritet(ReadOnlySpan<byte> aktuelltInnehall) =>
        ArchiveIntegrity.Verify(aktuelltInnehall, IntegritetsHash);

    /// <summary>Verifierar integriteten mot en redan beräknad hash.</summary>
    public bool VerifieraIntegritet(string hash) =>
        !string.IsNullOrEmpty(hash) &&
        string.Equals(hash, IntegritetsHash, StringComparison.OrdinalIgnoreCase);
}
