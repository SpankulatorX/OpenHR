namespace RegionHR.Leave.Services;

/// <summary>
/// Abstraktion för ett schemalagt pass som kan påverkas av godkänd frånvaro.
///
/// Scheduling-modulens ScheduledShift adapteras till detta interface i webblagret,
/// så att den affärskritiska logiken (vilka pass som ska markeras som frånvaro när en
/// ledighet godkänns) kan enhetstestas i Leave-modulen utan ett beroende till
/// Scheduling-modulen.
/// </summary>
public interface IPaverkbartPass
{
    /// <summary>Passets datum.</summary>
    DateOnly Datum { get; }

    /// <summary>
    /// True om passet fortfarande kan påverkas — dvs. är planerat och varken redan
    /// avbokat, bytt eller avslutat. Ett pass som inte kan påverkas lämnas orört.
    /// </summary>
    bool KanPaverkas { get; }

    /// <summary>Markerar passet som frånvaro (avbokas i schemat).</summary>
    void MarkeraSomFranvaro();
}
