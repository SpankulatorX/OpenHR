using RegionHR.SharedKernel.Domain;

namespace RegionHR.Scheduling.Domain;

/// <summary>
/// Flexinställningar per anställd. Definierar om flextid tillämpas och vilka
/// gränser (tak/golv) saldot får röra sig inom. Sätts av chef eller HR;
/// medarbetaren kan se men inte ändra sina egna gränser.
/// </summary>
public sealed class FlexInstallning
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EmployeeId AnstallId { get; set; }

    /// <summary>Om flextid tillämpas för den anställde. Är den av påverkar stämplingar inte flexsaldot.</summary>
    public bool FlexAktiverad { get; set; } = true;

    /// <summary>Övre gräns (tak) för flexsaldot i timmar. 0 = ingen övre gräns.</summary>
    public decimal MaxPlusTimmar { get; set; } = 50m;

    /// <summary>Undre gräns (golv) för flexsaldot i timmar. Negativt värde (t.ex. -10).</summary>
    public decimal MaxMinusTimmar { get; set; } = -10m;

    /// <summary>Maximal flexförändring som får tillgodoräknas per dag i timmar. 0 = ingen dagsgräns.</summary>
    public decimal DagligFlexgransTimmar { get; set; } = 3m;

    /// <summary>Anställd (chef/HR) som senast ändrade inställningen.</summary>
    public Guid? SenastAndradAv { get; set; }

    public DateTime UppdateradVid { get; set; } = DateTime.UtcNow;

    /// <summary>Standardinställning som används när ingen sparad inställning finns.</summary>
    public static FlexInstallning Standard(EmployeeId anstallId) => new() { AnstallId = anstallId };
}
