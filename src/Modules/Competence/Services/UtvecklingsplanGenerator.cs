using System.Text;
using RegionHR.Competence.Domain;

namespace RegionHR.Competence.Services;

/// <summary>
/// Genererar en konkret utvecklingsplan (DevelopmentPlan med milstolpar i steg)
/// ur en kompetensgap-analys. Detta är "motorn" som stänger länken
/// samtal → kompetensgap → utvecklingsprocess: varje ouppfyllt krav blir
/// en milstolpe med tydlig från/till-nivå och ett realistiskt måldatum
/// baserat på hur många nivåsteg som fattas.
///
/// Ren domänlogik utan EF — anropas med en <see cref="GapAnalys"/> och
/// returnerar ett osparat aggregat som anropande sida persisterar via DbContext.
/// </summary>
public sealed class UtvecklingsplanGenerator
{
    /// <summary>
    /// Antal månader som normalt avsätts per nivåsteg en kompetens ska höjas.
    /// Ett steg (t.ex. 3→4) ≈ ett kvartals riktad utveckling.
    /// </summary>
    public const int ManaderPerNivasteg = 3;

    /// <summary>
    /// Bygger en utvecklingsplan ur gap-analysen. Skapar en milstolpe per
    /// ouppfyllt krav, sorterade störst gap först, och kopplar planen till
    /// medarbetarsamtalet. Returnerar null om det inte finns några gap
    /// (inget att utveckla → ingen plan behövs).
    /// </summary>
    /// <param name="analys">Gap-analysen som planen ska baseras på.</param>
    /// <param name="samtalId">PerformanceReview.Id som planen kopplas till.</param>
    /// <param name="startDatum">Planens startdatum (normalt idag).</param>
    /// <param name="malRoll">
    /// Målroll för planen. Default = positionens titel, annars "Nuvarande roll".
    /// </param>
    public DevelopmentPlan? GenereraFranGap(
        GapAnalys analys,
        Guid samtalId,
        DateOnly startDatum,
        string? malRoll = null)
    {
        ArgumentNullException.ThrowIfNull(analys);
        if (samtalId == Guid.Empty)
            throw new ArgumentException("SamtalId får inte vara tomt", nameof(samtalId));

        var gap = analys.Gap;
        if (gap.Count == 0)
            return null;

        var roll = !string.IsNullOrWhiteSpace(malRoll)
            ? malRoll!
            : analys.PositionTitel ?? "Nuvarande roll";

        // Planens måldatum = start + tid för det största gapet.
        var storstaGap = gap.Max(g => g.GapPoang);
        var planMalDatum = startDatum.AddMonths(storstaGap * ManaderPerNivasteg);

        var plan = DevelopmentPlan.Skapa(analys.AnstallId, roll, startDatum, planMalDatum);
        plan.KopplaTillSamtal(samtalId);

        foreach (var g in gap)
        {
            var milDatum = startDatum.AddMonths(g.GapPoang * ManaderPerNivasteg);
            var beskrivning = g.Saknas
                ? $"Bygg upp \"{g.SkillNamn}\" till nivå {g.KravdNiva} (saknas idag)"
                : $"Höj \"{g.SkillNamn}\" från nivå {g.NuvarandeNiva} till {g.KravdNiva}";

            plan.LaggTillMilstolpe(
                beskrivning,
                typ: "Skill",
                malDatum: milDatum,
                skillId: g.SkillId,
                franNiva: g.NuvarandeNiva,
                malNiva: g.KravdNiva);
        }

        return plan;
    }

    /// <summary>
    /// Kortfattad textsammanfattning av utvecklingsmålen, lämplig att spara
    /// i PerformanceReview.Malsattning så samtalet och planen berättar samma sak.
    /// </summary>
    public string SammanfattaMalsattning(GapAnalys analys)
    {
        ArgumentNullException.ThrowIfNull(analys);

        var gap = analys.Gap;
        if (gap.Count == 0)
            return "Kravprofilen är helt uppfylld — inga kompetensgap att åtgärda.";

        var sb = new StringBuilder();
        sb.Append(gap.Count == 1 ? "1 kompetensgap" : $"{gap.Count} kompetensgap");
        if (!string.IsNullOrWhiteSpace(analys.PositionTitel))
            sb.Append($" mot kravprofilen för {analys.PositionTitel}");
        sb.Append(": ");
        sb.Append(string.Join("; ", gap.Select(g =>
            g.Saknas
                ? $"{g.SkillNamn} (bygg upp till nivå {g.KravdNiva})"
                : $"{g.SkillNamn} (nivå {g.NuvarandeNiva}→{g.KravdNiva})")));
        sb.Append('.');
        return sb.ToString();
    }
}
