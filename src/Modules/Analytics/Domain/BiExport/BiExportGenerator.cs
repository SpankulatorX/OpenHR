using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RegionHR.Analytics.Domain.BiExport;

/// <summary>
/// Serialiserar ett <see cref="BiStjarnschema"/> till CSV och JSON för leverans till
/// externa BI-/DW-verktyg.
///
/// Format-beslut:
///  • CSV: RFC 4180-liknande — kommaseparerat, fält som innehåller komma, citattecken
///    eller radbrytning citeras och inbäddade citattecken dubbleras. Decimaler skrivs
///    med <see cref="CultureInfo.InvariantCulture"/> (punkt som decimaltecken) så att
///    Power BI/Diver läser dem oberoende av lokal.
///  • JSON: hela stjärnschemat i ett dokument (System.Text.Json, invariant).
///  • Teckenkodning: UTF-8 (utan BOM) — Power BI och Diver läser UTF-8; till skillnad
///    från SIE-exporten (Latin1) finns inget legacy-krav här.
///
/// Klassen är ren (inga sidoeffekter, ingen DB) och därför trivialt enhetstestbar.
/// </summary>
public static class BiExportGenerator
{
    /// <summary>UTF-8 utan byte order mark — undviker att BI-verktyg tolkar BOM som data.</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Bevara svenska tecken i klartext i stället för \uXXXX-escaping.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Rubrikrader (extraherade till fält — undviker konstant-array-argument, CA1861).
    private static readonly string[] HeaderDimTid =
        ["TidId", "Ar", "Kvartal", "Manad", "ManadNamn"];
    private static readonly string[] HeaderDimEnhet =
        ["EnhetId", "Namn", "Kostnadsstalle", "Typ", "OverordnadEnhetId", "HsaId"];
    private static readonly string[] HeaderDimBefattning =
        ["BefattningId", "Titel", "BESTAKod", "AIDKod"];
    private static readonly string[] HeaderDimKon =
        ["KonId", "Beteckning"];
    private static readonly string[] HeaderDimAlder =
        ["AlderId", "Intervall", "MinAlder", "MaxAlder"];
    private static readonly string[] HeaderFaktaAnstallning =
        ["TidId", "EnhetId", "BefattningId", "KonId", "AlderId", "AntalAnstallningar",
         "Sysselsattningsgrad", "Fte", "ManadslonSEK", "ArTillsvidare", "ArTidsbegransad"];
    private static readonly string[] HeaderFaktaLon =
        ["TidId", "EnhetId", "KonId", "BruttoSEK", "SkattSEK", "NettoSEK",
         "ArbetsgivaravgifterSEK", "PensionsavgiftSEK", "TotalArbetskraftskostnadSEK"];
    private static readonly string[] HeaderFaktaFranvaro =
        ["TidId", "EnhetId", "KonId", "FranvaroTyp", "AntalDagar", "AntalFall"];
    private static readonly string[] HeaderPlattAnstallning =
        ["Ar", "Kvartal", "Manad", "EnhetNamn", "Kostnadsstalle", "Befattning",
         "Kon", "Aldersintervall", "AntalAnstallningar", "Sysselsattningsgrad",
         "Fte", "ManadslonSEK", "ArTillsvidare", "ArTidsbegransad"];

    // ── JSON ────────────────────────────────────────────────────────────────

    /// <summary>Serialiserar hela stjärnschemat till en JSON-sträng.</summary>
    public static string GenereraJson(BiStjarnschema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return JsonSerializer.Serialize(schema, JsonOptions);
    }

    /// <summary>Serialiserar hela stjärnschemat till JSON-byte (UTF-8 utan BOM).</summary>
    public static byte[] GenereraJsonBytes(BiStjarnschema schema) =>
        Utf8NoBom.GetBytes(GenereraJson(schema));

    // ── CSV per tabell (dimensionsmodellerad export) ──────────────────────────

