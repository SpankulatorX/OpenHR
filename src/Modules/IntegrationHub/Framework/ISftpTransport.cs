namespace RegionHR.IntegrationHub.Framework;

/// <summary>
/// Transportabstraktion för filbaserade integrationer. Regionens filintegrationer
/// går via SFTP mot integrationsmotorn Health Connect. Ramverket beror endast på
/// detta gränssnitt — den skarpa SFTP-klienten kan kopplas in senare utan att
/// jobbrunner eller UI ändras.
///
/// Standardimplementationen är en LOKAL fil-drop (skriver till en utkatalog på
/// disk). Skarp SFTP kräver endast konfiguration (host/port/nyckel via
/// <see cref="SftpTransportOptions"/>) — inget betalt avtal för själva ramverket.
/// </summary>
public interface ISftpTransport
{
    /// <summary>Läsbart namn på transporten (visas i övervaknings-UI).</summary>
    string Namn { get; }

    /// <summary>
    /// Sant om transporten är en skarp fjärranslutning. Den lokala fil-droppen
    /// returnerar false så att UI tydligt kan märka körningar som icke-skarpa.
    /// </summary>
    bool ArSkarp { get; }

    /// <summary>
    /// Levererar <paramref name="innehall"/> till <paramref name="relativSokvag"/>
    /// (relativt transportens baskatalog/fjärrkatalog).
    /// </summary>
    Task<SftpLeveransResultat> LaddaUppAsync(string relativSokvag, byte[] innehall, CancellationToken ct = default);

    /// <summary>Testar att transporten är nåbar/skrivbar (för hälsokontroll).</summary>
    Task<bool> TestaAnslutningAsync(CancellationToken ct = default);
}

/// <summary>Resultat av en filleverans.</summary>
/// <param name="Lyckades">Om filen skrevs/levererades.</param>
/// <param name="Plats">Var filen hamnade (lokal sökväg eller fjärr-URI).</param>
/// <param name="Bytes">Antal bytes som levererades.</param>
/// <param name="Fel">Felmeddelande om leveransen misslyckades.</param>
public sealed record SftpLeveransResultat(bool Lyckades, string Plats, long Bytes, string? Fel = null);

/// <summary>
/// Konfiguration för fil-transporten. Fälten för skarp SFTP är förberedda men
/// oanvända av den lokala fil-droppen.
/// </summary>
public sealed class SftpTransportOptions
{
    /// <summary>Katalog dit den lokala fil-droppen skriver filer.</summary>
    public string LokalDropKatalog { get; set; } = string.Empty;

    /// <summary>Skarp SFTP-host (config-ready, ej använd av lokal drop).</summary>
    public string? Host { get; set; }

    /// <summary>Skarp SFTP-port.</summary>
    public int Port { get; set; } = 22;

    /// <summary>Skarp SFTP-användarnamn.</summary>
    public string? Anvandarnamn { get; set; }

    /// <summary>Sökväg till privat nyckel för skarp SFTP.</summary>
    public string? PrivatNyckelSokvag { get; set; }

    /// <summary>Baskatalog på fjärrservern (Health Connect inbox).</summary>
    public string? FjarrBaskatalog { get; set; }

    /// <summary>Sant om tillräcklig konfiguration för skarp SFTP finns.</summary>
    public bool HarSkarpKonfiguration =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Anvandarnamn) &&
        !string.IsNullOrWhiteSpace(PrivatNyckelSokvag);
}
