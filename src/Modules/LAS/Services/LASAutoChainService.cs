using RegionHR.LAS.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.LAS.Services;

/// <summary>
/// LAS auto-kedja: kopplar ihop anställningshändelser med LAS-ackumuleringen så att
/// HR slipper registrera visstidsperioder manuellt.
///
/// Tidigare krävdes att HR öppnade LAS-sidan och registrerade varje visstidsperiod
/// för hand. Den här tjänsten anropas av domänevent-hanterarna
/// (<c>EmploymentCreatedEvent</c> / <c>EmploymentEndedEvent</c>) och:
///
/// 1. Skapar eller ökar LAS-ackumuleringen när en visstidsanställning skapas.
/// 2. Låter domänen automatiskt notera konvertering till tillsvidare när ackumuleringen
///    passerar gränsen (SAVA 365 dagar / vikariat 730 dagar) — se
///    <see cref="LASAccumulation.Omberakna"/>.
/// 3. Bedömer och sätter företrädesrätt (§25 LAS) när anställningen avslutas.
///
/// Systemet är experten: formvalidering och gränsberäkning görs här/i domänen,
/// inte i UI:t. Tjänsten är idempotent per anställning — samma anställnings-id
/// registreras aldrig som två perioder.
///
/// Endast SAVA och vikariat ackumuleras. Det är de två formerna som §5a LAS
/// konverterar till tillsvidare och som <see cref="LASAccumulation"/> modellerar.
/// Säsongsanställning ger företrädesrätt men konverteras inte enligt §5a och saknar
/// stöd i den nuvarande ackumuleringsmodellen — den hoppas därför över (dokumenterad
/// begränsning, samma som den manuella registreringsvägen).
/// </summary>
public sealed class LASAutoChainService
{
    private readonly ILASRepository _repository;
    private readonly IEmploymentLookup _employmentLookup;

    public LASAutoChainService(ILASRepository repository, IEmploymentLookup employmentLookup)
    {
        _repository = repository;
        _employmentLookup = employmentLookup;
    }

    /// <summary>
    /// Anställningsformer som ackumuleras för LAS-konvertering enligt §5a.
    /// </summary>
    public static bool ArAckumulerandeVisstid(EmploymentType form) =>
        form is EmploymentType.SAVA or EmploymentType.Vikariat;

    /// <summary>
    /// Reagerar på att en anställning skapats: registrerar en LAS-period för visstid
    /// (SAVA/vikariat) och låter domänen notera konvertering om gränsen passeras.
    /// </summary>
    public async Task<LASAutoChainResult> RegistreraFranAnstallningAsync(
        EmploymentId employmentId, CancellationToken ct = default)
    {
        var anstallning = await _employmentLookup.GetEmploymentAsync(employmentId, ct);
        if (anstallning is null)
            return LASAutoChainResult.Ignorerad("Anställningen kunde inte hittas — ingen LAS-period skapades.");

        if (!ArAckumulerandeVisstid(anstallning.Anstallningsform))
            return LASAutoChainResult.Ignorerad(
                $"Anställningsformen {anstallning.Anstallningsform} ackumuleras inte för LAS-konvertering.");

        if (anstallning.Slut is not { } slutDatum)
            return LASAutoChainResult.Ignorerad(
                "Visstidsanställning saknar slutdatum — LAS-period kan inte beräknas.");

        var referens = employmentId.Value.ToString();
        var acc = await _repository.GetByEmployeeAsync(anstallning.AnstallId, ct);
        var arNy = acc is null;

        if (acc is null)
        {
            acc = LASAccumulation.Skapa(anstallning.AnstallId, anstallning.Anstallningsform);
        }
        else if (acc.Perioder.Any(p => p.AnstallningsId == referens))
        {
            // Idempotens: perioden är redan registrerad (t.ex. om händelsen levereras igen).
            return LASAutoChainResult.RedanRegistrerad(acc);
        }

        acc.LaggTillPeriod(anstallning.Start, slutDatum, referens);

        if (arNy)
            await _repository.AddAsync(acc, ct);
        else
            await _repository.UpdateAsync(acc, ct);

        return LASAutoChainResult.Registrerad(acc);
    }

