// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   DeltaTModel               — SEMOD_DELTAT_* macros               (swephexp.h#L600-L606)
//   InDays                    — swe_deltat_ex                       (swephlib.c#L2701-L2710)
//   calc_deltat top-level     — calc_deltat                          (swephlib.c#L2545-L2699)
//   DeltaT_aa                 — deltat_aa                            (swephlib.c#L2733-L2839)
//   LongTermMorrisonStephenson — deltat_longterm_morrison_stephenson (swephlib.c#L2841-L2846)
//   Stephenson1997Below1600   — deltat_stephenson_morrison_1997_1600 (swephlib.c#L2848-L2887)
//   StephensonMorrison2004Below1600 — deltat_stephenson_morrison_2004_1600 (swephlib.c#L2890-L2933)
//   StephensonEtc2016Spline   — deltat_stephenson_etc_2016           (swephlib.c#L3001-L3036)
//   EspenakMeeus1620          — deltat_espenak_meeus_1620            (swephlib.c#L3038-L3084)
//   AdjustForTidacc           — adjust_for_tidacc                    (swephlib.c#L3143-L3151)
//   s_dt (AA table)           — swephlib.c#L2431-L2495
//   StephensonEtc2016 spline rows — swephlib.c#L2944-L2999
//   Tidal26 / TidalStephenson2016 / TidalDefault — SE_TIDAL_26 / SE_TIDAL_STEPHENSON_2016 /
//                                                  SE_TIDAL_DE431 = SE_TIDAL_DEFAULT
//                                                  (swephexp.h#L478-L490)
//
// The C library carries the tidal acceleration of the Moon as a mutable global
// (swed.tid_acc) overridable via swe_set_tid_acc(). Per the architectural rule
// "no mutable static state" it is exposed here as an explicit parameter on
// every call.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Time;

/// <summary>
/// Selects which polynomial / table family to use for ΔT.
/// </summary>
internal enum DeltaTModel
{
    /// <summary>Default — Stephenson, Morrison and Hohenkerk 2016 spline + Astronomical-Almanac table.</summary>
    StephensonEtc2016 = 5,

    /// <summary>Espenak &amp; Meeus 2006 polynomials before 1633.</summary>
    EspenakMeeus2006 = 4,

    /// <summary>Stephenson &amp; Morrison 2004 plus AA table.</summary>
    StephensonMorrison2004 = 3,

    /// <summary>Stephenson 1997 table plus AA table.</summary>
    Stephenson1997 = 2,

    /// <summary>Stephenson &amp; Morrison 1984 with Borkowski 1988 plus AA table.</summary>
    StephensonMorrison1984 = 1,
}

/// <summary>
/// Pure ΔT (TT − UT) calculation. All routines return ΔT in days; the
/// convenience overload <see cref="InSeconds(JulianDay, double, DeltaTModel)"/>
/// returns the value multiplied by 86 400. The Moon's tidal acceleration is
/// passed explicitly per call (default: <see cref="AstronomicalConstants.TidalAccelerationDefault"/>).
/// </summary>
internal static class DeltaT
{
    private const double SecondsPerDay = AstronomicalConstants.SecondsPerDay;
    private const double J2000 = AstronomicalConstants.J2000;

    /// <summary>−26 arcsec/cty², the reference value of the AA table.</summary>
    private const double Tidal26 = -26.0;

    /// <summary>−25.85 arcsec/cty² (Stephenson 2016).</summary>
    private const double TidalStephenson2016 = -25.85;

    /// <summary>Default tidal acceleration of the Moon (DE431).</summary>
    private const double TidalDefault = AstronomicalConstants.TidalAccelerationDefault;

    private const int TabStart = 1620;
    private const int TabEnd = 2028;

