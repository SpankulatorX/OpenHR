namespace RegionHR.GDPR.Klassa;

/// <summary>
/// Fördefinierad KLASSA-standardklassning för OpenHR:s känsliga informationsmängder. Utgör
/// utgångsläget i informationsklassningsregistret och kan förfinas i UI. Varje post är satt så
/// att den uppfyller (eller överstiger) kategorins rekommenderade miniminivåer i
/// <see cref="KlassaRegler"/>. Innehåller bl.a. de känsliga personuppgifterna enligt GDPR art. 9
/// (hälsa/rehab, facklig tillhörighet) samt lön och personnummer.
/// </summary>
public static class KlassaSeed
{
    /// <summary>Skapar en färsk lista med fördefinierade klassningsposter.</summary>
    public static IReadOnlyList<InformationsklassPost> Fordefinierade() =>
    [
        InformationsklassPost.Skapa(
            "Lönedata",
            "Löneutbetalningar, lönearter, skatteavdrag och ersättningar per anställd.",
            InformationsKategori.Loneuppgift,
            "Lön",
            KonsekvensNiva.Betydande, KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande,
            "Röjande av lönenivåer kan skada enskild och verksamhetens förtroende.",
            "Felaktig lön ger direkt ekonomisk skada och felaktig arbetsgivardeklaration (AGI).",
            "Lön måste kunna beräknas och betalas ut i tid varje månad.",
            "Behörighetsstyrd åtkomst (HR/Admin)\nLoggning av läsning/ändring\nKryptering i vila och transport\nSegregering av arbetsuppgifter vid utbetalning",
            "GDPR art. 6, Bokföringslagen, OSL 39 kap.",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Personuppgifter (grunddata)",
            "Namn, kontaktuppgifter, adress och anställningsuppgifter.",
            InformationsKategori.Grunddata,
            "Kärn-HR",
            KonsekvensNiva.Mattlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "Vanliga personuppgifter — begränsad men reell integritetspåverkan vid röjande.",
            "Felaktiga grunddata sprids till lön, schema och integrationer.",
            "Behövs löpande i personaladministrationen.",
            "Behörighetsstyrning\nÄndringslogg\nDataminimering",
            "GDPR art. 6, OSL 39 kap.",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Personnummer",
            "Fullständigt personnummer som identifierare i hela systemet.",
            InformationsKategori.Grunddata,
            "Kärn-HR",
            KonsekvensNiva.Betydande, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "Personnummer är en eftertraktad identifierare — röjande möjliggör identitetskapning.",
            "Felaktigt personnummer bryter integrationer mot Skatteverket/FK/bank.",
            "Krävs för lön, AGI och myndighetsrapportering.",
            "Åtkomst efter behovsprövning\nMaskning i listvyer\nLoggning\nKryptering",
            "GDPR art. 6 & art. 87, Lag (2018:218) kompletterande bestämmelser",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Rehabärenden och hälsodata",
            "Sjukfrånvaro, rehabkedja, arbetsförmåga och medicinska underlag (HälsoSAM).",
            InformationsKategori.Halsouppgift,
            "HälsoSAM",
            KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "Känslig personuppgift (GDPR art. 9) — röjande av hälsa ger allvarlig integritetsskada.",
            "Felaktiga underlag kan leda till felaktiga rehab- och arbetsmiljöbeslut.",
            "Behövs vid pågående rehabärende men inte affärskritiskt sekundsnabbt.",
            "Strikt behovsprövad åtkomst (HR/rehabhandläggare)\nSeparata behörigheter från övrig HR\nFullständig åtkomstlogg\nKryptering i vila\nGallring enligt fastställd frist",
            "GDPR art. 9.2 (b/h), OSL 25 kap., AML samt AFS 2020:5",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Facklig tillhörighet",
            "Uppgift om medlemskap i fackförbund (för avdrag och förhandling).",
            InformationsKategori.FackligTillhorighet,
            "Kärn-HR / Lön",
            KonsekvensNiva.Allvarlig, KonsekvensNiva.Mattlig, KonsekvensNiva.Forsumbar,
            "Känslig personuppgift (GDPR art. 9) — får aldrig röjas för obehöriga.",
            "Fel förbund kan ge fel avgiftsavdrag men reparerbart.",
            "Behövs sällan i realtid; låg tillgänglighetskänslighet.",
            "Snäv behörighet\nÅtkomstlogg\nKryptering\nEndast för avsett ändamål (avdrag/MBL)",
            "GDPR art. 9.2 (b), MBL",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Bank- och utbetalningsuppgifter",
            "Kontonummer och betalningsuppgifter för löneutbetalning.",
            InformationsKategori.Bankuppgift,
            "Lön",
            KonsekvensNiva.Betydande, KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande,
            "Röjande möjliggör bedrägeri och ekonomisk skada.",
            "Felaktigt kontonummer leder till felutbetalning till fel mottagare.",
            "Krävs vid varje löneutbetalning (betalfil pain.001).",
            "Fyra-ögon vid ändring av kontonummer\nÅtkomstlogg\nKryptering\nAvvikelsekontroll mot tidigare konto",
            "GDPR art. 6, Bokföringslagen",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Anställningsavtal och beslut",
            "Anställningsavtal, anställningsbeslut och villkorsändringar.",
            InformationsKategori.Grunddata,
            "Kärn-HR / Dokument",
            KonsekvensNiva.Betydande, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "Innehåller villkor och personuppgifter som inte bör röjas.",
            "Avtalet är rättsligt bindande — riktighet är avgörande vid tvist.",
            "Behöver vara åtkomligt vid personalärenden.",
            "Behörighetsstyrning\nVersionshantering\nBevarande enligt arkivlagen\nLoggning",
            "GDPR art. 6, LAS, Arkivlagen",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Rekryteringsunderlag",
            "Ansökningar, CV, referenser och urvalsdata för kandidater.",
            InformationsKategori.Rekrytering,
            "Rekrytering",
            KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig,
            "Kandidatuppgifter om ej anställda — röjande skadar förtroende och kan strida mot DL.",
            "Bedömningar ska vara spårbara men enstaka fel är hanterbart.",
            "Behövs under pågående rekrytering, därefter gallring.",
            "Behörighet till rekryterande team\nGallring efter avslutad rekrytering\nLoggning",
            "GDPR art. 6, Diskrimineringslagen",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Tillbud och arbetsskador",
            "Anmälda tillbud, olyckor och arbetsskador (kan innehålla hälsouppgifter).",
            InformationsKategori.Arbetsmiljo,
            "Arbetsmiljö",
            KonsekvensNiva.Betydande, KonsekvensNiva.Betydande, KonsekvensNiva.Mattlig,
            "Kan innehålla hälsouppgifter — begränsad åtkomst krävs.",
            "Underlag för anmälan till Försäkringskassan/Arbetsmiljöverket måste vara korrekt.",
            "Behövs vid utredning och rapportering.",
            "Behovsprövad åtkomst\nÅtkomstlogg\nSeparera hälsodetaljer\nBevarande enligt AFS",
            "AML, AFS 2020:5, GDPR art. 9 (vid hälsouppgift)",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Skyddad identitet",
            "Uppgifter om anställda med skyddad identitet/personuppgift.",
            InformationsKategori.SkyddadIdentitet,
            "Kärn-HR",
            KonsekvensNiva.Allvarlig, KonsekvensNiva.Betydande, KonsekvensNiva.Betydande,
            "Röjande kan innebära fara för liv och hälsa — högsta konfidentialitet.",
            "Fel i markering/hantering kan oavsiktligt röja identiteten.",
            "Måste hanteras korrekt i alla utflöden (lön, integrationer, utskrifter).",
            "Särskild behörighet och rutin\nUndantag från ordinarie utflöden\nMaskning\nManuell granskning före export\nFullständig logg",
            "OSL 21 kap. 3 §, Folkbokföringslagen, GDPR art. 6",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Kompetens och utbildning",
            "Genomförda utbildningar, certifieringar och kompetensprofiler.",
            InformationsKategori.Kompetens,
            "Kompetens/LMS",
            KonsekvensNiva.Forsumbar, KonsekvensNiva.Mattlig, KonsekvensNiva.Mattlig,
            "Begränsad känslighet — mestadels arbetsrelaterade uppgifter.",
            "Fel i behörighetskritisk certifiering (t.ex. legitimation) kan få följder.",
            "Behövs vid bemanning och behörighetskontroll.",
            "Behörighetsstyrning\nKälla för legitimationskrav verifieras",
            "GDPR art. 6",
            arFordefinierad: true),

        InformationsklassPost.Skapa(
            "Systemloggar och spårbarhet",
            "Åtkomst-, ändrings- och integrationsloggar (audit trail).",
            InformationsKategori.Systemteknisk,
            "Plattform/Audit",
            KonsekvensNiva.Mattlig, KonsekvensNiva.Betydande, KonsekvensNiva.Betydande,
            "Loggar kan avslöja vem som gjort vad — begränsad åtkomst.",
            "Loggar måste vara oförvanskade för att duga som bevis (ansvarsskyldighet).",
            "Behövs för incidentutredning och drift.",
            "Skrivskyddade loggar\nBegränsad åtkomst (Admin)\nTidsstämpling\nBevarande enligt policy",
            "GDPR art. 5.2 (ansvarsskyldighet), OSL",
            arFordefinierad: true),
    ];
}
