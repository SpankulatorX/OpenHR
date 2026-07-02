namespace RegionHR.Agreements.Domain;

/// <summary>
/// Typ av lokal avtalsavvikelse / lokal förmån som en organisationsenhet kan ha
/// utöver (eller i stället för) det centrala kollektivavtalet.
///
/// Lokala avvikelser är ett OVANPÅLIGGANDE lager: de centrala AB/HÖK-satserna
/// (<see cref="ABOTillaggSatser"/> m.fl.) är oförändrade — den lokala avvikelsen
/// justerar det effektiva värdet för just den enheten under en giltighetsperiod.
/// </summary>
public enum LokalAvvikelseTyp
{
    /// <summary>Lokalt OB-påslag ovanpå centralt O-tillägg (AB § 21).</summary>
    ObPaslag,

    /// <summary>Lokal förmån (t.ex. utökad friskvård, extra semesterväxling, lokalt friskvårdsbidrag).</summary>
    Forman,

    /// <summary>Lokalt lönetillägg (t.ex. rekryteringstillägg, storstadstillägg, kompetenstillägg).</summary>
    Tillagg,

    /// <summary>Lokalt övertidspåslag ovanpå centrala övertidsregler (AB § 20).</summary>
    OvertidPaslag,

    /// <summary>Annan lokal avvikelse som inte passar övriga kategorier.</summary>
    Annat
}

/// <summary>
/// Hur <see cref="LokalAvtalsAvvikelse.Varde"/> ska tillämpas på ett centralt basvärde.
/// </summary>
public enum LokalBerakningsTyp
{
    /// <summary>Fast belopp som LÄGGS TILL det centrala basvärdet (bas + värde).</summary>
    FastBelopp,

    /// <summary>Procentuellt påslag på det centrala basvärdet (bas × (1 + värde/100)).</summary>
    ProcentPaslag,

    /// <summary>Ersätter det centrala basvärdet helt (effektivt värde = värde).</summary>
    ErsattVarde
}

/// <summary>
/// Enhet för <see cref="LokalAvtalsAvvikelse.Varde"/>. Styr hur beloppet ska tolkas
/// i lön/UI (kr per timme för OB, kr per månad för tillägg, procent för påslag).
/// </summary>
public enum LokalBeloppsEnhet
{
    /// <summary>Kronor per timme (typiskt OB-påslag).</summary>
    KronorPerTimme,

    /// <summary>Kronor per månad (typiskt lönetillägg / förmån).</summary>
    KronorPerManad,

    /// <summary>Engångsbelopp i kronor.</summary>
    Kronor,

    /// <summary>Procent (används med <see cref="LokalBerakningsTyp.ProcentPaslag"/>).</summary>
    Procent
}
