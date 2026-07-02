using System.Globalization;

namespace RegionHR.Reporting.Engine;

/// <summary>
/// Minimal men korrekt 5-fälts cron-utvärderare (minut tim dag-i-månad månad veckodag).
/// Stödjer <c>*</c>, tal, listor (<c>1,15</c>), intervall (<c>1-5</c>) och steg (<c>*/15</c>,
/// <c>0-30/10</c>). Räcker för schemalagda rapporter ("enkel intervall" så väl som
/// dagligen/veckovis/månadsvis) utan externt cron-paket. Även svenska nyckelord
/// ("Daily"/"Weekly"/"Monthly"/"Hourly") accepteras via <see cref="TryParse"/>.
///
/// Veckodag: 0 = söndag ... 6 = lördag (7 tillåts också som söndag).
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _daysOfMonth = new bool[32]; // 1..31
    private readonly bool[] _months = new bool[13];      // 1..12
    private readonly bool[] _daysOfWeek = new bool[7];   // 0..6

    private CronSchedule() { }

    /// <summary>
    /// Försöker tolka ett cron-uttryck eller ett enkelt frekvensnyckelord.
    /// Returnerar null om uttrycket inte kunde tolkas (anroparen bör då köra som fallback).
    /// </summary>
    public static CronSchedule? TryParse(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var expr = expression.Trim();

        // Bekvämlighetsnyckelord som mappas till standard-cron.
        expr = expr switch
        {
            _ when expr.Equals("Hourly", StringComparison.OrdinalIgnoreCase) => "0 * * * *",
            _ when expr.Equals("Daily", StringComparison.OrdinalIgnoreCase) => "0 6 * * *",
            _ when expr.Equals("Weekly", StringComparison.OrdinalIgnoreCase) => "0 6 * * 1",
            _ when expr.Equals("Monthly", StringComparison.OrdinalIgnoreCase) => "0 6 1 * *",
            _ => expr
        };

        var parts = expr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return null;

        var schedule = new CronSchedule();
        if (!FyllFalt(parts[0], 0, 59, schedule._minutes, 0)) return null;
        if (!FyllFalt(parts[1], 0, 23, schedule._hours, 0)) return null;
        if (!FyllFalt(parts[2], 1, 31, schedule._daysOfMonth, 0)) return null;
        if (!FyllFalt(parts[3], 1, 12, schedule._months, 0)) return null;
        if (!FyllFaltVeckodag(parts[4], schedule._daysOfWeek)) return null;
        return schedule;
    }

    /// <summary>True om <paramref name="tidpunkt"/> (minutupplösning) matchar schemat.</summary>
    public bool Matchar(DateTime tidpunkt)
    {
        var domMatch = _daysOfMonth[tidpunkt.Day];
        var dowMatch = _daysOfWeek[(int)tidpunkt.DayOfWeek];

        // Standard cron-semantik: om BÅDE dag-i-månad och veckodag är begränsade (ej '*')
        // matchar tidpunkten om NÅGON av dem stämmer; annars måste den begränsade stämma.
        var domRestricted = !_daysOfMonth.Skip(1).All(b => b);          // index 1..31
        var dowRestricted = !_daysOfWeek.All(b => b);                    // index 0..6
        bool dagMatch;
        if (domRestricted && dowRestricted) dagMatch = domMatch || dowMatch;
        else dagMatch = domMatch && dowMatch;

        return _minutes[tidpunkt.Minute]
            && _hours[tidpunkt.Hour]
            && _months[tidpunkt.Month]
            && dagMatch;
    }

    /// <summary>
    /// Nästa tidpunkt (minutupplösning) strikt efter <paramref name="efter"/> som matchar schemat.
    /// Söker upp till ~2 år framåt; returnerar null om inget hittas (t.ex. 31 feb).
    /// </summary>
    public DateTime? NastaEfter(DateTime efter)
    {
        var t = new DateTime(efter.Year, efter.Month, efter.Day, efter.Hour, efter.Minute, 0, efter.Kind)
                    .AddMinutes(1);
        var grans = t.AddYears(2);
        while (t < grans)
        {
            if (Matchar(t)) return t;
            t = t.AddMinutes(1);
        }
        return null;
    }

    /// <summary>True om schemat skulle ha löpt någon gång i intervallet (<paramref name="sedan"/>, <paramref name="till"/>].</summary>
    public bool ArForfallenSedan(DateTime sedan, DateTime till)
    {
        var nasta = NastaEfter(sedan);
        return nasta.HasValue && nasta.Value <= till;
    }

    private static bool FyllFalt(string field, int min, int max, bool[] target, int offset)
    {
        foreach (var del in field.Split(','))
        {
            var stycke = del;
            var steg = 1;
            var stegIdx = stycke.IndexOf('/');
            if (stegIdx >= 0)
            {
                if (!int.TryParse(stycke[(stegIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out steg) || steg <= 0)
                    return false;
                stycke = stycke[..stegIdx];
            }

            int lo, hi;
            if (stycke == "*")
            {
                lo = min; hi = max;
            }
            else if (stycke.Contains('-'))
            {
                var mm = stycke.Split('-');
                if (mm.Length != 2
                    || !int.TryParse(mm[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lo)
                    || !int.TryParse(mm[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hi))
                    return false;
            }
            else
            {
                if (!int.TryParse(stycke, NumberStyles.Integer, CultureInfo.InvariantCulture, out lo)) return false;
                hi = lo;
            }

            if (lo < min || hi > max || lo > hi) return false;
            for (var v = lo; v <= hi; v += steg)
                target[v + offset] = true;
        }
        return true;
    }

    private static bool FyllFaltVeckodag(string field, bool[] target)
    {
        // Hantera 7 som söndag genom att normalisera till 0..6.
        var tmp = new bool[8];
        if (!FyllFalt(field, 0, 7, tmp, 0)) return false;
        for (var i = 0; i <= 7; i++)
            if (tmp[i]) target[i % 7] = true;
        return true;
    }
}
