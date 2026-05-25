// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Spherical-rotation helpers ported from <c>swe_cotrans</c> /
/// <c>swi_coortrf</c> (swephlib.c#L223 / #L279). All rotations are about the
/// X-axis (the "ecliptic ↔ equatorial" axis): positive <paramref name="epsDeg"/>
/// converts equatorial → ecliptic, negative converts ecliptic → equatorial,
/// matching the C convention.
/// </summary>
internal static class CoordinateRotation
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;

    /// <summary>
    /// In-place spherical rotation of a (longitude, latitude) pair (degrees)
    /// about the X-axis. Mirrors <c>swe_cotrans(xpo, xpn, eps)</c> for the
    /// position-only path (radius normalised to 1, hence the third component
    /// is unused).
    /// </summary>
    public static void Rotate(ref double lonDeg, ref double latDeg, double epsDeg)
    {
        var lon = lonDeg * DegToRad;
        var lat = latDeg * DegToRad;
        var (sinLon, cosLon) = System.Math.SinCos(lon);
        var (sinLat, cosLat) = System.Math.SinCos(lat);
        var x = cosLon * cosLat;
        var y = sinLon * cosLat;
        var z = sinLat;
        var (sineps, coseps) = System.Math.SinCos(epsDeg * DegToRad);
        var y2 = y * coseps + z * sineps;
        var z2 = -y * sineps + z * coseps;
        var rxy = System.Math.Sqrt(x * x + y2 * y2);
        var lon2 = System.Math.Atan2(y2, x) * RadToDeg;
        var lat2 = System.Math.Atan2(z2, rxy) * RadToDeg;
        lonDeg = AngleMath.NormalizeDegrees(lon2);
        latDeg = lat2;
    }
}
