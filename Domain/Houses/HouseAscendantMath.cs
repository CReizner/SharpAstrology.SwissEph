// Ported from swisseph-master/swehouse.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   VerySmall                — VERY_SMALL          (swehouse.h)
//   ArmcDegreesPerDay        — (SOLAR_YEAR + 1) / SOLAR_YEAR × 360 (swehouse.c#L70)
//   Asc1                     — Asc1                (swehouse.c#L2058)
//   Asc2                     — Asc2                (swehouse.c#L2100)
//   AscDash                  — AscDash             (swehouse.c#L2133, contributed by Graham Dawson)
//   ArmcToMc                 — armc_to_mc helper   (swehouse.c#L2149 / #L872)
//   FixAscPolar              — fix_asc_polar       (swehouse.c#L2169)
//   RotateEqToEclSpherical   — swe_cotrans         (swephlib.c, with positive obliquity)
//
// Asc1/Asc2/AscDash form the three-rung ladder for projecting a great-circle
// intersection (with given pole-height f) onto the ecliptic; degenerate-case
// logic is preserved verbatim. FixAscPolar keeps the ascendant east of the
// meridian for systems that depend on it (Porphyry / Sripati / Alcabitius /
// Equal / Carter / Krusinski).

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Houses;

/// <summary>
/// Trigonometric primitives that intersect great circles with the ecliptic
/// for house-cusp computation. All inputs/outputs in degrees.
/// </summary>
internal static class HouseAscendantMath
{
    /// <summary>Numerical near-zero threshold used by the trigonometric house solvers.</summary>
    public const double VerySmall = 1e-10;

    /// <summary>Degrees of ARMC advanced per Terrestrial-Time day.</summary>
    public const double ArmcDegreesPerDay = (SolarYearDays + 1.0) / SolarYearDays * 360.0;
    private const double SolarYearDays = 365.24219893;

    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;

    /// <summary>
    /// Crossing of a great circle (pole-height <paramref name="f"/>) with the
    /// ecliptic, given the equatorial intersection <paramref name="x1"/> (degrees).
    /// Quadrant-aware dispatch around <see cref="Asc2"/>.
    /// </summary>
    public static double Asc1(double x1, double f, double sine, double cose)
    {
        x1 = AngleMath.NormalizeDegrees(x1);
        var n = (int)((x1 / 90.0) + 1); // 1..4

        if (System.Math.Abs(90 - f) < VerySmall) return 180;
        if (System.Math.Abs(90 + f) < VerySmall) return 0;

        double ass = n switch
        {
            1 => Asc2(x1, f, sine, cose),
            2 => 180 - Asc2(180 - x1, -f, sine, cose),
            3 => 180 + Asc2(x1 - 180, -f, sine, cose),
            _ => 360 - Asc2(360 - x1, f, sine, cose),
        };
        ass = AngleMath.NormalizeDegrees(ass);

        if (System.Math.Abs(ass - 90) < VerySmall) ass = 90;
        if (System.Math.Abs(ass - 180) < VerySmall) ass = 180;
        if (System.Math.Abs(ass - 270) < VerySmall) ass = 270;
        if (System.Math.Abs(ass - 360) < VerySmall) ass = 0;
        return ass;
    }

    /// <summary>
    /// Workhorse called by <see cref="Asc1"/>. <paramref name="x"/> in
    /// 0..90, <paramref name="f"/> in -90..+90.
    /// </summary>
    public static double Asc2(double x, double f, double sine, double cose)
    {
        var sx = System.Math.Sin(x * DegToRad);
        var cx = System.Math.Cos(x * DegToRad);
        var tf = System.Math.Tan(f * DegToRad);

        var ass = -tf * sine + cose * cx;
        if (System.Math.Abs(ass) < VerySmall) ass = 0;
        var sinx = sx;
        if (System.Math.Abs(sinx) < VerySmall) sinx = 0;

        if (sinx == 0)
        {
            ass = ass < 0 ? -VerySmall : VerySmall;
        }
        else if (ass == 0)
        {
            ass = sinx < 0 ? -90 : 90;
        }
        else
        {
            ass = System.Math.Atan(sinx / ass) * RadToDeg;
        }
        if (ass < 0) ass = 180 + ass;
        return ass;
    }

