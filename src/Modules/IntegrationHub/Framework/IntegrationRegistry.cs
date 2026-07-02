namespace RegionHR.IntegrationHub.Framework;

/// <summary>
/// Registret över samtliga integrationer i regionens personalstöds-arkitektur
/// ("Personalstöd v7"). Best-of-breed betyder att OpenHR växlar data med ~26
/// motparter via integrationsmotorn Health Connect (HC) över FIL/WS/REST.
///
/// Registret är en <em>ren beskrivning</em> — det öppnar inga anslutningar. En
/// integration blir körbar när en <see cref="IIntegrationJob"/> registreras för
/// dess <see cref="IntegrationDefinition.Key"/> och en <see cref="ISftpTransport"/>
/// (eller motsvarande WS/REST-adapter) är konfigurerad. Skarp transport kräver
/// endast konfiguration (endpoint/nycklar) — inte betalt avtal för ramverket.
/// </summary>
public static class IntegrationRegistry
{
    private static readonly IReadOnlyList<IntegrationDefinition> _alla = BuildAlla();

    /// <summary>Alla registrerade integrationer, sorterade på namn.</summary>
    public static IReadOnlyList<IntegrationDefinition> Alla => _alla;

    /// <summary>Slår upp en integration på nyckel, eller null om okänd.</summary>
    public static IntegrationDefinition? HittaOrNull(string key) =>
        _alla.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Alla utgående integrationer.</summary>
    public static IEnumerable<IntegrationDefinition> Utgaende =>
        _alla.Where(d => d.Riktning is IntegrationDirection.Utgaende or IntegrationDirection.BadaRiktningar);

    /// <summary>Alla inkommande integrationer.</summary>
    public static IEnumerable<IntegrationDefinition> Inkommande =>
        _alla.Where(d => d.Riktning is IntegrationDirection.Inkommande or IntegrationDirection.BadaRiktningar);

