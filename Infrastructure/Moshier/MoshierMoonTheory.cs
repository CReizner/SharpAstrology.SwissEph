// Ported from swisseph-master/swemmoon.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputePolarOfDate          — swi_moshmoon2          (swemmoon.c:848-862)
//   ComputeMeanElements         — mean_elements          (swemmoon.c#L1763-L1818)
//   ComputeMoonAndPlanetElements — pre-loop save block of swi_intp_apsides
//                                                         (swemmoon.c#L1860-L1869)
//   ComputeApsidesPolar         — swi_intp_apsides loop  (swemmoon.c#L1893-L1923)
//   ComputeSpeed                — speed branch of swi_moshmoon (swemmoon.c:911-929)
//   MeanElementsPlanets         — mean_elements_pl       (swemmoon.c:1820-1850)
//   Moon1 / Moon2 / Moon3 / Moon4 — moon1..moon4         (swemmoon.c:1182-1454)
//   Chewm                       — chewm                  (swemmoon.c:1628-1691)
//   MoshierStartJd / MoshierEndJd — sweph.h:221 / sweph.h:222
//   SpeedIntervalDays           — MOON_SPEED_INTV
//   MoonMeanDistKm              — geocentric mean distance offset (swemmoon.c:1453)
//
// Build branch: DE404 (the upstream default; MOSH_MOON_200 is undefined).

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Moshier;

/// <summary>
/// Moshier lunar theory (DE404 fit): the Moon's geocentric polar (longitude,
/// latitude, distance) coordinates referred to the mean ecliptic and equinox
/// of date.
/// </summary>
internal static class MoshierMoonTheory
{
    private const double Str = MoshierPlanetTheory.Str;

    /// <summary>Lower JD bound of the lunar theory's validity range.</summary>
    internal const double MoshierStartJd = 625_000.5;
    /// <summary>Upper JD bound of the lunar theory's validity range.</summary>
    internal const double MoshierEndJd = 2_818_000.5;

    /// <summary>Speed step in TT days, used by the dispatcher's finite-difference velocity.</summary>
    internal const double SpeedIntervalDays = 5.0e-5;

    /// <summary>Geocentric mean lunar distance in km used as the radial-series offset.</summary>
    private const double MoonMeanDistKm = 385000.52899;

    /// <summary>
    /// Geocentric ecliptic-of-date polar position of the Moon: longitude (rad),
    /// latitude (rad), distance (AU).
    /// </summary>
    internal static Vec3 ComputePolarOfDate(double jdEt)
    {
        var ws = new MoonWorkspace();
        ws.T = (jdEt - AstronomicalConstants.J2000) / 36525.0;
        ws.T2 = ws.T * ws.T;
        ws.T3 = ws.T * ws.T2;
        ws.T4 = ws.T2 * ws.T2;
        MeanElements(ref ws);
        MeanElementsPlanets(ref ws);
        Moon1(ref ws);
        Moon2(ref ws);
        Moon3(ref ws);
        Moon4(ref ws);
        return new Vec3(ws.MoonPol0, ws.MoonPol1, ws.MoonPol2);
    }

    /// <summary>
    /// Cartesian helper (geocentric, ecliptic of date). Frame conversion onto
    /// the equator-2000 frame is left to the body-service correction pipeline.
    /// </summary>
    internal static Vec3 ComputeCartesianOfDate(double jdEt)
        => Polar.PolarToCartesian(ComputePolarOfDate(jdEt));

    /// <summary>
    /// Mean fundamental arguments of the Moon's orbit at <paramref name="jdEt"/>:
    /// <c>SWELP</c> (mean longitude), <c>NF</c> (argument of node), <c>MP</c>
    /// (mean anomaly), <c>M</c> (Sun's mean anomaly), and <c>D</c>, all in
    /// arc-seconds reduced modulo 1 296 000″. Multiply by <c>π/(180·3600)</c>
    /// to obtain radians.
    /// </summary>
    internal readonly record struct MeanLunarElements(double SWELP, double NF, double MP, double M, double D);

