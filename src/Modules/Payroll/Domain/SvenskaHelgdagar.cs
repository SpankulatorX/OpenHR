using RegionHR.SharedKernel.Domain;

namespace RegionHR.Payroll.Domain;

/// <summary>
/// Beräknar svenska helgdagar inklusive rörliga helgdagar.
/// Används för OB-kategoribestämning och arbetsschemaberäkningar.
/// </summary>
public static class SvenskaHelgdagar
{
    /// <summary>
    /// Avgör om ett datum är en svensk helgdag (röd dag).
    /// </summary>
    public static bool ArHelgdag(DateOnly datum)
    {
        var helgdagar = HelgdagarForAr(datum.Year);
        return helgdagar.Contains(datum);
    }

    /// <summary>
    /// Avgör om ett datum (hela dygnet) infaller under en storhelgsperiod
    /// (O-tilläggstid A enligt AB 25 § 21):
    /// julafton–annandag jul, nyårsafton–nyårsdagen,
    /// långfredagen–annandag påsk samt midsommarafton–söndagen efter midsommardagen.
    /// Kristi himmelsfärdsdag är helgdag (O-tilläggstid B) men INTE storhelg.
    /// Kanttimmarna (från kl. 18.00 dagen före långfredagen respektive till kl. 07.00
    /// måndagen efter midsommarhelgen) hanteras av <see cref="ArStorhelg(DateOnly, TimeOnly)"/>.
    /// </summary>
    public static bool ArStorhelg(DateOnly datum)
    {
        var year = datum.Year;
        var paskdagen = BeraknaPaskdagen(year);

        // Julafton (24 dec) - Annandag jul (26 dec)
        var julafton = new DateOnly(year, 12, 24);
        var annandagJul = new DateOnly(year, 12, 26);
        if (datum >= julafton && datum <= annandagJul)
            return true;

        // Nyårsafton (31 dec) - Nyårsdagen (1 jan)
        // Nyårsafton samma år
        if (datum == new DateOnly(year, 12, 31))
            return true;
        // Nyårsdagen
        if (datum == new DateOnly(year, 1, 1))
            return true;

        // Långfredagen - Annandag påsk (AB 25 § 21: O-tilläggstid A börjar redan
        // kl. 18.00 dagen före långfredagen — timkanten hanteras i tidsöverlasten)
        var langfredagen = paskdagen.AddDays(-2);
        var annandagPask = paskdagen.AddDays(1);
        if (datum >= langfredagen && datum <= annandagPask)
            return true;

        // Midsommarafton - söndagen efter midsommardagen (A-tiden löper till kl. 07.00
        // på måndagen — timkanten hanteras i tidsöverlasten)
        var midsommardagen = BeraknaMidsommardagen(year);
        var midsommarafton = midsommardagen.AddDays(-1);
        var sondagenEfterMidsommar = midsommardagen.AddDays(1);
        if (datum >= midsommarafton && datum <= sondagenEfterMidsommar)
            return true;

        // Kristi himmelsfärdsdag ingår INTE i O-tilläggstid A enligt AB 25 § 21
        // (den är helgdag → O-tilläggstid B).
        return false;
    }

    /// <summary>
    /// Tidsmedveten storhelgsbedömning (O-tilläggstid A enligt AB 25 § 21).
    /// Utöver hela storhelgsdygnen ingår kanterna:
    /// från kl. 18.00 dagen före långfredagen (skärtorsdagen) och
    /// till kl. 07.00 på måndagen efter midsommarhelgen.
    /// </summary>
    public static bool ArStorhelg(DateOnly datum, TimeOnly tid)
    {
        if (ArStorhelg(datum))
            return true;

        // Skärtorsdagen (dagen före långfredagen) från kl. 18.00
        var paskdagen = BeraknaPaskdagen(datum.Year);
        if (datum == paskdagen.AddDays(-3) && tid >= new TimeOnly(18, 0))
            return true;

        // Måndagen efter midsommarhelgen till kl. 07.00
        var midsommardagen = BeraknaMidsommardagen(datum.Year);
        if (datum == midsommardagen.AddDays(2) && tid < new TimeOnly(7, 0))
            return true;

        return false;
    }

