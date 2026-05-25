// Ported from swisseph-master/swedate.c and swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Time;

/// <summary>
/// A Julian Day value carried as a single <see cref="double"/>. The library
/// follows the C convention of using the same numeric type for ET (TT) and UT
/// JDs; the time scale is communicated by parameter naming, not by type.
/// </summary>
/// <remarks>
/// Conversion to/from <see cref="System.DateTime"/> assumes UTC and the
/// proleptic Gregorian calendar unless otherwise specified.
/// </remarks>
public readonly record struct JulianDay(double Value)
{
    /// <summary>JD value of the J2000.0 epoch (TT).</summary>
    public static readonly JulianDay J2000 = new(Constants.AstronomicalConstants.J2000);

    public double Days => Value;

    public static JulianDay operator +(JulianDay jd, double days) => new(jd.Value + days);
    public static JulianDay operator -(JulianDay jd, double days) => new(jd.Value - days);
    public static double operator -(JulianDay a, JulianDay b) => a.Value - b.Value;

    public static JulianDay FromCalendarDate(int year, int month, int day, double hour, CalendarSystem calendar)
        => Calendar.JulianDayConversion.FromCalendarDate(year, month, day, hour, calendar);

    public CalendarDate ToCalendarDate(CalendarSystem calendar)
        => Calendar.JulianDayConversion.ToCalendarDate(this, calendar);

    /// <summary>
    /// Builds a JD from a UTC <see cref="System.DateTime"/>. Throws when
    /// <c>dateTime.Kind</c> is not <see cref="System.DateTimeKind.Utc"/>.
    /// </summary>
    public static JulianDay FromUtc(System.DateTime utc, CalendarSystem calendar = CalendarSystem.Gregorian)
    {
        if (utc.Kind != System.DateTimeKind.Utc)
            throw new System.ArgumentException("DateTime.Kind must be Utc.", nameof(utc));

        var hour = utc.Hour + utc.Minute / 60.0 + (utc.Second + utc.Millisecond / 1000.0) / 3600.0;
        return FromCalendarDate(utc.Year, utc.Month, utc.Day, hour, calendar);
    }

    /// <summary>Converts a JD back to a UTC <see cref="System.DateTime"/>.</summary>
    public System.DateTime ToUtc(CalendarSystem calendar = CalendarSystem.Gregorian)
    {
        var date = ToCalendarDate(calendar);
        return new System.DateTime(date.Year, date.Month, date.Day, 0, 0, 0, System.DateTimeKind.Utc)
            .AddMilliseconds(System.Math.Round(date.Hour * 3_600_000.0));
    }
}

/// <summary>Calendar date returned by <see cref="JulianDay.ToCalendarDate"/>.</summary>
/// <param name="Year">Astronomical year (year 0 = 1 BC, year -1 = 2 BC, …).</param>
/// <param name="Month">1–12.</param>
/// <param name="Day">1–31.</param>
/// <param name="Hour">Hour of day with decimal fraction, in [0, 24).</param>
public readonly record struct CalendarDate(int Year, int Month, int Day, double Hour);
