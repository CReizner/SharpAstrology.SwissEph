// Ported from swisseph-master/sweph.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: sweph.c
//   meff       (sun mass-distribution interpolation)  — lines 5967-5981
//   eff_arr    (table)                                — lines 5858-5965
//   swi_deflect_light                                 — lines 3743-3920

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Relativistic deflection of light by the Sun (PPN γ = 1). The Domain API
/// takes the Earth heliocentric vector explicitly so it stays I/O-free.
/// </summary>
internal static class GravitationalDeflection
{
    /// <summary>Heliocentric gravitational parameter G·M☉ (m³/s²).</summary>
    public const double HelioGravitationalParameter = 1.32712440017987e20;

    /// <summary>Solar angular radius (radians).</summary>
    public const double SunAngularRadiusRad = 959.63 / 3600.0 * AstronomicalConstants.DegToRad;

    private const double Au = AstronomicalConstants.AstronomicalUnitMeters;
    private const double C = AstronomicalConstants.SpeedOfLightMeters;
    private const double DefSpeedIntervalDays = 0.0000005; // DEFL_SPEED_INTV at sweph.h#L304.

    /// <summary>
    /// Applies relativistic Sun deflection in place to the
    /// observer-relative (apparent geocentric) position
    /// <paramref name="xyzApparent"/>.
    /// </summary>
    /// <param name="xyzApparent">In/out — body position, observer-relative, AU. Optionally length-6 with velocity for the speed branch.</param>
    /// <param name="earthHelio">Earth heliocentric position (AU); X/Y/Z in 0..2.</param>
    /// <param name="earthHelioVelocity">Earth heliocentric velocity (AU/day); X/Y/Z in 0..2. Required only for the speed branch.</param>
    /// <param name="sunBaryRelativeToEarthAtMinusDt">Optional: barycentric Sun position at <c>t - dt</c> minus Earth's barycentric position at <c>t</c>; if not provided we treat earth-helio as the Earth-Sun vector. The C library fills this from cached barycentric vectors when JPL/SwissEph is selected.</param>
    /// <param name="includeSpeedCorrection">Apply finite-difference velocity correction (requires xyzApparent.Length ≥ 6).</param>
    public static void Apply(
        Span<double> xyzApparent,
        ReadOnlySpan<double> earthHelio,
        ReadOnlySpan<double> earthHelioVelocity,
        bool includeSpeedCorrection = false,
        ReadOnlySpan<double> sunBaryRelativeToEarthAtMinusDt = default)
    {
        if (xyzApparent.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyzApparent));
        if (includeSpeedCorrection && xyzApparent.Length < 6)
            throw new ArgumentException("Speed correction requires xyzApparent of length 6.", nameof(xyzApparent));
        if (earthHelio.Length < 3)
            throw new ArgumentException("Earth-helio span must contain at least 3 doubles.", nameof(earthHelio));
        if (includeSpeedCorrection && earthHelioVelocity.Length < 3)
            throw new ArgumentException("Earth-helio velocity span must contain at least 3 doubles.", nameof(earthHelioVelocity));

