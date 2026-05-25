// Ported from swisseph-master/swedate.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   s_dates       — embedded leap-second table  (swedate.c#L274-L305, NLEAP_SECONDS = 27)
//   J1972         — JD-UT1 of 1972-01-01        (swedate.c#L306)
//   NleapInit     — NLEAP_INIT                  (swedate.c#L307)
//   CountBefore   — counting loop               (swedate.c#L420-L424)
//
// The C code keeps an extra 0 end-mark and a 100-slot allocation to allow
// runtime extension via seleapsec.txt. The runtime-extension behaviour is
// intentionally dropped here: per project rules the Domain layer must not
// perform I/O (ARCHITECTURE.md §6).

using System;

namespace SharpAstrology.SwissEphemerides.Domain.Time;

/// <summary>
/// Embedded leap-second table. Each entry is the date (encoded as <c>YYYYMMDD</c>)
/// on whose final UTC second a leap second was inserted.
/// </summary>
/// <remarks>
/// The first leap-second occurs on 30 Jun 1972 at 23:59:60 UTC (i.e. effective
/// 1 Jul 1972 00:00:00 UTC). Before 1972 the input times are treated as UT1 and
/// there are no leap seconds.
/// </remarks>
internal static class LeapSeconds
{
    /// <summary>JD-UT1 of <c>1972-01-01 00:00:00 UTC</c>.</summary>
    public const double J1972 = 2_441_317.5;

    /// <summary>Initial difference between TAI and UTC in 1972, in whole seconds.</summary>
    public const int NleapInit = 10;

    /// <summary>
    /// Seconds between TAI and TT (<c>32.184 s</c>). Used to derive
    /// TT-UTC = (TAI-UTC) + 32.184 s.
    /// </summary>
    public const double TaiTtOffsetSeconds = 32.184;

    /// <summary>
    /// Encoded leap-second dates. Each entry is <c>year * 10000 + month * 100 + day</c>
    /// of the day on which the leap second was appended at 23:59:60 UTC.
    /// </summary>
    private static readonly int[] s_dates =
    {
        19720630,
        19721231,
        19731231,
        19741231,
        19751231,
        19761231,
        19771231,
        19781231,
        19791231,
        19810630,
        19820630,
        19830630,
        19850630,
        19871231,
        19891231,
        19901231,
        19920630,
        19930630,
        19940630,
        19951231,
        19970630,
        19981231,
        20051231,
        20081231,
        20120630,
        20150630,
        20161231,
    };

    /// <summary>The full list of encoded leap-second dates as a read-only span.</summary>
    public static ReadOnlySpan<int> Dates => s_dates;

    /// <summary>Number of leap-second insertions in the embedded table.</summary>
    public static int Count => s_dates.Length;

    /// <summary>
    /// Returns the encoded leap-second date at <paramref name="index"/>.
    /// </summary>
    public static int DateAt(int index) => s_dates[index];

    /// <summary>
    /// Counts the number of leap seconds inserted before or on the given UTC date
    /// (encoded the same way: <c>year*10000+month*100+day</c>).
    /// </summary>
    public static int CountBefore(int encodedDate)
    {
        var n = 0;
        for (var i = 0; i < s_dates.Length; i++)
        {
            if (encodedDate <= s_dates[i])
                break;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Returns <c>true</c> iff the given UTC date appears in the leap-second
    /// table (i.e. the day legitimately ends with a 23:59:60 UTC second).
    /// </summary>
    public static bool IsLeapSecondDay(int encodedDate)
    {
        for (var i = 0; i < s_dates.Length; i++)
        {
            if (s_dates[i] == encodedDate) return true;
        }
        return false;
    }
}
