// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: swephlib.c
//   swi_bias       (GCRS ↔ J2000 frame bias)        — lines 2205-2289
//   swi_icrs2fk5   (GCRS ↔ FK5)                     — lines 2292-2333
//   swi_FK4_FK5    (FK4 → FK5 catalogue conversion) — lines 4098-4123

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Catalogue-coordinate frame conversions used by the fixed-star pipeline:
/// FK4↔FK5, FK5↔ICRS (GCRS), and the GCRS↔J2000 frame bias. These are
/// pure rotations on a six-component (position + velocity) state vector
/// and never touch I/O.
/// </summary>
internal static class CatalogFrameTransforms
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double B1950 = AstronomicalConstants.B1950;
    // Tropical Julian century in days. Mirrors the literal at swephlib.c#L4108.
    private const double TropicalCentury = 36524.2198782;

    /// <summary>
    /// FK4 (B1950 mean equator/equinox) → FK5 (J2000 mean equator/equinox)
    /// catalogue alignment per <i>Explanatory Supplement</i> (1992) p. 167f.
    /// Mirrors <c>swi_FK4_FK5</c> (<c>swephlib.c#L4098-L4112</c>).
    /// </summary>
    /// <param name="state">In/out — six-component position+velocity in
    /// cartesian rectangular form. Zero position is a no-op.</param>
    /// <param name="tjd">Reference epoch (TT) used for the time-dependent
    /// 0.085 ″/century RA-drift term. The C library passes <c>B1950</c>.</param>
    public static void Fk4ToFk5(Span<double> state, double tjd)
    {
        if (state.Length < 6) throw new ArgumentException("State span must contain 6 doubles.", nameof(state));
        if (state[0] == 0.0 && state[1] == 0.0 && state[2] == 0.0)
            return;

        var correctSpeed = state[3] != 0.0;

        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(state, polar);

        // sweph: xp[0] += (0.035 + 0.085 * (tjd - B1950) / 36524.2198782) / 3600 * 15 * DEGTORAD;
        polar[0] += (0.035 + 0.085 * (tjd - B1950) / TropicalCentury) / 3600.0 * 15.0 * DegToRad;
        if (correctSpeed)
            polar[3] += (0.085 / TropicalCentury) / 3600.0 * 15.0 * DegToRad;

        Polar.PolarToCartesianWithSpeed(polar, state);
    }

    /// <summary>
    /// GCRS (ICRS) ↔ FK5 alignment rotation. Mirrors <c>swi_icrs2fk5</c>
    /// (<c>swephlib.c#L2292-L2333</c>). The matrix is the IAU 2006-era
    /// hard-coded value.
    /// </summary>
    /// <param name="state">In/out — six-component state vector.</param>
    /// <param name="includeSpeed">If true the velocity (indices 3..5) is
    /// rotated as well. Mirrors the <c>iflag &amp; SEFLG_SPEED</c> branch
    /// in C.</param>
    /// <param name="backward">When true, rotates GCRS → FK5; when false,
    /// rotates FK5 → GCRS. Mirrors the C parameter ordering exactly.</param>
    public static void IcrsToFk5(Span<double> state, bool includeSpeed, bool backward)
    {
        if (state.Length < 6) throw new ArgumentException("State span must contain 6 doubles.", nameof(state));

        // swephlib.c#L2302-L2310. The matrix maps FK5 → GCRS in row-major
        // form, so a forward (FK5 → GCRS) rotation reads x' = R · x via the
        // (i,j) indexing pattern x[0]·rb[0][i]+x[1]·rb[1][i]+x[2]·rb[2][i].
        // The backward rotation uses the transpose pattern.
        const double r00 = +0.9999999999999928, r01 = +0.0000001110223287, r02 = +0.0000000441180557;
        const double r10 = -0.0000001110223330, r11 = +0.9999999999999891, r12 = +0.0000000964779176;
        const double r20 = -0.0000000441180450, r21 = -0.0000000964779225, r22 = +0.9999999999999943;

        Span<double> outv = stackalloc double[6];
        if (backward)
        {
            // x' = R · x (GCRS → FK5 in row pattern).
            outv[0] = state[0] * r00 + state[1] * r01 + state[2] * r02;
            outv[1] = state[0] * r10 + state[1] * r11 + state[2] * r12;
            outv[2] = state[0] * r20 + state[1] * r21 + state[2] * r22;
            if (includeSpeed)
            {
                outv[3] = state[3] * r00 + state[4] * r01 + state[5] * r02;
                outv[4] = state[3] * r10 + state[4] * r11 + state[5] * r12;
                outv[5] = state[3] * r20 + state[4] * r21 + state[5] * r22;
            }
        }
        else
        {
            // x' = Rᵀ · x (FK5 → GCRS).
            outv[0] = state[0] * r00 + state[1] * r10 + state[2] * r20;
            outv[1] = state[0] * r01 + state[1] * r11 + state[2] * r21;
            outv[2] = state[0] * r02 + state[1] * r12 + state[2] * r22;
            if (includeSpeed)
            {
                outv[3] = state[3] * r00 + state[4] * r10 + state[5] * r20;
                outv[4] = state[3] * r01 + state[4] * r11 + state[5] * r21;
                outv[5] = state[3] * r02 + state[4] * r12 + state[5] * r22;
            }
        }

        state[0] = outv[0]; state[1] = outv[1]; state[2] = outv[2];
        if (includeSpeed)
        {
            state[3] = outv[3]; state[4] = outv[4]; state[5] = outv[5];
        }
    }

    /// <summary>
    /// GCRS ↔ J2000 frame bias. Mirrors <c>swi_bias</c>
    /// (<c>swephlib.c#L2205-L2289</c>) for the IAU 2000 / IAU 2006 / None
    /// branches. The (rare) JPL Horizons compatibility post/pre-rotation is
    /// not portable here; the C library applies it via
    /// <c>swi_approx_jplhor</c> only when <c>SEFLG_JPLHOR_APPROX</c> is set
    /// — this method ignores that flag and matches the C library's default
    /// (non-Horizons) path.
    /// </summary>
    /// <param name="state">In/out — six-component state vector.</param>
    /// <param name="includeSpeed">Apply the same rotation to velocity components 3..5.</param>
    /// <param name="backward">When true, rotates J2000 → GCRS; when false, GCRS → J2000.</param>
    /// <param name="model">Bias matrix selector. <see cref="FrameBiasModel.None"/>
    /// short-circuits the call.</param>
    public static void IcrsBias(Span<double> state, bool includeSpeed, bool backward, FrameBiasModel model = FrameBiasModel.Iau2006)
    {
        if (state.Length < 6) throw new ArgumentException("State span must contain 6 doubles.", nameof(state));
        if (model == FrameBiasModel.None)
            return;

        // swephlib.c#L2229-L2249. The matrix is identical for IAU2000 and
        // IAU2006 to ~9 decimals; we keep both for selector fidelity.
        double r00, r01, r02, r10, r11, r12, r20, r21, r22;
        if (model == FrameBiasModel.Iau2006)
        {
            r00 = +0.99999999999999412;
            r10 = -0.00000007078368961;
            r20 = +0.00000008056213978;
            r01 = +0.00000007078368695;
            r11 = +0.99999999999999700;
            r21 = +0.00000003306428553;
            r02 = -0.00000008056214212;
            r12 = -0.00000003306427981;
            r22 = +0.99999999999999634;
        }
        else
        {
            r00 = +0.9999999999999942;
            r10 = -0.0000000707827974;
            r20 = +0.0000000805621715;
            r01 = +0.0000000707827948;
            r11 = +0.9999999999999969;
            r21 = +0.0000000330604145;
            r02 = -0.0000000805621738;
            r12 = -0.0000000330604088;
            r22 = +0.9999999999999962;
        }

        Span<double> outv = stackalloc double[6];
        if (backward)
        {
            outv[0] = state[0] * r00 + state[1] * r01 + state[2] * r02;
            outv[1] = state[0] * r10 + state[1] * r11 + state[2] * r12;
            outv[2] = state[0] * r20 + state[1] * r21 + state[2] * r22;
            if (includeSpeed)
            {
                outv[3] = state[3] * r00 + state[4] * r01 + state[5] * r02;
                outv[4] = state[3] * r10 + state[4] * r11 + state[5] * r12;
                outv[5] = state[3] * r20 + state[4] * r21 + state[5] * r22;
            }
        }
        else
        {
            outv[0] = state[0] * r00 + state[1] * r10 + state[2] * r20;
            outv[1] = state[0] * r01 + state[1] * r11 + state[2] * r21;
            outv[2] = state[0] * r02 + state[1] * r12 + state[2] * r22;
            if (includeSpeed)
            {
                outv[3] = state[3] * r00 + state[4] * r10 + state[5] * r20;
                outv[4] = state[3] * r01 + state[4] * r11 + state[5] * r21;
                outv[5] = state[3] * r02 + state[4] * r12 + state[5] * r22;
            }
        }

        state[0] = outv[0]; state[1] = outv[1]; state[2] = outv[2];
        if (includeSpeed)
        {
            state[3] = outv[3]; state[4] = outv[4]; state[5] = outv[5];
        }
    }
}