    /// <summary>Astronomical-Almanac ΔT table in seconds, one entry per Julian year.</summary>
    private static readonly double[] s_dt =
    {
        // 1620.0 - 1659.0
        124.00, 119.00, 115.00, 110.00, 106.00, 102.00, 98.00, 95.00, 91.00, 88.00,
        85.00, 82.00, 79.00, 77.00, 74.00, 72.00, 70.00, 67.00, 65.00, 63.00,
        62.00, 60.00, 58.00, 57.00, 55.00, 54.00, 53.00, 51.00, 50.00, 49.00,
        48.00, 47.00, 46.00, 45.00, 44.00, 43.00, 42.00, 41.00, 40.00, 38.00,
        // 1660.0 - 1699.0
        37.00, 36.00, 35.00, 34.00, 33.00, 32.00, 31.00, 30.00, 28.00, 27.00,
        26.00, 25.00, 24.00, 23.00, 22.00, 21.00, 20.00, 19.00, 18.00, 17.00,
        16.00, 15.00, 14.00, 14.00, 13.00, 12.00, 12.00, 11.00, 11.00, 10.00,
        10.00, 10.00, 9.00, 9.00, 9.00, 9.00, 9.00, 9.00, 9.00, 9.00,
        // 1700.0 - 1739.0
        9.00, 9.00, 9.00, 9.00, 9.00, 9.00, 9.00, 9.00, 10.00, 10.00,
        10.00, 10.00, 10.00, 10.00, 10.00, 10.00, 10.00, 11.00, 11.00, 11.00,
        11.00, 11.00, 11.00, 11.00, 11.00, 11.00, 11.00, 11.00, 11.00, 11.00,
        11.00, 11.00, 11.00, 11.00, 12.00, 12.00, 12.00, 12.00, 12.00, 12.00,
        // 1740.0 - 1779.0
        12.00, 12.00, 12.00, 12.00, 13.00, 13.00, 13.00, 13.00, 13.00, 13.00,
        13.00, 14.00, 14.00, 14.00, 14.00, 14.00, 14.00, 14.00, 15.00, 15.00,
        15.00, 15.00, 15.00, 15.00, 15.00, 16.00, 16.00, 16.00, 16.00, 16.00,
        16.00, 16.00, 16.00, 16.00, 16.00, 17.00, 17.00, 17.00, 17.00, 17.00,
        // 1780.0 - 1799.0
        17.00, 17.00, 17.00, 17.00, 17.00, 17.00, 17.00, 17.00, 17.00, 17.00,
        17.00, 17.00, 16.00, 16.00, 16.00, 16.00, 15.00, 15.00, 14.00, 14.00,
        // 1800.0 - 1819.0
        13.70, 13.40, 13.10, 12.90, 12.70, 12.60, 12.50, 12.50, 12.50, 12.50,
        12.50, 12.50, 12.50, 12.50, 12.50, 12.50, 12.50, 12.40, 12.30, 12.20,
        // 1820.0 - 1859.0
        12.00, 11.70, 11.40, 11.10, 10.60, 10.20, 9.60, 9.10, 8.60, 8.00,
        7.50, 7.00, 6.60, 6.30, 6.00, 5.80, 5.70, 5.60, 5.60, 5.60,
        5.70, 5.80, 5.90, 6.10, 6.20, 6.30, 6.50, 6.60, 6.80, 6.90,
        7.10, 7.20, 7.30, 7.40, 7.50, 7.60, 7.70, 7.70, 7.80, 7.80,
        // 1860.0 - 1899.0
        7.88, 7.82, 7.54, 6.97, 6.40, 6.02, 5.41, 4.10, 2.92, 1.82,
        1.61, .10, -1.02, -1.28, -2.69, -3.24, -3.64, -4.54, -4.71, -5.11,
        -5.40, -5.42, -5.20, -5.46, -5.46, -5.79, -5.63, -5.64, -5.80, -5.66,
        -5.87, -6.01, -6.19, -6.64, -6.44, -6.47, -6.09, -5.76, -4.66, -3.74,
        // 1900.0 - 1939.0
        -2.72, -1.54, -.02, 1.24, 2.64, 3.86, 5.37, 6.14, 7.75, 9.13,
        10.46, 11.53, 13.36, 14.65, 16.01, 17.20, 18.24, 19.06, 20.25, 20.95,
        21.16, 22.25, 22.41, 23.03, 23.49, 23.62, 23.86, 24.49, 24.34, 24.08,
        24.02, 24.00, 23.87, 23.95, 23.86, 23.93, 23.73, 23.92, 23.96, 24.02,
        // 1940.0 - 1949.0
        24.33, 24.83, 25.30, 25.70, 26.24, 26.77, 27.28, 27.78, 28.25, 28.71,
        // 1950.0 - 1959.0
        29.15, 29.57, 29.97, 30.36, 30.72, 31.07, 31.35, 31.68, 32.18, 32.68,
        // 1960.0 - 1969.0
        33.15, 33.59, 34.00, 34.47, 35.03, 35.73, 36.54, 37.43, 38.29, 39.20,
        // 1970.0 - 1979.0 — from 1974 on (4-digit precision) calculated from IERS data
        40.18, 41.17, 42.23, 43.37, 44.4841, 45.4761, 46.4567, 47.5214, 48.5344, 49.5862,
        // 1980.0 - 1989.0
        50.5387, 51.3808, 52.1668, 52.9565, 53.7882, 54.3427, 54.8713, 55.3222, 55.8197, 56.3000,
        // 1990.0 - 1999.0
        56.8553, 57.5653, 58.3092, 59.1218, 59.9845, 60.7854, 61.6287, 62.2951, 62.9659, 63.4673,
        // 2000.0 - 2009.0
        63.8285, 64.0908, 64.2998, 64.4734, 64.5736, 64.6876, 64.8452, 65.1464, 65.4574, 65.7768,
        // 2010.0 - 2018.0
        66.0699, 66.3246, 66.6030, 66.9069, 67.2810, 67.6439, 68.1024, 68.5927, 68.9676, 69.2202,
        // 2020.0 - 2023.0
        69.3612, 69.3593, 69.2945, 69.1833,
        // Extrapolated values 2024 - 2028
        69.10, 69.00, 68.90, 68.80, 68.80,
    };

