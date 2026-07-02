namespace RegionHR.IntegrationHub.Framework;

/// <summary>
/// Statisk beskrivning av EN integration i regionens personalstöds-arkitektur.
/// Registret (<see cref="IntegrationRegistry"/>) är sanningen om vilka motparter,
/// riktningar, format och frekvenser som finns — oberoende av om en skarp
/// transport är konfigurerad ännu.
/// </summary>
/// <param name="Key">Stabil teknisk nyckel (används av jobbrunner + run-log).</param>
/// <param name="Namn">Läsbart namn på integrationen.</param>
/// <param name="Riktning">Ut, in eller båda sett från OpenHR.</param>
/// <param name="Transport">Fil (SFTP), webbtjänst (WS) eller REST.</param>
/// <param name="Motpart">Extern part / mottagande system.</param>
/// <param name="Frekvens">Schema/frekvens (t.ex. "Månadsvis", "Händelsestyrt", "Dygn").</param>
/// <param name="Format">Filformat/protokoll (t.ex. "AGI-XML", "pain.001", "SIE (Latin1)").</param>
/// <param name="ViaHealthConnect">Går transporten via integrationsmotorn Health Connect?</param>
/// <param name="Beskrivning">Kort förklaring av vad integrationen gör.</param>
public sealed record IntegrationDefinition(
    string Key,
    string Namn,
    IntegrationDirection Riktning,
    IntegrationTransport Transport,
    string Motpart,
    string Frekvens,
    string Format,
    bool ViaHealthConnect,
    string Beskrivning);