        Span<double> u = stackalloc double[3];
        Span<double> e = stackalloc double[3];
        Span<double> q = stackalloc double[3];
        // Position-only path matches sweph.c#L3756-L3818 with iephe == Moshier
        // (i.e. earth helio = earth bary, no sun bary subtraction).
        for (var i = 0; i < 3; i++)
        {
            u[i] = xyzApparent[i];
            e[i] = earthHelio[i];
            q[i] = xyzApparent[i] + earthHelio[i] - (sunBaryRelativeToEarthAtMinusDt.Length >= 3 ? sunBaryRelativeToEarthAtMinusDt[i] : 0.0);
        }
        var ru = System.Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        var rq = System.Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2]);
        var re = System.Math.Sqrt(e[0] * e[0] + e[1] * e[1] + e[2] * e[2]);
        for (var i = 0; i < 3; i++)
        {
            u[i] /= ru;
            q[i] /= rq;
            e[i] /= re;
        }
        var uq = u[0] * q[0] + u[1] * q[1] + u[2] * q[2];
        var ue = u[0] * e[0] + u[1] * e[1] + u[2] * e[2];
        var qe = q[0] * e[0] + q[1] * e[1] + q[2] * e[2];
        var sina = System.Math.Sqrt(System.Math.Max(0.0, 1 - ue * ue));
        var sinSunR = SunAngularRadiusRad / re;
        var meffFact = sina < sinSunR ? Meff(sina / sinSunR) : 1.0;
        var g1 = 2.0 * HelioGravitationalParameter * meffFact / C / C / Au / re;
        var g2 = 1.0 + qe;
        Span<double> xx2 = stackalloc double[3];
        for (var i = 0; i < 3; i++)
            xx2[i] = ru * (u[i] + g1 / g2 * (uq * e[i] - ue * q[i]));

        if (!includeSpeedCorrection)
        {
            xyzApparent[0] = xx2[0];
            xyzApparent[1] = xx2[1];
            xyzApparent[2] = xx2[2];
            return;
        }

        // Speed correction: same calculation at t-dtsp, finite-difference. sweph.c#L3819-L3905.
        var dtsp = -DefSpeedIntervalDays;
        Span<double> u2 = stackalloc double[3];
        Span<double> e2 = stackalloc double[3];
        Span<double> q2 = stackalloc double[3];
        for (var i = 0; i < 3; i++)
        {
            u2[i] = xyzApparent[i] - dtsp * xyzApparent[i + 3];
            e2[i] = earthHelio[i] - dtsp * earthHelioVelocity[i];
            q2[i] = u2[i] + earthHelio[i] - (sunBaryRelativeToEarthAtMinusDt.Length >= 3 ? sunBaryRelativeToEarthAtMinusDt[i] : 0.0)
                    - dtsp * earthHelioVelocity[i];
        }
        var ru2 = System.Math.Sqrt(u2[0] * u2[0] + u2[1] * u2[1] + u2[2] * u2[2]);
        var rq2 = System.Math.Sqrt(q2[0] * q2[0] + q2[1] * q2[1] + q2[2] * q2[2]);
        var re2 = System.Math.Sqrt(e2[0] * e2[0] + e2[1] * e2[1] + e2[2] * e2[2]);
        for (var i = 0; i < 3; i++)
        {
            u2[i] /= ru2;
            q2[i] /= rq2;
            e2[i] /= re2;
        }
        var uq2 = u2[0] * q2[0] + u2[1] * q2[1] + u2[2] * q2[2];
        var ue2 = u2[0] * e2[0] + u2[1] * e2[1] + u2[2] * e2[2];
        var qe2 = q2[0] * e2[0] + q2[1] * e2[1] + q2[2] * e2[2];
        sina = System.Math.Sqrt(System.Math.Max(0.0, 1 - ue2 * ue2));
        sinSunR = SunAngularRadiusRad / re2;
        meffFact = sina < sinSunR ? Meff(sina / sinSunR) : 1.0;
        g1 = 2.0 * HelioGravitationalParameter * meffFact / C / C / Au / re2;
        g2 = 1.0 + qe2;
        Span<double> xx3 = stackalloc double[3];
        for (var i = 0; i < 3; i++)
            xx3[i] = ru2 * (u2[i] + g1 / g2 * (uq2 * e2[i] - ue2 * q2[i]));
        for (var i = 0; i < 3; i++)
        {
            var dx1 = xx2[i] - xyzApparent[i];
            var dx2 = xx3[i] - (xyzApparent[i] - dtsp * xyzApparent[i + 3]);
            dx1 -= dx2;
            xyzApparent[i + 3] += dx1 / dtsp;
        }
        xyzApparent[0] = xx2[0];
        xyzApparent[1] = xx2[1];
        xyzApparent[2] = xx2[2];
    }

    /// <summary>
    /// Effective Sun mass within the disc, interpolated from the embedded mass-
    /// distribution table. <paramref name="r"/> is the fractional distance from
    /// sun centre (0 = centre, 1 = limb); outside [0,1] the value clamps to 0/1.
    /// </summary>
    public static double Meff(double r)
    {
        if (r <= 0) return 0.0;
        if (r >= 1) return 1.0;
        var i = 0;
        while (s_meffR[i] > r)
            i++;
        var f = (r - s_meffR[i - 1]) / (s_meffR[i] - s_meffR[i - 1]);
        return s_meffM[i - 1] + f * (s_meffM[i] - s_meffM[i - 1]);
    }

    /// <summary>Sun mass-distribution table for the non-point-mass deflection model.</summary>
    private static readonly double[] s_meffR =
    {
        1.000, 0.990, 0.980, 0.970, 0.960, 0.950, 0.940, 0.930, 0.920, 0.910,
        0.900, 0.890, 0.880, 0.870, 0.860, 0.850, 0.840, 0.830, 0.820, 0.810,
        0.800, 0.790, 0.780, 0.770, 0.760, 0.750, 0.740, 0.730, 0.720, 0.710,
        0.700, 0.690, 0.680, 0.670, 0.660, 0.650, 0.640, 0.630, 0.620, 0.610,
        0.600, 0.590, 0.580, 0.570, 0.560, 0.550, 0.540, 0.530, 0.520, 0.510,
        0.500, 0.490, 0.480, 0.470, 0.460, 0.450, 0.440, 0.430, 0.420, 0.410,
        0.400, 0.390, 0.380, 0.370, 0.360, 0.350, 0.340, 0.330, 0.320, 0.310,
        0.300, 0.290, 0.280, 0.270, 0.260, 0.250, 0.240, 0.230, 0.220, 0.210,
        0.200, 0.190, 0.180, 0.170, 0.160, 0.150, 0.140, 0.130, 0.120, 0.110,
        0.100, 0.090, 0.080, 0.070, 0.060, 0.050, 0.040, 0.030, 0.020, 0.010,
        0.000,
    };

    private static readonly double[] s_meffM =
    {
        1.000000, 0.999979, 0.999940, 0.999881, 0.999811, 0.999724, 0.999622, 0.999497, 0.999354, 0.999192,
        0.999000, 0.998786, 0.998535, 0.998242, 0.997919, 0.997571, 0.997198, 0.996792, 0.996316, 0.995791,
        0.995226, 0.994625, 0.993991, 0.993326, 0.992598, 0.991770, 0.990873, 0.989919, 0.988912, 0.987856,
        0.986755, 0.985610, 0.984398, 0.982986, 0.981437, 0.979779, 0.978024, 0.976182, 0.974256, 0.972253,
        0.970174, 0.968024, 0.965594, 0.962797, 0.959758, 0.956515, 0.953088, 0.949495, 0.945741, 0.941838,
        0.937790, 0.933563, 0.928668, 0.923288, 0.917527, 0.911432, 0.905035, 0.898353, 0.891022, 0.882940,
        0.874312, 0.865206, 0.855423, 0.844619, 0.833074, 0.820876, 0.808031, 0.793962, 0.778931, 0.763021,
        0.745815, 0.727557, 0.708234, 0.687583, 0.665741, 0.642597, 0.618252, 0.592586, 0.565747, 0.537697,
        0.508554, 0.478420, 0.447322, 0.415454, 0.382892, 0.349955, 0.316691, 0.283565, 0.250431, 0.218327,
        0.186794, 0.156287, 0.128421, 0.102237, 0.077393, 0.054833, 0.036361, 0.020953, 0.009645, 0.002767,
        0.000000,
    };
}
