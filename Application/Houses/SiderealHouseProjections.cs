// Ported from swisseph-master/swehouse.c sidereal_houses_ecl_t0 (#L318-L403)
// and sidereal_houses_ssypl (#L425-L532). Original license: see
// LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses;

/// <summary>
/// Geometry helpers for the two non-traditional sidereal house projections
/// — <c>SE_SIDBIT_ECL_T0</c> (project onto ecliptic of T0) and
/// <c>SE_SIDBIT_SSY_PLANE</c> (project onto solar-system rotation plane).
/// Each helper computes the auxiliary ARMC, auxiliary obliquity, and the
/// constant offset that must be subtracted from the cusps and the
/// non-ARMC ascmc points after the standard tropical house computation.
/// </summary>
internal static class SiderealHouseProjections
{
    // sweph.h#L291-L295 — ascending node and inclination of the ecliptic on
    // the solar-system rotation plane.
    private const double SsyPlaneNodeE2000Rad = 107.582569 * AstronomicalConstants.DegToRad;
    private const double SsyPlaneNodeRad     = 107.58883388 * AstronomicalConstants.DegToRad;
    private const double SsyPlaneInclRad     = 1.578701 * AstronomicalConstants.DegToRad;

    /// <summary>
    /// Result of the auxiliary computation: aux ARMC and aux obliquity to
    /// feed into <see cref="HouseService.ComputeFromArmcInto"/>, plus the
    /// constant degree offset to subtract from cusp/ascmc afterwards.
    /// </summary>
    internal readonly record struct Auxiliary(double ArmcDeg, double ObliquityDeg, double ShiftDeg);

    /// <summary>
    /// Mirrors <c>sidereal_houses_ecl_t0</c> (swehouse.c#L318-L403): rotate
    /// the t0 vernal point onto the true equator of <paramref name="tjdeTt"/>,
    /// extract the auxiliary armc/obliquity, and return the additive shift
    /// (<c>dvpxe + ayanT0</c>) for the cusps.
    /// </summary>
    public static Auxiliary EclT0(
        double tjdeTt,
        double armcDeg,
        double trueEpsDeg,
        double nutLonDeg,
        double nutObqDeg,
        double t0Tt,
        double ayanT0Deg,
        AstronomicalModelOverrides? models = null)
    {
        var meanEpsDeg = trueEpsDeg - nutObqDeg;
        var epsT0Rad = Precession.MeanObliquity(t0Tt, models);

        // x[0..2] = position (1,0,0) on ecliptic of t0; x[3..5] = velocity
        // (0,1,0) — together they span the t0-ecliptic plane.
        Span<double> x = stackalloc double[6];
        x[0] = 1.0;
        x[4] = 1.0;

        // ecliptic of t0 → equator of t0  (swi_coortrf with -eps_t0)
        FrameTransform.EclipticToEquatorial(x.Slice(0, 3), epsT0Rad);
        FrameTransform.EclipticToEquatorial(x.Slice(3, 3), epsT0Rad);

        // equator of t0 → equator of tjde, via J2000 (Precession.Apply
        // takes care of the two-step journey when neither end is J2000).
        Precession.Apply(x.Slice(0, 3), t0Tt, tjdeTt, models);
        Precession.Apply(x.Slice(3, 3), t0Tt, tjdeTt, models);

        ApplyTrueEquatorOfTjde(x, meanEpsDeg, trueEpsDeg, nutLonDeg);

        // Auxiliary obliquity = inclination of (pos × vel) to the equator.
        var xnorm = Vec3.Cross(
            new Vec3(x[0], x[1], x[2]),
            new Vec3(x[3], x[4], x[5]));
        var rxy = System.Math.Sqrt(xnorm.X * xnorm.X + xnorm.Y * xnorm.Y);
        var rxyz = System.Math.Sqrt(rxy * rxy + xnorm.Z * xnorm.Z);
        var epsxRad = System.Math.Asin(rxy / rxyz);

        // Auxiliary vernal point: project x onto the equator along the
        // velocity direction (so xvpx[2] = 0). The C source guards x[5]≈0
        // to avoid a division blow-up, then signs the result with sgn(x[5]).
        if (System.Math.Abs(x[5]) < 1e-15) x[5] = 1e-15;
        var fac = x[2] / x[5];
        var sgn = x[5] >= 0 ? 1.0 : -1.0;
        var xvpx = new Vec3(
            (x[0] - fac * x[3]) * sgn,
            (x[1] - fac * x[4]) * sgn,
            (x[2] - fac * x[5]) * sgn);

        // Distance of aux. vernal point from tjde vernal point on equator.
        var dvpxRad = System.Math.Atan2(xvpx.Y, xvpx.X);
        if (dvpxRad < 0) dvpxRad += AstronomicalConstants.TwoPi;
        var dvpxDeg = dvpxRad * AstronomicalConstants.RadToDeg;

        var armcxDeg = AngleMath.NormalizeDegrees(armcDeg - dvpxDeg);
        var epsxDeg = epsxRad * AstronomicalConstants.RadToDeg;

        // Distance between the auxiliary vernal point and the t0 vernal
        // point (measured on the sidereal plane). Positive for tjde > t0,
        // negative for tjde < t0 (sign-flip mirrors swehouse.c#L388-L389).
        var xPos = new Vec3(x[0], x[1], x[2]);
        var dvpxeRad = System.Math.Acos(Vec3.Dot(xPos, xvpx) / (xPos.Length * xvpx.Length));
        var dvpxeDeg = dvpxeRad * AstronomicalConstants.RadToDeg;
        if (tjdeTt < t0Tt) dvpxeDeg = -dvpxeDeg;

        return new Auxiliary(armcxDeg, epsxDeg, dvpxeDeg + ayanT0Deg);
    }

