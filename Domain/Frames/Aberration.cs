// Ported from swisseph-master/sweph.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: sweph.c
//   aberr_light            (helper, position-only)        — lines 3647-3663
//   swi_aberr_light_ex     (with light-time correction)   — lines 3672-3692
//   swi_aberr_light        (annual aberration + speed)    — lines 3699-3736

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Annual aberration. Faithful port of <c>swi_aberr_light</c>: relativistic
/// formula, valid in the planet's barycentric (apparent geocentric) frame
/// once the ICRS body vector has been corrected for light-time.
/// </summary>
internal static class Aberration
{
    private const double Au = AstronomicalConstants.AstronomicalUnitMeters;
    private const double C = AstronomicalConstants.SpeedOfLightMeters;
    private const double SecondsPerDay = AstronomicalConstants.SecondsPerDay;

    /// <summary>
    /// PLAN_SPEED_INTV — finite-difference time interval used for the speed
    /// correction. Mirrors <c>PLAN_SPEED_INTV</c> at <c>sweph.h#L299</c>
    /// (8.64 seconds, expressed in days).
    /// </summary>
    private const double SpeedIntervalDays = 0.0001;

    /// <summary>
    /// Applies annual aberration to the position vector
    /// <paramref name="xyz"/> at index 0..2. If <paramref name="xyz"/> has
    /// length 6 and <paramref name="includeSpeedCorrection"/> is set, the
    /// velocity at index 3..5 is also corrected (mirrors the
    /// <c>SEFLG_SPEED</c> branch of <c>swi_aberr_light</c>).
    /// </summary>
    /// <param name="xyz">In/out — position (and optionally velocity) of the body relative to the observer, AU and AU/day.</param>
    /// <param name="earthBaryVelocity">Earth's barycentric velocity in AU/day; need only first 3 elements.</param>
    /// <param name="includeSpeedCorrection">If true, requires <paramref name="xyz"/>.Length ≥ 6 and updates xyz[3..5].</param>
    public static void Apply(Span<double> xyz, ReadOnlySpan<double> earthBaryVelocity, bool includeSpeedCorrection = false)
    {
        if (xyz.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyz));
        if (includeSpeedCorrection && xyz.Length < 6)
            throw new ArgumentException("Speed correction requires xyz of length 6.", nameof(xyz));
        if (earthBaryVelocity.Length < 3)
            throw new ArgumentException("Earth velocity must contain at least 3 doubles.", nameof(earthBaryVelocity));

        Span<double> xxs = stackalloc double[6];
        Span<double> u = stackalloc double[3];
        Span<double> v = stackalloc double[3];
        Span<double> xx2 = stackalloc double[3];
        var includeSpeed = includeSpeedCorrection;
        for (var i = 0; i < 3; i++)
        {
            xxs[i] = xyz[i];
            u[i] = xyz[i];
        }
        if (includeSpeed)
            for (var i = 0; i < 3; i++) xxs[i + 3] = xyz[i + 3];

        var ru = System.Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        // v = velocity / c, but velocities supplied in AU/day → convert.
        // sweph.c#L3710: v[i] = xe[i+3] / 86400 / CLIGHT * AUNIT.
        var velScale = Au / SecondsPerDay / C;
        for (var i = 0; i < 3; i++)
            v[i] = earthBaryVelocity[i] * velScale;
        var v2 = v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
        var bInv = System.Math.Sqrt(1 - v2);
        var f1 = (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) / ru;
        var f2 = 1.0 + f1 / (1.0 + bInv);
        for (var i = 0; i < 3; i++)
            xyz[i] = (bInv * xyz[i] + f2 * ru * v[i]) / (1.0 + f1);

        if (!includeSpeed)
            return;

        // Speed correction by finite differencing across PLAN_SPEED_INTV.
        // sweph.c#L3717-L3735.
        for (var i = 0; i < 3; i++)
            u[i] = xxs[i] - SpeedIntervalDays * xxs[i + 3];
        var ru2 = System.Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        var f1b = (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) / ru2;
        var f2b = 1.0 + f1b / (1.0 + bInv);
        for (var i = 0; i < 3; i++)
            xx2[i] = (bInv * u[i] + f2b * ru2 * v[i]) / (1.0 + f1b);
        for (var i = 0; i < 3; i++)
        {
            var dx1 = xyz[i] - xxs[i];
            var dx2 = xx2[i] - u[i];
            dx1 -= dx2;
            xyz[i + 3] += dx1 / SpeedIntervalDays;
        }
    }
}
