// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Cartesian ↔ polar conversions, with and without speed. Replaces
/// <c>swi_cartpol</c>, <c>swi_polcart</c>, <c>swi_cartpol_sp</c>, and
/// <c>swi_polcart_sp</c> from <c>swephlib.c</c>.
/// </summary>
/// <remarks>
/// Output longitudes are normalized to <c>[0, 2π)</c> radians. Latitudes are in
/// <c>[-π/2, +π/2]</c> radians. Distances are unchanged.
/// </remarks>
internal static class Polar
{
    /// <summary>
    /// Converts a Cartesian position to polar (longitude, latitude, radius), all
    /// in radians/length units.
    /// </summary>
    public static Vec3 CartesianToPolar(Vec3 cartesian)
    {
        if (cartesian.X == 0.0 && cartesian.Y == 0.0 && cartesian.Z == 0.0)
            return Vec3.Zero;

        var rxy2 = cartesian.X * cartesian.X + cartesian.Y * cartesian.Y;
        var radius = System.Math.Sqrt(rxy2 + cartesian.Z * cartesian.Z);
        var rxy = System.Math.Sqrt(rxy2);

        var longitude = System.Math.Atan2(cartesian.Y, cartesian.X);
        if (longitude < 0.0)
            longitude += AstronomicalConstants.TwoPi;

        var latitude = rxy == 0.0
            ? (cartesian.Z >= 0.0 ? AstronomicalConstants.Pi / 2.0 : -AstronomicalConstants.Pi / 2.0)
            : System.Math.Atan(cartesian.Z / rxy);

        return new Vec3(longitude, latitude, radius);
    }

    /// <summary>
    /// Converts a polar (longitude, latitude, radius) triple to Cartesian.
    /// Inputs are in radians.
    /// </summary>
    public static Vec3 PolarToCartesian(Vec3 polar)
    {
        var (sinLon, cosLon) = System.Math.SinCos(polar.X);
        var (sinLat, cosLat) = System.Math.SinCos(polar.Y);
        return new Vec3(
            polar.Z * cosLat * cosLon,
            polar.Z * cosLat * sinLon,
            polar.Z * sinLat);
    }

    /// <summary>
    /// Cartesian (position + velocity) → polar (longitude, latitude, radius,
    /// dλ/dt, dβ/dt, dr/dt). Mirrors <c>swi_cartpol_sp</c> exactly. Inputs and
    /// outputs are six-component spans.
    /// </summary>
    public static void CartesianToPolarWithSpeed(ReadOnlySpan<double> source, Span<double> destination)
    {
        if (source.Length < 6) throw new ArgumentException("Source span must contain 6 doubles.", nameof(source));
        if (destination.Length < 6) throw new ArgumentException("Destination span must contain 6 doubles.", nameof(destination));

        // Zero position: fall back to direction-of-motion of the velocity vector.
        if (source[0] == 0.0 && source[1] == 0.0 && source[2] == 0.0)
        {
            var vel = new Vec3(source[3], source[4], source[5]);
            var velPolar = CartesianToPolar(vel);
            destination[0] = velPolar.X;
            destination[1] = velPolar.Y;
            destination[2] = 0.0;
            destination[3] = 0.0;
            destination[4] = 0.0;
            destination[5] = velPolar.Z;
            return;
        }

        // Zero velocity: position-only conversion, leaves speeds at 0.
        if (source[3] == 0.0 && source[4] == 0.0 && source[5] == 0.0)
        {
            var posPolar = CartesianToPolar(new Vec3(source[0], source[1], source[2]));
            destination[0] = posPolar.X;
            destination[1] = posPolar.Y;
            destination[2] = posPolar.Z;
            destination[3] = 0.0;
            destination[4] = 0.0;
            destination[5] = 0.0;
            return;
        }

        var rxy2 = source[0] * source[0] + source[1] * source[1];
        var radius = System.Math.Sqrt(rxy2 + source[2] * source[2]);
        var rxy = System.Math.Sqrt(rxy2);

        var lon = System.Math.Atan2(source[1], source[0]);
        if (lon < 0.0) lon += AstronomicalConstants.TwoPi;
        var lat = System.Math.Atan(source[2] / rxy);

        var cosLon = source[0] / rxy;
        var sinLon = source[1] / rxy;
        var cosLat = rxy / radius;
        var sinLat = source[2] / radius;

        var v0 = source[3] * cosLon + source[4] * sinLon;
        var v1 = -source[3] * sinLon + source[4] * cosLon;
        var dLon = v1 / rxy;

        var v1z = -sinLat * v0 + cosLat * source[5];
        var dRadial = cosLat * v0 + sinLat * source[5];
        var dLat = v1z / radius;

        destination[0] = lon;
        destination[1] = lat;
        destination[2] = radius;
        destination[3] = dLon;
        destination[4] = dLat;
        destination[5] = dRadial;
    }

    /// <summary>
    /// Polar (position + speed) → Cartesian (position + velocity). Mirrors
    /// <c>swi_polcart_sp</c>.
    /// </summary>
    public static void PolarToCartesianWithSpeed(ReadOnlySpan<double> source, Span<double> destination)
    {
        if (source.Length < 6) throw new ArgumentException("Source span must contain 6 doubles.", nameof(source));
        if (destination.Length < 6) throw new ArgumentException("Destination span must contain 6 doubles.", nameof(destination));

        if (source[3] == 0.0 && source[4] == 0.0 && source[5] == 0.0)
        {
            var posPolar = new Vec3(source[0], source[1], source[2]);
            var posCart = PolarToCartesian(posPolar);
            destination[0] = posCart.X;
            destination[1] = posCart.Y;
            destination[2] = posCart.Z;
            destination[3] = 0.0;
            destination[4] = 0.0;
            destination[5] = 0.0;
            return;
        }

        var cosLon = System.Math.Cos(source[0]);
        var sinLon = System.Math.Sin(source[0]);
        var cosLat = System.Math.Cos(source[1]);
        var sinLat = System.Math.Sin(source[1]);

        var x = source[2] * cosLat * cosLon;
        var y = source[2] * cosLat * sinLon;
        var z = source[2] * sinLat;

        var rxyz = source[2];
        var rxy = System.Math.Sqrt(x * x + y * y);

        var dRadial = source[5];
        var dLat = source[4] * rxyz;

        var velZ = sinLat * dRadial + cosLat * dLat;
        var v3 = cosLat * dRadial - sinLat * dLat;
        var v4 = source[3] * rxy;
        var velX = cosLon * v3 - sinLon * v4;
        var velY = sinLon * v3 + cosLon * v4;

        destination[0] = x;
        destination[1] = y;
        destination[2] = z;
        destination[3] = velX;
        destination[4] = velY;
        destination[5] = velZ;
    }
}