    /// <summary>
    /// Returnerar alla helgdagar för ett givet år.
    /// </summary>
    public static IReadOnlyList<DateOnly> HelgdagarForAr(int year)
    {
        var paskdagen = BeraknaPaskdagen(year);
        var helgdagar = new List<DateOnly>
        {
            // Fasta helgdagar
            new(year, 1, 1),    // Nyårsdagen
            new(year, 1, 6),    // Trettondedag jul
            new(year, 5, 1),    // Första maj
            new(year, 6, 6),    // Nationaldagen
            new(year, 12, 24),  // Julafton
            new(year, 12, 25),  // Juldagen
            new(year, 12, 26),  // Annandag jul
            new(year, 12, 31),  // Nyårsafton

            // Rörliga helgdagar baserade på påsk
            paskdagen.AddDays(-2),   // Långfredagen
            paskdagen.AddDays(-1),   // Påskafton
            paskdagen,               // Påskdagen
            paskdagen.AddDays(1),    // Annandag påsk
            paskdagen.AddDays(39),   // Kristi himmelsfärdsdag
            paskdagen.AddDays(49),   // Pingstdagen
        };

        // Midsommarafton och midsommardagen
        var midsommardagen = BeraknaMidsommardagen(year);
        helgdagar.Add(midsommardagen.AddDays(-1));  // Midsommarafton
        helgdagar.Add(midsommardagen);               // Midsommardagen

        // Alla helgons dag (lördag mellan 31 okt och 6 nov)
        helgdagar.Add(BeraknaAllaHelgonsDag(year));

        helgdagar.Sort();
        return helgdagar.AsReadOnly();
    }

    /// <summary>
    /// Bestäm OB-kategori baserat på datum och tid enligt AB 25 § 21.
    /// Prioritet: storhelg (A) > helg (B) > vardagsnatt (C) > vardagskväll (D) > ingen.
    ///
    /// O-tilläggstider per AB 25 (Allmänna bestämmelser, i lydelse 2025-04-01):
    /// - A (storhelg): storhelgsperioderna inkl. kanterna (se <see cref="ArStorhelg(DateOnly, TimeOnly)"/>)
    /// - B (helg): helgdag/lördag/söndag hela dygnet, fredag från kl. 17:00 till 24:00
    ///   (före 2025-04-01: från kl. 19:00) samt måndag 00:00–07:00
    /// - C (vardagsnatt): övriga vardagar 22:00–06:00
    /// - D (vardagskväll): övriga vardagar 19:00–22:00
    /// </summary>
    public static OBCategory BeraknaOBKategori(DateOnly datum, TimeOnly tid)
    {
        // O-tilläggstid A (storhelg) har högst prioritet — tidsmedveten så att
        // kanterna (skärtorsdag 18:00, måndag efter midsommar till 07:00) träffas.
        if (ArStorhelg(datum, tid))
            return OBCategory.Storhelg;

        // O-tilläggstid B: helgdag (röd dag som inte är storhelg) eller lördag/söndag, hela dygnet
        if (ArHelgdag(datum) || datum.DayOfWeek == DayOfWeek.Saturday || datum.DayOfWeek == DayOfWeek.Sunday)
            return OBCategory.Helg;

        // O-tilläggstid B: fredag från kl. 17:00 (AB 25, fr.o.m. 2025-04-01; tidigare 19:00)
        // till 24:00 — helgtid, inte vardagskväll (D).
        if (datum.DayOfWeek == DayOfWeek.Friday)
        {
            var helgStart = datum >= new DateOnly(2025, 4, 1) ? new TimeOnly(17, 0) : new TimeOnly(19, 0);
            if (tid >= helgStart)
                return OBCategory.Helg;
        }

        // O-tilläggstid B: måndag 00:00–07:00 (helgtiden löper till kl. 07 på måndagen)
        if (datum.DayOfWeek == DayOfWeek.Monday && tid < new TimeOnly(7, 0))
            return OBCategory.Helg;

        // O-tilläggstid C: vardagsnatt 22:00–06:00
        if (tid >= new TimeOnly(22, 0) || tid < new TimeOnly(6, 0))
            return OBCategory.VardagNatt;

        // O-tilläggstid D: vardagskväll 19:00–22:00
        if (tid >= new TimeOnly(19, 0) && tid < new TimeOnly(22, 0))
            return OBCategory.VardagKvall;

        // Dagtid vardag: ingen OB
        return OBCategory.Ingen;
    }

    /// <summary>
    /// Beräknar påskdagen med Anonymous Gregorian-algoritmen.
    /// </summary>
    internal static DateOnly BeraknaPaskdagen(int year)
    {
        // Anonymous Gregorian algorithm (Meeus/Jones/Butcher)
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// Midsommardagen: lördagen mellan 20 och 26 juni.
    /// </summary>
    internal static DateOnly BeraknaMidsommardagen(int year)
    {
        var datum = new DateOnly(year, 6, 20);
        while (datum.DayOfWeek != DayOfWeek.Saturday)
            datum = datum.AddDays(1);
        return datum;
    }

    /// <summary>
    /// Alla helgons dag: lördagen mellan 31 oktober och 6 november.
    /// </summary>
    internal static DateOnly BeraknaAllaHelgonsDag(int year)
    {
        var datum = new DateOnly(year, 10, 31);
        while (datum.DayOfWeek != DayOfWeek.Saturday)
            datum = datum.AddDays(1);
        return datum;
    }
}
