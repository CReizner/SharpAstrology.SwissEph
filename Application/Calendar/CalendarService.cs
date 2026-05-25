// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Time;
using SharpAstrology.SwissEphemerides.Domain.Time.Calendar;

namespace SharpAstrology.SwissEphemerides.Application.Calendar;

/// <summary>
/// Calendar / Julian-Day / time-scale conversions: UTC ↔ JD, Gregorian ↔
/// Julian, ΔT, sidereal time, and the Local-Mean-Time / Local-Apparent-
/// Time / equation-of-time helpers. Equivalent to the C library's
/// <c>swe_julday</c> / <c>swe_revjul</c> / <c>swe_deltat_ex</c> /
/// <c>swe_sidtime</c> / <c>swe_utc_to_jd</c> family. Time-scale operations
/// are stateless; Sun-dependent operations require a provider attached
/// during composition.
/// </summary>
/// <remarks>
/// <para>
/// Time-scale-only operations (<see cref="DeltaT(JulianDay, double?)"/>,
/// <see cref="SiderealTime(JulianDay, double?)"/>,
/// <see cref="UtcToJulianDayPair"/>, …) require no Sun-position support and
/// work without a provider.
/// </para>
/// <para>
/// <see cref="EquationOfTime"/>, <see cref="LmtToLat"/> and
/// <see cref="LatToLmt"/> need a Sun-position provider, which is wired
/// up by the composition root
/// (<see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder.Build"/>)
/// through an internal adapter over <c>BodyService</c>. The hookup lives
/// in the same assembly because the cycle-breaker
/// (<c>ISunPositionProvider</c>) is intentionally not part of the public
/// extension surface. Without a provider the three Sun-dependent methods
/// throw <see cref="NotSupportedException"/>.
/// </para>
/// </remarks>
public sealed class CalendarService
{
    private ISunPositionProvider? _sun;

    /// <summary>Constructs a calendar service that has no Sun-position provider; EquationOfTime / LMT↔LAT throw.</summary>
    public CalendarService()
    {
    }

    /// <summary>Constructs a calendar service backed by an explicit Sun-position provider. Used by the composition root.</summary>
    internal CalendarService(ISunPositionProvider sunProvider)
    {
        AttachSunPositionProvider(sunProvider);
    }

    /// <summary>
    /// Attaches the Sun-position provider needed by EquationOfTime / LMT-LAT
    /// helpers after the body service has been built. Internal because the
    /// provider abstraction is a composition-root concern, not a public
    /// extension point.
    /// </summary>
    internal void AttachSunPositionProvider(ISunPositionProvider sunProvider)
    {
        _sun = sunProvider ?? throw new ArgumentNullException(nameof(sunProvider));
    }

    /// <summary>
    /// Converts a UTC <see cref="DateTime"/> to a UT1 Julian Day.
    /// </summary>
    /// <param name="utc">A <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="calendar">Calendar to interpret the date in.</param>
    /// <returns>UT1 Julian Day.</returns>
    public JulianDay UtcToJulianDay(DateTime utc, CalendarSystem calendar = CalendarSystem.Gregorian)
        => JulianDay.FromUtc(utc, calendar);

    /// <summary>
    /// Inverse of <see cref="UtcToJulianDay"/>: a Julian Day back to a
    /// UTC <see cref="DateTime"/>.
    /// </summary>
    public DateTime JulianDayToUtc(JulianDay jd, CalendarSystem calendar = CalendarSystem.Gregorian)
        => jd.ToUtc(calendar);

    /// <summary>
    /// Builds a Julian Day from a calendar date plus decimal hour.
    /// Equivalent to <c>swe_julday</c>.
    /// </summary>
    /// <param name="hour">Hour of the day, expressed as a decimal (e.g. 12.5 = 12:30).</param>
    public JulianDay FromCalendarDate(int year, int month, int day, double hour, CalendarSystem calendar)
        => JulianDayConversion.FromCalendarDate(year, month, day, hour, calendar);