    /// <summary>Computes the four (five) mean fundamental arguments at <paramref name="jdEt"/>.</summary>
    internal static MeanLunarElements ComputeMeanElements(double jdEt)
    {
        var ws = new MoonWorkspace();
        ws.T = (jdEt - AstronomicalConstants.J2000) / 36525.0;
        ws.T2 = ws.T * ws.T;
        ws.T3 = ws.T * ws.T2;
        ws.T4 = ws.T2 * ws.T2;
        MeanElements(ref ws);
        return new MeanLunarElements(ws.SWELP, ws.NF, ws.MP, ws.M, ws.D);
    }

    /// <summary>
    /// Lunar + planet mean elements at <paramref name="jdEt"/> — the scalars
    /// required to seed the interpolated-apsides solver. All values are kept
    /// verbatim (no modular reduction); the apsides engine reduces the lunar
    /// elements itself, while the planet elements <c>Ve, Ea, Ma, Ju, Sa</c>
    /// stay raw.
    /// </summary>
    internal readonly record struct MoonAndPlanetElements(
        double SWELP, double NF, double MP, double M, double D,
        double Ve, double Ea, double Ma, double Ju, double Sa);

    /// <summary>
    /// Computes the lunar + planet mean elements together at <paramref name="jdEt"/>.
    /// </summary>
    internal static MoonAndPlanetElements ComputeMoonAndPlanetElements(double jdEt)
    {
        var ws = new MoonWorkspace();
        ws.T = (jdEt - AstronomicalConstants.J2000) / 36525.0;
        ws.T2 = ws.T * ws.T;
        ws.T3 = ws.T * ws.T2;
        ws.T4 = ws.T2 * ws.T2;
        MeanElements(ref ws);
        MeanElementsPlanets(ref ws);
        return new MoonAndPlanetElements(
            ws.SWELP, ws.NF, ws.MP, ws.M, ws.D,
            ws.Ve, ws.Ea, ws.Ma, ws.Ju, ws.Sa);
    }

    /// <summary>
    /// Runs the lunar perturbation series with caller-supplied mean elements
    /// while keeping <c>T/T2/T3/T4</c> consistent with <paramref name="jdEt"/>.
    /// Used by the apsides solver: the caller writes the mean-anomaly variants
    /// it wants, this routine returns the polar (longitude rad, latitude rad,
    /// distance AU).
    /// </summary>
    internal static Vec3 ComputePolarWithElements(double jdEt, in MoonAndPlanetElements e)
    {
        var ws = new MoonWorkspace();
        ws.T = (jdEt - AstronomicalConstants.J2000) / 36525.0;
        ws.T2 = ws.T * ws.T;
        ws.T3 = ws.T * ws.T2;
        ws.T4 = ws.T2 * ws.T2;
        ws.SWELP = e.SWELP;
        ws.NF = e.NF;
        ws.MP = e.MP;
        ws.M = e.M;
        ws.D = e.D;
        ws.Ve = e.Ve;
        ws.Ea = e.Ea;
        ws.Ma = e.Ma;
        ws.Ju = e.Ju;
        ws.Sa = e.Sa;
        Moon1(ref ws);
        Moon2(ref ws);
        Moon3(ref ws);
        Moon4(ref ws);
        return new Vec3(ws.MoonPol0, ws.MoonPol1, ws.MoonPol2);
    }

