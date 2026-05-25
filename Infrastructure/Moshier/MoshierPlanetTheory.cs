// Ported from swisseph-master/swemplan.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputePolarJ2000          — swi_moshplan2          (swemplan.c:134-264)
//   ComputeCartesianJ2000WithSpeed — cartesian half of swi_moshplan
//   Sscc                       — sscc()                  (swemplan.c:387-408)
//   Str                        — STR                     (sweph.h:663)
//   TimeScale                  — TIMESCALE               (swemplan.c:67)
//   Mods3600                   — modulo 1 296 000 arcsec (swemplan.c:69)
//   EarthMoonMassRatio         — DE431                   (sweph.h:267)
//   SpeedIntervalDays          — speed-step              (swemplan.c:299)
//   MoshierStartJd / MoshierEndJd — sweph.h:219 / sweph.h:220
//   Freqs                      — freqs[] (Simon et al. 1994)  (swemplan.c:88-100)
//   Phases                     — phase offsets           (swemplan.c:102-114)

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Moshier;

/// <summary>
/// Moshier planetary theory: heliocentric J2000 ecliptic polar position and the
/// cartesian J2000 ecliptic position + first derivative (finite difference).
/// </summary>
internal static class MoshierPlanetTheory
{
    /// <summary>Radians per arc-second.</summary>
    internal const double Str = 4.8481368110953599359e-6;

    /// <summary>Time scale (3 652 500 days = 10 000 Julian years).</summary>
    internal const double TimeScale = 3_652_500.0;

    /// <summary>Earth–Moon mass ratio (DE431).</summary>
    internal const double EarthMoonMassRatio = 81.30056907419062;

    /// <summary>Speed step in TT days, used by the dispatcher's finite-difference velocity.</summary>
    internal const double SpeedIntervalDays = 0.0001;

    /// <summary>Lower JD bound of the Moshier planet ephemeris validity range.</summary>
    internal const double MoshierStartJd = 625_000.5;
    /// <summary>Upper JD bound of the Moshier planet ephemeris validity range.</summary>
    internal const double MoshierEndJd = 2_818_000.5;

    /// <summary>
    /// Mean orbital frequencies of Mercury..Pluto in arcsec per 10 000 Julian
    /// years (Simon et al., 1994).
    /// </summary>
    private static readonly double[] Freqs =
    {
         53_810_162_868.8982,
         21_066_413_643.3548,
         12_959_774_228.3429,
          6_890_507_749.3988,
          1_092_566_037.7991,
            439_960_985.5372,
            154_248_119.3933,
             78_655_032.0744,
             52_272_245.1795,
    };

    /// <summary>Constant phase offsets in arcsec, same order as <see cref="Freqs"/>.</summary>
    private static readonly double[] Phases =
    {
        252.25090552 * 3600.0,
        181.97980085 * 3600.0,
        100.46645683 * 3600.0,
        355.43299958 * 3600.0,
         34.35151874 * 3600.0,
         50.07744430 * 3600.0,
        314.05500511 * 3600.0,
        304.34866548 * 3600.0,
        860_492.1546,
    };

    /// <summary>Reduces the input modulo 1 296 000 arcsec (= 360°).</summary>
    private static double Mods3600(double x) => x - 1.296e6 * System.Math.Floor(x / 1.296e6);

    /// <summary>
    /// Maps the C-internal planet index (0..8 = Mer..Pluto) to a coefficient
    /// table. Public so that the body-position source can dispatch.
    /// </summary>
    internal static MoshierPlanetTable GetTable(int planetIndex0To8) => planetIndex0To8 switch
    {
        0 => MoshierTables.Mercury,
        1 => MoshierTables.Venus,
        2 => MoshierTables.EarthMoonBary,
        3 => MoshierTables.Mars,
        4 => MoshierTables.Jupiter,
        5 => MoshierTables.Saturn,
        6 => MoshierTables.Uranus,
        7 => MoshierTables.Neptune,
        8 => MoshierTables.Pluto,
        _ => throw new System.ArgumentOutOfRangeException(nameof(planetIndex0To8),
            "Moshier planet index must be 0..8 (Mercury..Pluto)."),
    };