    /// <summary>
    /// Inverse of <see cref="FromCalendarDate"/>: extracts a
    /// year/month/day/hour tuple from a Julian Day.
    /// </summary>
    public CalendarDate ToCalendarDate(JulianDay jd, CalendarSystem calendar)
        => JulianDayConversion.ToCalendarDate(jd, calendar);

    /// <summary>Day of the week for a Julian Day. 0 = Monday … 6 = Sunday.</summary>
    public int DayOfWeek(JulianDay jd)
        => JulianDayConversion.DayOfWeek(jd);

    /// <summary>
    /// Validates a calendar date and converts it to a Julian Day in one
    /// step. Returns <see langword="false"/> for invalid dates rather
    /// than throwing.
    /// </summary>
    /// <param name="utHour">Hour of the day, decimal (UT).</param>
    /// <param name="jd">Receives the Julian Day on success.</param>
    public bool TryConvertDate(int year, int month, int day, double utHour, CalendarSystem calendar, out JulianDay jd)
        => JulianDayConversion.TryConvertDate(year, month, day, utHour, calendar, out jd);

    /// <summary>ΔT (TT − UT), in <b>days</b>.</summary>
    /// <param name="jdUt">Julian Day in UT.</param>
    /// <param name="tidalAcceleration">
    /// Optional override of the lunar tidal-acceleration value
    /// (arcsec/cty²). Pass <see langword="null"/> for the default
    /// model-tied value.
    /// </param>
    public double DeltaT(JulianDay jdUt, double? tidalAcceleration = null)
        => Domain.Time.DeltaT.InDays(jdUt, tidalAcceleration);

    /// <summary>ΔT (TT − UT), in <b>seconds</b>.</summary>
    public double DeltaTSeconds(JulianDay jdUt, double? tidalAcceleration = null)
        => Domain.Time.DeltaT.InSeconds(jdUt, tidalAcceleration);

    /// <summary>
    /// Greenwich <i>mean</i> sidereal time in hours — i.e. without
    /// nutation / equation-of-equinoxes. Use the four-argument overload
    /// to get apparent sidereal time when the true obliquity and
    /// nutation-in-longitude are available.
    /// </summary>
    public double SiderealTime(JulianDay jdUt, double? tidalAcceleration = null)
        => Domain.Time.SiderealTime.MeanGreenwichHours(jdUt, tidalAcceleration);

    /// <summary>
    /// Greenwich <i>apparent</i> sidereal time in hours, computed with the
    /// caller-supplied true obliquity and nutation in longitude.
    /// </summary>
    /// <param name="trueObliquityDegrees">True obliquity of the ecliptic.</param>
    /// <param name="nutationLongitudeDegrees">Nutation Δψ in degrees.</param>
    public double SiderealTime(JulianDay jdUt, double trueObliquityDegrees, double nutationLongitudeDegrees, double? tidalAcceleration = null)
        => Domain.Time.SiderealTime.Hours(jdUt, trueObliquityDegrees, nutationLongitudeDegrees, tidalAcceleration);

    /// <summary>
    /// Converts a UTC clock time to the (TT, UT1) Julian-Day pair,
    /// applying leap seconds where appropriate. Equivalent to
    /// <c>swe_utc_to_jd</c>.
    /// </summary>
    public JulianDayPair UtcToJulianDayPair(int year, int month, int day, int hour, int minute, double second,
        CalendarSystem calendar = CalendarSystem.Gregorian, double? tidalAcceleration = null)
        => UtcConversion.UtcToJulianDay(new UtcDateTime(year, month, day, hour, minute, second), calendar, tidalAcceleration);

