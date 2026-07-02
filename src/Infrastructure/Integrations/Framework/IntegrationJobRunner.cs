using System.Diagnostics;
using RegionHR.IntegrationHub.Framework;

namespace RegionHR.Infrastructure.Integrations.Framework;

/// <summary>
/// Kör en integration end-to-end: slår upp definitionen i registret, hittar en
/// registrerad <see cref="IIntegrationJob"/> för nyckeln, bygger filen, levererar
/// den via <see cref="ISftpTransport"/> till drop-katalogen och skriver utfallet
/// till <see cref="IIntegrationRunLogStore"/>.
///
/// <para>Integrationer utan registrerad jobbdefinition loggas som
/// <see cref="IntegrationRunStatus.SaknarJobb"/> — övriga agenters self-contained
/// generatorer kan registreras senare utan att röra runnern.</para>
/// </summary>
public sealed class IntegrationJobRunner
{
    private readonly IReadOnlyDictionary<string, IIntegrationJob> _jobb;
    private readonly ISftpTransport _transport;
    private readonly IIntegrationRunLogStore _runLog;
    private readonly TimeProvider _time;

    public IntegrationJobRunner(
        IEnumerable<IIntegrationJob> jobb,
        ISftpTransport transport,
        IIntegrationRunLogStore runLog,
        TimeProvider? time = null)
    {
        // Sista registrering per nyckel vinner (deterministiskt via ToDictionary).
        _jobb = jobb.GroupBy(j => j.IntegrationKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        _transport = transport;
        _runLog = runLog;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Finns en körbar jobbdefinition för nyckeln?</summary>
    public bool HarKorbartJobb(string integrationKey) => _jobb.ContainsKey(integrationKey);

    /// <summary>
    /// Kör integrationen och returnerar utfallet. Kastar aldrig för jobbfel —
    /// fel fångas och loggas som <see cref="IntegrationRunStatus.Misslyckad"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Om nyckeln inte finns i registret.</exception>
    public async Task<IntegrationKorningsResultat> KorAsync(
        string integrationKey, string utlostAv, CancellationToken ct = default)
    {
        var def = IntegrationRegistry.HittaOrNull(integrationKey)
            ?? throw new ArgumentException($"Okänd integration: '{integrationKey}'.", nameof(integrationKey));

        var start = _time.GetUtcNow().UtcDateTime;
        var sw = Stopwatch.StartNew();

        // Ingen registrerad jobbdefinition ännu.
        if (!_jobb.TryGetValue(integrationKey, out var jobb))
        {
            sw.Stop();
            var utan = new IntegrationKorningsResultat(
                Guid.NewGuid(), def.Key, def.Namn, start, start.AddMilliseconds(sw.Elapsed.TotalMilliseconds),
                IntegrationRunStatus.SaknarJobb, 0, null, null, _transport.Namn, _transport.ArSkarp,
                utlostAv, null,
                "Ingen körbar jobbdefinition registrerad ännu — filen byggs av respektive adapter när den kopplas in.");
            await _runLog.SparaAsync(utan, ct);
            return utan;
        }

        try
        {
            var payload = await jobb.KorAsync(new IntegrationJobKontext(start, utlostAv), ct);
            var relativSokvag = $"{def.Key}/{payload.Filnamn}";
            var leverans = await _transport.LaddaUppAsync(relativSokvag, payload.Innehall, ct);
            sw.Stop();

            var status = leverans.Lyckades ? IntegrationRunStatus.Lyckad : IntegrationRunStatus.Misslyckad;
            var resultat = new IntegrationKorningsResultat(
                Guid.NewGuid(), def.Key, def.Namn, start, start.AddMilliseconds(sw.Elapsed.TotalMilliseconds),
                status, payload.AntalPoster, payload.Filnamn,
                leverans.Lyckades ? leverans.Plats : null,
                _transport.Namn, _transport.ArSkarp, utlostAv,
                leverans.Lyckades ? null : leverans.Fel, payload.Anmarkning);
            await _runLog.SparaAsync(resultat, ct);
            return resultat;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var fel = new IntegrationKorningsResultat(
                Guid.NewGuid(), def.Key, def.Namn, start, start.AddMilliseconds(sw.Elapsed.TotalMilliseconds),
                IntegrationRunStatus.Misslyckad, 0, null, null, _transport.Namn, _transport.ArSkarp,
                utlostAv, ex.Message, null);
            await _runLog.SparaAsync(fel, ct);
            return fel;
        }
    }
}
