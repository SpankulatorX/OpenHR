namespace RegionHR.IntegrationHub.Framework;

/// <summary>Riktning för en integration sett från OpenHR.</summary>
public enum IntegrationDirection
{
    /// <summary>OpenHR skickar data ut (t.ex. AGI, betalfil, pension).</summary>
    Utgaende,
    /// <summary>OpenHR tar emot data in (t.ex. folkbokföring, legitimationer).</summary>
    Inkommande,
    /// <summary>Både ut och in (t.ex. FK-ärenden, friskvårdssaldon).</summary>
    BadaRiktningar
}

/// <summary>
/// Transportsätt. Regionens best-of-breed-arkitektur körs via integrationsmotorn
/// Health Connect (HC) och kräver att OpenHR kan tala FIL (SFTP), WEBBTJÄNST (WS)
/// och REST-API.
/// </summary>
public enum IntegrationTransport
{
    /// <summary>Filöverföring, normalt via SFTP till/från Health Connect.</summary>
    Fil,
    /// <summary>SOAP/WS-* webbtjänst (t.ex. SSBTEK, HSA).</summary>
    Webbtjanst,
    /// <summary>REST-API (JSON).</summary>
    RestApi
}

/// <summary>Utfall för en enskild integrationskörning (loggas i run-log).</summary>
public enum IntegrationRunStatus
{
    /// <summary>Filen genererades och levererades till drop-katalogen/HC.</summary>
    Lyckad,
    /// <summary>Körningen avbröts av ett fel.</summary>
    Misslyckad,
    /// <summary>Integrationen är registrerad men saknar ännu en körbar jobbdefinition.</summary>
    SaknarJobb
}
