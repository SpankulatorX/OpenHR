using System.Globalization;
using System.Text;
using RegionHR.IntegrationHub.Framework;

namespace RegionHR.Infrastructure.Integrations.Framework;

/// <summary>
/// Inbyggd referensintegration: exporterar en självbeskrivande CSV-manifest över
/// alla integrationer OpenHR exponerar (från <see cref="IntegrationRegistry"/>).
/// Kräver ingen extern data och fungerar direkt — den bevisar hela kedjan
/// jobb → transport → run-log ur lådan och ger något körbart att testa "kör om" på
/// innan övriga adaptrar registrerar sina egna jobb.
/// </summary>
public sealed class HealthConnectManifestJob : IIntegrationJob
{
    public const string Nyckel = "hc-manifest";

    public string IntegrationKey => Nyckel;

    public Task<IntegrationJobResultat> KorAsync(IntegrationJobKontext kontext, CancellationToken ct = default)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# OpenHR — integrationsmanifest för Health Connect");
        sb.AppendLine($"# Genererad;{kontext.Tidpunkt.ToString("yyyy-MM-dd HH:mm:ss", inv)}Z");
        sb.AppendLine($"# UtlostAv;{Rensa(kontext.UtlostAv)}");
        sb.AppendLine($"# AntalIntegrationer;{IntegrationRegistry.Alla.Count.ToString(inv)}");
        sb.AppendLine("Nyckel;Namn;Riktning;Transport;Motpart;Frekvens;Format;ViaHealthConnect");

        foreach (var d in IntegrationRegistry.Alla)
        {
            sb.Append(Rensa(d.Key)).Append(';')
              .Append(Rensa(d.Namn)).Append(';')
              .Append(d.Riktning).Append(';')
              .Append(d.Transport).Append(';')
              .Append(Rensa(d.Motpart)).Append(';')
              .Append(Rensa(d.Frekvens)).Append(';')
              .Append(Rensa(d.Format)).Append(';')
              .Append(d.ViaHealthConnect ? "Ja" : "Nej")
              .Append('\n');
        }

        var innehall = Encoding.UTF8.GetBytes(sb.ToString());
        var filnamn = $"openhr-integrationsmanifest_{kontext.Tidpunkt.ToString("yyyyMMddHHmmss", inv)}.csv";
        var resultat = new IntegrationJobResultat(
            filnamn, innehall, IntegrationRegistry.Alla.Count,
            "Referenskörning — inga externa system kontaktas.");
        return Task.FromResult(resultat);
    }

    // CSV är semikolon-separerad; byt ut avgränsare/radbrytning i fritext.
    private static string Rensa(string? s) =>
        (s ?? string.Empty).Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
}