    private static IReadOnlyList<IntegrationDefinition> BuildAlla()
    {
        var list = new List<IntegrationDefinition>
        {
            Def("skatteverket-agi", "Skatteverket — Arbetsgivardeklaration (AGI)",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Skatteverket",
                "Månadsvis", "AGI-XML", true,
                "Arbetsgivardeklaration på individnivå per lönekörning."),
            Def("skatteverket-navet", "Skatteverket Navet — folkbokföring",
                IntegrationDirection.Inkommande, IntegrationTransport.Fil, "Skatteverket (Navet)",
                "Dygn", "Fast postfil", true,
                "Aviseringar om ändrade person-/adressuppgifter för anställda."),
            Def("nordea-pain001", "Nordea — löneutbetalning (pain.001)",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Nordea",
                "Månadsvis", "ISO 20022 pain.001", true,
                "Betalfil med nettolöner per lönekörning."),
            Def("kpa-pension", "KPA — tjänstepension (AKAP-KR)",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "KPA",
                "Månadsvis", "CSV/XML", true,
                "Pensionsgrundande löneunderlag och premier."),
            Def("skandia-pension", "Skandia — pensionsredovisning",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Skandia",
                "Månadsvis", "Fil", true,
                "Kompletterande pensionsredovisning för valbara avtal."),
            Def("afa-forsakring", "AFA Försäkring — försäkringsunderlag",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "AFA Försäkring",
                "Årsvis", "Fil", true,
                "Löne- och anställningsunderlag för kollektivavtalade försäkringar."),
            Def("fk-ssbtek", "Försäkringskassan — sjuk/rehab (SSBTEK)",
                IntegrationDirection.BadaRiktningar, IntegrationTransport.Webbtjanst, "Försäkringskassan",
                "Händelsestyrt", "SSBTEK / XML", true,
                "Sjukanmälan och rehabiliteringsärenden."),
            Def("scb-klr", "SCB — konjunkturlönestatistik (KLR)",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "SCB",
                "Månadsvis", "CSV", true,
                "Löne- och sysselsättningsstatistik till SCB."),
            Def("scb-lonestruktur", "SCB — lönestrukturstatistik",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "SCB",
                "Årsvis", "CSV", true,
                "Årlig lönestrukturstatistik (offentlig sektor)."),
            Def("skr-statistik", "SKR — personal- och lönestatistik",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Sveriges Kommuner och Regioner",
                "Årsvis", "CSV", true,
                "Personalstatistik till SKR (novemberstatistiken m.fl.)."),
            Def("platsbanken", "Arbetsförmedlingen — Platsbanken",
                IntegrationDirection.Utgaende, IntegrationTransport.RestApi, "Arbetsförmedlingen",
                "Händelsestyrt", "REST (HR-XML/JobTech)", true,
                "Publicering av lediga tjänster."),
            Def("koll-hosp", "Socialstyrelsen — legitimationer (HOSP)",
                IntegrationDirection.Inkommande, IntegrationTransport.RestApi, "Socialstyrelsen",
                "Dygn", "REST", true,
                "Verifiering av legitimationer och specialiteter för vårdpersonal."),
            Def("koll-katalog", "KOLL — RÖL katalogtjänst",
                IntegrationDirection.Inkommande, IntegrationTransport.Webbtjanst, "Region Örebro län (KOLL)",
                "Dygn", "WS", true,
                "Regionens interna katalogtjänst för organisation och behörigheter."),
            Def("hsa-katalog", "HSA-katalogen (Inera)",
                IntegrationDirection.Inkommande, IntegrationTransport.Webbtjanst, "Inera",
                "Dygn", "WS / LDAP (SITHS)", true,
                "Hälso- och sjukvårdens adressregister — HSA-id på enheter och personer."),
            Def("kronofogden-utmatning", "Kronofogden — löneutmätning",
                IntegrationDirection.Inkommande, IntegrationTransport.Fil, "Kronofogden",
                "Månadsvis", "Fil", true,
                "Beslut om löneutmätning som påverkar nettolön."),
            Def("fackforbund-avgift", "Fackförbund — medlemsavgifter",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Kommunal/Vision/SSR m.fl.",
                "Månadsvis", "Fil", true,
                "Avdragna fackavgifter per förbund och medlem."),
            Def("raindance-kontering", "Raindance — kontering (ekonomi)",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Raindance (CGI)",
                "Månadsvis", "Konteringsfil", true,
                "Lönekontering till regionens ekonomisystem."),
            Def("sie-export", "SIE — bokföringsexport",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Ekonomisystem",
                "Månadsvis", "SIE (Latin1)", false,
                "SIE-fil för bokföring/avstämning (teckenkodning Latin1)."),
            Def("ad-entra-scim", "Microsoft Entra ID — kontoprovisionering",
                IntegrationDirection.Utgaende, IntegrationTransport.RestApi, "Microsoft Entra ID",
                "Händelsestyrt", "SCIM 2.0", true,
                "Skapar/avslutar konton vid anställning och offboarding."),
            Def("diver-bi", "Diver — beslutsstöd",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Diver (Dimensional Insight)",
                "Dygn", "Fil", true,
                "Personaldata till regionens beslutsstöd."),
            Def("powerbi", "Power BI — analysdataset",
                IntegrationDirection.Utgaende, IntegrationTransport.RestApi, "Microsoft Power BI",
                "Dygn", "CSV/Push-API", true,
                "Aggregerad HR-data för rapportportalen."),
            Def("epassi-friskvard", "Epassi — friskvård",
                IntegrationDirection.BadaRiktningar, IntegrationTransport.RestApi, "Epassi",
                "Månadsvis", "REST", true,
                "Friskvårdssaldon och nyttjande."),
            Def("grade-lms", "Grade — kompetens/LMS",
                IntegrationDirection.BadaRiktningar, IntegrationTransport.RestApi, "Grade",
                "Dygn", "REST", true,
                "Utbildningsresultat och certifieringar."),
            Def("minkompetens", "MinKompetens — kompetensregister",
                IntegrationDirection.BadaRiktningar, IntegrationTransport.RestApi, "MinKompetens",
                "Dygn", "REST", true,
                "Externa kompetens- och certifikatuppgifter."),
            Def("microweb-arkiv", "Microweb — e-arkiv",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Microweb",
                "Månadsvis", "Fil", true,
                "Långtidsarkivering av personaldokument."),
            Def("troman", "Troman — förtroendevalda",
                IntegrationDirection.Inkommande, IntegrationTransport.Webbtjanst, "Troman",
                "Dygn", "WS", true,
                "Uppgifter om förtroendevalda och arvoden."),
            Def("hc-manifest", "Health Connect — integrationsmanifest",
                IntegrationDirection.Utgaende, IntegrationTransport.Fil, "Health Connect (integrationsmotor)",
                "Dygn", "CSV", true,
                "Självbeskrivande katalog över alla integrationer OpenHR exponerar för HC. " +
                "Fungerar direkt utan extern konfiguration och används som referenskörning."),
        };

        return list.OrderBy(d => d.Namn, StringComparer.Create(new System.Globalization.CultureInfo("sv-SE"), ignoreCase: true))
                   .ToList();
    }

    private static IntegrationDefinition Def(
        string key, string namn, IntegrationDirection riktning, IntegrationTransport transport,
        string motpart, string frekvens, string format, bool viaHc, string beskrivning) =>
        new(key, namn, riktning, transport, motpart, frekvens, format, viaHc, beskrivning);
}
