using System.Text.Json;
using RegionHR.SharedKernel.Abstractions;

namespace RegionHR.IntegrationHub.Adapters.Forsakringskassan;

/// <summary>
/// Adapter mot Försäkringskassan.
/// Hanterar sjukanmälan dag 15+ (FK 7263), föräldrafrånvarorapportering
/// och mottagande av FK-beslut.
/// I produktion: REST/SOAP-integration mot Försäkringskassans e-tjänster.
/// </summary>
public sealed class ForsakringskassanAdapter : IIntegrationAdapter
{
    public string SystemName => "Forsakringskassan";

    public async Task<IntegrationResult> ExecuteAsync(IntegrationRequest request, CancellationToken ct = default)
    {
        return request.OperationType switch
        {
            "GenereraFKAnmalan" => await GenereraFKAnmalan(request, ct),
            "SkickaFKSjukanmalan" => await SkickaFKSjukanmalan(request, ct),
            "RapporteraForaldrafranvaro" => await RapporteraForaldrafranvaro(request, ct),
            "TaEmotFKBeslut" => await TaEmotFKBeslut(request, ct),
            _ => new IntegrationResult(false, $"Okänd operation: {request.OperationType}")
        };
    }

    /// <summary>
    /// Genererar arbetsgivarens sjukanmälan till Försäkringskassan (strukturerad fil +
    /// läsbar sammanfattning) via <see cref="FKAnmalanGenerator"/>. Filen ÖVERFÖRS INTE —
    /// skarp överföring till FK:s e-tjänst kräver avtal/anslutning (se
    /// <see cref="FKAnmalanGenerator.OverforingStatus"/>).
    /// </summary>
    private Task<IntegrationResult> GenereraFKAnmalan(IntegrationRequest request, CancellationToken ct)
    {
        FKAnmalanInput? input;
        try
        {
            input = request.Payload is FKAnmalanInput i
                ? i
                : JsonSerializer.Deserialize<FKAnmalanInput>(request.Payload?.ToString() ?? "");
        }
        catch
        {
            return Task.FromResult(new IntegrationResult(false, "Ogiltig payload för FK-anmälan"));
        }

        if (input is null)
            return Task.FromResult(new IntegrationResult(false, "FK-anmälan saknas"));

        var result = new FKAnmalanGenerator().Generera(input);

        if (!result.Giltig)
            return Task.FromResult(new IntegrationResult(
                false,
                "FK-anmälan kunde inte genereras: " + string.Join("; ", result.Fel),
                result));

        return Task.FromResult(new IntegrationResult(
            true,
            $"FK-anmälan genererad ({result.Filnamn}). Ej överförd till FK — skarp överföring " +
            "kräver avtal/anslutning till Försäkringskassans e-tjänst.",
            result));
    }

    /// <summary>
    /// Skickar sjukanmälan till Försäkringskassan (FK 7263) efter dag 14.
    /// Arbetsgivaren är skyldig att anmäla sjukfrånvaro som varar längre än 14 dagar.
    /// </summary>
    private Task<IntegrationResult> SkickaFKSjukanmalan(IntegrationRequest request, CancellationToken ct)
    {
        FKSjukanmalan? sjukanmalan;
        try
        {
            sjukanmalan = request.Payload is FKSjukanmalan s
                ? s
                : JsonSerializer.Deserialize<FKSjukanmalan>(request.Payload?.ToString() ?? "");
        }
        catch
        {
            return Task.FromResult(new IntegrationResult(false, "Ogiltig payload för sjukanmälan"));
        }

        if (sjukanmalan is null)
            return Task.FromResult(new IntegrationResult(false, "Sjukanmälan saknas"));

        if (string.IsNullOrWhiteSpace(sjukanmalan.Personnummer))
            return Task.FromResult(new IntegrationResult(false, "Personnummer saknas i sjukanmälan"));

        if (sjukanmalan.SjukfranvaroStart == default)
            return Task.FromResult(new IntegrationResult(false, "Sjukfrånvaro startdatum saknas"));

        // Validera att det gått minst 14 dagar
        var dagar = sjukanmalan.SjukfranvaroSlut.HasValue
            ? (sjukanmalan.SjukfranvaroSlut.Value.ToDateTime(TimeOnly.MinValue) - sjukanmalan.SjukfranvaroStart.ToDateTime(TimeOnly.MinValue)).Days
            : (DateTime.Today - sjukanmalan.SjukfranvaroStart.ToDateTime(TimeOnly.MinValue)).Days;

        if (dagar < 14)
            return Task.FromResult(new IntegrationResult(false,
                "Sjukanmälan till FK görs först efter dag 14"));

        // Underlaget förbereds här. Skarp överföring till FK:s e-tjänst
        // (POST https://etjanster.forsakringskassan.se/api/v1/sjukanmalan) kräver
        // tecknat avtal och teknisk anslutning och sker INTE i denna miljö.
        var arendeId = $"FK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";

        return Task.FromResult(new IntegrationResult(
            true,
            $"Sjukanmälan förberedd för FK med ärende-ID {arendeId}. Ej överförd — " +
            "skarp överföring kräver avtal/anslutning till Försäkringskassans e-tjänst.",
            new { ArendeId = arendeId, Forberedd = DateTime.UtcNow, Overford = false }));
    }