    /// <summary>
    /// Mirrors <c>sidereal_houses_ssypl</c> (swehouse.c#L425-L532): rotate
    /// the J2000 zero point of the solar-system rotation plane onto the
    /// true equator of <paramref name="tjdeTt"/>, then return the auxiliary
    /// pair plus the additive cusp shift (<c>dvpxe + ayanT0 + x00</c>).
    /// </summary>
    public static Auxiliary SsyPlane(
        double tjdeTt,
        double armcDeg,
        double trueEpsDeg,
        double nutLonDeg,
        double nutObqDeg,
        double t0Tt,
        double ayanT0Deg,
        AstronomicalModelOverrides? models = null)
    {
        var meanEpsDeg = trueEpsDeg - nutObqDeg;
        var eps2000Rad = Precession.MeanObliquity(AstronomicalConstants.J2000, models);

        Span<double> x = stackalloc double[6];
        x[0] = 1.0;
        x[4] = 1.0;

        // Solar-system-rotation-plane → ecliptic 2000:  swi_coortrf(x, -SSY_PLANE_INCL)
        // tilts about X by -incl; then add the ascending-node longitude
        // SSY_PLANE_NODE_E2000 in the ecliptic-2000 frame. The cartpol_sp /
        // polcart_sp pair handles the velocity rotation correctly.
        FrameTransform.EclipticToEquatorial(x.Slice(0, 3), SsyPlaneInclRad);
        FrameTransform.EclipticToEquatorial(x.Slice(3, 3), SsyPlaneInclRad);
        ShiftLongitude(x, SsyPlaneNodeE2000Rad);

        // ecliptic 2000 → equator 2000:  swi_coortrf(x, -eps2000)
        FrameTransform.EclipticToEquatorial(x.Slice(0, 3), eps2000Rad);
        FrameTransform.EclipticToEquatorial(x.Slice(3, 3), eps2000Rad);

        // equator 2000 → mean equator of tjde
        Precession.Apply(x.Slice(0, 3), AstronomicalConstants.J2000, tjdeTt, models);
        Precession.Apply(x.Slice(3, 3), AstronomicalConstants.J2000, tjdeTt, models);

        ApplyTrueEquatorOfTjde(x, meanEpsDeg, trueEpsDeg, nutLonDeg);

        var xnorm = Vec3.Cross(
            new Vec3(x[0], x[1], x[2]),
            new Vec3(x[3], x[4], x[5]));
        var rxy = System.Math.Sqrt(xnorm.X * xnorm.X + xnorm.Y * xnorm.Y);
        var rxyz = System.Math.Sqrt(rxy * rxy + xnorm.Z * xnorm.Z);
        var epsxRad = System.Math.Asin(rxy / rxyz);

        if (System.Math.Abs(x[5]) < 1e-15) x[5] = 1e-15;
        var fac = x[2] / x[5];
        var sgn = x[5] >= 0 ? 1.0 : -1.0;
        var xvpx = new Vec3(
            (x[0] - fac * x[3]) * sgn,
            (x[1] - fac * x[4]) * sgn,
            (x[2] - fac * x[5]) * sgn);

        var dvpxRad = System.Math.Atan2(xvpx.Y, xvpx.X);
        if (dvpxRad < 0) dvpxRad += AstronomicalConstants.TwoPi;
        var dvpxDeg = dvpxRad * AstronomicalConstants.RadToDeg;

        var armcxDeg = AngleMath.NormalizeDegrees(armcDeg - dvpxDeg);
        var epsxDeg = epsxRad * AstronomicalConstants.RadToDeg;

        var xPos = new Vec3(x[0], x[1], x[2]);
        var dvpxeRad = System.Math.Acos(Vec3.Dot(xPos, xvpx) / (xPos.Length * xvpx.Length));
        var dvpxeDeg = dvpxeRad * AstronomicalConstants.RadToDeg;
        // Subtract the SSY-plane node (always — the comment at swehouse.c#L500
        // notes the subtraction is positive for dates after 5400 BC).
        dvpxeDeg -= SsyPlaneNodeRad * AstronomicalConstants.RadToDeg;

        // Ayanamsha on the solar-system plane between t0 and J2000.
        // Position of the t0 zero point measured on the SSY plane.
        Span<double> x0 = stackalloc double[3];
        x0[0] = 1.0;
        if (t0Tt != AstronomicalConstants.J2000)
            Precession.Apply(x0, t0Tt, AstronomicalConstants.J2000, models);
        // equator 2000 → ecliptic 2000:  swi_coortrf(x, +eps2000)
        FrameTransform.EquatorialToEcliptic(x0, eps2000Rad);
        var x0Polar = Polar.CartesianToPolar(new Vec3(x0[0], x0[1], x0[2]));
        // Rotate to the SSY plane: subtract the ascending-node longitude in
        // ecliptic-2000, then tilt by +incl, then add the node measured on
        // the SSY plane.
        var lonAfterNode = AngleMath.NormalizeRadians(x0Polar.X - SsyPlaneNodeE2000Rad);
        var x0Cart = Polar.PolarToCartesian(new Vec3(lonAfterNode, x0Polar.Y, x0Polar.Z));
        Span<double> x0Span = stackalloc double[3] { x0Cart.X, x0Cart.Y, x0Cart.Z };
        // ecliptic 2000 → SSY plane:  swi_coortrf(x, +incl)
        FrameTransform.EquatorialToEcliptic(x0Span, SsyPlaneInclRad);
        var x0Polar2 = Polar.CartesianToPolar(new Vec3(x0Span[0], x0Span[1], x0Span[2]));
        var x00Rad = AngleMath.NormalizeRadians(x0Polar2.X + SsyPlaneNodeRad);
        var x00Deg = x00Rad * AstronomicalConstants.RadToDeg;

        return new Auxiliary(armcxDeg, epsxDeg, dvpxeDeg + ayanT0Deg + x00Deg);
    }

