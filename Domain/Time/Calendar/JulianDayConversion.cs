// Ported from swisseph-master/swedate.c and swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Time.Calendar;

/// <summary>
/// Pure calendar ↔ Julian Day conversions. Faithful ports of <c>swe_julday</c>,
/// <c>swe_revjul</c>, and <c>swe_day_of_week</c> from the C library.
/// </summary>
internal static class JulianDayConversion
{
    /// <summary>
    /// Calendar date → Julian Day. Mirrors <c>swe_julday</c>
    /// (swisseph-master/swedate.c#L159-L179) including the bug fix for
    /// <c>year &lt; -4711</c> by Alois Treindl (15-aug-1988).
    /// </summary>
    public static JulianDay FromCalendarDate(int year, int month, int day, double hour, CalendarSystem calendar)
    {
        double u = year;
        if (month < 3) u -= 1.0;
        var u0 = u + 4712.0;
        var u1 = month + 1.0;
        if (u1 < 4.0) u1 += 12.0;
        var jd = System.Math.Floor(u0 * 365.25)
               + System.Math.Floor(30.6 * u1 + 0.000001)
               + day + hour / 24.0 - 63.5;

        if (calendar == CalendarSystem.Gregorian)
        {
            var u2 = System.Math.Floor(System.Math.Abs(u) / 100.0)
                   - System.Math.Floor(System.Math.Abs(u) / 400.0);
            if (u < 0.0) u2 = -u2;
            jd = jd - u2 + 2.0;
            if (u < 0.0
                && u / 100.0 == System.Math.Floor(u / 100.0)
                && u / 400.0 != System.Math.Floor(u / 400.0))
            {
                jd -= 1.0;
            }
        }

        return new JulianDay(jd);
    }

    /// <summary>
    /// Julian Day → calendar date. Mirrors <c>swe_revjul</c>
    /// (swisseph-master/swedate.c#L200-L218).
    /// </summary>
    public static CalendarDate ToCalendarDate(JulianDay jd, CalendarSystem calendar)
    {
        var u0 = jd.Value + 32082.5;
        if (calendar == CalendarSystem.Gregorian)
        {
            var u1 = u0 + System.Math.Floor(u0 / 36525.0) - System.Math.Floor(u0 / 146100.0) - 38.0;
            if (jd.Value >= 1830691.5) u1 += 1.0;
            u0 = u0 + System.Math.Floor(u1 / 36525.0) - System.Math.Floor(u1 / 146100.0) - 38.0;
        }

        var u2 = System.Math.Floor(u0 + 123.0);
        var u3 = System.Math.Floor((u2 - 122.2) / 365.25);
        var u4 = System.Math.Floor((u2 - System.Math.Floor(365.25 * u3)) / 30.6001);

        var month = (int)(u4 - 1.0);
        if (month > 12) month -= 12;
        var day = (int)(u2 - System.Math.Floor(365.25 * u3) - System.Math.Floor(30.6001 * u4));
        var year = (int)(u3 + System.Math.Floor((u4 - 2.0) / 12.0) - 4800);
        var hour = (jd.Value - System.Math.Floor(jd.Value + 0.5) + 0.5) * 24.0;

        return new CalendarDate(year, month, day, hour);
    }

    /// <summary>
    /// Day of the week, 0 = Monday … 6 = Sunday. Mirrors <c>swe_day_of_week</c>
    /// (swisseph-master/swephlib.c#L3859-L3862).
    /// </summary>
    public static int DayOfWeek(JulianDay jd)
        => (((int)System.Math.Floor(jd.Value - 2_433_282.0 - 1.5) % 7) + 7) % 7;

    /// <summary>
    /// Round-trips a calendar date through JD and verifies it equals the input.
    /// Mirrors <c>swe_date_conversion</c> (swisseph-master/swedate.c#L90-L111).
    /// Returns <c>true</c> if the input describes a valid civil date.
    /// </summary>
    public static bool TryConvertDate(int year, int month, int day, double utHour, CalendarSystem calendar, out JulianDay jd)
    {
        jd = FromCalendarDate(year, month, day, utHour, calendar);
        var roundTrip = ToCalendarDate(jd, calendar);
        return roundTrip.Year == year && roundTrip.Month == month && roundTrip.Day == day;
    }
}
