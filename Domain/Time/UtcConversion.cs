// Ported from swisseph-master/swedate.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Time.Calendar;

namespace SharpAstrology.SwissEphemerides.Domain.Time;

/// <summary>
/// Result of <see cref="UtcConversion.UtcToJulianDay"/>: the same UTC instant
/// expressed as a JD in TT (ephemeris time) and as a JD in UT1.
/// </summary>
/// <remarks>
/// Mirrors the two-element <c>dret[]</c> output of <c>swe_utc_to_jd</c>
/// (<c>swedate.c#L361-L362</c>).
/// </remarks>
public readonly record struct JulianDayPair(JulianDay Et, JulianDay Ut1);

/// <summary>
/// A UTC clock-time, mirroring the integer/decimal-second tuple that the C
/// library passes through <c>swe_jdet_to_utc</c> /
/// <c>swe_jdut1_to_utc</c> / <c>swe_utc_time_zone</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="System.DateTime"/>, <see cref="Second"/> can legitimately
/// reach 60.0 (a leap-second insertion) and the date can carry a negative
/// year, so we keep it as a record struct rather than a <see cref="DateTime"/>.
/// </para>
/// </remarks>
public readonly record struct UtcDateTime(int Year, int Month, int Day, int Hour, int Minute, double Second);

/// <summary>
/// UTC ↔ Julian-Day conversions with leap-second handling. Faithful port of
/// <c>swe_utc_to_jd</c> (<c>swedate.c#L375-L470</c>),
/// <c>swe_jdet_to_utc</c> (<c>swedate.c#L486-L567</c>),
/// <c>swe_jdut1_to_utc</c> (<c>swedate.c#L583-L587</c>) and
/// <c>swe_utc_time_zone</c> (<c>swedate.c#L234-L267</c>).
/// </summary>
internal static class UtcConversion
{
    /// <summary>JD-UT of <c>1972-01-01 00:00:00 UTC</c>.</summary>
    public const double J1972 = LeapSeconds.J1972;

    /// <summary>Initial offset TAI−UTC at 1972-01-01.</summary>
    private const int NleapInit = LeapSeconds.NleapInit;

    private const double TaiTtSeconds = LeapSeconds.TaiTtOffsetSeconds;
    private const double SecondsPerDay = Constants.AstronomicalConstants.SecondsPerDay;

    /// <summary>
    /// Convert a UTC clock time to a (TT, UT1) Julian-Day pair. Mirrors
    /// <c>swe_utc_to_jd</c> (<c>swedate.c#L375-L470</c>).
    /// </summary>
    /// <param name="utc">UTC clock time. <c>Second</c> may be in [0, 61) and may equal 60 only on a valid leap-second day.</param>
    /// <param name="calendar">Julian or Gregorian calendar.</param>
    /// <param name="tidalAcceleration">Optional ΔT tidal-acceleration override.</param>
    /// <exception cref="ArgumentException">If the date is invalid or the time is out of range.</exception>
    public static JulianDayPair UtcToJulianDay(UtcDateTime utc, CalendarSystem calendar, double? tidalAcceleration = null)
    {
        var (year, month, day, hour, minute, dsec) = utc;

        // Validate calendar date (round-trip must reproduce the input).
        // swedate.c#L383-L389
        var tjdUt1 = JulianDayConversion.FromCalendarDate(year, month, day, 0.0, calendar).Value;
        var roundTrip = JulianDayConversion.ToCalendarDate(new JulianDay(tjdUt1), calendar);
        if (roundTrip.Year != year || roundTrip.Month != month || roundTrip.Day != day)
            throw new ArgumentException($"invalid date: year = {year}, month = {month}, day = {day}", nameof(utc));

        // Validate time. swedate.c#L390-L397.
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || dsec < 0 || dsec >= 61
            || (dsec >= 60 && (minute < 59 || hour < 23 || tjdUt1 < J1972)))
            throw new ArgumentException($"invalid time: {hour}:{minute}:{dsec}", nameof(utc));

        var dhour = hour + minute / 60.0 + dsec / 3600.0;

        // Pre-1972: treat input as UT1, no leap seconds. swedate.c#L399-L406.
        if (tjdUt1 < J1972)
        {
            var ut1Pre = JulianDayConversion.FromCalendarDate(year, month, day, dhour, calendar);
            var et = ut1Pre + DeltaT.InDays(ut1Pre, tidalAcceleration);
            return new JulianDayPair(et, ut1Pre);
        }

        // If Julian calendar, recompute (year,month,day) on Gregorian. swedate.c#L410-L413.
        if (calendar == CalendarSystem.Julian)
        {
            var d2 = JulianDayConversion.ToCalendarDate(new JulianDay(tjdUt1), CalendarSystem.Gregorian);
            year = d2.Year; month = d2.Month; day = d2.Day;
        }