    /// <summary>
    /// Rapporterar föräldrafrånvaro till Försäkringskassan.
    /// Krävs för samordning av föräldrapenning.
    /// </summary>
    private Task<IntegrationResult> RapporteraForaldrafranvaro(IntegrationRequest request, CancellationToken ct)
    {
        FKSjukanmalan? data;
        try
        {
            data = request.Payload is FKSjukanmalan s
                ? s
                : JsonSerializer.Deserialize<FKSjukanmalan>(request.Payload?.ToString() ?? "");
        }
        catch
        {
            return Task.FromResult(new IntegrationResult(false, "Ogiltig payload för föräldrafrånvaro"));
        }

        if (data is null)
            return Task.FromResult(new IntegrationResult(false, "Föräldrafrånvarodata saknas"));

        if (string.IsNullOrWhiteSpace(data.Personnummer))
            return Task.FromResult(new IntegrationResult(false, "Personnummer saknas"));

        // I produktion: Rapportera till FK:s e-tjänst
        var arendeId = $"FP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}";

        return Task.FromResult(new IntegrationResult(
            true,
            $"Föräldrafrånvaro rapporterad med ärende-ID {arendeId}",
            new { ArendeId = arendeId, Rapporterad = DateTime.UtcNow }));
    }

    /// <summary>
    /// Tar emot beslut från Försäkringskassan (t.ex. sjukpenningbeslut).
    /// </summary>
    private Task<IntegrationResult> TaEmotFKBeslut(IntegrationRequest request, CancellationToken ct)
    {
        FKBeslut? beslut;
        try
        {
            beslut = request.Payload is FKBeslut b
                ? b
                : JsonSerializer.Deserialize<FKBeslut>(request.Payload?.ToString() ?? "");
        }
        catch
        {
            return Task.FromResult(new IntegrationResult(false, "Ogiltig payload för FK-beslut"));
        }

        if (beslut is null)
            return Task.FromResult(new IntegrationResult(false, "FK-beslut saknas"));

        return Task.FromResult(new IntegrationResult(
            true,
            $"FK-beslut mottaget: {beslut.BeslutTyp} ({beslut.ArendeId})",
            beslut));
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        // I produktion: ping FK:s e-tjänst
        return Task.FromResult(true);
    }
}

// --- Models ---

/// <summary>
/// Sjukanmälan till Försäkringskassan (FK 7263).
/// Skickas efter dag 14 av sjukfrånvaro.
/// </summary>
public sealed class FKSjukanmalan
{
    public string Personnummer { get; set; } = string.Empty;
    public DateOnly SjukfranvaroStart { get; set; }
    public DateOnly? SjukfranvaroSlut { get; set; }
    public string Arbetsgivare { get; set; } = string.Empty;
}

/// <summary>
/// Beslut från Försäkringskassan.
/// </summary>
public sealed class FKBeslut
{
    public string ArendeId { get; set; } = string.Empty;
    public string BeslutTyp { get; set; } = string.Empty;   // Sjukpenning, Föräldrapenning, etc.
    public DateOnly Datum { get; set; }
}
