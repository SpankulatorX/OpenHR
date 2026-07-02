using System.IO.Compression;
using System.Text;
using RegionHR.LMS.Domain;
using Xunit;

namespace RegionHR.LMS.Tests;

public class ScormManifestParserTests
{
    private const string Scorm12Manifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest identifier="COURSE-001" version="1.0"
          xmlns="http://www.imsproject.org/xsd/imscp_rootv1p1p2"
          xmlns:adlcp="http://www.adlnet.org/xsd/adlcp_rootv1p2"
          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <metadata>
            <schema>ADL SCORM</schema>
            <schemaversion>1.2</schemaversion>
          </metadata>
          <organizations default="ORG-1">
            <organization identifier="ORG-1">
              <title>Arbetsmiljö grundkurs</title>
              <item identifier="ITEM-1" identifierref="RES-1">
                <title>Introduktion</title>
                <adlcp:masteryscore>80</adlcp:masteryscore>
              </item>
            </organization>
          </organizations>
          <resources>
            <resource identifier="RES-1" type="webcontent" adlcp:scormtype="sco" href="index.html">
              <file href="index.html"/>
            </resource>
          </resources>
        </manifest>
        """;

    private const string Scorm2004Manifest = """
        <?xml version="1.0"?>
        <manifest identifier="C2"
          xmlns="http://www.imsglobal.org/xsd/imscp_v1p1"
          xmlns:adlcp="http://www.adlnet.org/xsd/adlcp_v1p3">
          <metadata>
            <schema>ADL SCORM</schema>
            <schemaversion>2004 3rd Edition</schemaversion>
          </metadata>
          <organizations default="O">
            <organization identifier="O">
              <title>HLR 2004</title>
              <item identifier="I1" identifierref="R1"><title>Del 1</title></item>
            </organization>
          </organizations>
          <resources>
            <resource identifier="R1" href="start/launch.html"/>
          </resources>
        </manifest>
        """;

    [Fact]
    public void ParseManifestXml_Scorm12_ExtraherarMetadata()
    {
        var info = ScormManifestParser.ParseManifestXml(Scorm12Manifest);

        Assert.Equal("COURSE-001", info.Identifier);
        Assert.Equal("Arbetsmiljö grundkurs", info.Titel);
        Assert.Equal(ScormVersion.Scorm12, info.Version);
        Assert.Equal("index.html", info.LaunchUrl);
        Assert.Equal(80m, info.MasteryScore);
    }

    [Fact]
    public void ParseManifestXml_Scorm2004_ExtraherarMetadata()
    {
        var info = ScormManifestParser.ParseManifestXml(Scorm2004Manifest);

        Assert.Equal("C2", info.Identifier);
        Assert.Equal("HLR 2004", info.Titel);
        Assert.Equal(ScormVersion.Scorm2004, info.Version);
        Assert.Equal("start/launch.html", info.LaunchUrl);
        Assert.Null(info.MasteryScore);
    }

    [Fact]
    public void ParseManifestXml_UtanSchemaversionOchItems_OkandVersionOchFallbackLaunch()
    {
        const string xml = """
            <manifest identifier="X">
              <resources>
                <resource identifier="R" href="fallback.html"/>
              </resources>
            </manifest>
            """;

        var info = ScormManifestParser.ParseManifestXml(xml);

        Assert.Equal("X", info.Identifier);
        Assert.Equal("X", info.Titel); // ingen org-titel → identifier används
        Assert.Equal(ScormVersion.Okand, info.Version);
        Assert.Equal("fallback.html", info.LaunchUrl);
    }

    [Fact]
    public void ParseManifestXml_OgiltigXml_KastarInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() => ScormManifestParser.ParseManifestXml("<manifest><oavslutad"));
    }

    [Fact]
    public void Parse_FranZip_LaserManifest()
    {
        using var zip = BuildZip(("imsmanifest.xml", Scorm12Manifest), ("index.html", "<html></html>"));

        var info = ScormManifestParser.Parse(zip);

        Assert.Equal("COURSE-001", info.Identifier);
        Assert.Equal(ScormVersion.Scorm12, info.Version);
    }

    [Fact]
    public void InnehallerManifest_MedManifest_ReturnerarTrue()
    {
        using var zip = BuildZip(("imsmanifest.xml", Scorm12Manifest), ("index.html", "x"));

        Assert.True(ScormManifestParser.InnehallerManifest(zip));
    }

    [Fact]
    public void InnehallerManifest_UtanManifest_ReturnerarFalse()
    {
        using var zip = BuildZip(("index.html", "x"), ("style.css", "y"));

        Assert.False(ScormManifestParser.InnehallerManifest(zip));
    }

    [Fact]
    public void Parse_UtanManifest_KastarInvalidOperation()
    {
        using var zip = BuildZip(("index.html", "x"));

        Assert.Throws<InvalidOperationException>(() => ScormManifestParser.Parse(zip));
    }

    [Fact]
    public void Parse_ManifestIUndermapp_HittasOcksa()
    {
        using var zip = BuildZip(("content/imsmanifest.xml", Scorm2004Manifest), ("content/start/launch.html", "x"));

        var info = ScormManifestParser.Parse(zip);

        Assert.Equal("C2", info.Identifier);
    }

    private static MemoryStream BuildZip(params (string Path, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
                w.Write(content);
            }
        }
        ms.Position = 0;
        return ms;
    }
}
