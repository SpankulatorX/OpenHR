using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RegionHR.Documents.Domain;

/// <summary>
/// Beräknar och verifierar integritetshash (SHA-256) för arkiverade handlingar.
/// Hashen fångar handlingens innehåll vid arkiveringstillfället; en förändring i
/// efterhand (manipulation) upptäcks eftersom den omräknade hashen då avviker.
/// Detta är den tekniska garanten för e-arkivets <em>oföränderlighet</em>.
/// </summary>
public static class ArchiveIntegrity
{
    /// <summary>Namnet på den hashalgoritm som används (för spårbarhet i arkivmetadata).</summary>
    public const string Algoritm = "SHA-256";

    /// <summary>Beräknar SHA-256 över binärt innehåll och returnerar hex (gemener).</summary>
    public static string Hash(ReadOnlySpan<byte> content)
    {
        Span<byte> digest = stackalloc byte[32];
        _ = SHA256.HashData(content, digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Beräknar SHA-256 över en textrepresentation (UTF-8) och returnerar hex (gemener).</summary>
    public static string Hash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Hash(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Deterministiskt metadata-fingeravtryck att hasha när det fysiska innehållet inte
    /// är åtkomligt (demo/konfigklar drift). Bygger en stabil sträng av arkivmetadata.
    /// </summary>
    public static string MetadataFingerprint(string storagePath, string fileName, long fileSizeBytes, string contentType)
    {
        var fingerprint = string.Concat(
            storagePath ?? string.Empty, "\n",
            fileName ?? string.Empty, "\n",
            fileSizeBytes.ToString(CultureInfo.InvariantCulture), "\n",
            contentType ?? string.Empty);
        return Hash(fingerprint);
    }

    /// <summary>
    /// Jämför en förväntad hash mot innehållets omräknade hash (skiftlägesokänsligt).
    /// Returnerar true om integriteten är intakt.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> content, string expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash)) return false;
        return string.Equals(Hash(content), expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