    /// <summary>
    /// Genererar en CSV-fil per tabell i stjärnschemat. Nyckeln i ordlistan är filnamnet
    /// (t.ex. <c>fakta_anstallning.csv</c>) och värdet är filens innehåll.
    /// Lämpad att buntas till ett ZIP-paket för schemalagd leverans.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GenereraCsvPaket(BiStjarnschema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dim_tid.csv"] = ToCsv(
                HeaderDimTid,
                schema.DimTid,
                d => [d.TidId, Int(d.Ar), Int(d.Kvartal), Int(d.Manad), d.ManadNamn]),

            ["dim_enhet.csv"] = ToCsv(
                HeaderDimEnhet,
                schema.DimEnhet,
                d => [d.EnhetId, d.Namn, d.Kostnadsstalle, d.Typ, d.OverordnadEnhetId ?? "", d.HsaId ?? ""]),

            ["dim_befattning.csv"] = ToCsv(
                HeaderDimBefattning,
                schema.DimBefattning,
                d => [d.BefattningId, d.Titel, d.BESTAKod ?? "", d.AIDKod ?? ""]),

            ["dim_kon.csv"] = ToCsv(
                HeaderDimKon,
                schema.DimKon,
                d => [d.KonId, d.Beteckning]),

            ["dim_alder.csv"] = ToCsv(
                HeaderDimAlder,
                schema.DimAlder,
                d => [d.AlderId, d.Intervall, Int(d.MinAlder), Int(d.MaxAlder)]),

            ["fakta_anstallning.csv"] = ToCsv(
                HeaderFaktaAnstallning,
                schema.FaktaAnstallning,
                f =>
                [
                    f.TidId, f.EnhetId, f.BefattningId, f.KonId, f.AlderId, Int(f.AntalAnstallningar),
                    Dec(f.Sysselsattningsgrad), Dec(f.Fte), Dec(f.ManadslonSEK),
                    Int(f.ArTillsvidare), Int(f.ArTidsbegransad)
                ]),

            ["fakta_lon.csv"] = ToCsv(
                HeaderFaktaLon,
                schema.FaktaLon,
                f =>
                [
                    f.TidId, f.EnhetId, f.KonId, Dec(f.BruttoSEK), Dec(f.SkattSEK), Dec(f.NettoSEK),
                    Dec(f.ArbetsgivaravgifterSEK), Dec(f.PensionsavgiftSEK), Dec(f.TotalArbetskraftskostnadSEK)
                ]),

            ["fakta_franvaro.csv"] = ToCsv(
                HeaderFaktaFranvaro,
                schema.FaktaFranvaro,
                f => [f.TidId, f.EnhetId, f.KonId, f.FranvaroTyp, Int(f.AntalDagar), Int(f.AntalFall)]),
        };
    }

    // ── Platt (denormaliserad) export ─────────────────────────────────────────

    /// <summary>
    /// Genererar en <b>platt</b> (denormaliserad) anställnings-CSV där varje faktarad
    /// får dimensionsattributen inlinade (enhetsnamn, kostnadsställe, befattning, kön,
    /// åldersintervall, period). Lämpad för verktyg där man vill dra in en enda tabell
    /// utan att modellera relationer.
    /// </summary>
    public static string GenereraPlattAnstallningCsv(BiStjarnschema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var tid = schema.DimTid.ToDictionary(d => d.TidId, StringComparer.Ordinal);
        var enhet = schema.DimEnhet.ToDictionary(d => d.EnhetId, StringComparer.Ordinal);
        var befattning = schema.DimBefattning.ToDictionary(d => d.BefattningId, StringComparer.Ordinal);
        var kon = schema.DimKon.ToDictionary(d => d.KonId, StringComparer.Ordinal);
        var alder = schema.DimAlder.ToDictionary(d => d.AlderId, StringComparer.Ordinal);

        return ToCsv(
            HeaderPlattAnstallning,
            schema.FaktaAnstallning,
            f =>
            {
                var t = tid.GetValueOrDefault(f.TidId);
                var e = enhet.GetValueOrDefault(f.EnhetId);
                var b = befattning.GetValueOrDefault(f.BefattningId);
                var k = kon.GetValueOrDefault(f.KonId);
                var a = alder.GetValueOrDefault(f.AlderId);
                return
                [
                    Int(t?.Ar ?? 0),
                    Int(t?.Kvartal ?? 0),
                    Int(t?.Manad ?? 0),
                    e?.Namn ?? f.EnhetId,
                    e?.Kostnadsstalle ?? "",
                    b?.Titel ?? f.BefattningId,
                    k?.Beteckning ?? f.KonId,
                    a?.Intervall ?? f.AlderId,
                    Int(f.AntalAnstallningar),
                    Dec(f.Sysselsattningsgrad),
                    Dec(f.Fte),
                    Dec(f.ManadslonSEK),
                    Int(f.ArTillsvidare),
                    Int(f.ArTidsbegransad)
                ];
            });
    }

    // ── Hjälpare ──────────────────────────────────────────────────────────────

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Dec(decimal value) =>
        Math.Round(value, 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture);

    /// <summary>Bygger en CSV-sträng ur en rubrikrad + en radprojektion.</summary>
    private static string ToCsv<T>(string[] header, IReadOnlyList<T> rows, Func<T, string[]> project)
    {
        var sb = new StringBuilder();
        sb.Append(JoinRow(header));
        foreach (var row in rows)
            sb.Append(JoinRow(project(row)));
        return sb.ToString();
    }

    private static string JoinRow(string[] fields)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(EscapeCsv(fields[i]));
        }
        // CRLF enligt RFC 4180.
        sb.Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Citerar och escapar ett CSV-fält vid behov (komma, citat, radbrytning).</summary>
    public static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        var behoverCitat = field.Contains(',') || field.Contains('"')
            || field.Contains('\n') || field.Contains('\r');

        if (!behoverCitat)
            return field;

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
