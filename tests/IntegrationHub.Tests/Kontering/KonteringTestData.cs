using RegionHR.Payroll.Domain;
using RegionHR.SharedKernel.Domain;

namespace RegionHR.IntegrationHub.Tests.Kontering;

/// <summary>
/// Bygger en balanserad lönekörning för konteringstester (Raindance + SIE).
/// Två anställda på var sitt kostnadsställe. Varje kostnadsställe balanserar
/// (lönekostnad + arbetsgivaravgift + pension = skatt + AG-skuld + löneskuld + pensionsskuld).
/// </summary>
internal static class KonteringTestData
{
    public const string Kst1 = "1000";
    public const string Kst2 = "2000";

    public static PayrollRun SkapaBalanseradKorning(int year = 2026, int month = 3)
    {
        var run = PayrollRun.Skapa(year, month, "test");

        // ── Kostnadsställe 1000: grundlön 30000 + OB 2000 ──
        var a = PayrollResult.Skapa(
            run.Id, EmployeeId.New(), EmploymentId.New(),
            year, month, Money.SEK(30000m), 100m, CollectiveAgreementType.AB);
        a.LaggTillRad(Rad("1100", "Månadslön", 30000m, Kst1));
        a.LaggTillRad(Rad("1310", "OB-tillägg", 2000m, Kst1));
        a.Brutto = Money.SEK(32000m);
        a.OBTillagg = Money.SEK(2000m);
        a.Skatt = Money.SEK(9600m);
        a.Netto = Money.SEK(22400m);
        a.Arbetsgivaravgifter = Money.SEK(10000m);
        a.Pensionsavgift = Money.SEK(1440m);
        run.LaggTillResultat(a);

        // ── Kostnadsställe 2000: grundlön 25000 ──
        var b = PayrollResult.Skapa(
            run.Id, EmployeeId.New(), EmploymentId.New(),
            year, month, Money.SEK(25000m), 100m, CollectiveAgreementType.AB);
        b.LaggTillRad(Rad("1100", "Månadslön", 25000m, Kst2));
        b.Brutto = Money.SEK(25000m);
        b.Skatt = Money.SEK(7500m);
        b.Netto = Money.SEK(17500m);
        b.Arbetsgivaravgifter = Money.SEK(8000m);
        b.Pensionsavgift = Money.SEK(1125m);
        run.LaggTillResultat(b);

        return run;
    }

    private static PayrollResultLine Rad(string kod, string benamning, decimal belopp, string kostnadsstalle) => new()
    {
        LoneartKod = kod,
        Benamning = benamning,
        Antal = 1,
        Sats = Money.SEK(belopp),
        Belopp = Money.SEK(belopp),
        Skattekategori = TaxCategory.Skattepliktig,
        ArSemestergrundande = true,
        ArPensionsgrundande = true,
        Kostnadsstalle = kostnadsstalle
    };
}