    // -------- Stephenson & Morrison 2004 table (swephlib.c#L2497-L2511) --------

    private const int Tab2Start = -1000;
    private const int Tab2End = 1600;
    private const int Tab2Step = 100;

    private static readonly short[] s_dt2 =
    {
        // -1000 .. -100
        25400, 23700, 22000, 21000, 19040, 17190, 15530, 14080, 12790, 11640,
        // 0 .. 900
        10580, 9600, 8640, 7680, 6700, 5710, 4740, 3810, 2960, 2200,
        // 1000 .. 1600
        1570, 1090, 740, 490, 320, 200, 120,
    };

    // -------- Stephenson 1997 table (swephlib.c#L2519-L2534) --------

    private const int Tab97Start = -500;
    private const int Tab97End = 1600;
    private const int Tab97Step = 50;

    private static readonly short[] s_dt97 =
    {
        // -500 .. -50
        16800, 16000, 15300, 14600, 14000, 13400, 12800, 12200, 11600, 11100,
        // 0 .. 450
        10600, 10100, 9600, 9100, 8600, 8200, 7700, 7200, 6700, 6200,
        // 500 .. 950
        5700, 5200, 4700, 4300, 3800, 3400, 3000, 2600, 2200, 1900,
        // 1000 .. 1450
        1600, 1350, 1100, 900, 750, 600, 470, 380, 300, 230,
        // 1500 .. 1600
        180, 140, 110,
    };

    /// <summary>One row of the Stephenson-2016 cubic-spline table.</summary>
    /// <param name="JdBegin">JD at start of segment.</param>
    /// <param name="JdEnd">JD at end of segment.</param>
    /// <param name="C0">Constant coefficient.</param>
    /// <param name="C1">Linear coefficient.</param>
    /// <param name="C2">Quadratic coefficient.</param>
    /// <param name="C3">Cubic coefficient.</param>
    private readonly record struct Dtcf16Row(double JdBegin, double JdEnd, double C0, double C1, double C2, double C3);

