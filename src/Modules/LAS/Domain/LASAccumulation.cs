using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.LAS.Domain;

/// <summary>
/// LAS-ackumulering per anställd.
///
/// Regler:
/// - SAVA: mer än 12 månader i en 5-årsperiod (sedan 2022-10-01) → konvertering
/// - Vikariat: mer än 2 år i en 5-årsperiod → konvertering
/// - SAVA-tid och vikariatstid ackumuleras SEPARAT mot sina respektive gränser —
///   varje period bär sin egen anställningsform och räknas aldrig mot fel gräns
/// - 3-i-månad-regeln: 3+ SAVA-avtal i samma kalendermånad → mellanliggande perioder räknas
/// - Företrädesrätt: 9 månader efter anställningens slut
/// </summary>
public sealed class LASAccumulation : AggregateRoot<Guid>
{
    // Konstanter
    public const int SAVA_MAX_DAGAR_5AR = 365;      // ~12 månader
    public const int VIKARIAT_MAX_DAGAR_5AR = 730;   // 2 år
    public const int REFERENSFONSTER_AR = 5;
    public const int FORETRADESRATT_MANADER = 9;

    // Företrädesrätt: tröskelvärden i en 3-årsperiod
    // SAVA: 9 månader (~274 dagar) i 3 år
    // Tillsvidare/vikariat: 12 månader (~365 dagar) i 3 år
    public const int FORETRADESRATT_SAVA_MANADER = 9;
    public const int FORETRADESRATT_SAVA_DAGAR = 274;        // ~9 månader
    public const int FORETRADESRATT_VIKARIAT_MANADER = 12;
    public const int FORETRADESRATT_VIKARIAT_DAGAR = 365;     // ~12 månader
    public const int FORETRADESRATT_REFERENSFONSTER_AR = 3;

    // Tröskelvärden för alarm
    public const int SAVA_ALARM_10_MANADER = 305;
    public const int SAVA_ALARM_11_MANADER = 335;
    public const int SAVA_ALARM_12_MANADER = 365;

    public EmployeeId AnstallId { get; private set; }

    /// <summary>
    /// Ursprunglig/huvudsaklig anställningsform (den form ackumuleringen skapades med).
    /// Själva ackumuleringen räknas per periodens form — se
    /// <see cref="AckumuleradeSavaDagar"/> och <see cref="AckumuleradeVikariatDagar"/>.
    /// </summary>
    public EmploymentType Anstallningsform { get; private set; }

    /// <summary>Totalt ackumulerade dagar (SAVA + vikariat) — används för turordning.</summary>
    public int AckumuleradeDagar { get; private set; }

    /// <summary>SAVA-dagar inom referensfönstret, räknas mot gränsen 365 dagar.</summary>
    public int AckumuleradeSavaDagar { get; private set; }

    /// <summary>Vikariatsdagar inom referensfönstret, räknas mot gränsen 730 dagar.</summary>
    public int AckumuleradeVikariatDagar { get; private set; }
    public DateOnly ReferensfonsterStart { get; private set; }
    public DateOnly ReferensfonsterSlut { get; private set; }
    public LASStatus Status { get; private set; }
    public DateOnly? KonverteringsDatum { get; private set; }
    public bool HarForetradesratt { get; private set; }
    public DateOnly? ForetradesrattUtgar { get; private set; }

    private readonly List<LASEvent> _handelser = [];
    public IReadOnlyList<LASEvent> Handelser => _handelser.AsReadOnly();

    private readonly List<LASPeriod> _perioder = [];
    public IReadOnlyList<LASPeriod> Perioder => _perioder.AsReadOnly();

    private LASAccumulation() { }

    public static LASAccumulation Skapa(EmployeeId anstallId, EmploymentType anstallningsform)
    {
        if (anstallningsform is not (EmploymentType.SAVA or EmploymentType.Vikariat))
            throw new ArgumentException("LAS-ackumulering gäller bara SAVA och vikariat");

        var now = DateOnly.FromDateTime(DateTime.Today);
        return new LASAccumulation
        {
            Id = Guid.NewGuid(),
            AnstallId = anstallId,
            Anstallningsform = anstallningsform,
            AckumuleradeDagar = 0,
            ReferensfonsterStart = now.AddYears(-REFERENSFONSTER_AR),
            ReferensfonsterSlut = now,
            Status = LASStatus.UnderGrans
        };
    }