        // Number of leap seconds since 1972. swedate.c#L417-L424.
        var ndat = year * 10000 + month * 100 + day;
        var nleap = NleapInit + LeapSeconds.CountBefore(ndat);

        // Outdated leap-second table check. swedate.c#L431-L436.
        var dt0Sec = DeltaT.InDays(new JulianDay(tjdUt1), tidalAcceleration) * SecondsPerDay;
        if (dt0Sec - nleap - TaiTtSeconds >= 1.0)
        {
            var ut1 = new JulianDay(tjdUt1 + dhour / 24.0);
            var et = ut1 + DeltaT.InDays(ut1, tidalAcceleration);
            return new JulianDayPair(et, ut1);
        }

        // Validate leap second.
        if (dsec >= 60.0 && !LeapSeconds.IsLeapSecondDay(ndat))
            throw new ArgumentException($"invalid time (no leap second!): {hour}:{minute}:{dsec}", nameof(utc));

        // Build TT (ET) directly from SI seconds since 1972. swedate.c#L457-L468.
        var d = tjdUt1 - J1972
                + hour / 24.0 + minute / 1440.0 + dsec / SecondsPerDay;
        var tjdEt1972 = J1972 + (TaiTtSeconds + NleapInit) / SecondsPerDay;
        var tjdEt = tjdEt1972 + d + (nleap - NleapInit) / SecondsPerDay;

        // Iterate ΔT to obtain UT1. swedate.c#L464-L466.
        var dDays = DeltaT.InDays(new JulianDay(tjdEt), tidalAcceleration);
        var ut1Iter = tjdEt - DeltaT.InDays(new JulianDay(tjdEt - dDays), tidalAcceleration);
        ut1Iter = tjdEt - DeltaT.InDays(new JulianDay(ut1Iter), tidalAcceleration);

