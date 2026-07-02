using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;
using RegionHR.Travel.Domain;

namespace RegionHR.Travel.Services;

/// <summary>
/// Tjänst för hantering av resekrav, traktamenten, milersättning och utlägg.
/// </summary>
public sealed class TravelService
{
    private readonly IRepository<TravelClaim, Guid> _repository;

    public TravelService(IRepository<TravelClaim, Guid> repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Skapar ett nytt resekrav.
    /// </summary>
    public async Task<TravelClaim> SkapaResekravAsync(
        EmployeeId anstallId, string beskrivning, DateOnly datum, CancellationToken ct)
    {
        var claim = TravelClaim.Skapa(anstallId, beskrivning, datum);
        await _repository.AddAsync(claim, ct);
        return claim;
    }

    /// <summary>
    /// Lägger till traktamente (hela och halva dagar, Skatteverkets satser).
    /// </summary>
    public async Task<TravelClaim> LaggTillTraktamenteAsync(
        Guid claimId, int helaDagar, int halvaDagar, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.SattTraktamente(helaDagar, halvaDagar);
        await _repository.UpdateAsync(claim, ct);
        return claim;
    }

    /// <summary>
    /// Lägger till milersättning.
    /// </summary>
    public async Task<TravelClaim> LaggTillMilersattningAsync(
        Guid claimId, decimal mil, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.SattMilersattning(mil);
        await _repository.UpdateAsync(claim, ct);
        return claim;
    }

    /// <summary>
    /// Lägger till utlägg med valfritt kvitto.
    /// </summary>
    public async Task<TravelClaim> LaggTillUtlaggAsync(
        Guid claimId, string beskrivning, Money belopp, string? kvittoId, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.LaggTillUtlagg(beskrivning, belopp, kvittoId);
        await _repository.UpdateAsync(claim, ct);
        return claim;
    }

    /// <summary>
    /// Skickar in resekravet för attestering.
    /// </summary>
    public async Task SkickaInAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.SkickaIn();
        await _repository.UpdateAsync(claim, ct);
    }

    /// <summary>
    /// Attesterar (godkänner) ett resekrav med full behörighetskontroll.
    /// Kräver att attestanten inte är inlämnaren och att HR-behörighet finns
    /// för belopp över <see cref="TravelClaim.ATTEST_GRANS_KRAVER_HR"/>.
    /// </summary>
    public async Task AttesteraAsync(
        Guid claimId, EmployeeId attestantId, string attestantNamn, bool attestantArHR, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.Attestera(attestantId, attestantNamn, attestantArHR);
        await _repository.UpdateAsync(claim, ct);
    }

    /// <summary>
    /// Avvisar ett resekrav med angiven anledning. Attestanten får inte vara
    /// inlämnaren.
    /// </summary>
    public async Task AvvisaAsync(
        Guid claimId, EmployeeId attestantId, string attestantNamn, string anledning, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.Avvisa(attestantId, attestantNamn, anledning);
        await _repository.UpdateAsync(claim, ct);
    }

    /// <summary>
    /// Markerar ett godkänt resekrav som utbetalt (anropas efter att
    /// lönekörningen betalat ut kravet).
    /// </summary>
    public async Task MarkeraSomUtbetaldAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await _repository.GetByIdAsync(claimId, ct)
            ?? throw new InvalidOperationException($"Resekrav {claimId} hittades inte");

        claim.MarkeraSomUtbetald();
        await _repository.UpdateAsync(claim, ct);
    }

    /// <summary>
    /// Hämtar resekrav som väntar på attestering. Utesluter attestantens egna
    /// krav (självattest är inte tillåten).
    /// </summary>
    public async Task<IReadOnlyList<TravelClaim>> HamtaForAttestAsync(
        EmployeeId attestant, CancellationToken ct)
    {
        var alla = await _repository.GetAllAsync(ct);
        return alla
            .Where(c => c.Status == TravelClaimStatus.Inskickad && c.AnstallId != attestant)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Hämtar attesterade resekrav som är klara för utbetalning via lönekörning.
    /// Lönekörningen anropar detta, betalar ut, och anropar sedan
    /// <see cref="MarkeraSomUtbetaldAsync"/> per krav.
    /// </summary>
    public async Task<IReadOnlyList<TravelClaim>> HamtaKlaraForUtbetalningAsync(CancellationToken ct)
    {
        var alla = await _repository.GetAllAsync(ct);
        return alla
            .Where(c => c.ArKlarForUtbetalning)
            .ToList()
            .AsReadOnly();
    }
}
