using RegionHR.Competence.Domain;

namespace RegionHR.Competence.Services;

/// <summary>
/// Ett kravställt kompetenskrav ställt mot den anställdes faktiska nivå.
/// </summary>
public sealed record SkillGap(
    Guid SkillId,
    string SkillNamn,
    int KravdNiva,
    int NuvarandeNiva)
{
    /// <summary>Antal nivåsteg som fattas (0 om uppfyllt).</summary>
    public int GapPoang => Math.Max(0, KravdNiva - NuvarandeNiva);

    /// <summary>Sant om den anställde når eller överträffar kravnivån.</summary>
    public bool ArUppfylld => NuvarandeNiva >= KravdNiva;

    /// <summary>Sant om den anställde helt saknar registrerad nivå (0).</summary>
    public bool Saknas => NuvarandeNiva <= 0;
}

/// <summary>
/// Resultatet av en gap-analys för en anställd mot en positions kravprofil.
/// </summary>
public sealed record GapAnalys(
    Guid AnstallId,
    Guid? PositionId,
    string? PositionTitel,
    IReadOnlyList<SkillGap> Skills)
{
    /// <summary>Alla krav som inte är uppfyllda (sorteras störst gap först).</summary>
    public IReadOnlyList<SkillGap> Gap =>
        Skills.Where(s => !s.ArUppfylld)
              .OrderByDescending(s => s.GapPoang)
              .ThenBy(s => s.SkillNamn)
              .ToList();

    public int AntalKrav => Skills.Count;
    public int AntalUppfyllda => Skills.Count(s => s.ArUppfylld);
    public int AntalGap => AntalKrav - AntalUppfyllda;
    public bool HarGap => AntalGap > 0;

    /// <summary>Summan av alla nivåsteg som fattas — grov storleksuppskattning av utvecklingsbehovet.</summary>
    public int TotaltGapPoang => Skills.Sum(s => s.GapPoang);

    /// <summary>Andel av kraven som är uppfyllda, 0-100.</summary>
    public int TackningsgradProcent =>
        AntalKrav == 0 ? 100 : (int)Math.Round((double)AntalUppfyllda / AntalKrav * 100);
}

/// <summary>
/// Ren, tillståndslös motor som räknar ut kompetensgap: den anställdes
/// registrerade skills (EmployeeSkill) ställda mot positionens kravprofil
/// (PositionSkillRequirement). Ingen EF/DB-koppling — anropas med redan
/// inlästa listor så att den är enkel att enhetstesta och återanvända
/// från både gap-vyn och samtalsflödet.
/// </summary>
public sealed class CompetenceGapAnalyzer
{
    /// <summary>
    /// Analyserar en anställds kompetens mot en positions kravprofil.
    /// </summary>
    /// <param name="anstallId">Den anställde som analyseras.</param>
    /// <param name="positionId">Positionen (om känd) vars krav gäller.</param>
    /// <param name="positionTitel">Positionens titel för presentation.</param>
    /// <param name="krav">Kravprofilen (PositionSkillRequirement) för positionen.</param>
    /// <param name="anstalldsSkills">Den anställdes registrerade EmployeeSkill.</param>
    /// <param name="skillNamn">Uppslag SkillId → visningsnamn.</param>
    public GapAnalys Analysera(
        Guid anstallId,
        Guid? positionId,
        string? positionTitel,
        IEnumerable<PositionSkillRequirement> krav,
        IEnumerable<EmployeeSkill> anstalldsSkills,
        IReadOnlyDictionary<Guid, string> skillNamn)
    {
        ArgumentNullException.ThrowIfNull(krav);
        ArgumentNullException.ThrowIfNull(anstalldsSkills);
        ArgumentNullException.ThrowIfNull(skillNamn);

        // Den anställdes högsta nivå per skill (skyddar mot dubbletter).
        var nivaPerSkill = anstalldsSkills
            .Where(es => es.AnstallId == anstallId)
            .GroupBy(es => es.SkillId)
            .ToDictionary(g => g.Key, g => g.Max(es => es.Niva));

        var gaps = krav
            .Where(k => positionId is null || k.PositionId == positionId)
            .Select(k =>
            {
                var niva = nivaPerSkill.TryGetValue(k.SkillId, out var n) ? n : 0;
                var namn = skillNamn.TryGetValue(k.SkillId, out var s) ? s : "Okänd kompetens";
                return new SkillGap(k.SkillId, namn, k.MinNiva, niva);
            })
            .OrderByDescending(s => s.GapPoang)
            .ThenBy(s => s.SkillNamn)
            .ToList();

        return new GapAnalys(anstallId, positionId, positionTitel, gaps);
    }
}