    private static readonly Dtcf16Row[] s_dtcf16 =
    {
        new(1458085.5, 1867156.5, 20550.593, -21268.478, 11863.418, -4541.129),
        new(1867156.5, 2086302.5,  6604.404,  -5981.266,  -505.093,  1349.609),
        new(2086302.5, 2268923.5,  1467.654,  -2452.187,  2460.927, -1183.759),
        new(2268923.5, 2305447.5,   292.635,   -216.322,   -43.614,    56.681),
        new(2305447.5, 2323710.5,    89.380,    -66.754,    31.607,   -10.497),
        new(2323710.5, 2349276.5,    43.736,    -49.043,     0.227,    15.811),
        new(2349276.5, 2378496.5,    10.730,     -1.321,    62.250,   -52.946),
        new(2378496.5, 2382148.5,    18.714,     -4.457,    -1.509,     2.507),
        new(2382148.5, 2385800.5,    15.255,      0.046,     6.012,    -4.634),
        new(2385800.5, 2389453.5,    16.679,     -1.831,    -7.889,     3.799),
        new(2389453.5, 2393105.5,    10.758,     -6.211,     3.509,    -0.388),
        new(2393105.5, 2396758.5,     7.668,     -0.357,     2.345,    -0.338),
        new(2396758.5, 2398584.5,     9.317,      1.659,     0.332,    -0.932),
        new(2398584.5, 2400410.5,    10.376,     -0.472,    -2.463,     1.596),
        new(2400410.5, 2402237.5,     9.038,     -0.610,     2.325,    -2.497),
        new(2402237.5, 2404063.5,     8.256,     -3.450,    -5.166,     2.729),
        new(2404063.5, 2405889.5,     2.369,     -5.596,     3.020,    -0.919),
        new(2405889.5, 2407715.5,    -1.126,     -2.312,     0.264,    -0.037),
        new(2407715.5, 2409542.5,    -3.211,     -1.894,     0.154,     0.562),
        new(2409542.5, 2411368.5,    -4.388,      0.101,     1.841,    -1.438),
        new(2411368.5, 2413194.5,    -3.884,     -0.531,    -2.473,     1.870),
        new(2413194.5, 2415020.5,    -5.017,      0.134,     3.138,    -0.232),
        new(2415020.5, 2416846.5,    -1.977,      5.715,     2.443,    -1.257),
        new(2416846.5, 2418672.5,     4.923,      6.828,    -1.329,     0.720),
        new(2418672.5, 2420498.5,    11.142,      6.330,     0.831,    -0.825),
        new(2420498.5, 2422324.5,    17.479,      5.518,    -1.643,     0.262),
        new(2422324.5, 2424151.5,    21.617,      3.020,    -0.856,     0.008),
        new(2424151.5, 2425977.5,    23.789,      1.333,    -0.831,     0.127),
        new(2425977.5, 2427803.5,    24.418,      0.052,    -0.449,     0.142),
        new(2427803.5, 2429629.5,    24.164,     -0.419,    -0.022,     0.702),
        new(2429629.5, 2431456.5,    24.426,      1.645,     2.086,    -1.106),
        new(2431456.5, 2433282.5,    27.050,      2.499,    -1.232,     0.614),
        new(2433282.5, 2434378.5,    28.932,      1.127,     0.220,    -0.277),
        new(2434378.5, 2435473.5,    30.002,      0.737,    -0.610,     0.631),
        new(2435473.5, 2436569.5,    30.760,      1.409,     1.282,    -0.799),
        new(2436569.5, 2437665.5,    32.652,      1.577,    -1.115,     0.507),
        new(2437665.5, 2438761.5,    33.621,      0.868,     0.406,     0.199),
        new(2438761.5, 2439856.5,    35.093,      2.275,     1.002,    -0.414),
        new(2439856.5, 2440952.5,    37.956,      3.035,    -0.242,     0.202),
        new(2440952.5, 2442048.5,    40.951,      3.157,     0.364,    -0.229),
        new(2442048.5, 2443144.5,    44.244,      3.198,    -0.323,     0.172),
        new(2443144.5, 2444239.5,    47.291,      3.069,     0.193,    -0.192),
        new(2444239.5, 2445335.5,    50.361,      2.878,    -0.384,     0.081),
        new(2445335.5, 2446431.5,    52.936,      2.354,    -0.140,    -0.166),
        new(2446431.5, 2447527.5,    54.984,      1.577,    -0.637,     0.448),
        new(2447527.5, 2448622.5,    56.373,      1.649,     0.709,    -0.277),
        new(2448622.5, 2449718.5,    58.453,      2.235,    -0.122,     0.111),
        new(2449718.5, 2450814.5,    60.677,      2.324,     0.212,    -0.315),
        new(2450814.5, 2451910.5,    62.899,      1.804,    -0.732,     0.112),
        new(2451910.5, 2453005.5,    64.082,      0.675,    -0.396,     0.193),
        new(2453005.5, 2454101.5,    64.555,      0.463,     0.184,    -0.008),
        new(2454101.5, 2455197.5,    65.194,      0.809,     0.161,    -0.101),
        new(2455197.5, 2456293.5,    66.063,      0.828,    -0.142,     0.168),
        new(2456293.5, 2457388.5,    66.917,      1.046,     0.360,    -0.282),
    };

