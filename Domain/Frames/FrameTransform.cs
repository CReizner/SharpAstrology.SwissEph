// Ported in concept from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Frame-conversion facade that orchestrates the Domain primitives:
//   • Precession.Apply / BuildMatrixFromJ2000
//   • Nutation.Compute / BuildMatrix
//   • mean obliquity (Precession.MeanObliquity)
// Concept follows swi_coortrf in swephlib.c#L279-L291 and the apparent-of-date
// composition documented in app_pos_etc_plan (sweph.c).

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Convenience facade that combines precession + nutation + obliquity into
/// the four common rotations used by the body-position pipeline. Pure
/// rotations only — no light-time, no aberration, no deflection (those
/// belong to <see cref="Aberration"/> and <see cref="GravitationalDeflection"/>).
/// </summary>
internal static class FrameTransform
{
    /// <summary>
    /// Rotate equatorial Cartesian coordinates to ecliptic Cartesian
    /// coordinates given the obliquity ε. Mirrors <c>swi_coortrf</c> with
    /// <c>eps</c> positive (swephlib.c#L279-L291).
    /// </summary>
    /// <param name="xyz">In/out — equatorial vector becomes ecliptic vector.</param>
    /// <param name="obliquityRad">Obliquity ε in radians (mean or true).</param>
    public static void EquatorialToEcliptic(Span<double> xyz, double obliquityRad)
    {
        if (xyz.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyz));
        var sin = System.Math.Sin(obliquityRad);
        var cos = System.Math.Cos(obliquityRad);
        var x1 = xyz[0];
        var y1 = xyz[1] * cos + xyz[2] * sin;
        var z1 = -xyz[1] * sin + xyz[2] * cos;
        xyz[0] = x1;
        xyz[1] = y1;
        xyz[2] = z1;
    }

    /// <summary>
    /// Rotate ecliptic Cartesian coordinates to equatorial Cartesian
    /// coordinates. Mirrors <c>swi_coortrf</c> with <c>eps</c> negated
    /// (swephlib.c#L279-L291).
    /// </summary>
    public static void EclipticToEquatorial(Span<double> xyz, double obliquityRad)
        => EquatorialToEcliptic(xyz, -obliquityRad);

    /// <summary>
    /// Maps an ICRS-equatorial position vector at J2000 to the
    /// apparent (true) equator-and-equinox of date: precession +
    /// nutation. Frame bias (the ICRS ↔ J2000 rotation) is applied
    /// separately by the body-position pipeline.
    /// </summary>
    /// <param name="xyz">In/out: ICRS at J2000 → true-of-date.</param>
    /// <param name="jdTargetTt">Target epoch (Julian Day, TT).</param>
    /// <param name="overrides">
    /// Model selectors. Defaults to Vondrák long-term precession + IAU
    /// 2000B nutation when <see langword="null"/>.
    /// </param>
    public static void IcrsToTrueOfDate(Span<double> xyz, double jdTargetTt, AstronomicalModelOverrides? overrides = null)
    {
        if (xyz.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyz));
        var models = overrides ?? AstronomicalModelOverrides.Default;
        Precession.Apply(xyz, AstronomicalConstants.J2000, jdTargetTt, models);
        var meanEps = Precession.MeanObliquity(jdTargetTt, models);
        var nut = Nutation.Compute(jdTargetTt, models);
        Nutation.Apply(xyz, nut, meanEps, backward: false);
    }

    /// <summary>
    /// Inverse of <see cref="IcrsToTrueOfDate"/>: True-of-date → J2000.
    /// </summary>
    public static void TrueOfDateToIcrs(Span<double> xyz, double jdSourceTt, AstronomicalModelOverrides? overrides = null)
    {
        if (xyz.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyz));
        var models = overrides ?? AstronomicalModelOverrides.Default;
        var meanEps = Precession.MeanObliquity(jdSourceTt, models);
        var nut = Nutation.Compute(jdSourceTt, models);
        Nutation.Apply(xyz, nut, meanEps, backward: true);
        Precession.Apply(xyz, jdSourceTt, AstronomicalConstants.J2000, models);
    }
}