    /// <summary>
    /// Position + 3-point finite-difference velocity, both ecliptic-of-date cartesian.
    /// </summary>
    internal static (Vec3 Position, Vec3 Velocity) ComputeCartesianOfDateWithSpeed(double jdEt)
    {
        var p = ComputeCartesianOfDate(jdEt);
        var p1 = ComputeCartesianOfDate(jdEt + SpeedIntervalDays);
        var p2 = ComputeCartesianOfDate(jdEt - SpeedIntervalDays);
        // 3-point parabolic estimator from swemmoon.c:925-927.
        var bx = (p1.X - p2.X) * 0.5;
        var by = (p1.Y - p2.Y) * 0.5;
        var bz = (p1.Z - p2.Z) * 0.5;
        var ax = (p1.X + p2.X) * 0.5 - p.X;
        var ay = (p1.Y + p2.Y) * 0.5 - p.Y;
        var az = (p1.Z + p2.Z) * 0.5 - p.Z;
        var v = new Vec3(
            (2.0 * ax + bx) / SpeedIntervalDays,
            (2.0 * ay + by) / SpeedIntervalDays,
            (2.0 * az + bz) / SpeedIntervalDays);
        return (p, v);
    }

    // ---- internal scratch state -------------------------------------------

    /// <summary>
    /// Stack-allocated workspace holding the time powers, mean elements, planet
    /// elements, accumulators, and the 5 × 8 sine/cosine harmonics tables used
    /// by the perturbation series.
    /// </summary>
    private struct MoonWorkspace
    {
        public double T, T2, T3, T4;
        public double D, M, MP, NF, SWELP;
        public double Ve, Ea, Ma, Ju, Sa;
        public double l, B, l1, l2, l3, l4;
        public double f, g, cg, sg;
        public double MoonPol0, MoonPol1, MoonPol2;
        // 5 angles × 8 harmonics
        public Ss40 ss;
        public Ss40 cc;

        [System.Runtime.CompilerServices.InlineArray(40)]
        public struct Ss40 { private double _e; }
    }

    private static double Mods3600(double x)
        => x - 1_296_000.0 * System.Math.Floor(x / 1_296_000.0);

    /// <summary>Fill the multi-angle sin/cos lookup row <paramref name="k"/>.</summary>
    private static void Sscc(ref MoonWorkspace ws, int k, double arg, int n)
    {
        var su = System.Math.Sin(arg);
        var cu = System.Math.Cos(arg);
        ws.ss[k * 8 + 0] = su;
        ws.cc[k * 8 + 0] = cu;
        if (n < 2) return;
        var sv = 2.0 * su * cu;
        var cv = cu * cu - su * su;
        ws.ss[k * 8 + 1] = sv;
        ws.cc[k * 8 + 1] = cv;
        for (var i = 2; i < n; i++)
        {
            var s = su * cv + cu * sv;
            cv = cu * cv - su * sv;
            sv = s;
            ws.ss[k * 8 + i] = sv;
            ws.cc[k * 8 + i] = cv;
        }
    }

    /// <summary>
    /// Step through a perturbation table, accumulating the contribution of each row.
    /// </summary>
    private static void Chewm(ref MoonWorkspace ws, ReadOnlySpan<short> pt, int nlines, int nangles, int typflg, ref MoonWorkspace ans)
    {
        var idx = 0;
        for (var i = 0; i < nlines; i++)
        {
            var k1 = 0;
            double sv = 0.0, cv = 0.0, su = 0.0, cu = 0.0;
            for (var mIdx = 0; mIdx < nangles; mIdx++)
            {
                var j = (int)pt[idx++];
                if (j != 0)
                {
                    var k = j;
                    if (k < 0) k = -k;
                    su = ans.ss[mIdx * 8 + (k - 1)];
                    cu = ans.cc[mIdx * 8 + (k - 1)];
                    if (j < 0) su = -su;
                    if (k1 == 0)
                    {
                        sv = su;
                        cv = cu;
                        k1 = 1;
                    }
                    else
                    {
                        var ff = su * cv + cu * sv;
                        cv = cu * cv - su * sv;
                        sv = ff;
                    }
                }
            }
            switch (typflg)
            {
                case 1:
                {
                    int j2 = pt[idx++];
                    int k2 = pt[idx++];
                    ans.MoonPol0 += (10000.0 * j2 + k2) * sv;
                    j2 = pt[idx++];
                    k2 = pt[idx++];
                    if (k2 != 0) ans.MoonPol2 += (10000.0 * j2 + k2) * cv;
                    break;
                }
                case 2:
                {
                    int j2 = pt[idx++];
                    int k2 = pt[idx++];
                    ans.MoonPol0 += j2 * sv;
                    ans.MoonPol2 += k2 * cv;
                    break;
                }
                case 3:
                {
                    int j2 = pt[idx++];
                    int k2 = pt[idx++];
                    ans.MoonPol1 += (10000.0 * j2 + k2) * sv;
                    break;
                }
                case 4:
                {
                    int j2 = pt[idx++];
                    ans.MoonPol1 += j2 * sv;
                    break;
                }
            }
        }
    }