    /// <summary>
    /// ΔT in <b>days</b> (TT − UT).
    /// </summary>
    /// <param name="jd">Julian Day in UT.</param>
    /// <param name="tidalAcceleration">
    /// Optional override of the lunar tidal acceleration ndot (arcsec/cty²).
    /// Pass <c>null</c> to use <see cref="AstronomicalConstants.TidalAccelerationDefault"/>.
    /// </param>
    /// <param name="model">ΔT model selector. Defaults to <see cref="DeltaTModel.StephensonEtc2016"/>.</param>
    public static double InDays(JulianDay jd, double? tidalAcceleration = null, DeltaTModel model = DeltaTModel.StephensonEtc2016)
    {
        var tidAcc = tidalAcceleration ?? TidalDefault;
        return ComputeInDays(jd.Value, tidAcc, model);
    }

    /// <summary>ΔT in <b>seconds</b> — convenience wrapper around <see cref="InDays"/>.</summary>
    public static double InSeconds(JulianDay jd, double? tidalAcceleration = null, DeltaTModel model = DeltaTModel.StephensonEtc2016)
        => InDays(jd, tidalAcceleration, model) * SecondsPerDay;

    private static double ComputeInDays(double tjd, double tidAcc, DeltaTModel deltatModel)
    {
        var Y = 2000.0 + (tjd - J2000) / 365.25;
        var Ygreg = 2000.0 + (tjd - J2000) / 365.2425;

        // (1) Default model — Stephenson, Morrison, Hohenkerk 2016 + linear blend before AA-table.
        // swephlib.c#L2589-L2595
        if (deltatModel == DeltaTModel.StephensonEtc2016 && tjd < 2435108.5)
        {
            var dt = DeltatStephensonEtc2016(tjd, tidAcc);
            if (tjd >= 2434108.5)
            {
                dt += (1.0 - (2435108.5 - tjd) / 1000.0) * 0.6610218 / SecondsPerDay;
            }
            return dt;
        }

        // (2) Espenak & Meeus 2006 polynomials — only used before 1633.
        // swephlib.c#L2603-L2606
        if (deltatModel == DeltaTModel.EspenakMeeus2006 && tjd < 2317746.13090277789)
        {
            return DeltatEspenakMeeus1620(tjd, tidAcc);
        }

        // (3) Stephenson & Morrison 2004 — before TABSTART.
        // swephlib.c#L2610-L2629
        if (deltatModel == DeltaTModel.StephensonMorrison2004 && Y < TabStart)
        {
            if (Y < Tab2End)
                return DeltatStephensonMorrison2004(tjd, tidAcc);

            // 1600-1620 linear blend between dt2 and AA table.
            var B = (double)(TabStart - Tab2End);
            var iy = (Tab2End - Tab2Start) / Tab2Step;
            var dd = (Y - Tab2End) / B;
            var ans = s_dt2[iy] + dd * (s_dt[0] - s_dt2[iy]);
            ans = AdjustForTidacc(ans, Ygreg, tidAcc, Tidal26, false);
            return ans / SecondsPerDay;
        }

        // (4) Stephenson 1997 — before TABSTART.
        // swephlib.c#L2633-L2652
        if (deltatModel == DeltaTModel.Stephenson1997 && Y < TabStart)
        {
            if (Y < Tab97End)
                return DeltatStephensonMorrison1997(tjd, tidAcc);

            var B = (double)(TabStart - Tab97End);
            var iy = (Tab97End - Tab97Start) / Tab97Step;
            var dd = (Y - Tab97End) / B;
            var ans = s_dt97[iy] + dd * (s_dt[0] - s_dt97[iy]);
            ans = AdjustForTidacc(ans, Ygreg, tidAcc, Tidal26, false);
            return ans / SecondsPerDay;
        }

        // (5) Stephenson & Morrison 1984 / Borkowski 1988 — before TABSTART.
        // swephlib.c#L2656-L2668
        if (deltatModel == DeltaTModel.StephensonMorrison1984 && Y < TabStart)
        {
            double ans, B;
            if (Y >= 948.0)
            {
                B = 0.01 * (Y - 2000.0);
                ans = (23.58 * B + 100.3) * B + 101.6;
            }
            else
            {
                B = 0.01 * (Y - 2000.0) + 3.75;
                ans = 35.0 * B * B + 40.0;
            }
            return ans / SecondsPerDay;
        }

        // (6) From 1620 onwards: AA / IERS table interpolation. swephlib.c#L2675-L2678
        if (Y >= TabStart)
            return DeltatAa(tjd, tidAcc, deltatModel);

        // For models that do not match any branch (e.g. an old model and
        // we fell through). The C library returns 0 / 86400 here. We follow.
        return 0.0;
    }

