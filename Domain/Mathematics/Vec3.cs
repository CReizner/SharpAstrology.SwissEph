// Ported in concept from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   Vec3      — replaces the double[3] arrays used pervasively in the C library
//   Cross     — swi_cross_prod (swephlib.c)
//   Normalize — zero-magnitude returns the all-zero sentinel, matching the C library

using System;

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Stack-allocated three-component vector with double precision. Used for
/// positions, velocities, polar coordinates, and Cartesian unit vectors.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => default;

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => System.Math.Sqrt(LengthSquared);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 v) => new(-v.X, -v.Y, -v.Z);
    public static Vec3 operator *(Vec3 v, double s) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vec3 operator *(double s, Vec3 v) => v * s;
    public static Vec3 operator /(Vec3 v, double s) => new(v.X / s, v.Y / s, v.Z / s);

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Cross product <c>a × b</c>.</summary>
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>
    /// Returns a unit vector aligned with <paramref name="v"/>. If the magnitude is
    /// zero, returns <see cref="Zero"/>.
    /// </summary>
    public static Vec3 Normalize(Vec3 v)
    {
        var len = v.Length;
        return len == 0.0 ? Zero : v / len;
    }

    /// <summary>Reads three doubles from a span. Throws if the span is too short.</summary>
    public static Vec3 FromSpan(ReadOnlySpan<double> source)
    {
        if (source.Length < 3)
            throw new ArgumentException("Source span must contain at least 3 doubles.", nameof(source));
        return new Vec3(source[0], source[1], source[2]);
    }

    /// <summary>Writes the components into a span. Throws if the destination is too short.</summary>
    public void WriteTo(Span<double> destination)
    {
        if (destination.Length < 3)
            throw new ArgumentException("Destination span must contain at least 3 doubles.", nameof(destination));
        destination[0] = X;
        destination[1] = Y;
        destination[2] = Z;
    }
}
