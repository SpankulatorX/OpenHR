using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace RegionHR.LMS.Domain;

/// <summary>
/// Tolkar <c>imsmanifest.xml</c> ur ett SCORM-paket (zip). Namnrymds-agnostisk
/// (matchar på element-lokalnamn) så att både SCORM 1.2 och 2004 fungerar utan
/// att binda mot specifika XSD-namnrymder.
///
/// FÖRENKLAT jämfört med full SCORM-import: vi extraherar identifier, titel,
/// schemaversion, första launch-resursens href och ev. masteryscore. Vi validerar
/// inte hela manifest-schemat, följer inte imsss-sekvensering och tolkar inte
/// sub-manifest/externa metadata-filer.
/// </summary>
public static class ScormManifestParser
{
    public const string ManifestFileName = "imsmanifest.xml";

    /// <summary>Snabbkoll: innehåller zip-strömmen en imsmanifest.xml (i roten eller valfri mapp)?</summary>
    public static bool InnehallerManifest(Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Any(IsManifestEntry);
    }

    private static bool IsManifestEntry(ZipArchiveEntry e) =>
        string.Equals(e.FullName, ManifestFileName, StringComparison.OrdinalIgnoreCase)
        || e.FullName.EndsWith("/" + ManifestFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Läser och tolkar imsmanifest.xml ur zip-strömmen.</summary>
    /// <exception cref="InvalidOperationException">Om manifest saknas eller inte är giltig XML.</exception>
    public static ScormManifestInfo Parse(Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        // Föredra manifest närmast roten (kortast sökväg).
        var entry = archive.Entries
            .Where(IsManifestEntry)
            .OrderBy(e => e.FullName.Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Zip-paketet saknar {ManifestFileName} — inte ett giltigt SCORM-paket.");

        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        return ParseManifestXml(xml);
    }

    /// <summary>Tolkar en imsmanifest.xml-sträng.</summary>
    public static ScormManifestInfo ParseManifestXml(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException("imsmanifest.xml kunde inte tolkas som XML.", ex);
        }

        var manifest = doc.Root ?? throw new InvalidOperationException("imsmanifest.xml saknar rot-element.");

        var identifier = ((string?)manifest.Attribute("identifier") ?? "").Trim();

        var schemaVersion = Descendants(manifest, "schemaversion").FirstOrDefault()?.Value?.Trim() ?? "";
        var version = TolkaVersion(schemaVersion, manifest);

        // Titel: direkt <title> under en <organization>, annars valfri <title>, annars identifier.
        var orgTitle = Descendants(manifest, "organization")
            .Select(o => DirectChild(o, "title")?.Value?.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        var titel = orgTitle
            ?? Descendants(manifest, "title").FirstOrDefault()?.Value?.Trim()
            ?? (string.IsNullOrWhiteSpace(identifier) ? "SCORM-paket" : identifier);

        var launchUrl = HittaLaunchUrl(manifest);

        decimal? mastery = null;
        var msText = Descendants(manifest, "masteryscore").FirstOrDefault()?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(msText)
            && decimal.TryParse(msText, NumberStyles.Any, CultureInfo.InvariantCulture, out var ms))
        {
            mastery = ms;
        }

        return new ScormManifestInfo(identifier, titel, version, launchUrl, mastery);
    }

    private static string HittaLaunchUrl(XElement manifest)
    {
        var resources = Descendants(manifest, "resource").ToList();

        // Koppla organization → första <item identifierref="..."> → matchande <resource identifier="...">.
        var firstItemRef = Descendants(manifest, "item")
            .Select(i => (string?)i.Attribute("identifierref"))
            .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

        if (!string.IsNullOrWhiteSpace(firstItemRef))
        {
            var matched = resources.FirstOrDefault(r => (string?)r.Attribute("identifier") == firstItemRef);
            var href = (string?)matched?.Attribute("href");
            if (!string.IsNullOrWhiteSpace(href)) return href!.Trim();
        }

        // Fallback: första resource med en href.
        var firstHref = resources
            .Select(r => (string?)r.Attribute("href"))
            .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
        return firstHref?.Trim() ?? "";
    }

    private static ScormVersion TolkaVersion(string schemaVersion, XElement manifest)
    {
        var sv = schemaVersion.ToLowerInvariant();
        if (sv.Contains("1.2")) return ScormVersion.Scorm12;
        if (sv.Contains("2004") || sv.Contains("1.3") || sv.Contains("cam")) return ScormVersion.Scorm2004;

        // Fallback: namnrymd på rot-elementet.
        var ns = manifest.Name.NamespaceName;
        if (ns.Contains("imscp_rootv1p1p2", StringComparison.OrdinalIgnoreCase)) return ScormVersion.Scorm12;
        if (ns.Contains("imscp_v1p1", StringComparison.OrdinalIgnoreCase)) return ScormVersion.Scorm2004;

        // Fallback: ADL-namnrymdsdeklarationer.
        var declared = manifest.Attributes().Where(a => a.IsNamespaceDeclaration).Select(a => a.Value).ToList();
        if (declared.Any(n => n.Contains("adlcp_rootv1p2", StringComparison.OrdinalIgnoreCase))) return ScormVersion.Scorm12;
        if (declared.Any(n => n.Contains("adlcp_v1p3", StringComparison.OrdinalIgnoreCase))) return ScormVersion.Scorm2004;

        return ScormVersion.Okand;
    }

    private static IEnumerable<XElement> Descendants(XElement root, string localName) =>
        root.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static XElement? DirectChild(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
}