    /// <summary>
    /// Bessel-style 4th-order interpolation through the AA / IERS table
    /// (1620 onwards), with a smooth blend to a long-term parabola for extrapolation.
    /// </summary>
    private static double DeltatAa(double tjd, double tidAcc, DeltaTModel deltatModel)
    {
        var tabsiz = s_dt.Length;
        var tabend = TabStart + tabsiz - 1;
        var Y = 2000.0 + (tjd - 2451544.5) / 365.25;

        double ans = 0;

        if (Y <= tabend)
        {
            // Bessel interpolation, see swephlib.c#L2745-L2799.
            var p = Math.Floor(Y);
            var iy = (int)(p - TabStart);
            // Zeroth order
            ans = s_dt[iy];
            var k = iy + 1;
            if (k >= tabsiz)
                return AdjustForTidacc(ans, Y, tidAcc, Tidal26, false) / SecondsPerDay;

            p = Y - p;
            ans += p * (s_dt[k] - s_dt[iy]);
            if (iy - 1 < 0 || iy + 2 >= tabsiz)
                return AdjustForTidacc(ans, Y, tidAcc, Tidal26, false) / SecondsPerDay;

            Span<double> d = stackalloc double[6];
            k = iy - 2;
            for (var i = 0; i < 5; i++)
            {
                if (k < 0 || k + 1 >= tabsiz)
                    d[i] = 0;
                else
                    d[i] = s_dt[k + 1] - s_dt[k];
                k += 1;
            }
            for (var i = 0; i < 4; i++)
                d[i] = d[i + 1] - d[i];

            var B = 0.25 * p * (p - 1.0);
            ans += B * (d[1] + d[2]);

            if (iy + 2 >= tabsiz)
                return AdjustForTidacc(ans, Y, tidAcc, Tidal26, false) / SecondsPerDay;

            for (var i = 0; i < 3; i++)
                d[i] = d[i + 1] - d[i];
            B = 2.0 * B / 3.0;
            ans += (p - 0.5) * B * d[1];

            if (iy - 2 < 0 || iy + 3 > tabsiz)
                return AdjustForTidacc(ans, Y, tidAcc, Tidal26, false) / SecondsPerDay;

            for (var i = 0; i < 2; i++)
                d[i] = d[i + 1] - d[i];
            B = 0.125 * B * (p + 1.0) * (p - 2.0);
            ans += B * (d[0] + d[1]);

            return AdjustForTidacc(ans, Y, tidAcc, Tidal26, false) / SecondsPerDay;
        }

        // After the table: third-order polynomial (Stephenson 2016) blended with
        // tabulated values across the next 100 years.
        double ans2 = 0;
        double Bb;

        if (deltatModel == DeltaTModel.StephensonEtc2016)
        {
            Bb = Y - 2000;
            if (Y < 2500)
            {
                ans = Bb * Bb * Bb * 121.0 / 30000000.0 + Bb * Bb / 1250.0 + Bb * 521.0 / 3000.0 + 64.0;
                var B2 = (double)(tabend - 2000);
                ans2 = B2 * B2 * B2 * 121.0 / 30000000.0 + B2 * B2 / 1250.0 + B2 * 521.0 / 3000.0 + 64.0;
            }
            else
            {
                Bb = 0.01 * (Y - 2000);
                ans = Bb * Bb * 32.5 + 42.5;
            }
        }
        else
        {
            Bb = 0.01 * (Y - 1820);
            ans = -20 + 31 * Bb * Bb;
            var B2 = 0.01 * (tabend - 1820);
            ans2 = -20 + 31 * B2 * B2;
        }

        if (Y <= tabend + 100)
        {
            var ans3 = s_dt[tabsiz - 1];
            var dd = ans2 - ans3;
            ans += dd * (Y - (tabend + 100)) * 0.01;
        }

        return ans / SecondsPerDay;
    }