    /// <summary>
    /// Reagerar på att en anställning avslutats: bedömer och sätter företrädesrätt (§25 LAS)
    /// för den anställdes LAS-ackumulering. Domänen avgör om kravet i dagar är uppfyllt.
    /// </summary>
    public async Task<LASAutoChainResult> AvslutaFranAnstallningAsync(
        EmployeeId anstallId, DateOnly slutDatum, CancellationToken ct = default)
    {
        var acc = await _repository.GetByEmployeeAsync(anstallId, ct);
        if (acc is null)
            return LASAutoChainResult.Ignorerad(
                "Ingen LAS-ackumulering finns för den anställde — inget att avsluta.");

        acc.SattForetradesratt(slutDatum);
        await _repository.UpdateAsync(acc, ct);
        return LASAutoChainResult.Avslutad(acc);
    }
}

/// <summary>Utfall av en auto-kedjeåtgärd.</summary>
public enum LASAutoChainStatus
{
    /// <summary>En LAS-period registrerades.</summary>
    Registrerad,
    /// <summary>Perioden var redan registrerad (idempotent no-op).</summary>
    RedanRegistrerad,
    /// <summary>Anställningen avslutades och företrädesrätt bedömdes.</summary>
    Avslutad,
    /// <summary>Ingen åtgärd (t.ex. tillsvidare, saknad anställning eller okänd form).</summary>
    Ignorerad
}

/// <summary>
/// Resultat från <see cref="LASAutoChainService"/>. Bär statusen, ett människoläsbart
/// meddelande för loggning och — när relevant — den påverkade ackumuleringen samt
/// om konvertering respektive företrädesrätt utlöstes.
/// </summary>
public sealed record LASAutoChainResult(
    LASAutoChainStatus Status,
    string Meddelande,
    LASAccumulation? Ackumulering,
    bool KonverteringNoterad,
    bool ForetradesrattNoterad)
{
    internal static LASAutoChainResult Registrerad(LASAccumulation acc)
    {
        var konverterad = acc.Status == LASStatus.KonverteradTillTillsvidare;
        var meddelande = konverterad
            ? $"LAS-period registrerad; visstiden passerade gränsen ({acc.AckumuleradeDagar} dagar) " +
              "→ konvertering till tillsvidareanställning noterad."
            : $"LAS-period registrerad ({acc.AckumuleradeDagar} ackumulerade dagar, status {acc.Status}).";
        return new LASAutoChainResult(LASAutoChainStatus.Registrerad, meddelande, acc, konverterad, acc.HarForetradesratt);
    }

    internal static LASAutoChainResult RedanRegistrerad(LASAccumulation acc) => new(
        LASAutoChainStatus.RedanRegistrerad,
        "LAS-perioden var redan registrerad för denna anställning — hoppar över (idempotent).",
        acc,
        acc.Status == LASStatus.KonverteradTillTillsvidare,
        acc.HarForetradesratt);

    internal static LASAutoChainResult Avslutad(LASAccumulation acc)
    {
        var meddelande = acc.HarForetradesratt
            ? $"Anställning avslutad; företrädesrätt noterad t.o.m. {acc.ForetradesrattUtgar:yyyy-MM-dd}."
            : "Anställning avslutad; företrädesrätt ej uppfylld (för få ackumulerade dagar).";
        return new LASAutoChainResult(LASAutoChainStatus.Avslutad, meddelande, acc,
            acc.Status == LASStatus.KonverteradTillTillsvidare, acc.HarForetradesratt);
    }

    internal static LASAutoChainResult Ignorerad(string meddelande) =>
        new(LASAutoChainStatus.Ignorerad, meddelande, null, false, false);
}
