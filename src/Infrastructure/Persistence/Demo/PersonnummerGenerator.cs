using RegionHR.SharedKernel.Domain;

namespace RegionHR.Infrastructure.Persistence.Demo;

/// <summary>
/// Genererar giltiga, unika svenska personnummer för demo-befolkningen.
///
/// Ett personnummer byggs som YYYYMMDD + 3-siffrigt födelsenummer (NNN) +
/// en <em>beräknad</em> Luhn-kontrollsiffra (aldrig slumpad). Näst sista siffran
/// (sista siffran i NNN) styr juridiskt kön: jämn = Kvinna, udda = Man — vilket
/// hålls konsekvent med det valda förnamnets kön.
///
/// Unikhet garanteras via en <see cref="HashSet{T}"/> över det normaliserade
/// 12-siffriga värdet. Varje returnerat nummer valideras dessutom mot
/// <see cref="Personnummer"/>-värdesobjektet (som kör Luhn på nytt), så att
/// generatorn inte kan producera något som systemet självt skulle underkänna.
/// </summary>
public sealed class PersonnummerGenerator
{
    private readonly HashSet<string> _anvanda;
    private readonly Random _rng;

    /// <param name="rng">Slumpkälla (injiceras för deterministiska tester).</param>
    /// <param name="reserverade">
    /// Redan använda personnummer (t.ex. de handplockade demo-användarna) som
    /// generatorn måste undvika att kollidera med.
    /// </param>
    public PersonnummerGenerator(Random? rng = null, IEnumerable<string>? reserverade = null)
    {
        _rng = rng ?? new Random();
        _anvanda = new HashSet<string>();
        if (reserverade is not null)
        {
            foreach (var r in reserverade)
            {
                var normal = Normalisera(r);
                if (normal is not null) _anvanda.Add(normal);
            }
        }
    }

    /// <summary>Antal hittills genererade (exklusive reserverade).</summary>
    public int AntalGenererade { get; private set; }

    /// <summary>
    /// Returnerar ett nytt, unikt och giltigt personnummer för en person med
    /// angiven ålder och kön (per <paramref name="arKvinna"/>).
    /// </summary>
    public Personnummer NastaUnikt(int alder, bool arKvinna, DateOnly idag)
    {
        // Rimlighetsvakt: åldersintervall 18–100 (demo använder 20–67).
        if (alder < 18) alder = 18;
        if (alder > 100) alder = 100;

        // Försök tills ett unikt nummer hittas. Sökrymden (≈17 000 datum × 1000
        // födelsenummer) är enorm jämfört med ~11 000 personer → i praktiken 0–1 omtag.
        for (var forsok = 0; forsok < 10_000; forsok++)
        {
            var fodelsear = idag.Year - alder;
            var manad = _rng.Next(1, 13);
            var dag = _rng.Next(1, 29); // 1–28 är alltid en giltig dag i alla månader

            // 3-siffrigt födelsenummer där sista siffran matchar kön.
            var forsta = _rng.Next(0, 10);
            var andra = _rng.Next(0, 10);
            var sista = arKvinna
                ? _rng.Next(0, 5) * 2        // 0,2,4,6,8
                : _rng.Next(0, 5) * 2 + 1;   // 1,3,5,7,9
            if (forsta == 0 && andra == 0 && sista == 0) continue; // undvik NNN = 000

            var yyyymmdd = $"{fodelsear:D4}{manad:D2}{dag:D2}";
            var nnn = $"{forsta}{andra}{sista}";
            var kontroll = BeraknaLuhnKontrollsiffra(yyyymmdd[2..] + nnn); // YYMMDD + NNN (9 siffror)
            var tolvSiffror = yyyymmdd + nnn + kontroll;

            if (!_anvanda.Add(tolvSiffror)) continue; // krock → nytt försök

            // Slutlig sanity-check mot värdesobjektet (format + Luhn + könsparitet).
            var pnr = new Personnummer(tolvSiffror);
            AntalGenererade++;
            return pnr;
        }

        throw new InvalidOperationException(
            "Kunde inte generera ett unikt personnummer efter 10 000 försök — sökrymden bör vara uttömd först.");
    }

    /// <summary>
    /// Beräknar Luhn-kontrollsiffran för de 9 siffrorna YYMMDDNNN enligt exakt samma
    /// algoritm som <see cref="Personnummer"/> validerar med (multiplikator 2,1,2,1…).
    /// </summary>
    public static int BeraknaLuhnKontrollsiffra(string nioSiffror)
    {
        if (nioSiffror.Length != 9)
            throw new ArgumentException("Luhn-underlaget måste vara exakt 9 siffror (YYMMDDNNN).", nameof(nioSiffror));

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var d = nioSiffror[i] - '0';
            var m = d * (i % 2 == 0 ? 2 : 1);
            sum += m > 9 ? m - 9 : m;
        }
        return (10 - sum % 10) % 10;
    }

    private static string? Normalisera(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = input.Replace(" ", "").Replace("-", "").Replace("+", "");
        return cleaned.Length == 12 && cleaned.All(char.IsDigit) ? cleaned : null;
    }
}
