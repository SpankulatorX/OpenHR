using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.Scheduling.Domain;

/// <summary>
/// Schema/schemaplan. Kan vara grundschema (mall) eller periodschema.
/// </summary>
public sealed class Schedule : AggregateRoot<ScheduleId>
{
    public OrganizationId EnhetId { get; private set; }
    public string Namn { get; private set; } = string.Empty;
    public ScheduleType Typ { get; private set; }
    public DateRange Period { get; private set; } = null!;
    public int CykelLangdVeckor { get; private set; }   // T.ex. 4-veckors rullande
    public ScheduleStatus Status { get; private set; }

    private readonly List<ScheduledShift> _pass = [];
    public IReadOnlyList<ScheduledShift> Pass => _pass.AsReadOnly();

    private Schedule() { }

    public static Schedule SkapaGrundschema(
        OrganizationId enhetId, string namn, DateOnly start, int cykelVeckor)
    {
        return new Schedule
        {
            Id = ScheduleId.New(),
            EnhetId = enhetId,
            Namn = namn,
            Typ = ScheduleType.Grundschema,
            Period = DateRange.Infinite(start),
            CykelLangdVeckor = cykelVeckor,
            Status = ScheduleStatus.Utkast
        };
    }

    public static Schedule SkapaPeriodschema(
        OrganizationId enhetId, string namn, DateOnly start, DateOnly slut)
    {
        return new Schedule
        {
            Id = ScheduleId.New(),
            EnhetId = enhetId,
            Namn = namn,
            Typ = ScheduleType.Periodschema,
            Period = new DateRange(start, slut),
            Status = ScheduleStatus.Utkast
        };
    }

    public ScheduledShift LaggTillPass(
        EmployeeId anstallId, DateOnly datum, ShiftType passTyp,
        TimeOnly start, TimeOnly slut, TimeSpan rast)
    {
        var shift = new ScheduledShift
        {
            Id = Guid.NewGuid(),
            SchemaId = Id,
            AnstallId = anstallId,
            Datum = datum,
            PassTyp = passTyp,
            PlaneradStart = start,
            PlaneradSlut = slut,
            Rast = rast,
            Status = ShiftStatus.Planerad
        };
        _pass.Add(shift);
        return shift;
    }

    /// <summary>
    /// Ta bort ett planerat pass ur schemat. Returnerar true om passet fanns och togs bort.
    /// </summary>
    public bool TaBortPass(Guid passId)
    {
        var pass = _pass.FirstOrDefault(p => p.Id == passId);
        if (pass is null) return false;
        _pass.Remove(pass);
        return true;
    }

    public void Publicera()
    {
        if (Status != ScheduleStatus.Utkast)
            throw new InvalidOperationException("Kan bara publicera utkast");
        Status = ScheduleStatus.Publicerad;
    }
}

public enum ScheduleType
{
    Grundschema,    // Template/base schedule
    Periodschema,   // Period-specific schedule
    Operativt       // Operational day-to-day
}

public enum ScheduleStatus
{
    Utkast,
    Publicerad,
    Arkiverad
}
