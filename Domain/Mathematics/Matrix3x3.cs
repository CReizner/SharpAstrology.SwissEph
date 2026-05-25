// Ported in concept from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Stack-allocated 3×3 row-major matrix. Used by precession and nutation
/// to compose rotations; replaces the <c>double[3][3]</c> arrays in the C
/// library (e.g. <c>nu-&gt;matrix[3][3]</c> in <c>sweph.c#L5073</c>).
/// </summary>
/// <remarks>
/// Storage is row-major: <see cref="M00"/>, <see cref="M01"/>, <see cref="M02"/>
/// is row 0; multiplying with a column vector follows the conventional
/// <c>v' = M · v</c> with <c>v'_i = M[i,0]·v0 + M[i,1]·v1 + M[i,2]·v2</c>.
/// </remarks>
internal readonly record struct Matrix3x3(
    double M00, double M01, double M02,
    double M10, double M11, double M12,
    double M20, double M21, double M22)
{
    /// <summary>The identity matrix.</summary>
    public static Matrix3x3 Identity => new(
        1, 0, 0,
        0, 1, 0,
        0, 0, 1);

    /// <summary>
    /// Rotation about the X axis by <paramref name="radians"/>. Mirrors
    /// <c>swi_coortrf</c>: positive <paramref name="radians"/> rotates the
    /// y-z plane such that a point on +y moves toward +z (right-hand rule).
    /// </summary>
    public static Matrix3x3 RotationX(double radians)
    {
        var (s, c) = System.Math.SinCos(radians);
        return new Matrix3x3(
            1, 0, 0,
            0, c, s,
            0, -s, c);
    }

    /// <summary>Rotation about the Y axis by <paramref name="radians"/> (right-hand rule).</summary>
    public static Matrix3x3 RotationY(double radians)
    {
        var (s, c) = System.Math.SinCos(radians);
        return new Matrix3x3(
            c, 0, -s,
            0, 1, 0,
            s, 0, c);
    }

    /// <summary>Rotation about the Z axis by <paramref name="radians"/> (right-hand rule).</summary>
    public static Matrix3x3 RotationZ(double radians)
    {
        var (s, c) = System.Math.SinCos(radians);
        return new Matrix3x3(
            c, s, 0,
            -s, c, 0,
            0, 0, 1);
    }

    /// <summary>Returns the matrix product <c>this · right</c>.</summary>
    public Matrix3x3 Multiply(Matrix3x3 right) => new(
        M00 * right.M00 + M01 * right.M10 + M02 * right.M20,
        M00 * right.M01 + M01 * right.M11 + M02 * right.M21,
        M00 * right.M02 + M01 * right.M12 + M02 * right.M22,
        M10 * right.M00 + M11 * right.M10 + M12 * right.M20,
        M10 * right.M01 + M11 * right.M11 + M12 * right.M21,
        M10 * right.M02 + M11 * right.M12 + M12 * right.M22,
        M20 * right.M00 + M21 * right.M10 + M22 * right.M20,
        M20 * right.M01 + M21 * right.M11 + M22 * right.M21,
        M20 * right.M02 + M21 * right.M12 + M22 * right.M22);

    /// <summary>Returns the transposed matrix.</summary>
    public Matrix3x3 Transpose() => new(
        M00, M10, M20,
        M01, M11, M21,
        M02, M12, M22);

    /// <summary>Transforms a vector: <c>v' = this · v</c>.</summary>
    public Vec3 Transform(Vec3 v) => new(
        M00 * v.X + M01 * v.Y + M02 * v.Z,
        M10 * v.X + M11 * v.Y + M12 * v.Z,
        M20 * v.X + M21 * v.Y + M22 * v.Z);

    /// <summary>
    /// Transforms three doubles in-place (or into <paramref name="destination"/>
    /// if it differs). Both spans must be ≥ 3 elements long. Allocation-free.
    /// </summary>
    public void Transform(ReadOnlySpan<double> source, Span<double> destination)
    {
        if (source.Length < 3)
            throw new ArgumentException("Source span must contain at least 3 doubles.", nameof(source));
        if (destination.Length < 3)
            throw new ArgumentException("Destination span must contain at least 3 doubles.", nameof(destination));
        var x = source[0];
        var y = source[1];
        var z = source[2];
        destination[0] = M00 * x + M01 * y + M02 * z;
        destination[1] = M10 * x + M11 * y + M12 * z;
        destination[2] = M20 * x + M21 * y + M22 * z;
    }

    /// <summary>
    /// Transforms three doubles in <paramref name="vector"/> in place.
    /// Allocation-free. Length must be ≥ 3.
    /// </summary>
    public void TransformInPlace(Span<double> vector)
    {
        if (vector.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(vector));
        var x = vector[0];
        var y = vector[1];
        var z = vector[2];
        vector[0] = M00 * x + M01 * y + M02 * z;
        vector[1] = M10 * x + M11 * y + M12 * z;
        vector[2] = M20 * x + M21 * y + M22 * z;
    }
}