    /// <summary>
    /// Time-derivative of <see cref="Asc1"/> in degrees per day (along ARMC).
    /// </summary>
    public static double AscDash(double x, double f, double sine, double cose)
    {
        var cx = System.Math.Cos(x * DegToRad);
        var sx = System.Math.Sin(x * DegToRad);
        var tf = System.Math.Tan(f * DegToRad);
        var sinx2 = sx * sx;
        var c = cose * cx - tf * sine;
        var d = sinx2 + c * c;
        var dudt = d > VerySmall ? (cx * c + cose * sinx2) / d : 0.0;
        return dudt * ArmcDegreesPerDay;
    }

    /// <summary>
    /// Right ascension → ecliptic-longitude of the meridian (the MC), quadrant-correct.
    /// </summary>
    public static double ArmcToMc(double armcDeg, double obliquityDeg)
    {
        if (System.Math.Abs(armcDeg - 90) > VerySmall && System.Math.Abs(armcDeg - 270) > VerySmall)
        {
            var cose = System.Math.Cos(obliquityDeg * DegToRad);
            var tant = System.Math.Tan(armcDeg * DegToRad);
            var mc = System.Math.Atan(tant / cose) * RadToDeg;
            if (armcDeg > 90 && armcDeg <= 270) mc = AngleMath.NormalizeDegrees(mc + 180);
            return AngleMath.NormalizeDegrees(mc);
        }
        return System.Math.Abs(armcDeg - 90) <= VerySmall ? 90 : 270;
    }

    /// <summary>
    /// "If the ascendant is on the western half of the horizon, add 180°."
    /// Used by <see cref="HousePositionMath"/> to keep Asc east of the meridian
    /// for systems that depend on it (Porphyry / Sripati / Alcabitius / Equal /
    /// Carter / Krusinski).
    /// </summary>
    public static double FixAscPolar(double asc, double armcDeg, double obliquityDeg, double geolatDeg)
    {
        var demc = System.Math.Atan(
            System.Math.Sin(armcDeg * DegToRad)
            * System.Math.Tan(obliquityDeg * DegToRad))
            * RadToDeg;
        if (geolatDeg >= 0 && 90 - geolatDeg + demc < 0) asc = AngleMath.NormalizeDegrees(asc + 180);
        if (geolatDeg < 0 && -90 - geolatDeg + demc > 0) asc = AngleMath.NormalizeDegrees(asc + 180);
        return asc;
    }

    /// <summary>
    /// Equatorial → ecliptic coordinate transform (degrees), spherical form.
    /// In/out: <paramref name="lonDeg"/> ∈ [0,360), <paramref name="latDeg"/> ∈ [-90,90].
    /// </summary>
    public static void RotateEqToEclSpherical(ref double lonDeg, ref double latDeg, double obliquityDeg)
    {
        var lonRad = lonDeg * DegToRad;
        var latRad = latDeg * DegToRad;
        var cl = System.Math.Cos(lonRad);
        var sl = System.Math.Sin(lonRad);
        var cb = System.Math.Cos(latRad);
        var sb = System.Math.Sin(latRad);
        var x = cl * cb;
        var y = sl * cb;
        var z = sb;
        var ce = System.Math.Cos(obliquityDeg * DegToRad);
        var se = System.Math.Sin(obliquityDeg * DegToRad);
        var y2 = y * ce + z * se;
        var z2 = -y * se + z * ce;
        var rxy = System.Math.Sqrt(x * x + y2 * y2);
        var lon2 = System.Math.Atan2(y2, x) * RadToDeg;
        var lat2 = System.Math.Atan2(z2, rxy) * RadToDeg;
        lonDeg = AngleMath.NormalizeDegrees(lon2);
        latDeg = lat2;
    }
}