    /// <summary>
    /// Long-term Morrison &amp; Stephenson parabola.
    /// </summary>
    private static double DeltatLongtermMorrisonStephenson(double tjd)
    {
        var Ygreg = 2000.0 + (tjd - J2000) / 365.2425;
        var u = (Ygreg - 1820) / 100.0;
        return -20 + 32 * u * u;
    }

    /// <summary>Stephenson (1997) ΔT model for years before 1600.</summary>
    private static double DeltatStephensonMorrison1997(double tjd, double tidAcc)
    {
        double ans = 0, ans2, ans3;
        var Y = 2000.0 + (tjd - J2000) / 365.25;

        if (Y < Tab97Start)
        {
            var B = (Y - 1735) * 0.01;
            ans = -20 + 35 * B * B;
            ans = AdjustForTidacc(ans, Y, tidAcc, Tidal26, false);
            if (Y >= Tab97Start - 100)
            {
                ans2 = AdjustForTidacc(s_dt97[0], Tab97Start, tidAcc, Tidal26, false);
                B = (Tab97Start - 1735) * 0.01;
                ans3 = -20 + 35 * B * B;
                ans3 = AdjustForTidacc(ans3, Y, tidAcc, Tidal26, false);
                var dd = ans3 - ans2;
                B = (Y - (Tab97Start - 100)) * 0.01;
                ans = ans - dd * B;
            }
        }

        if (Y >= Tab97Start && Y < Tab2End)
        {
            var p = Math.Floor(Y);
            var iy = (int)((p - Tab97Start) / 50.0);
            var dd = (Y - (Tab97Start + 50 * iy)) / 50.0;
            ans = s_dt97[iy] + (s_dt97[iy + 1] - s_dt97[iy]) * dd;
            ans = AdjustForTidacc(ans, Y, tidAcc, Tidal26, false);
        }

        return ans / SecondsPerDay;
    }

    /// <summary>Stephenson &amp; Morrison (2004) ΔT model for years before 1600.</summary>
    private static double DeltatStephensonMorrison2004(double tjd, double tidAcc)
    {
        double ans = 0, ans2, ans3;
        var Y = 2000.0 + (tjd - J2000) / 365.2425;

        if (Y < Tab2Start)
        {
            ans = DeltatLongtermMorrisonStephenson(tjd);
            ans = AdjustForTidacc(ans, Y, tidAcc, Tidal26, false);
            if (Y >= Tab2Start - 100)
            {
                ans2 = AdjustForTidacc(s_dt2[0], Tab2Start, tidAcc, Tidal26, false);
                var tjd0 = (Tab2Start - 2000) * 365.2425 + J2000;
                ans3 = DeltatLongtermMorrisonStephenson(tjd0);
                ans3 = AdjustForTidacc(ans3, Y, tidAcc, Tidal26, false);
                var dd = ans3 - ans2;
                var B = (Y - (Tab2Start - 100)) * 0.01;
                ans = ans - dd * B;
            }
        }

        if (Y >= Tab2Start && Y < Tab2End)
        {
            var Yjul = 2000 + (tjd - 2451557.5) / 365.25;
            var p = Math.Floor(Yjul);
            var iy = (int)((p - Tab2Start) / Tab2Step);
            var dd = (Yjul - (Tab2Start + Tab2Step * iy)) / Tab2Step;
            ans = s_dt2[iy] + (s_dt2[iy + 1] - s_dt2[iy]) * dd;
            ans = AdjustForTidacc(ans, Y, tidAcc, Tidal26, false);
        }

        return ans / SecondsPerDay;
    }

    /// <summary>Stephenson, Morrison &amp; Hohenkerk (2016) cubic-spline ΔT.</summary>
    private static double DeltatStephensonEtc2016(double tjd, double tidAcc)
    {
        var Ygreg = 2000.0 + (tjd - J2000) / 365.2425;

        var irec = -1;
        for (var i = 0; i < s_dtcf16.Length; i++)
        {
            if (tjd < s_dtcf16[i].JdBegin) break;
            if (tjd < s_dtcf16[i].JdEnd) { irec = i; break; }
        }

        double t, dt;
        if (irec >= 0)
        {
            var row = s_dtcf16[irec];
            t = (tjd - row.JdBegin) / (row.JdEnd - row.JdBegin);
            dt = row.C0 + row.C1 * t + row.C2 * t * t + row.C3 * t * t * t;
        }
        else if (Ygreg < -720)
        {
            t = (Ygreg - 1825) / 100.0;
            dt = -320 + 32.5 * t * t;
            dt -= 179.7337208;
        }
        else
        {
            t = (Ygreg - 1825) / 100.0;
            dt = -320 + 32.5 * t * t;
            dt += 269.4790417;
        }

        // adjust_after_1955 == TRUE because Stephenson 2016 is occultation-based.
        dt = AdjustForTidacc(dt, Ygreg, tidAcc, TidalStephenson2016, true);
        return dt / SecondsPerDay;
    }