    /// <summary>Mean fundamental arguments of the Moon.</summary>
    private static void MeanElements(ref MoonWorkspace ws)
    {
        var z = MoshierLunarTables.Z;
        var fracT = ws.T - System.Math.Truncate(ws.T);
        // Mean anomaly of sun = M (l').
        ws.M = Mods3600(129_600_000.0 * fracT - 3_418.961646 * ws.T + 1_287_104.76154);
        ws.M += ((((((((
              1.62e-20 * ws.T
            - 1.0390e-17) * ws.T
            - 3.83508e-15) * ws.T
            + 4.237343e-13) * ws.T
            + 8.8555011e-11) * ws.T
            - 4.77258489e-8) * ws.T
            - 1.1297037031e-5) * ws.T
            + 1.4732069041e-4) * ws.T
            - 0.552891801772) * ws.T2;

        // DE404 branch — secular corrections from z[0..11].
        ws.NF = Mods3600(1_739_232_000.0 * fracT + 295_263.0983 * ws.T - 2.079419901760e-01 * ws.T + 335_779.55755);
        ws.MP = Mods3600(1_717_200_000.0 * fracT + 715_923.4728 * ws.T - 2.035946368532e-01 * ws.T + 485_868.28096);
        ws.D = Mods3600(1_601_856_000.0 * fracT + 1_105_601.4603 * ws.T + 3.962893294503e-01 * ws.T + 1_072_260.73512);
        ws.SWELP = Mods3600(1_731_456_000.0 * fracT + 1_108_372.83264 * ws.T - 6.784914260953e-01 * ws.T + 785_939.95571);

        ws.NF += ((z[2] * ws.T + z[1]) * ws.T + z[0]) * ws.T2;
        ws.MP += ((z[5] * ws.T + z[4]) * ws.T + z[3]) * ws.T2;
        ws.D += ((z[8] * ws.T + z[7]) * ws.T + z[6]) * ws.T2;
        ws.SWELP += ((z[11] * ws.T + z[10]) * ws.T + z[9]) * ws.T2;
    }

    /// <summary>Mean longitudes of the perturbing planets used by the lunar series.</summary>
    private static void MeanElementsPlanets(ref MoonWorkspace ws)
    {
        ws.Ve = Mods3600(210_664_136.4335482 * ws.T + 655_127.283046);
        ws.Ve += ((((((((
              -9.36e-23 * ws.T
            - 1.95e-20) * ws.T
            + 6.097e-18) * ws.T
            + 4.43201e-15) * ws.T
            + 2.509418e-13) * ws.T
            - 3.0622898e-10) * ws.T
            - 2.26602516e-9) * ws.T
            - 1.4244812531e-5) * ws.T
            + 0.005871373088) * ws.T2;

        ws.Ea = Mods3600(129_597_742.26669231 * ws.T + 361_679.214649);
        ws.Ea += ((((((((
              -1.16e-22 * ws.T
            + 2.976e-19) * ws.T
            + 2.8460e-17) * ws.T
            - 1.08402e-14) * ws.T
            - 1.226182e-12) * ws.T
            + 1.7228268e-10) * ws.T
            + 1.515912254e-7) * ws.T
            + 8.863982531e-6) * ws.T
            - 2.0199859001e-2) * ws.T2;

        ws.Ma = Mods3600(68_905_077.59284 * ws.T + 1_279_559.78866);
        ws.Ma += (-1.043e-5 * ws.T + 9.38012e-3) * ws.T2;

        ws.Ju = Mods3600(10_925_660.428608 * ws.T + 123_665.342120);
        ws.Ju += (1.543273e-5 * ws.T - 3.06037836351e-1) * ws.T2;

        ws.Sa = Mods3600(4_399_609.65932 * ws.T + 180_278.89694);
        ws.Sa += ((4.475946e-8 * ws.T - 6.874806e-5) * ws.T + 7.56161437443e-1) * ws.T2;
    }

