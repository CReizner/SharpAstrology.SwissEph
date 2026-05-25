// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   NormalizeDegrees          — swe_degnorm
//   NormalizeRadians          — swe_radnorm
//   DifferenceDegreesUnsigned — swe_difdegn
//   DifferenceDegreesSigned   — swe_difdeg2n
//   DifferenceRadiansSigned   — swe_difrad2n
//   DegreeMidpoint            — swe_deg_midp
//   RadianMidpoint            — swe_rad_midp
//   Epsilon clamp inside NormalizeDegrees follows the Alois fix at swephlib.c:110.
using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Angle helpers: degree/radian normalization and signed/unsigned distance functions.
/// </summary>
internal static class AngleMath
{
    private const double Epsilon = 1e-13;

    /// <summary>Reduces an angle in degrees to the half-open interval <c>[0, 360)</c>.</summary>
    public static double NormalizeDegrees(double x)
    {
        var y = x % 360.0;
        if (System.Math.Abs(y) < Epsilon) y = 0.0;
        if (y < 0.0) y += 360.0;
        return y;
    }

    /// <summary>Reduces an angle in radians to <c>[0, 2π)</c>.</summary>
    public static double NormalizeRadians(double x)
    {
        var y = x % AstronomicalConstants.TwoPi;
        if (System.Math.Abs(y) < Epsilon) y = 0.0;
        if (y < 0.0) y += AstronomicalConstants.TwoPi;
        return y;
    }

    /// <summary>Unsigned distance in degrees, normalized to <c>[0, 360)</c>.</summary>
    public static double DifferenceDegreesUnsigned(double a, double b) => NormalizeDegrees(a - b);

    /// <summary>Signed distance in degrees, normalized to <c>[-180, +180)</c>.</summary>
    public static double DifferenceDegreesSigned(double a, double b)
    {
        var d = NormalizeDegrees(a - b);
        return d >= 180.0 ? d - 360.0 : d;
    }

    /// <summary>Signed distance in radians, normalized to <c>[-π, +π)</c>.</summary>
    public static double DifferenceRadiansSigned(double a, double b)
    {
        var d = NormalizeRadians(a - b);
        return d >= AstronomicalConstants.Pi ? d - AstronomicalConstants.TwoPi : d;
    }

    /// <summary>
    /// Midpoint between two angles in degrees, taken along the short arc.
    /// </summary>
    public static double DegreeMidpoint(double x1, double x0)
    {
        var d = DifferenceDegreesSigned(x1, x0);
        return NormalizeDegrees(x0 + d / 2.0);
    }

    /// <summary>Midpoint in radians.</summary>
    public static double RadianMidpoint(double x1, double x0) =>
        AstronomicalConstants.DegToRad * DegreeMidpoint(x1 * AstronomicalConstants.RadToDeg, x0 * AstronomicalConstants.RadToDeg);
}
