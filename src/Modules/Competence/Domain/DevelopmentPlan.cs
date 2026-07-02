using RegionHR.SharedKernel.Domain;

namespace RegionHR.Competence.Domain;

public enum DevelopmentPlanStatus
{
    Draft,
    Active,
    Completed
}

public enum MilestoneStatus
{
    Pending,
    InProgress,
    Completed
}

/// <summary>
/// Utvecklingsplan för en anställd med mål-roll och milstolpar.
/// </summary>
public class DevelopmentPlan
{
    public DevelopmentPlanId Id { get; private set; }
    public Guid AnstallId { get; private set; }
    public string MalRoll { get; private set; } = default!;
    public DevelopmentPlanStatus Status { get; private set; }
    public DateOnly StartDatum { get; private set; }
    public DateOnly? MalDatum { get; private set; }

    /// <summary>
    /// Om planen genererades ur ett medarbetarsamtal pekar detta på
    /// PerformanceReview.Id — spårbar länk samtal → kompetensgap → utvecklingsplan.
    /// Null för planer som skapats fristående (t.ex. karriärplanering).
    /// </summary>
    public Guid? KopplatSamtalId { get; private set; }

    private readonly List<DevelopmentMilestone> _milstolpar = [];
    public IReadOnlyList<DevelopmentMilestone> Milstolpar => _milstolpar.AsReadOnly();

    private DevelopmentPlan() { }

    public static DevelopmentPlan Skapa(Guid anstallId, string malRoll, DateOnly startDatum, DateOnly? malDatum = null)
    {
        return new DevelopmentPlan
        {
            Id = DevelopmentPlanId.New(),
            AnstallId = anstallId,
            MalRoll = malRoll,
            Status = DevelopmentPlanStatus.Draft,
            StartDatum = startDatum,
            MalDatum = malDatum
        };
    }

    public void Aktivera()
    {
        if (Status != DevelopmentPlanStatus.Draft)
            throw new InvalidOperationException("Kan bara aktivera utkast");
        Status = DevelopmentPlanStatus.Active;
    }

    public void Slutfor()
    {
        if (Status != DevelopmentPlanStatus.Active)
            throw new InvalidOperationException("Kan bara slutföra aktiva planer");
        Status = DevelopmentPlanStatus.Completed;
    }

    /// <summary>
    /// Kopplar planen till ett medarbetarsamtal (PerformanceReview.Id).
    /// Idempotent — sätter länken oavsett tidigare värde.
    /// </summary>
    public void KopplaTillSamtal(Guid samtalId)
    {
        if (samtalId == Guid.Empty)
            throw new ArgumentException("SamtalId får inte vara tomt", nameof(samtalId));
        KopplatSamtalId = samtalId;
    }

    public DevelopmentMilestone LaggTillMilstolpe(
        string beskrivning, string typ, DateOnly? malDatum = null,
        Guid? skillId = null, int? franNiva = null, int? malNiva = null)
    {
        var milstolpe = DevelopmentMilestone.Skapa(Id, beskrivning, typ, malDatum, skillId, franNiva, malNiva);
        _milstolpar.Add(milstolpe);
        return milstolpe;
    }
}

/// <summary>
/// En milstolpe i en utvecklingsplan.
/// </summary>
public class DevelopmentMilestone
{
    public Guid Id { get; private set; }
    public DevelopmentPlanId DevelopmentPlanId { get; private set; }
    public string Beskrivning { get; private set; } = default!;

    /// <summary>Skill, Certifiering, Kurs, Erfarenhet</summary>
    public string Typ { get; private set; } = default!;

    public DateOnly? MalDatum { get; private set; }
    public MilestoneStatus Status { get; private set; }

    /// <summary>Om milstolpen härrör ur en kompetensgap: skill som ska höjas.</summary>
    public Guid? SkillId { get; private set; }

    /// <summary>Nuvarande nivå (0 = saknas) vid milstolpens skapande.</summary>
    public int? FranNiva { get; private set; }

    /// <summary>Målnivå som milstolpen ska ta skillen till.</summary>
    public int? MalNiva { get; private set; }

    private DevelopmentMilestone() { }

    internal static DevelopmentMilestone Skapa(
        DevelopmentPlanId planId, string beskrivning, string typ, DateOnly? malDatum,
        Guid? skillId = null, int? franNiva = null, int? malNiva = null)
    {
        return new DevelopmentMilestone
        {
            Id = Guid.NewGuid(),
            DevelopmentPlanId = planId,
            Beskrivning = beskrivning,
            Typ = typ,
            MalDatum = malDatum,
            Status = MilestoneStatus.Pending,
            SkillId = skillId,
            FranNiva = franNiva,
            MalNiva = malNiva
        };
    }

    public void MarkeraPaborjad()
    {
        if (Status != MilestoneStatus.Pending)
            throw new InvalidOperationException("Kan bara påbörja väntande milstolpar");
        Status = MilestoneStatus.InProgress;
    }

    public void MarkeraKlar()
    {
        if (Status == MilestoneStatus.Completed)
            throw new InvalidOperationException("Milstolpe redan klar");
        Status = MilestoneStatus.Completed;
    }
}