    /// <summary>Converts a Julian Day in TT to a UTC clock time.</summary>
    public UtcDateTime JdEtToUtc(JulianDay jdEt, CalendarSystem calendar = CalendarSystem.Gregorian, double? tidalAcceleration = null)
        => UtcConversion.JdEtToUtc(jdEt, calendar, tidalAcceleration);

    /// <summary>Converts a Julian Day in UT1 to a UTC clock time.</summary>
    public UtcDateTime JdUt1ToUtc(JulianDay jdUt, CalendarSystem calendar = CalendarSystem.Gregorian, double? tidalAcceleration = null)
        => UtcConversion.JdUt1ToUtc(jdUt, calendar, tidalAcceleration);

    /// <summary>
    /// Shifts a clock time by a time-zone offset. Subtracts
    /// <paramref name="timezoneHours"/> from <paramref name="clockTime"/>.
    /// Positive offsets are east of UTC: pass <c>+1</c> to convert
    /// CET local → UTC, <c>-1</c> to convert UTC → CET local.
    /// Mirrors <c>swe_utc_time_zone</c> and is leap-second-unaware.
    /// </summary>
    /// <param name="clockTime">Calendar clock time to shift.</param>
    /// <param name="timezoneHours">Hours east of UTC.</param>
    public UtcDateTime ApplyTimezone(UtcDateTime clockTime, double timezoneHours)
        => UtcConversion.ApplyTimezone(clockTime, timezoneHours);

    /// <summary>
    /// Equation of time (LAT − LMT) at <paramref name="jdUt"/>, in days.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>swe_time_equ</c> (sweph.c#L7387-L7413). E = LAT − LMT in
    /// days. The Sun's apparent equatorial right ascension is taken from the
    /// provider; ΔT is added to <paramref name="jdUt"/> to obtain the TT
    /// epoch the provider expects.
    /// </remarks>
    public double EquationOfTime(JulianDay jdUt)
    {
        if (_sun is null) throw NoSun();
        var sidt = SiderealTime(jdUt); // hours, mean GMST
        var t = jdUt.Value + 0.5;
        var dtFrac = t - System.Math.Floor(t);
        sidt -= dtFrac * 24.0;
        sidt *= 15.0; // → degrees
        var jdEt = new JulianDay(jdUt.Value + DeltaT(jdUt));
        var raDeg = _sun.ApparentRightAscensionDegrees(jdEt);
        var dt = NormDeg(sidt - raDeg - 180.0);
        if (dt > 180.0) dt -= 360.0;
        dt *= 4.0;        // degrees → minutes
        return dt / 1440.0; // minutes → days
    }

    /// <summary>
    /// Converts a Julian Day in Local Mean Time to Local Apparent Time.
    /// </summary>
    public double LmtToLat(JulianDay jdLmt, double geographicLongitudeDegrees)
    {
        if (_sun is null) throw NoSun();
        var jdLmt0 = jdLmt.Value - geographicLongitudeDegrees / 360.0;
        var e = EquationOfTime(new JulianDay(jdLmt0));
        return jdLmt.Value + e;
    }

    /// <summary>
    /// Converts a Julian Day in Local Apparent Time to Local Mean Time.
    /// </summary>
    public double LatToLmt(JulianDay jdLat, double geographicLongitudeDegrees)
    {
        if (_sun is null) throw NoSun();
        var jdLmt0 = jdLat.Value - geographicLongitudeDegrees / 360.0;
        var e = EquationOfTime(new JulianDay(jdLmt0));
        // Iteration mirrors sweph.c#L7431-L7433.
        e = EquationOfTime(new JulianDay(jdLmt0 - e));
        e = EquationOfTime(new JulianDay(jdLmt0 - e));
        return jdLat.Value - e;
    }

    private static NotSupportedException NoSun() => new(
        "EquationOfTime / LMT↔LAT require a Sun-position provider; "
        + "build the CalendarService through EphemerisContextBuilder.Build() so the composition root wires one in.");

    private static double NormDeg(double deg)
    {
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }
}
