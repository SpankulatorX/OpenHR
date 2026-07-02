using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Core.Domain;

public sealed class Employment : Entity<EmploymentId>
{
    /// <summary>LAS 6 § (SFS 1982:80): en provanställning får vara i högst sex månader.</summary>
    public const int MaxProvanstallningManader = 6;

    public EmployeeId AnstallId { get; private set; }
    public OrganizationId EnhetId { get; private set; }
    public EmploymentType Anstallningsform { get; private set; }
    public CollectiveAgreementType Kollektivavtal { get; private set; }
    public Money Manadslon { get; private set; }
    public Percentage Sysselsattningsgrad { get; private set; }
    public DateRange Giltighetsperiod { get; private set; } = null!;

    // BESTA/AID-koder för statistik
    public string? BESTAKod { get; private set; }
    public string? AIDKod { get; private set; }

    // Befattning
    public string? Befattningstitel { get; private set; }

    // Kollektivavtals-referens (DB-backed)
    public CollectiveAgreementId? AvtalsId { get; private set; }

    // LAS-relevant
    public bool ArTillsvidareanstallning => Anstallningsform == EmploymentType.Tillsvidare;
    public bool ArProvanstallning => Anstallningsform == EmploymentType.Provanstallning;
    public bool ArTidsbegransad => Anstallningsform is EmploymentType.Vikariat or EmploymentType.SAVA or EmploymentType.Sasongsanstallning;

    /// <summary>Anställningsformer som enligt lag/avtal måste ha ett slutdatum.</summary>
    public static bool KraverSlutdatum(EmploymentType form) =>
        form is EmploymentType.Vikariat or EmploymentType.Provanstallning
            or EmploymentType.SAVA or EmploymentType.Sasongsanstallning;

    public bool ArAktivPa(DateOnly datum) => Giltighetsperiod.IsActiveOn(datum);
    public bool ArAvslutad(DateOnly datum) => Giltighetsperiod.End is { } slut && slut < datum;

    private Employment() { } // EF Core

    internal static Employment Skapa(
        EmployeeId anstallId,
        OrganizationId enhet,
        EmploymentType anstallningsform,
        CollectiveAgreementType kollektivavtal,
        Money manadslon,
        Percentage sysselsattningsgrad,
        DateOnly startdatum,
        DateOnly? slutdatum,
        string? bestaKod,
        string? aidKod)
    {
        Validera(anstallningsform, manadslon, sysselsattningsgrad, startdatum, slutdatum);

        return new Employment
        {
            Id = EmploymentId.New(),
            AnstallId = anstallId,
            EnhetId = enhet,
            Anstallningsform = anstallningsform,
            Kollektivavtal = kollektivavtal,
            Manadslon = manadslon,
            Sysselsattningsgrad = sysselsattningsgrad,
            Giltighetsperiod = new DateRange(startdatum, slutdatum),
            BESTAKod = bestaKod,
            AIDKod = aidKod
        };
    }

    /// <summary>
    /// Validerar en anställning mot svensk arbetsrätt (LAS) innan den skapas.
    /// Systemet är experten: felaktiga kombinationer avvisas här, inte i UI:t.
    /// </summary>
    public static void Validera(
        EmploymentType anstallningsform,
        Money manadslon,
        Percentage sysselsattningsgrad,
        DateOnly startdatum,
        DateOnly? slutdatum)
    {
        if (manadslon.Amount < 0)
            throw new ArgumentException("Månadslön kan inte vara negativ.");
        if (anstallningsform != EmploymentType.Timavlonad && manadslon.Amount == 0)
            throw new ArgumentException("Månadslön måste anges för anställningen.");
        if (sysselsattningsgrad.Value <= 0)
            throw new ArgumentException("Sysselsättningsgrad måste vara större än 0 %.");

        if (slutdatum is { } slut && slut < startdatum)
            throw new ArgumentException($"Slutdatum ({slut}) kan inte vara före startdatum ({startdatum}).");

        if (anstallningsform == EmploymentType.Tillsvidare && slutdatum is not null)
            throw new ArgumentException("En tillsvidareanställning får inte ha något slutdatum.");

        if (KraverSlutdatum(anstallningsform) && slutdatum is null)
            throw new ArgumentException($"Anställningsformen {anstallningsform} är tidsbegränsad och kräver ett slutdatum.");

        if (anstallningsform == EmploymentType.Provanstallning && slutdatum is { } provSlut
            && provSlut > startdatum.AddMonths(MaxProvanstallningManader))
        {
            throw new ArgumentException(
                $"En provanställning får enligt LAS 6 § vara i högst {MaxProvanstallningManader} månader.");
        }
    }

    public void AndraLon(Money nyLon, string andradAv)
    {
        if (nyLon.Amount <= 0)
            throw new ArgumentException("Lön måste vara positiv");
        Manadslon = nyLon;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = andradAv;
    }

    public void SattBefattning(string befattningstitel, string? andradAv = null)
    {
        if (string.IsNullOrWhiteSpace(befattningstitel))
            throw new ArgumentException("Befattningstitel får inte vara tom.");
        Befattningstitel = befattningstitel;
        UpdatedAt = DateTime.UtcNow;
        if (andradAv is not null) UpdatedBy = andradAv;
    }

    public void AndraSysselsattningsgrad(Percentage nyGrad, string? andradAv = null)
    {
        if (nyGrad.Value <= 0)
            throw new ArgumentException("Sysselsättningsgrad måste vara större än 0 %.");
        Sysselsattningsgrad = nyGrad;
        UpdatedAt = DateTime.UtcNow;
        if (andradAv is not null) UpdatedBy = andradAv;
    }

    public void AvslutaAnstallning(DateOnly slutdatum, string? andradAv = null)
    {
        if (slutdatum < Giltighetsperiod.Start)
            throw new ArgumentException(
                $"Slutdatum ({slutdatum}) kan inte vara före anställningens startdatum ({Giltighetsperiod.Start}).");
        Giltighetsperiod = new DateRange(Giltighetsperiod.Start, slutdatum);
        UpdatedAt = DateTime.UtcNow;
        if (andradAv is not null) UpdatedBy = andradAv;
    }

    public void SattKollektivavtal(CollectiveAgreementId avtalsId)
    {
        AvtalsId = avtalsId;
        UpdatedAt = DateTime.UtcNow;
    }

    public Money BeraknaDaglon() => Manadslon / 21m; // Genomsnittliga arbetsdagar/månad

    /// <summary>Beräkna timlön baserat på heltidstimmar per vecka (vanligen 38.25 för AB)</summary>
    public Money BeraknaTimlon(decimal veckoarbetstid = 38.25m)
    {
        var timmarPerManad = veckoarbetstid * 52m / 12m;
        return Manadslon / timmarPerManad;
    }
}
