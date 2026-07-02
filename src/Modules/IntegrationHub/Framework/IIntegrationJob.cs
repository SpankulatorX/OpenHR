namespace RegionHR.IntegrationHub.Framework;

/// <summary>
/// En körbar jobbdefinition för EN integration. De self-contained
/// fil-generatorerna (AGI, pain.001, KPA, SIE, ...) kan var för sig implementera
/// detta gränssnitt och registreras i DI — jobbrunner hittar dem via
/// <see cref="IntegrationKey"/> och behöver inte känna till hur filen byggs.
/// </summary>
public interface IIntegrationJob
{
    /// <summary>Nyckeln till <see cref="IntegrationDefinition"/> som jobbet kör.</summary>
    string IntegrationKey { get; }

    /// <summary>Bygger filen som ska levereras för denna integration.</summary>
    Task<IntegrationJobResultat> KorAsync(IntegrationJobKontext kontext, CancellationToken ct = default);
}

/// <summary>Indata till ett jobb (period och vem/vad som utlöste körningen).</summary>
/// <param name="Tidpunkt">Tidpunkt då körningen startade (UTC).</param>
/// <param name="UtlostAv">Källa: t.ex. "Manuell (Karl Berg)" eller "Schemalagt".</param>
public sealed record IntegrationJobKontext(DateTime Tidpunkt, string UtlostAv);

/// <summary>Filen ett jobb producerar.</summary>
/// <param name="Filnamn">Filnamn (utan katalog).</param>
/// <param name="Innehall">Filens bytes (korrekt teckenkodning för formatet).</param>
/// <param name="AntalPoster">Antal poster/rader i filen (för run-log).</param>
/// <param name="Anmarkning">Valfri notering (t.ex. "0 anställda utan bankkonto").</param>
public sealed record IntegrationJobResultat(
    string Filnamn,
    byte[] Innehall,
    int AntalPoster,
    string? Anmarkning = null);