    /// <summary>
    /// First perturbation block: adds T² perturbations and Venus/Earth-related
    /// long-period terms; sets up ss/cc for D, M, MP, NF.
    /// </summary>
    private static void Moon1(ref MoonWorkspace ws)
    {
        var z = MoshierLunarTables.Z;

        // Initialize ss/cc to zero (the Pinnamaneni fix per swemmoon.c:1186-1199).
        for (var i = 0; i < 40; i++)
        {
            ws.ss[i] = 0.0;
            ws.cc[i] = 0.0;
        }

        Sscc(ref ws, 0, Str * ws.D, 6);
        Sscc(ref ws, 1, Str * ws.M, 4);
        Sscc(ref ws, 2, Str * ws.MP, 4);
        Sscc(ref ws, 3, Str * ws.NF, 4);

        ws.MoonPol0 = 0.0;
        ws.MoonPol1 = 0.0;
        ws.MoonPol2 = 0.0;

        Chewm(ref ws, MoshierLunarTables.LRT2, MoshierLunarTables.NLRT2, 4, 2, ref ws);
        Chewm(ref ws, MoshierLunarTables.BT2, MoshierLunarTables.NBT2, 4, 4, ref ws);

        ws.f = 18.0 * ws.Ve - 16.0 * ws.Ea;

        ws.g = Str * (ws.f - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l = 6.367278 * ws.cg + 12.747036 * ws.sg;
        ws.l1 = 23123.70 * ws.cg - 10570.02 * ws.sg;
        ws.l2 = z[12] * ws.cg + z[13] * ws.sg;
        ws.MoonPol2 += 5.01 * ws.cg + 2.72 * ws.sg;

        ws.g = Str * (10.0 * ws.Ve - 3.0 * ws.Ea - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.253102 * ws.cg + 0.503359 * ws.sg;
        ws.l1 += 1258.46 * ws.cg + 707.29 * ws.sg;
        ws.l2 += z[14] * ws.cg + z[15] * ws.sg;

        ws.g = Str * (8.0 * ws.Ve - 13.0 * ws.Ea);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.187231 * ws.cg - 0.127481 * ws.sg;
        ws.l1 += -319.87 * ws.cg - 18.34 * ws.sg;
        ws.l2 += z[16] * ws.cg + z[17] * ws.sg;

        var a = 4.0 * ws.Ea - 8.0 * ws.Ma + 3.0 * ws.Ju;
        ws.g = Str * a;
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.866287 * ws.cg + 0.248192 * ws.sg;
        ws.l1 += 41.87 * ws.cg + 1053.97 * ws.sg;
        ws.l2 += z[18] * ws.cg + z[19] * ws.sg;

        ws.g = Str * (a - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.165009 * ws.cg + 0.044176 * ws.sg;
        ws.l1 += 4.67 * ws.cg + 201.55 * ws.sg;

        ws.g = Str * ws.f;
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.330401 * ws.cg + 0.661362 * ws.sg;
        ws.l1 += 1202.67 * ws.cg - 555.59 * ws.sg;
        ws.l2 += z[20] * ws.cg + z[21] * ws.sg;

        ws.g = Str * (ws.f - 2.0 * ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.352185 * ws.cg + 0.705041 * ws.sg;
        ws.l1 += 1283.59 * ws.cg - 586.43 * ws.sg;

        ws.g = Str * (2.0 * ws.Ju - 5.0 * ws.Sa);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.034700 * ws.cg + 0.160041 * ws.sg;
        ws.l2 += z[22] * ws.cg + z[23] * ws.sg;

        ws.g = Str * (ws.SWELP - ws.NF);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.000116 * ws.cg + 7.063040 * ws.sg;
        ws.l1 += 298.8 * ws.sg;

        // T^3 terms (DE404 branch only takes z[24]).
        ws.sg = System.Math.Sin(Str * ws.M);
        ws.l3 = z[24] * ws.sg;
        ws.l4 = 0.0;

        ws.g = Str * (2.0 * ws.D - ws.M);
        ws.sg = System.Math.Sin(ws.g);
        ws.cg = System.Math.Cos(ws.g);
        ws.MoonPol2 += -0.2655 * ws.cg * ws.T;

        ws.g = Str * (ws.M - ws.MP);
        ws.MoonPol2 += -0.1568 * System.Math.Cos(ws.g) * ws.T;

        ws.g = Str * (ws.M + ws.MP);
        ws.MoonPol2 += 0.1309 * System.Math.Cos(ws.g) * ws.T;

        ws.g = Str * (2.0 * (ws.D + ws.M) - ws.MP);
        ws.sg = System.Math.Sin(ws.g);
        ws.cg = System.Math.Cos(ws.g);
        ws.MoonPol2 += 0.5568 * ws.cg * ws.T;

        ws.l2 += ws.MoonPol0;

        ws.g = Str * (2.0 * ws.D - ws.M - ws.MP);
        ws.MoonPol2 += -0.1910 * System.Math.Cos(ws.g) * ws.T;

        ws.MoonPol1 *= ws.T;
        ws.MoonPol2 *= ws.T;

        ws.MoonPol0 = 0.0;
        Chewm(ref ws, MoshierLunarTables.BT, MoshierLunarTables.NBT, 4, 4, ref ws);
        Chewm(ref ws, MoshierLunarTables.LRT, MoshierLunarTables.NLRT, 4, 1, ref ws);

        ws.g = Str * (ws.f - ws.MP - ws.NF - 2_355_767.6);
        ws.MoonPol1 += -1127.0 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.f - ws.MP + ws.NF - 235_353.6);
        ws.MoonPol1 += -1123.0 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea + ws.D + 51_987.6);
        ws.MoonPol1 += 1303.0 * System.Math.Sin(ws.g);
        ws.g = Str * ws.SWELP;
        ws.MoonPol1 += 342.0 * System.Math.Sin(ws.g);

        ws.g = Str * (2.0 * ws.Ve - 3.0 * ws.Ea);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.343550 * ws.cg - 0.000276 * ws.sg;
        ws.l1 += 105.90 * ws.cg + 336.53 * ws.sg;

        ws.g = Str * (ws.f - 2.0 * ws.D);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.074668 * ws.cg + 0.149501 * ws.sg;
        ws.l1 += 271.77 * ws.cg - 124.20 * ws.sg;

        ws.g = Str * (ws.f - 2.0 * ws.D - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.073444 * ws.cg + 0.147094 * ws.sg;
        ws.l1 += 265.24 * ws.cg - 121.16 * ws.sg;

        ws.g = Str * (ws.f + 2.0 * ws.D - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.072844 * ws.cg + 0.145829 * ws.sg;
        ws.l1 += 265.18 * ws.cg - 121.29 * ws.sg;

        ws.g = Str * (ws.f + 2.0 * (ws.D - ws.MP));
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.070201 * ws.cg + 0.140542 * ws.sg;
        ws.l1 += 255.36 * ws.cg - 116.79 * ws.sg;

        ws.g = Str * (ws.Ea + ws.D - ws.NF);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.288209 * ws.cg - 0.025901 * ws.sg;
        ws.l1 += -63.51 * ws.cg - 240.14 * ws.sg;

        ws.g = Str * (2.0 * ws.Ea - 3.0 * ws.Ju + 2.0 * ws.D - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += 0.077865 * ws.cg + 0.438460 * ws.sg;
        ws.l1 += 210.57 * ws.cg + 124.84 * ws.sg;

        ws.g = Str * (ws.Ea - 2.0 * ws.Ma);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.216579 * ws.cg + 0.241702 * ws.sg;
        ws.l1 += 197.67 * ws.cg + 125.23 * ws.sg;

        ws.g = Str * (a + ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.165009 * ws.cg + 0.044176 * ws.sg;
        ws.l1 += 4.67 * ws.cg + 201.55 * ws.sg;

        ws.g = Str * (a + 2.0 * ws.D - ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.133533 * ws.cg + 0.041116 * ws.sg;
        ws.l1 += 6.95 * ws.cg + 187.07 * ws.sg;

        ws.g = Str * (a - 2.0 * ws.D + ws.MP);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.133430 * ws.cg + 0.041079 * ws.sg;
        ws.l1 += 6.28 * ws.cg + 169.08 * ws.sg;

        ws.g = Str * (3.0 * ws.Ve - 4.0 * ws.Ea);
        ws.cg = System.Math.Cos(ws.g);
        ws.sg = System.Math.Sin(ws.g);
        ws.l += -0.175074 * ws.cg + 0.003035 * ws.sg;
        ws.l1 += 49.17 * ws.cg + 150.57 * ws.sg;

        ws.g = Str * (2.0 * (ws.Ea + ws.D - ws.MP) - 3.0 * ws.Ju + 213_534.0);
        ws.l1 += 158.4 * System.Math.Sin(ws.g);
        ws.l1 += ws.MoonPol0;

        var aT = 0.1 * ws.T;
        ws.MoonPol1 *= aT;
        ws.MoonPol2 *= aT;
    }

    /// <summary>Second perturbation block: T⁰ long-period perturbations.</summary>
    private static void Moon2(ref MoonWorkspace ws)
    {
        ws.g = Str * (2.0 * (ws.Ea - ws.Ju + ws.D) - ws.MP + 648_431.172);
        ws.l += 1.14307 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ve - ws.Ea + 648_035.568);
        ws.l += 0.82155 * System.Math.Sin(ws.g);
        ws.g = Str * (3.0 * (ws.Ve - ws.Ea) + 2.0 * ws.D - ws.MP + 647_933.184);
        ws.l += 0.64371 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea - ws.Ju + 4424.04);
        ws.l += 0.63880 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP + ws.MP - ws.NF + 4.68);
        ws.l += 0.49331 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP - ws.MP - ws.NF + 4.68);
        ws.l += 0.4914 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP + ws.NF + 2.52);
        ws.l += 0.36061 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * ws.Ve - 2.0 * ws.Ea + 736.2);
        ws.l += 0.30154 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * ws.Ea - 3.0 * ws.Ju + 2.0 * ws.D - 2.0 * ws.MP + 36_138.2);
        ws.l += 0.28282 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * ws.Ea - 2.0 * ws.Ju + 2.0 * ws.D - 2.0 * ws.MP + 311.0);
        ws.l += 0.24516 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea - ws.Ju - 2.0 * ws.D + ws.MP + 6275.88);
        ws.l += 0.21117 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * (ws.Ea - ws.Ma) - 846.36);
        ws.l += 0.19444 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * (ws.Ea - ws.Ju) + 1569.96);
        ws.l -= 0.18457 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * (ws.Ea - ws.Ju) - ws.MP - 55.8);
        ws.l += 0.18256 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea - ws.Ju - 2.0 * ws.D + 6490.08);
        ws.l += 0.16499 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea - 2.0 * ws.Ju - 212_378.4);
        ws.l += 0.16427 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * (ws.Ve - ws.Ea - ws.D) + ws.MP + 1122.48);
        ws.l += 0.16088 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ve - ws.Ea - ws.MP + 32.04);
        ws.l -= 0.15350 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea - ws.Ju - ws.MP + 4488.88);
        ws.l += 0.14346 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * (ws.Ve - ws.Ea + ws.D) - ws.MP - 8.64);
        ws.l += 0.13594 * System.Math.Sin(ws.g);
        ws.g = Str * (2.0 * (ws.Ve - ws.Ea - ws.D) + 1319.76);
        ws.l += 0.13432 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ve - ws.Ea - 2.0 * ws.D + ws.MP - 56.16);
        ws.l -= 0.13122 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ve - ws.Ea + ws.MP + 54.36);
        ws.l -= 0.12722 * System.Math.Sin(ws.g);
        ws.g = Str * (3.0 * (ws.Ve - ws.Ea) - ws.MP + 433.8);
        ws.l += 0.12539 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea - ws.Ju + ws.MP + 4002.12);
        ws.l += 0.10994 * System.Math.Sin(ws.g);
        ws.g = Str * (20.0 * ws.Ve - 21.0 * ws.Ea - 2.0 * ws.D + ws.MP - 317_511.72);
        ws.l += 0.10652 * System.Math.Sin(ws.g);
        ws.g = Str * (26.0 * ws.Ve - 29.0 * ws.Ea - ws.MP + 270_002.52);
        ws.l += 0.10490 * System.Math.Sin(ws.g);
        ws.g = Str * (3.0 * ws.Ve - 4.0 * ws.Ea + ws.D - ws.MP - 322_765.56);
        ws.l += 0.10386 * System.Math.Sin(ws.g);

        ws.g = Str * (ws.SWELP + 648_002.556);
        ws.B = 8.04508 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.Ea + ws.D + 996_048.252);
        ws.B += 1.51021 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.f - ws.MP + ws.NF + 95_554.332);
        ws.B += 0.63037 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.f - ws.MP - ws.NF + 95_553.792);
        ws.B += 0.63014 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP - ws.MP + 2.9);
        ws.B += 0.45587 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP + ws.MP + 2.5);
        ws.B += -0.41573 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP - 2.0 * ws.NF + 3.2);
        ws.B += 0.32623 * System.Math.Sin(ws.g);
        ws.g = Str * (ws.SWELP - 2.0 * ws.D + 2.5);
        ws.B += 0.29855 * System.Math.Sin(ws.g);
    }

    /// <summary>Third perturbation block: applies the LR/MB tables and assembles the polar.</summary>
    private static void Moon3(ref MoonWorkspace ws)
    {
        ws.MoonPol0 = 0.0;
        Chewm(ref ws, MoshierLunarTables.LR, MoshierLunarTables.NLR, 4, 1, ref ws);
        Chewm(ref ws, MoshierLunarTables.MB, MoshierLunarTables.NMB, 4, 3, ref ws);
        ws.l += (((ws.l4 * ws.T + ws.l3) * ws.T + ws.l2) * ws.T + ws.l1) * ws.T * 1.0e-5;
        ws.MoonPol0 = ws.SWELP + ws.l + 1.0e-4 * ws.MoonPol0;
        ws.MoonPol1 = 1.0e-4 * ws.MoonPol1 + ws.B;
        ws.MoonPol2 = 1.0e-4 * ws.MoonPol2 + MoonMeanDistKm; // km
    }

    /// <summary>Fourth perturbation block: scales the result to AU and radians.</summary>
    private static void Moon4(ref MoonWorkspace ws)
    {
        ws.MoonPol2 /= AstronomicalConstants.AstronomicalUnitKilometers; // km → AU
        ws.MoonPol0 = Str * Mods3600(ws.MoonPol0);
        ws.MoonPol1 = Str * ws.MoonPol1;
        ws.B = ws.MoonPol1;
    }
}