        return new JulianDayPair(new JulianDay(tjdEt), new JulianDay(ut1Iter));
    }

    /// <summary>
    /// Convert a JD in TT (ephemeris time) to UTC clock time. Mirrors
    /// <c>swe_jdet_to_utc</c> (<c>swedate.c#L486-L567</c>).
    /// </summary>
    public static UtcDateTime JdEtToUtc(JulianDay jdEt, CalendarSystem calendar, double? tidalAcceleration = null)
    {
        var tjdEt = jdEt.Value;
        var tjdEt1972 = J1972 + (TaiTtSeconds + NleapInit) / SecondsPerDay;

        // Initial UT estimate via two ΔT iterations.
        var dDays = DeltaT.InDays(jdEt, tidalAcceleration);
        var tjdUt = tjdEt - DeltaT.InDays(new JulianDay(tjdEt - dDays), tidalAcceleration);
        tjdUt = tjdEt - DeltaT.InDays(new JulianDay(tjdUt), tidalAcceleration);

        // Pre-1972: return UT1. swedate.c#L499-L507.
        if (tjdEt < tjdEt1972)
        {
            var dPre = JulianDayConversion.ToCalendarDate(new JulianDay(tjdUt), calendar);
            return SplitHourFraction(dPre);
        }

        // Find tentative leap-second count. swedate.c#L512-L520.
        var dPrev = JulianDayConversion.ToCalendarDate(new JulianDay(tjdUt - 1), CalendarSystem.Gregorian);
        var ndat = dPrev.Year * 10000 + dPrev.Month * 100 + dPrev.Day;
        var nleap = LeapSeconds.CountBefore(ndat);

        var second60 = 0;
        if (nleap < LeapSeconds.Count)
        {
            // Date of potentially missing leap second. swedate.c#L522-L536.
            var nextDate = LeapSeconds.DateAt(nleap);
            var ny = nextDate / 10000;
            var nm = (nextDate % 10000) / 100;
            var nd = nextDate % 100;
            var tjd = JulianDayConversion.FromCalendarDate(ny, nm, nd, 0.0, CalendarSystem.Gregorian).Value;
            var dayAfter = JulianDayConversion.ToCalendarDate(new JulianDay(tjd + 1), CalendarSystem.Gregorian);
            // Recursively find ET for that midnight UTC.
            var dret = UtcToJulianDay(
                new UtcDateTime(dayAfter.Year, dayAfter.Month, dayAfter.Day, 0, 0, 0.0),
                CalendarSystem.Gregorian, tidalAcceleration);
            var diff = tjdEt - dret.Et.Value;
            if (diff >= 0)
                nleap++;
            else if (diff > -1.0 / SecondsPerDay)
                second60 = 1;
        }

        // UTC clock time, modulo one possible leap second. swedate.c#L540-L546.
        var tjdUtc = J1972 + (tjdEt - tjdEt1972) - (nleap + second60) / SecondsPerDay;
        var dCal = JulianDayConversion.ToCalendarDate(new JulianDay(tjdUtc), CalendarSystem.Gregorian);

        var year = dCal.Year;
        var month = dCal.Month;
        var day = dCal.Day;
        var ihour = (int)dCal.Hour;
        var minRem = (dCal.Hour - ihour) * 60.0;
        var imin = (int)minRem;
        var dsecOut = (minRem - imin) * 60.0 + second60;

        // Outdated-table fallback. swedate.c#L553-L562.
        var dCheck = DeltaT.InDays(jdEt, tidalAcceleration);
        dCheck = DeltaT.InDays(new JulianDay(tjdEt - dCheck), tidalAcceleration);
        if (dCheck * SecondsPerDay - (nleap + NleapInit) - TaiTtSeconds >= 1.0)
        {
            var dUt = JulianDayConversion.ToCalendarDate(new JulianDay(tjdEt - dCheck), CalendarSystem.Gregorian);
            year = dUt.Year; month = dUt.Month; day = dUt.Day;
            ihour = (int)dUt.Hour;
            var rem = (dUt.Hour - ihour) * 60.0;
            imin = (int)rem;
            dsecOut = (rem - imin) * 60.0;
        }

        // Convert back to Julian calendar if requested. swedate.c#L563-L566.
        if (calendar == CalendarSystem.Julian)
        {
            var tjd = JulianDayConversion.FromCalendarDate(year, month, day, 0.0, CalendarSystem.Gregorian);
            var dJul = JulianDayConversion.ToCalendarDate(tjd, CalendarSystem.Julian);
            year = dJul.Year; month = dJul.Month; day = dJul.Day;
        }

        return new UtcDateTime(year, month, day, ihour, imin, dsecOut);
    }

    /// <summary>
    /// Convert a JD in UT1 to UTC clock time. Mirrors <c>swe_jdut1_to_utc</c>
    /// (<c>swedate.c#L583-L587</c>).
    /// </summary>
    public static UtcDateTime JdUt1ToUtc(JulianDay jdUt, CalendarSystem calendar, double? tidalAcceleration = null)
    {
        var tjdEt = jdUt + DeltaT.InDays(jdUt, tidalAcceleration);
        return JdEtToUtc(tjdEt, calendar, tidalAcceleration);
    }

    /// <summary>
    /// Convert between local and UTC clock time, with no leap-second
    /// awareness. Mirrors <c>swe_utc_time_zone</c> (<c>swedate.c#L234-L267</c>).
    /// </summary>
    /// <param name="local">Local clock time.</param>
    /// <param name="timezoneHours">
    /// Time-zone offset. Positive eastward.
    /// For local→UTC use <c>+timezoneHours</c>. For UTC→local use <c>-timezoneHours</c>.
    /// </param>
    public static UtcDateTime ApplyTimezone(UtcDateTime local, double timezoneHours)
    {
        var (year, month, day, hour, minute, sec) = local;
        var haveLeap = false;
        if (sec >= 60.0)
        {
            haveLeap = true;
            sec -= 1.0;
        }

        var dhour = hour + minute / 60.0 + sec / 3600.0;
        var tjd = JulianDayConversion.FromCalendarDate(year, month, day, 0.0, CalendarSystem.Gregorian).Value;
        dhour -= timezoneHours;
        if (dhour < 0.0) { tjd -= 1.0; dhour += 24.0; }
        if (dhour >= 24.0) { tjd += 1.0; dhour -= 24.0; }

        // Add a small epsilon to avoid floor-rounding errors at midnight, exactly as the C does.
        var d2 = JulianDayConversion.ToCalendarDate(new JulianDay(tjd + 0.001), CalendarSystem.Gregorian);
        var ihourOut = (int)dhour;
        var rem = (dhour - ihourOut) * 60.0;
        var iminOut = (int)rem;
        var dsecOut = (rem - iminOut) * 60.0;
        if (haveLeap) dsecOut += 1.0;

        return new UtcDateTime(d2.Year, d2.Month, d2.Day, ihourOut, iminOut, dsecOut);
    }

    private static UtcDateTime SplitHourFraction(CalendarDate cd)
    {
        var ihour = (int)cd.Hour;
        var rem = (cd.Hour - ihour) * 60.0;
        var imin = (int)rem;
        var dsec = (rem - imin) * 60.0;
        return new UtcDateTime(cd.Year, cd.Month, cd.Day, ihour, imin, dsec);
    }
}