    /// <summary>Espenak &amp; Meeus 2006 polynomial family.</summary>
    private static double DeltatEspenakMeeus1620(double tjd, double tidAcc)
    {
        double ans = 0;
        var Ygreg = 2000.0 + (tjd - J2000) / 365.2425;

        if (Ygreg < -500)
        {
            ans = DeltatLongtermMorrisonStephenson(tjd);
        }
        else if (Ygreg < 500)
        {
            var u = Ygreg / 100.0;
            ans = (((((0.0090316521 * u + 0.022174192) * u - 0.1798452) * u - 5.952053) * u + 33.78311) * u - 1014.41) * u + 10583.6;
        }
        else if (Ygreg < 1600)
        {
            var u = (Ygreg - 1000) / 100.0;
            ans = (((((0.0083572073 * u - 0.005050998) * u - 0.8503463) * u + 0.319781) * u + 71.23472) * u - 556.01) * u + 1574.2;
        }
        else if (Ygreg < 1700)
        {
            var u = Ygreg - 1600;
            ans = 120 - 0.9808 * u - 0.01532 * u * u + u * u * u / 7129.0;
        }
        else if (Ygreg < 1800)
        {
            var u = Ygreg - 1700;
            ans = (((-u / 1174000.0 + 0.00013336) * u - 0.0059285) * u + 0.1603) * u + 8.83;
        }
        else if (Ygreg < 1860)
        {
            var u = Ygreg - 1800;
            ans = ((((((0.000000000875 * u - 0.0000001699) * u + 0.0000121272) * u - 0.00037436) * u + 0.0041116) * u + 0.0068612) * u - 0.332447) * u + 13.72;
        }
        else if (Ygreg < 1900)
        {
            var u = Ygreg - 1860;
            ans = ((((u / 233174.0 - 0.0004473624) * u + 0.01680668) * u - 0.251754) * u + 0.5737) * u + 7.62;
        }
        else if (Ygreg < 1920)
        {
            var u = Ygreg - 1900;
            ans = (((-0.000197 * u + 0.0061966) * u - 0.0598939) * u + 1.494119) * u - 2.79;
        }
        else if (Ygreg < 1941)
        {
            var u = Ygreg - 1920;
            ans = 21.20 + 0.84493 * u - 0.076100 * u * u + 0.0020936 * u * u * u;
        }
        else if (Ygreg < 1961)
        {
            var u = Ygreg - 1950;
            ans = 29.07 + 0.407 * u - u * u / 233.0 + u * u * u / 2547.0;
        }
        else if (Ygreg < 1986)
        {
            var u = Ygreg - 1975;
            ans = 45.45 + 1.067 * u - u * u / 260.0 - u * u * u / 718.0;
        }
        else if (Ygreg < 2005)
        {
            var u = Ygreg - 2000;
            ans = ((((0.00002373599 * u + 0.000651814) * u + 0.0017275) * u - 0.060374) * u + 0.3345) * u + 63.86;
        }

        ans = AdjustForTidacc(ans, Ygreg, tidAcc, Tidal26, false);
        return ans / SecondsPerDay;
    }

    /// <summary>
    /// Astronomical-Almanac correction applied when the lunar tidal acceleration
    /// differs from the value the underlying ΔT table is referenced to.
    /// </summary>
    private static double AdjustForTidacc(double ans, double Y, double tidAcc, double tidAcc0, bool adjustAfter1955)
    {
        if (Y < 1955.0 || adjustAfter1955)
        {
            var B = Y - 1955.0;
            ans += -0.000091 * (tidAcc - tidAcc0) * B * B;
        }
        return ans;
    }
}
