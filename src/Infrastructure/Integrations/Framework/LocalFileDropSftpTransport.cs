using RegionHR.IntegrationHub.Framework;

namespace RegionHR.Infrastructure.Integrations.Framework;

/// <summary>
/// Lokal fil-drop-implementation av <see cref="ISftpTransport"/>. Skriver genererade
/// integrationsfiler till en utkatalog på disk i stället för att öppna en skarp
/// SFTP-anslutning mot Health Connect.
///
/// <para><b>Skarp SFTP är config-ready men EJ live:</b> <see cref="SftpTransportOptions"/>
/// bär host/port/nyckel-fält. När de fylls i (och den skarpa SFTP-klienten kopplas
/// in) byts denna klass ut i DI — jobbrunner och UI påverkas inte. Ramverket kräver
/// alltså ingen skarp anslutning och inget betalt avtal för att fungera.</para>
/// </summary>
public sealed class LocalFileDropSftpTransport : ISftpTransport
{
    private readonly SftpTransportOptions _options;

    public LocalFileDropSftpTransport(SftpTransportOptions options)
    {
        _options = options;
    }

    public string Namn => "Lokal fil-drop (Health Connect-förberedd)";

    /// <summary>Den lokala droppen är aldrig en skarp fjärranslutning.</summary>
    public bool ArSkarp => false;

    public async Task<SftpLeveransResultat> LaddaUppAsync(
        string relativSokvag, byte[] innehall, CancellationToken ct = default)
    {
        try
        {
            var bas = string.IsNullOrWhiteSpace(_options.LokalDropKatalog)
                ? Path.Combine(AppContext.BaseDirectory, "integration-drop")
                : _options.LokalDropKatalog;

            // Rensa relativ sökväg mot traversal och normalisera separatorer.
            var trygg = relativSokvag.Replace('\\', '/').TrimStart('/');
            var delar = trygg.Split('/', StringSplitOptions.RemoveEmptyEntries)
                             .Where(d => d != "." && d != "..")
                             .ToArray();
            if (delar.Length == 0)
                return new SftpLeveransResultat(false, string.Empty, 0, "Tom målsökväg.");

            var fullPath = Path.Combine(new[] { bas }.Concat(delar).ToArray());
            var katalog = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(katalog);

            await File.WriteAllBytesAsync(fullPath, innehall, ct);
            return new SftpLeveransResultat(true, fullPath, innehall.LongLength);
        }
        catch (Exception ex)
        {
            return new SftpLeveransResultat(false, string.Empty, 0, ex.Message);
        }
    }

    public Task<bool> TestaAnslutningAsync(CancellationToken ct = default)
    {
        try
        {
            var bas = string.IsNullOrWhiteSpace(_options.LokalDropKatalog)
                ? Path.Combine(AppContext.BaseDirectory, "integration-drop")
                : _options.LokalDropKatalog;
            Directory.CreateDirectory(bas);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