    /// <summary>
    /// Heliocentric J2000 ecliptic polar position of the requested planet
    /// (0 = Mercury .. 8 = Pluto). Output: longitude (rad), latitude (rad), radius (AU).
    /// </summary>
    internal static Vec3 ComputePolarJ2000(int planetIndex0To8, double jdEt)
    {
        var plan = GetTable(planetIndex0To8);
        var t = (jdEt - AstronomicalConstants.J2000) / TimeScale;

        // sin/cos lookup tables for each fundamental argument: max harmonic 24.
        // Stack-allocated, never overflows because the C analogue uses the same
        // [9][24] dimensions (struct plantbl::max_harmonic ≤ 24 by design).
        Span<double> ss = stackalloc double[9 * 24];
        Span<double> cc = stackalloc double[9 * 24];

        for (var i = 0; i < 9; i++)
        {
            var n = plan.MaxHarmonic[i];
            if (n > 0)
            {
                var arg = (Mods3600(Freqs[i] * t) + Phases[i]) * Str;
                ComputeMultipleAngles(ss, cc, i, arg, n);
            }
        }

        // Walk the argument table.
        var args = plan.ArgTbl;
        var lon = plan.LonTbl;
        var lat = plan.LatTbl;
        var rad = plan.RadTbl;
        var aIdx = 0;
        var lIdx = 0;
        var bIdx = 0;
        var rIdx = 0;
        double sl = 0.0, sb = 0.0, sr = 0.0;

        while (true)
        {
            var np = args[aIdx++];
            if (np < 0)
                break;

            if (np == 0)
            {
                // Polynomial term: power-of-T cascade for each of L, B, R.
                var nt = args[aIdx++];
                var cu = lon[lIdx++];
                for (var ip = 0; ip < nt; ip++)
                    cu = cu * t + lon[lIdx++];
                sl += Mods3600(cu);

                cu = lat[bIdx++];
                for (var ip = 0; ip < nt; ip++)
                    cu = cu * t + lat[bIdx++];
                sb += cu;

                cu = rad[rIdx++];
                for (var ip = 0; ip < nt; ip++)
                    cu = cu * t + rad[rIdx++];
                sr += cu;
                continue;
            }

            // Trigonometric term: combine multi-angle factors.
            var k1 = 0;
            double cv = 0.0, sv = 0.0;
            for (var ip = 0; ip < np; ip++)
            {
                int j = args[aIdx++];     // harmonic
                int m = args[aIdx++] - 1; // planet index
                if (j != 0)
                {
                    var k = j;
                    if (k < 0) k = -k;
                    k -= 1;
                    var su = ss[m * 24 + k];
                    if (j < 0) su = -su;
                    var cuu = cc[m * 24 + k];
                    if (k1 == 0)
                    {
                        sv = su;
                        cv = cuu;
                        k1 = 1;
                    }
                    else
                    {
                        var tmp = su * cv + cuu * sv;
                        cv = cuu * cv - su * sv;
                        sv = tmp;
                    }
                }
            }

            // Highest power of T.
            var nt2 = args[aIdx++];
            // Longitude: alternating cos/sin amplitudes, polynomial in T.
            {
                var cu = lon[lIdx++];
                var su = lon[lIdx++];
                for (var ip = 0; ip < nt2; ip++)
                {
                    cu = cu * t + lon[lIdx++];
                    su = su * t + lon[lIdx++];
                }
                sl += cu * cv + su * sv;
            }
            {
                var cu = lat[bIdx++];
                var su = lat[bIdx++];
                for (var ip = 0; ip < nt2; ip++)
                {
                    cu = cu * t + lat[bIdx++];
                    su = su * t + lat[bIdx++];
                }
                sb += cu * cv + su * sv;
            }
            {
                var cu = rad[rIdx++];
                var su = rad[rIdx++];
                for (var ip = 0; ip < nt2; ip++)
                {
                    cu = cu * t + rad[rIdx++];
                    su = su * t + rad[rIdx++];
                }
                sr += cu * cv + su * sv;
            }
        }

        // sl, sb, sr are in arcsec scaled by various factors.
        var longitude = Str * sl;
        var latitude = Str * sb;
        var radius = Str * plan.Distance * sr + plan.Distance;
        return new Vec3(longitude, latitude, radius);
    }

    /// <summary>
    /// Heliocentric J2000 ecliptic cartesian position with finite-difference
    /// velocity (evaluated at <c>jd - dt</c>). Result stays in the J2000
    /// ecliptic frame; the body service handles further frame conversions.
    /// </summary>
    internal static (Vec3 Position, Vec3 Velocity) ComputeCartesianJ2000WithSpeed(int planetIndex0To8, double jdEt)
    {
        var p1 = Polar.PolarToCartesian(ComputePolarJ2000(planetIndex0To8, jdEt));
        var p2 = Polar.PolarToCartesian(ComputePolarJ2000(planetIndex0To8, jdEt - SpeedIntervalDays));
        var v = (p1 - p2) / SpeedIntervalDays;
        return (p1, v);
    }

    /// <summary>
    /// Heliocentric J2000 ecliptic cartesian position only (no velocity).
    /// </summary>
    internal static Vec3 ComputeCartesianJ2000(int planetIndex0To8, double jdEt)
        => Polar.PolarToCartesian(ComputePolarJ2000(planetIndex0To8, jdEt));

    /// <summary>
    /// Fills <paramref name="ss"/>[<paramref name="row"/>][0..n-1] = sin(k·arg)
    /// and the parallel <paramref name="cc"/> with cos(k·arg) using a
    /// double-angle recurrence.
    /// </summary>
    private static void ComputeMultipleAngles(Span<double> ss, Span<double> cc, int row, double arg, int n)
    {
        var su = System.Math.Sin(arg);
        var cu = System.Math.Cos(arg);
        var basePtr = row * 24;
        ss[basePtr + 0] = su;
        cc[basePtr + 0] = cu;
        if (n < 2) return;
        var sv = 2.0 * su * cu;
        var cv = cu * cu - su * su;
        ss[basePtr + 1] = sv;
        cc[basePtr + 1] = cv;
        for (var i = 2; i < n; i++)
        {
            var s = su * cv + cu * sv;
            cv = cu * cv - su * sv;
            sv = s;
            ss[basePtr + i] = sv;
            cc[basePtr + i] = cv;
        }
    }
}