    /// <summary>
    /// Registrera en anställningsperiod för LAS-ackumulering.
    /// Periodens form styr vilken gräns den räknas mot (SAVA 365 / vikariat 730).
    /// Utelämnas <paramref name="form"/> används ackumuleringens ursprungsform.
    /// </summary>
    public void LaggTillPeriod(DateOnly startDatum, DateOnly slutDatum, string? anstallningsId = null, EmploymentType? form = null)
    {
        var periodForm = form ?? Anstallningsform;
        if (periodForm is not (EmploymentType.SAVA or EmploymentType.Vikariat))
            throw new ArgumentException("LAS-perioder kan bara registreras för SAVA och vikariat");

        var period = new LASPeriod
        {
            StartDatum = startDatum,
            SlutDatum = slutDatum,
            AntalDagar = slutDatum.DayNumber - startDatum.DayNumber + 1,
            AnstallningsId = anstallningsId,
            Anstallningsform = periodForm
        };
        _perioder.Add(period);

        // Omberäkna ackumulering inom referensfönstret
        Omberakna(DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>Omberäkna ackumulerade dagar baserat på perioder inom referensfönstret.</summary>
    public void Omberakna(DateOnly referensDatum)
    {
        ReferensfonsterStart = referensDatum.AddYears(-REFERENSFONSTER_AR);
        ReferensfonsterSlut = referensDatum;

        // SAVA- och vikariatstid ackumuleras SEPARAT mot sina respektive gränser
        // (365 resp. 730 dagar i 5-årsfönstret) — de blandas aldrig mot fel gräns.
        AckumuleradeSavaDagar = DagarInomFonster(EmploymentType.SAVA, ReferensfonsterStart, ReferensfonsterSlut);
        AckumuleradeVikariatDagar = DagarInomFonster(EmploymentType.Vikariat, ReferensfonsterStart, ReferensfonsterSlut);

        // Kontrollera 3-i-månad-regeln (gäller SAVA-avtal)
        KontrolleraTreIManad();

        AckumuleradeDagar = AckumuleradeSavaDagar + AckumuleradeVikariatDagar;

        // Uppdatera status
        UppdateraStatus();
    }

    /// <summary>Summera dagar för en anställningsform inom ett datumfönster (klipper mot fönstrets kanter).</summary>
    private int DagarInomFonster(EmploymentType form, DateOnly fonsterStart, DateOnly fonsterSlut) =>
        _perioder
            .Where(p => p.Anstallningsform == form && p.SlutDatum >= fonsterStart && p.StartDatum <= fonsterSlut)
            .Sum(p =>
            {
                var effectiveStart = p.StartDatum < fonsterStart ? fonsterStart : p.StartDatum;
                var effectiveEnd = p.SlutDatum > fonsterSlut ? fonsterSlut : p.SlutDatum;
                return effectiveEnd.DayNumber - effectiveStart.DayNumber + 1;
            });

    private void KontrolleraTreIManad()
    {
        // Gruppera SAVA-perioder per kalendermånad (regeln gäller SAVA-avtal)
        var perioderPerManad = _perioder
            .Where(p => p.Anstallningsform == EmploymentType.SAVA && p.SlutDatum >= ReferensfonsterStart)
            .GroupBy(p => new { p.StartDatum.Year, p.StartDatum.Month });

        foreach (var group in perioderPerManad)
        {
            if (group.Count() >= 3)
            {
                // Mellanliggande perioder ska räknas
                var sortedPeriods = group.OrderBy(p => p.StartDatum).ToList();
                for (int i = 0; i < sortedPeriods.Count - 1; i++)
                {
                    var gap = sortedPeriods[i + 1].StartDatum.DayNumber - sortedPeriods[i].SlutDatum.DayNumber - 1;
                    if (gap > 0)
                    {
                        AckumuleradeSavaDagar += gap;
                        var beskrivning =
                            $"3-i-månadsregeln: {gap} mellanliggande dagar räknas ({sortedPeriods[i].SlutDatum} - {sortedPeriods[i + 1].StartDatum})";
                        // Omberakna körs återkommande (dagligt jobb) — logga inte samma händelse igen.
                        if (!_handelser.Any(h => h.Typ == LASEventTyp.TreIManadRegelTillampad && h.Beskrivning == beskrivning))
                            _handelser.Add(LASEvent.Skapa(LASEventTyp.TreIManadRegelTillampad, beskrivning));
                    }
                }
            }
        }
    }

    private void UppdateraStatus()
    {
        var previousStatus = Status;

        // §5a LAS säger "mer än" — konvertering sker först när gränsen PASSERAS (strikt >).
        var savaOverGrans = AckumuleradeSavaDagar > SAVA_MAX_DAGAR_5AR;
        var vikariatOverGrans = AckumuleradeVikariatDagar > VIKARIAT_MAX_DAGAR_5AR;

        if (savaOverGrans || vikariatOverGrans)
        {
            Status = LASStatus.KonverteradTillTillsvidare;
            KonverteringsDatum ??= DateOnly.FromDateTime(DateTime.Today);
            if (previousStatus != LASStatus.KonverteradTillTillsvidare)
            {
                var (form, dagar, grans) = savaOverGrans
                    ? (EmploymentType.SAVA, AckumuleradeSavaDagar, SAVA_MAX_DAGAR_5AR)
                    : (EmploymentType.Vikariat, AckumuleradeVikariatDagar, VIKARIAT_MAX_DAGAR_5AR);
                _handelser.Add(LASEvent.Skapa(
                    LASEventTyp.Konvertering,
                    $"Konverterad till tillsvidareanställning efter {dagar} dagar ({form}, gräns {grans})"));
                RaiseDomainEvent(new LASConversionTriggeredEvent(AnstallId, form, dagar));
            }
        }
        else if (previousStatus == LASStatus.KonverteradTillTillsvidare)
        {
            // Konvertering är enkelriktad: att referensfönstret glider vidare vid den
            // återkommande omberäkningen (dagligt jobb) ska inte "avkonvertera", och en
            // manuell konvertering ska inte wipas. Endast HR-korrigering av perioder
            // återställer — se AterstallKonverteringOmUnderGrans().
        }
        else if (AckumuleradeSavaDagar >= SAVA_ALARM_11_MANADER)
        {
            Status = LASStatus.KritiskNara;
            if (previousStatus != LASStatus.KritiskNara)
                _handelser.Add(LASEvent.Skapa(LASEventTyp.Alarm, $"KRITISKT: {AckumuleradeSavaDagar} dagar av {SAVA_MAX_DAGAR_5AR}"));
        }
        else if (AckumuleradeSavaDagar >= SAVA_ALARM_10_MANADER)
        {
            Status = LASStatus.NaraGrans;
            if (previousStatus != LASStatus.NaraGrans)
                _handelser.Add(LASEvent.Skapa(LASEventTyp.Alarm, $"Varning: {AckumuleradeSavaDagar} dagar av {SAVA_MAX_DAGAR_5AR}"));
        }
        else
        {
            Status = LASStatus.UnderGrans;
        }
    }

    /// <summary>
    /// Sätt företrädesrätt efter anställningens slut.
    /// Per 25§ LAS ("mer än" — kravet ska PASSERAS, strikt >):
    /// - SAVA: företrädesrätt efter mer än 9 månader (274 dagar) SAVA-tid i en 3-årsperiod
    /// - Vikariat: företrädesrätt efter mer än 12 månader (365 dagar) vikariatstid i en 3-årsperiod
    /// Kravet bedöms per anställningsform — SAVA-tid och vikariatstid blandas inte.
    /// Företrädesrätten gäller i 9 månader från anställningens slut.
    /// </summary>
    public void SattForetradesratt(DateOnly anstallningSlut)
    {
        // Beräkna dagar i 3-årsperioden — per anställningsform, mot respektive lagkrav
        var treArsStart = anstallningSlut.AddYears(-FORETRADESRATT_REFERENSFONSTER_AR);
        var savaDagar = DagarInomFonster(EmploymentType.SAVA, treArsStart, anstallningSlut);
        var vikariatDagar = DagarInomFonster(EmploymentType.Vikariat, treArsStart, anstallningSlut);

        var (uppfylld, dagar, kravDagar) =
            savaDagar > FORETRADESRATT_SAVA_DAGAR
                ? (true, savaDagar, FORETRADESRATT_SAVA_DAGAR)
                : vikariatDagar > FORETRADESRATT_VIKARIAT_DAGAR
                    ? (true, vikariatDagar, FORETRADESRATT_VIKARIAT_DAGAR)
                    : (false, 0, 0);

        if (uppfylld)
        {
            HarForetradesratt = true;
            ForetradesrattUtgar = anstallningSlut.AddMonths(FORETRADESRATT_MANADER);
            _handelser.Add(LASEvent.Skapa(
                LASEventTyp.ForetradesrattBeviljad,
                $"Företrädesrätt till {ForetradesrattUtgar.Value} ({dagar} dagar i 3-årsperiod, krav: mer än {kravDagar} dagar)"));
        }
    }

    /// <summary>
    /// Ta bort en felregistrerad period (HR-korrigering). Matchar på start- och slutdatum.
    /// Omberäknar ackumuleringen och nollställer konverteringsdatum om dagarna faller under gränsen.
    /// </summary>
    public bool TaBortPeriod(DateOnly startDatum, DateOnly slutDatum)
    {
        var period = _perioder.FirstOrDefault(p => p.StartDatum == startDatum && p.SlutDatum == slutDatum);
        if (period is null)
            return false;

        _perioder.Remove(period);
        _handelser.Add(LASEvent.Skapa(
            LASEventTyp.PeriodRegistrerad,
            $"Period borttagen (HR-korrigering): {startDatum:yyyy-MM-dd} – {slutDatum:yyyy-MM-dd}"));

        Omberakna(DateOnly.FromDateTime(DateTime.Today));
        AterstallKonverteringOmUnderGrans();
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Ändra datum för en registrerad period (HR-korrigering). Matchar den gamla perioden
    /// på start- och slutdatum. Omberäknar ackumuleringen.
    /// </summary>
    public bool AndraPeriod(DateOnly gammalStart, DateOnly gammalSlut, DateOnly nyStart, DateOnly nySlut)
    {
        if (nySlut < nyStart)
            throw new ArgumentException("Slutdatum kan inte vara före startdatum.");

        var period = _perioder.FirstOrDefault(p => p.StartDatum == gammalStart && p.SlutDatum == gammalSlut);
        if (period is null)
            return false;

        period.StartDatum = nyStart;
        period.SlutDatum = nySlut;
        period.AntalDagar = nySlut.DayNumber - nyStart.DayNumber + 1;
        _handelser.Add(LASEvent.Skapa(
            LASEventTyp.PeriodRegistrerad,
            $"Period korrigerad (HR): {gammalStart:yyyy-MM-dd}–{gammalSlut:yyyy-MM-dd} → {nyStart:yyyy-MM-dd}–{nySlut:yyyy-MM-dd}"));

        Omberakna(DateOnly.FromDateTime(DateTime.Today));
        AterstallKonverteringOmUnderGrans();
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Manuell konvertering av visstidsanställning till tillsvidareanställning (formbyte, HR-beslut).
    /// Registrerar beslutsdatum och vem som fattade beslutet. Idempotent — redan konverterad = ingen ändring.
    /// </summary>
    public bool KonverteraTillTillsvidare(DateOnly konverteringsDatum, string beslutadAv)
    {
        if (string.IsNullOrWhiteSpace(beslutadAv))
            throw new ArgumentException("Beslutsfattare måste anges vid konvertering.");
        if (Status == LASStatus.KonverteradTillTillsvidare)
            return false;

        Status = LASStatus.KonverteradTillTillsvidare;
        KonverteringsDatum = konverteringsDatum;
        _handelser.Add(LASEvent.Skapa(
            LASEventTyp.Konvertering,
            $"Manuell konvertering till tillsvidareanställning {konverteringsDatum:yyyy-MM-dd} " +
            $"(beslut av {beslutadAv}) efter {AckumuleradeDagar} dagar"));
        RaiseDomainEvent(new LASConversionTriggeredEvent(AnstallId, Anstallningsform, AckumuleradeDagar));
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private void AterstallKonverteringOmUnderGrans()
    {
        if (Status != LASStatus.KonverteradTillTillsvidare)
        {
            KonverteringsDatum = null;
            return;
        }

        // HR-korrigering: om dagarna efter korrigeringen inte längre passerar någon
        // gräns ska konverteringen återställas och statusnivån härledas om.
        if (AckumuleradeSavaDagar <= SAVA_MAX_DAGAR_5AR && AckumuleradeVikariatDagar <= VIKARIAT_MAX_DAGAR_5AR)
        {
            Status = LASStatus.UnderGrans;
            KonverteringsDatum = null;
            UppdateraStatus();
        }
    }

    /// <summary>Generera turordningslista per driftsenhet.</summary>
    public int BeraknaLASDagar() => AckumuleradeDagar;
}

public sealed class LASPeriod
{
    public DateOnly StartDatum { get; set; }
    public DateOnly SlutDatum { get; set; }
    public int AntalDagar { get; set; }
    public string? AnstallningsId { get; set; }

    /// <summary>Periodens anställningsform — avgör vilken LAS-gräns dagarna räknas mot.</summary>
    public EmploymentType Anstallningsform { get; set; }
}

public sealed class LASEvent
{
    public Guid Id { get; private set; }
    public LASEventTyp Typ { get; private set; }
    public DateTime Tidpunkt { get; private set; }
    public string Beskrivning { get; private set; } = string.Empty;

    public static LASEvent Skapa(LASEventTyp typ, string beskrivning) => new()
    {
        Id = Guid.NewGuid(),
        Typ = typ,
        Tidpunkt = DateTime.UtcNow,
        Beskrivning = beskrivning
    };
}

public enum LASEventTyp
{
    PeriodRegistrerad,
    Alarm,
    Konvertering,
    ForetradesrattBeviljad,
    ForetradesrattUtgangen,
    TreIManadRegelTillampad
}

public enum LASStatus
{
    UnderGrans,
    NaraGrans,
    KritiskNara,
    KonverteradTillTillsvidare
}

public sealed record LASConversionTriggeredEvent(
    EmployeeId AnstallId,
    EmploymentType Anstallningsform,
    int AckumuleradeDagar) : DomainEvent;