    /// <summary>
    /// Apply the standard "mean-equator → true-equator-of-date" sequence to
    /// the (position, velocity) pair held in <paramref name="x"/>: rotate
    /// to mean ecliptic with +ε_mean, add Δψ in longitude, rotate back to
    /// true equator with -ε_true. Mirrors swehouse.c#L354-L361 / #L464-L471.
    /// </summary>
    private static void ApplyTrueEquatorOfTjde(Span<double> x, double meanEpsDeg, double trueEpsDeg, double nutLonDeg)
    {
        var meanEpsRad = meanEpsDeg * AstronomicalConstants.DegToRad;
        var trueEpsRad = trueEpsDeg * AstronomicalConstants.DegToRad;
        // mean equator of tjde → mean ecliptic of tjde
        FrameTransform.EquatorialToEcliptic(x.Slice(0, 3), meanEpsRad);
        FrameTransform.EquatorialToEcliptic(x.Slice(3, 3), meanEpsRad);
        // add Δψ to longitude (in polar form)
        ShiftLongitude(x, nutLonDeg * AstronomicalConstants.DegToRad);
        // mean ecliptic + Δψ in longitude == true ecliptic; rotate to true
        // equator of tjde via -ε_true.
        FrameTransform.EclipticToEquatorial(x.Slice(0, 3), trueEpsRad);
        FrameTransform.EclipticToEquatorial(x.Slice(3, 3), trueEpsRad);
    }

    /// <summary>
    /// Round-trip <paramref name="x"/> (position + velocity) through polar
    /// coordinates, adding <paramref name="deltaLonRad"/> to the longitude.
    /// Mirrors the <c>cartpol_sp / x[0]+=Δλ / polcart_sp</c> idiom used by
    /// the C-side house projections.
    /// </summary>
    private static void ShiftLongitude(Span<double> x, double deltaLonRad)
    {
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(x, polar);
        polar[0] += deltaLonRad;
        Polar.PolarToCartesianWithSpeed(polar, x);
    }
}
