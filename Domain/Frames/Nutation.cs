// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: swephlib.c
//   nt table (IAU 1980 + Herring 1987)              — lines 1487-1612
//   calc_nutation_iau1980                           — lines 1615-1764
//   calc_nutation_iau2000ab (uses NutationTables)   — lines 1813-1944
//   calc_nutation_woolard                           — lines 1947-2002
//   calc_nutation (model dispatch)                  — lines 2069-2114
//   swi_nutation                                    — lines 2126-2158
// Source: sweph.c
//   nut_matrix                                      — lines 5073-5094

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Nutation in longitude (Δψ) and obliquity (Δε) at a TT epoch.
/// Returned as <see cref="NutationAngles"/> in radians, matching the C
/// convention (<c>swi_nutation</c> output is radians).
/// </summary>
internal readonly record struct NutationAngles(double DeltaPsiRad, double DeltaEpsilonRad);

/// <summary>
/// Nutation port. Exposes the four C-library models
/// (<see cref="NutationModel.Iau1980"/>, <see cref="NutationModel.Iau1980Corrections1987"/>,
/// <see cref="NutationModel.Iau2000A"/>, <see cref="NutationModel.Iau2000B"/>,
/// <see cref="NutationModel.Woolard"/>) through
/// <see cref="AstronomicalModelOverrides"/>.
/// </summary>
internal static class Nutation
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double J2000 = AstronomicalConstants.J2000;
    private const double JulianCentury = AstronomicalConstants.JulianCentury;

    /// <summary>
    /// Computes Δψ and Δε for the given TT epoch using the model selected
    /// in <paramref name="overrides"/>. Allocation-free.
    /// </summary>
    public static NutationAngles Compute(double jdTt, AstronomicalModelOverrides? overrides = null)
    {
        var models = overrides ?? AstronomicalModelOverrides.Default;
        Span<double> nutlo = stackalloc double[2];
        switch (models.Nutation)
        {
            case NutationModel.Iau1980:
            case NutationModel.Iau1980Corrections1987:
                CalcIau1980(jdTt, nutlo, models.Nutation == NutationModel.Iau1980Corrections1987);
                break;
            case NutationModel.Iau2000A:
                CalcIau2000Ab(jdTt, nutlo, includePlanetary: true);
                break;
            case NutationModel.Iau2000B:
                CalcIau2000Ab(jdTt, nutlo, includePlanetary: false);
                break;
            case NutationModel.Woolard:
                CalcWoolard(jdTt, nutlo);
                break;
            default:
                CalcIau2000Ab(jdTt, nutlo, includePlanetary: false);
                break;
        }
        return new NutationAngles(nutlo[0], nutlo[1]);
    }

    /// <summary>
    /// Builds the nutation rotation matrix that maps a vector from the
    /// mean equator-and-equinox of date to the true equator-and-equinox
    /// of date. Mirrors <c>nut_matrix</c> (<c>sweph.c#L5073-L5094</c>).
    /// </summary>
    /// <param name="angles">Δψ, Δε from <see cref="Compute"/>.</param>
    /// <param name="meanObliquityRad">Mean obliquity ε at the same epoch (radians).</param>
    public static Matrix3x3 BuildMatrix(NutationAngles angles, double meanObliquityRad)
    {
        var psi = angles.DeltaPsiRad;
        var eps = meanObliquityRad + angles.DeltaEpsilonRad;
        var (sinpsi, cospsi) = System.Math.SinCos(psi);
        var (sineps0, coseps0) = System.Math.SinCos(meanObliquityRad);
        var (sineps, coseps) = System.Math.SinCos(eps);
        // Row-major; sweph.c#L5085-L5093.
        return new Matrix3x3(
            cospsi, sinpsi * coseps, sinpsi * sineps,
            -sinpsi * coseps0, cospsi * coseps * coseps0 + sineps * sineps0, cospsi * sineps * coseps0 - coseps * sineps0,
            -sinpsi * sineps0, cospsi * coseps * sineps0 - sineps * coseps0, cospsi * sineps * sineps0 + coseps * coseps0);
    }

    /// <summary>
    /// Applies the nutation rotation in-place to a length-≥3 span.
    /// Equivalent to <c>swi_nutate</c> (<c>sweph.c#L3592-L3640</c>) on the
    /// position component.
    /// </summary>
    public static void Apply(Span<double> xyz, NutationAngles angles, double meanObliquityRad, bool backward = false)
    {
        if (xyz.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyz));
        var matrix = BuildMatrix(angles, meanObliquityRad);
        if (backward)
            matrix.TransformInPlace(xyz);
        else
        {
            // Forward in C is x · matrix (row of M times column of v): equivalent to matrix^T · v.
            var transposed = matrix.Transpose();
            transposed.TransformInPlace(xyz);
        }
    }

    /// <summary>
    /// Time interval in days used for the nutation-derivative speed
    /// correction. Matches <c>NUT_SPEED_INTV</c> at <c>sweph.h#L303</c>
    /// (8.64 seconds, expressed in days).
    /// </summary>
    private const double NutSpeedIntervalDays = 0.0001;

    /// <summary>
    /// Velocity-aware variant of <see cref="Apply"/>. Mirrors the speed
    /// branch of <c>swi_nutate</c> (sweph.c#L3607-L3635): rotates both
    /// position and velocity with the nutation matrix at <paramref name="jdTt"/>,
    /// then adds the apparent-motion contribution caused by the change of
    /// nutation across <c>NUT_SPEED_INTV</c> (i.e. the matrix evaluated at
    /// <c>jdTt − NUT_SPEED_INTV</c>). Without this contribution the velocity
    /// is off by ~0.01" / day.
    /// </summary>
    /// <param name="state">In/out — six doubles (pos[0..2] + vel[3..5]).</param>
    /// <param name="jdTt">Body's TT epoch.</param>
    /// <param name="backward">If true, nutation is undone (true→mean); if false, applied (mean→true).</param>
    /// <param name="overrides">Model selectors.</param>
    public static void ApplyWithSpeed(
        Span<double> state,
        double jdTt,
        bool backward,
        AstronomicalModelOverrides? overrides = null)
    {
        if (state.Length < 6)
            throw new ArgumentException("State span must contain at least 6 doubles.", nameof(state));
        var models = overrides ?? AstronomicalModelOverrides.Default;
        var meanEps = Precession.MeanObliquity(jdTt, models);
        var nut = Compute(jdTt, models);
        // The C code uses swed.oec at the body's TT for BOTH nut.matrix and
        // nutv.matrix (sweph.c#L6048-L6058). We follow the same convention.
        var nutv = Compute(jdTt - NutSpeedIntervalDays, models);

        // Save original position before mutation.
        var x0 = state[0]; var y0 = state[1]; var z0 = state[2];

        // Rotate position with current nutation.
        Span<double> pos = state.Slice(0, 3);
        Apply(pos, nut, meanEps, backward);

        // Rotate velocity with current nutation (same matrix as position).
        Span<double> vel = state.Slice(3, 3);
        Apply(vel, nut, meanEps, backward);

        // Compute xv = nutv·x0 (in 'backward' direction, per C swi_nutate
        // which uses the same flag for the secondary matrix at sweph.c#L3624).
        var matrixV = BuildMatrix(nutv, meanEps);
        Span<double> xv = stackalloc double[3];
        xv[0] = x0; xv[1] = y0; xv[2] = z0;
        if (backward)
            matrixV.TransformInPlace(xv);
        else
        {
            var transposedV = matrixV.Transpose();
            transposedV.TransformInPlace(xv);
        }

        // Apparent-motion contribution: (pos - xv) / NUT_SPEED_INTV is the
        // additional drift of the rotation axes during the day; pos has been
        // mutated to the rotated coordinates already.
        state[3] += (pos[0] - xv[0]) / NutSpeedIntervalDays;
        state[4] += (pos[1] - xv[1]) / NutSpeedIntervalDays;
        state[5] += (pos[2] - xv[2]) / NutSpeedIntervalDays;
    }

    // ---- IAU 1980 nutation ---------------------------------------------------

    /// <summary>
    /// IAU 1980 nutation table. Each row is 9 ints
    /// (MM, MS, FF, DD, OM, LS, LS2, OC, OC2). The first byte (MM, when ≥100)
    /// flags Herring 1987 corrections (101 = ordinary sin/cos, 102 = swapped).
    /// Mirrors <c>nt[]</c> at <c>swephlib.c#L1487-L1612</c> (107 active rows).
    /// </summary>
    private static readonly short[] s_iau1980Table =
    {
        0, 0, 0, 0, 2,  2062,  2, -895,  5,
        -2, 0, 2, 0, 1,    46,  0,  -24,  0,
        2, 0,-2, 0, 0,    11,  0,    0,  0,
        -2, 0, 2, 0, 2,    -3,  0,    1,  0,
        1,-1, 0,-1, 0,    -3,  0,    0,  0,
        0,-2, 2,-2, 1,    -2,  0,    1,  0,
        2, 0,-2, 0, 1,     1,  0,    0,  0,
        0, 0, 2,-2, 2,-13187,-16, 5736,-31,
        0, 1, 0, 0, 0,  1426,-34,   54, -1,
        0, 1, 2,-2, 2,  -517, 12,  224, -6,
        0,-1, 2,-2, 2,   217, -5,  -95,  3,
        0, 0, 2,-2, 1,   129,  1,  -70,  0,
        2, 0, 0,-2, 0,    48,  0,    1,  0,
        0, 0, 2,-2, 0,   -22,  0,    0,  0,
        0, 2, 0, 0, 0,    17, -1,    0,  0,
        0, 1, 0, 0, 1,   -15,  0,    9,  0,
        0, 2, 2,-2, 2,   -16,  1,    7,  0,
        0,-1, 0, 0, 1,   -12,  0,    6,  0,
        -2, 0, 0, 2, 1,    -6,  0,    3,  0,
        0,-1, 2,-2, 1,    -5,  0,    3,  0,
        2, 0, 0,-2, 1,     4,  0,   -2,  0,
        0, 1, 2,-2, 1,     4,  0,   -2,  0,
        1, 0, 0,-1, 0,    -4,  0,    0,  0,
        2, 1, 0,-2, 0,     1,  0,    0,  0,
        0, 0,-2, 2, 1,     1,  0,    0,  0,
        0, 1,-2, 2, 0,    -1,  0,    0,  0,
        0, 1, 0, 0, 2,     1,  0,    0,  0,
        -1, 0, 0, 1, 1,     1,  0,    0,  0,
        0, 1, 2,-2, 0,    -1,  0,    0,  0,
        0, 0, 2, 0, 2, -2274, -2,  977, -5,
        1, 0, 0, 0, 0,   712,  1,   -7,  0,
        0, 0, 2, 0, 1,  -386, -4,  200,  0,
        1, 0, 2, 0, 2,  -301,  0,  129, -1,
        1, 0, 0,-2, 0,  -158,  0,   -1,  0,
        -1, 0, 2, 0, 2,   123,  0,  -53,  0,
        0, 0, 0, 2, 0,    63,  0,   -2,  0,
        1, 0, 0, 0, 1,    63,  1,  -33,  0,
        -1, 0, 0, 0, 1,   -58, -1,   32,  0,
        -1, 0, 2, 2, 2,   -59,  0,   26,  0,
        1, 0, 2, 0, 1,   -51,  0,   27,  0,
        0, 0, 2, 2, 2,   -38,  0,   16,  0,
        2, 0, 0, 0, 0,    29,  0,   -1,  0,
        1, 0, 2,-2, 2,    29,  0,  -12,  0,
        2, 0, 2, 0, 2,   -31,  0,   13,  0,
        0, 0, 2, 0, 0,    26,  0,   -1,  0,
        -1, 0, 2, 0, 1,    21,  0,  -10,  0,
        -1, 0, 0, 2, 1,    16,  0,   -8,  0,
        1, 0, 0,-2, 1,   -13,  0,    7,  0,
        -1, 0, 2, 2, 1,   -10,  0,    5,  0,
        1, 1, 0,-2, 0,    -7,  0,    0,  0,
        0, 1, 2, 0, 2,     7,  0,   -3,  0,
        0,-1, 2, 0, 2,    -7,  0,    3,  0,
        1, 0, 2, 2, 2,    -8,  0,    3,  0,
        1, 0, 0, 2, 0,     6,  0,    0,  0,
        2, 0, 2,-2, 2,     6,  0,   -3,  0,
        0, 0, 0, 2, 1,    -6,  0,    3,  0,
        0, 0, 2, 2, 1,    -7,  0,    3,  0,
        1, 0, 2,-2, 1,     6,  0,   -3,  0,
        0, 0, 0,-2, 1,    -5,  0,    3,  0,
        1,-1, 0, 0, 0,     5,  0,    0,  0,
        2, 0, 2, 0, 1,    -5,  0,    3,  0,
        0, 1, 0,-2, 0,    -4,  0,    0,  0,
        1, 0,-2, 0, 0,     4,  0,    0,  0,
        0, 0, 0, 1, 0,    -4,  0,    0,  0,
        1, 1, 0, 0, 0,    -3,  0,    0,  0,
        1, 0, 2, 0, 0,     3,  0,    0,  0,
        1,-1, 2, 0, 2,    -3,  0,    1,  0,
        -1,-1, 2, 2, 2,    -3,  0,    1,  0,
        -2, 0, 0, 0, 1,    -2,  0,    1,  0,
        3, 0, 2, 0, 2,    -3,  0,    1,  0,
        0,-1, 2, 2, 2,    -3,  0,    1,  0,
        1, 1, 2, 0, 2,     2,  0,   -1,  0,
        -1, 0, 2,-2, 1,    -2,  0,    1,  0,
        2, 0, 0, 0, 1,     2,  0,   -1,  0,
        1, 0, 0, 0, 2,    -2,  0,    1,  0,
        3, 0, 0, 0, 0,     2,  0,    0,  0,
        0, 0, 2, 1, 2,     2,  0,   -1,  0,
        -1, 0, 0, 0, 2,     1,  0,   -1,  0,
        1, 0, 0,-4, 0,    -1,  0,    0,  0,
        -2, 0, 2, 2, 2,     1,  0,   -1,  0,
        -1, 0, 2, 4, 2,    -2,  0,    1,  0,
        2, 0, 0,-4, 0,    -1,  0,    0,  0,
        1, 1, 2,-2, 2,     1,  0,   -1,  0,
        1, 0, 2, 2, 1,    -1,  0,    1,  0,
        -2, 0, 2, 4, 2,    -1,  0,    1,  0,
        -1, 0, 4, 0, 2,     1,  0,    0,  0,
        1,-1, 0,-2, 0,     1,  0,    0,  0,
        2, 0, 2,-2, 1,     1,  0,   -1,  0,
        2, 0, 2, 2, 2,    -1,  0,    0,  0,
        1, 0, 0, 2, 1,    -1,  0,    0,  0,
        0, 0, 4,-2, 2,     1,  0,    0,  0,
        3, 0, 2,-2, 2,     1,  0,    0,  0,
        1, 0, 2,-2, 0,    -1,  0,    0,  0,
        0, 1, 2, 0, 1,     1,  0,    0,  0,
        -1,-1, 0, 2, 1,     1,  0,    0,  0,
        0, 0,-2, 0, 1,    -1,  0,    0,  0,
        0, 0, 2,-1, 2,    -1,  0,    0,  0,
        0, 1, 0, 2, 0,    -1,  0,    0,  0,
        1, 0,-2,-2, 0,    -1,  0,    0,  0,
        0,-1, 2, 0, 1,    -1,  0,    0,  0,
        1, 1, 0,-2, 1,    -1,  0,    0,  0,
        1, 0,-2, 2, 0,    -1,  0,    0,  0,
        2, 0, 0, 2, 0,     1,  0,    0,  0,
        0, 0, 2, 4, 2,    -1,  0,    0,  0,
        0, 1, 0, 1, 0,     1,  0,    0,  0,
        // Herring 1987 corrections (LS, OC), units 0.00001"
        101, 0, 0, 0, 1,-725, 0, 213, 0,
        101, 1, 0, 0, 0, 523, 0, 208, 0,
        101, 0, 2,-2, 2, 102, 0, -41, 0,
        101, 0, 2, 0, 2, -81, 0,  32, 0,
        // Herring 1987 corrections (LC, OS — cosine/sine swapped), units 0.00001"
        102, 0, 0, 0, 1, 417, 0, 224, 0,
        102, 1, 0, 0, 0,  61, 0, -24, 0,
        102, 0, 2,-2, 2,-118, 0, -47, 0,
    };

    private static void CalcIau1980(double j, Span<double> nutlo, bool includeHerring1987)
    {
        // Fundamental arguments (FK5 reference system).
        var t = (j - J2000) / JulianCentury;
        var t2 = t * t;
        var om = -6962890.539 * t + 450160.280 + (0.008 * t + 7.455) * t2;
        om = AngleMath.NormalizeDegrees(om / 3600.0) * DegToRad;
        var ms = 129596581.224 * t + 1287099.804 - (0.012 * t + 0.577) * t2;
        ms = AngleMath.NormalizeDegrees(ms / 3600.0) * DegToRad;
        var mm = 1717915922.633 * t + 485866.733 + (0.064 * t + 31.310) * t2;
        mm = AngleMath.NormalizeDegrees(mm / 3600.0) * DegToRad;
        var ff = 1739527263.137 * t + 335778.877 + (0.011 * t - 13.257) * t2;
        ff = AngleMath.NormalizeDegrees(ff / 3600.0) * DegToRad;
        var dd = 1602961601.328 * t + 1072261.307 + (0.019 * t - 6.891) * t2;
        dd = AngleMath.NormalizeDegrees(dd / 3600.0) * DegToRad;

        // Multiple-angle sin/cos table: ss[5][8], cc[5][8] in C; flatten to ss[40], cc[40].
        Span<double> ss = stackalloc double[5 * 8];
        Span<double> cc = stackalloc double[5 * 8];
        Span<double> args = stackalloc double[5];
        Span<int> ns = stackalloc int[5];
        args[0] = mm; ns[0] = 3;
        args[1] = ms; ns[1] = 2;
        args[2] = ff; ns[2] = 4;
        args[3] = dd; ns[3] = 4;
        args[4] = om; ns[4] = 2;
        for (var k = 0; k < 5; k++)
        {
            var arg = args[k];
            var n = ns[k];
            var su = System.Math.Sin(arg);
            var cu = System.Math.Cos(arg);
            ss[k * 8 + 0] = su; cc[k * 8 + 0] = cu;
            var sv = 2.0 * su * cu;
            var cv = cu * cu - su * su;
            ss[k * 8 + 1] = sv; cc[k * 8 + 1] = cv;
            for (var i = 2; i < n; i++)
            {
                var s = su * cv + cu * sv;
                cv = cu * cv - su * sv;
                sv = s;
                ss[k * 8 + i] = sv;
                cc[k * 8 + i] = cv;
            }
        }
        // First terms (not in table).
        var c = (-0.01742 * t - 17.1996) * ss[4 * 8 + 0]; // sin(OM)
        var d = (0.00089 * t + 9.2025) * cc[4 * 8 + 0];    // cos(OM)

        var rows = s_iau1980Table.Length / 9;
        for (var rowIdx = 0; rowIdx < rows; rowIdx++)
        {
            var p = rowIdx * 9;
            var flag = s_iau1980Table[p];
            if (!includeHerring1987 && (flag == 101 || flag == 102))
                continue;
            // Build sin/cos of the multiple-angle combination.
            var k1 = 0;
            double cv = 0, sv = 0;
            for (var m = 0; m < 5; m++)
            {
                int jcoef = s_iau1980Table[p + m];
                if (jcoef > 100) jcoef = 0;
                if (jcoef != 0)
                {
                    var k = jcoef < 0 ? -jcoef : jcoef;
                    var su = ss[m * 8 + k - 1];
                    if (jcoef < 0) su = -su;
                    var cu = cc[m * 8 + k - 1];
                    if (k1 == 0)
                    {
                        sv = su; cv = cu; k1 = 1;
                    }
                    else
                    {
                        var sw = su * cv + cu * sv;
                        cv = cu * cv - su * sv;
                        sv = sw;
                    }
                }
            }
            // Longitude / obliquity coefficients in 0.0001″.
            double f = s_iau1980Table[p + 5] * 0.0001;
            if (s_iau1980Table[p + 6] != 0)
                f += 0.00001 * t * s_iau1980Table[p + 6];
            double g = s_iau1980Table[p + 7] * 0.0001;
            if (s_iau1980Table[p + 8] != 0)
                g += 0.00001 * t * s_iau1980Table[p + 8];
            if (flag >= 100)
            {
                f *= 0.1;
                g *= 0.1;
            }
            if (flag != 102)
            {
                c += f * sv;
                d += g * cv;
            }
            else
            {
                // cosine for nutl, sine for nuto.
                c += f * cv;
                d += g * sv;
            }
        }

        nutlo[0] = DegToRad * c / 3600.0;
        nutlo[1] = DegToRad * d / 3600.0;
    }

    // ---- IAU 2000A / 2000B ---------------------------------------------------

    // Vectorised IAU 2000A/B nutation. The argument table is rolled into
    // contiguous double[] columns once at type init (NutationTablesColumnMajor)
    // so the per-term `darg` build is a 5-tap (lunisolar) or 14-tap (planetary)
    // FMA chain that streams through Vector<double>; sin/cos are batched via
    // TensorPrimitives.SinCos. ≈2.6× over the scalar loop on AVX2 / 4-lane.
    private static void CalcIau2000Ab(double j, Span<double> nutlo, bool includePlanetary)
    {
        var t = (j - J2000) / JulianCentury;
        const double arcsecPerDegree = 1.0 / 3600.0;
        var m = AngleMath.NormalizeDegrees((485868.249036 +
                t * (1717915923.2178 +
                t * (31.8792 +
                t * (0.051635 +
                t * (-0.00024470))))) * arcsecPerDegree) * DegToRad;
        var sm = AngleMath.NormalizeDegrees((1287104.79305 +
                t * (129596581.0481 +
                t * (-0.5532 +
                t * (0.000136 +
                t * (-0.00001149))))) * arcsecPerDegree) * DegToRad;
        var f = AngleMath.NormalizeDegrees((335779.526232 +
                t * (1739527262.8478 +
                t * (-12.7512 +
                t * (-0.001037 +
                t * (0.00000417))))) * arcsecPerDegree) * DegToRad;
        var d = AngleMath.NormalizeDegrees((1072260.70369 +
                t * (1602961601.2090 +
                t * (-6.3706 +
                t * (0.006593 +
                t * (-0.00003169))))) * arcsecPerDegree) * DegToRad;
        var om = AngleMath.NormalizeDegrees((450160.398036 +
                t * (-6962890.5431 +
                t * (7.4722 +
                t * (0.007702 +
                t * (-0.00005939))))) * arcsecPerDegree) * DegToRad;

        var inls = includePlanetary ? NutationTables.LuniSolarTerms : NutationTables.LuniSolarTerms2000B;
        Span<double> dargLs = stackalloc double[inls];
        Span<double> sinLs = stackalloc double[inls];
        Span<double> cosLs = stackalloc double[inls];
        BuildLuniSolarArgs(inls, m, sm, f, d, om, dargLs);
        System.Numerics.Tensors.TensorPrimitives.SinCos(dargLs, sinLs, cosLs);
        AccumulateLuniSolar(inls, t, sinLs, cosLs, out var dpsi, out var deps);

        nutlo[0] = dpsi * NutationTables.OneTenthMicroArcSecondToDegree;
        nutlo[1] = deps * NutationTables.OneTenthMicroArcSecondToDegree;

        if (includePlanetary)
        {
            var al = AngleMath.NormalizeRadians(2.35555598 + 8328.6914269554 * t);
            var alsu = AngleMath.NormalizeRadians(6.24006013 + 628.301955 * t);
            var af = AngleMath.NormalizeRadians(1.627905234 + 8433.466158131 * t);
            var ad = AngleMath.NormalizeRadians(5.198466741 + 7771.3771468121 * t);
            var aom = AngleMath.NormalizeRadians(2.18243920 - 33.757045 * t);
            var alme = AngleMath.NormalizeRadians(4.402608842 + 2608.7903141574 * t);
            var alve = AngleMath.NormalizeRadians(3.176146697 + 1021.3285546211 * t);
            var alea = AngleMath.NormalizeRadians(1.753470314 + 628.3075849991 * t);
            var alma = AngleMath.NormalizeRadians(6.203480913 + 334.06124267 * t);
            var alju = AngleMath.NormalizeRadians(0.599546497 + 52.9690962641 * t);
            var alsa = AngleMath.NormalizeRadians(0.874016757 + 21.3299104960 * t);
            var alur = AngleMath.NormalizeRadians(5.481293871 + 7.4781598567 * t);
            var alne = AngleMath.NormalizeRadians(5.321159000 + 3.8127774000 * t);
            var apa = (0.02438175 + 0.00000538691 * t) * t;

            var n = NutationTables.PlanetaryTerms;
            Span<double> dargP = stackalloc double[n];
            Span<double> sinP = stackalloc double[n];
            Span<double> cosP = stackalloc double[n];
            BuildPlanetaryArgs(n, al, alsu, af, ad, aom, alme, alve, alea, alma, alju, alsa, alur, alne, apa, dargP);
            System.Numerics.Tensors.TensorPrimitives.SinCos(dargP, sinP, cosP);
            AccumulatePlanetary(n, sinP, cosP, out var pdpsi, out var pdeps);
            nutlo[0] += pdpsi * NutationTables.OneTenthMicroArcSecondToDegree;
            nutlo[1] += pdeps * NutationTables.OneTenthMicroArcSecondToDegree;

            // Adjustments for adoption of P03 precession (Capitaine et al. 2005 / IAU 2006).
            // swephlib.c#L1934-L1939. Units: micro-arcseconds.
            var dpsi2 = -8.1 * System.Math.Sin(om) - 0.6 * System.Math.Sin(2 * f - 2 * d + 2 * om);
            dpsi2 += t * (47.8 * System.Math.Sin(om) + 3.7 * System.Math.Sin(2 * f - 2 * d + 2 * om) + 0.6 * System.Math.Sin(2 * f + 2 * om) - 0.6 * System.Math.Sin(2 * om));
            var deps2 = t * (-25.6 * System.Math.Cos(om) - 1.6 * System.Math.Cos(2 * f - 2 * d + 2 * om));
            nutlo[0] += dpsi2 / (3600.0 * 1_000_000.0);
            nutlo[1] += deps2 / (3600.0 * 1_000_000.0);
        }
        nutlo[0] *= DegToRad;
        nutlo[1] *= DegToRad;
    }

    private static void BuildLuniSolarArgs(int n, double m, double sm, double f, double d, double om, Span<double> darg)
    {
        var argM = NutationTablesColumnMajor.LsArgM.AsSpan(0, n);
        var argSm = NutationTablesColumnMajor.LsArgSm.AsSpan(0, n);
        var argF = NutationTablesColumnMajor.LsArgF.AsSpan(0, n);
        var argD = NutationTablesColumnMajor.LsArgD.AsSpan(0, n);
        var argOm = NutationTablesColumnMajor.LsArgOm.AsSpan(0, n);
        var width = System.Numerics.Vector<double>.Count;
        var i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= width)
        {
            var vM = new System.Numerics.Vector<double>(m);
            var vSm = new System.Numerics.Vector<double>(sm);
            var vF = new System.Numerics.Vector<double>(f);
            var vD = new System.Numerics.Vector<double>(d);
            var vOm = new System.Numerics.Vector<double>(om);
            for (; i <= n - width; i += width)
            {
                var acc = new System.Numerics.Vector<double>(argM.Slice(i, width)) * vM;
                acc += new System.Numerics.Vector<double>(argSm.Slice(i, width)) * vSm;
                acc += new System.Numerics.Vector<double>(argF.Slice(i, width)) * vF;
                acc += new System.Numerics.Vector<double>(argD.Slice(i, width)) * vD;
                acc += new System.Numerics.Vector<double>(argOm.Slice(i, width)) * vOm;
                acc.CopyTo(darg.Slice(i, width));
            }
        }
        for (; i < n; i++)
            darg[i] = argM[i] * m + argSm[i] * sm + argF[i] * f + argD[i] * d + argOm[i] * om;
    }

    private static void AccumulateLuniSolar(int n, double t, ReadOnlySpan<double> sin, ReadOnlySpan<double> cos, out double dpsi, out double deps)
    {
        var psiSinC = NutationTablesColumnMajor.LsPsiSinConst.AsSpan(0, n);
        var psiSinT = NutationTablesColumnMajor.LsPsiSinT.AsSpan(0, n);
        var psiCos = NutationTablesColumnMajor.LsPsiCos.AsSpan(0, n);
        var epsCosC = NutationTablesColumnMajor.LsEpsCosConst.AsSpan(0, n);
        var epsCosT = NutationTablesColumnMajor.LsEpsCosT.AsSpan(0, n);
        var epsSin = NutationTablesColumnMajor.LsEpsSin.AsSpan(0, n);
        var width = System.Numerics.Vector<double>.Count;
        var psiAcc = System.Numerics.Vector<double>.Zero;
        var epsAcc = System.Numerics.Vector<double>.Zero;
        var vT = new System.Numerics.Vector<double>(t);
        var i = n - width;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= width)
        {
            for (; i >= 0; i -= width)
            {
                var s = new System.Numerics.Vector<double>(sin.Slice(i, width));
                var c = new System.Numerics.Vector<double>(cos.Slice(i, width));
                psiAcc += (new System.Numerics.Vector<double>(psiSinC.Slice(i, width)) + new System.Numerics.Vector<double>(psiSinT.Slice(i, width)) * vT) * s
                        + new System.Numerics.Vector<double>(psiCos.Slice(i, width)) * c;
                epsAcc += (new System.Numerics.Vector<double>(epsCosC.Slice(i, width)) + new System.Numerics.Vector<double>(epsCosT.Slice(i, width)) * vT) * c
                        + new System.Numerics.Vector<double>(epsSin.Slice(i, width)) * s;
            }
            i += width;
        }
        else
        {
            i = n;
        }
        var psi = System.Numerics.Vector.Sum(psiAcc);
        var eps = System.Numerics.Vector.Sum(epsAcc);
        for (var k = i - 1; k >= 0; k--)
        {
            var s = sin[k];
            var c = cos[k];
            psi += (psiSinC[k] + psiSinT[k] * t) * s + psiCos[k] * c;
            eps += (epsCosC[k] + epsCosT[k] * t) * c + epsSin[k] * s;
        }
        dpsi = psi;
        deps = eps;
    }

    private static void BuildPlanetaryArgs(
        int n,
        double al, double alsu, double af, double ad, double aom,
        double alme, double alve, double alea, double alma, double alju, double alsa, double alur, double alne,
        double apa,
        Span<double> darg)
    {
        var c0 = NutationTablesColumnMajor.PlArgL.AsSpan(0, n);
        var c1 = NutationTablesColumnMajor.PlArgLsu.AsSpan(0, n);
        var c2 = NutationTablesColumnMajor.PlArgF.AsSpan(0, n);
        var c3 = NutationTablesColumnMajor.PlArgD.AsSpan(0, n);
        var c4 = NutationTablesColumnMajor.PlArgOm.AsSpan(0, n);
        var c5 = NutationTablesColumnMajor.PlArgMe.AsSpan(0, n);
        var c6 = NutationTablesColumnMajor.PlArgVe.AsSpan(0, n);
        var c7 = NutationTablesColumnMajor.PlArgEa.AsSpan(0, n);
        var c8 = NutationTablesColumnMajor.PlArgMa.AsSpan(0, n);
        var c9 = NutationTablesColumnMajor.PlArgJu.AsSpan(0, n);
        var c10 = NutationTablesColumnMajor.PlArgSa.AsSpan(0, n);
        var c11 = NutationTablesColumnMajor.PlArgUr.AsSpan(0, n);
        var c12 = NutationTablesColumnMajor.PlArgNe.AsSpan(0, n);
        var c13 = NutationTablesColumnMajor.PlArgPa.AsSpan(0, n);
        var width = System.Numerics.Vector<double>.Count;
        var i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= width)
        {
            var v0 = new System.Numerics.Vector<double>(al);
            var v1 = new System.Numerics.Vector<double>(alsu);
            var v2 = new System.Numerics.Vector<double>(af);
            var v3 = new System.Numerics.Vector<double>(ad);
            var v4 = new System.Numerics.Vector<double>(aom);
            var v5 = new System.Numerics.Vector<double>(alme);
            var v6 = new System.Numerics.Vector<double>(alve);
            var v7 = new System.Numerics.Vector<double>(alea);
            var v8 = new System.Numerics.Vector<double>(alma);
            var v9 = new System.Numerics.Vector<double>(alju);
            var v10 = new System.Numerics.Vector<double>(alsa);
            var v11 = new System.Numerics.Vector<double>(alur);
            var v12 = new System.Numerics.Vector<double>(alne);
            var v13 = new System.Numerics.Vector<double>(apa);
            for (; i <= n - width; i += width)
            {
                var acc = new System.Numerics.Vector<double>(c0.Slice(i, width)) * v0;
                acc += new System.Numerics.Vector<double>(c1.Slice(i, width)) * v1;
                acc += new System.Numerics.Vector<double>(c2.Slice(i, width)) * v2;
                acc += new System.Numerics.Vector<double>(c3.Slice(i, width)) * v3;
                acc += new System.Numerics.Vector<double>(c4.Slice(i, width)) * v4;
                acc += new System.Numerics.Vector<double>(c5.Slice(i, width)) * v5;
                acc += new System.Numerics.Vector<double>(c6.Slice(i, width)) * v6;
                acc += new System.Numerics.Vector<double>(c7.Slice(i, width)) * v7;
                acc += new System.Numerics.Vector<double>(c8.Slice(i, width)) * v8;
                acc += new System.Numerics.Vector<double>(c9.Slice(i, width)) * v9;
                acc += new System.Numerics.Vector<double>(c10.Slice(i, width)) * v10;
                acc += new System.Numerics.Vector<double>(c11.Slice(i, width)) * v11;
                acc += new System.Numerics.Vector<double>(c12.Slice(i, width)) * v12;
                acc += new System.Numerics.Vector<double>(c13.Slice(i, width)) * v13;
                acc.CopyTo(darg.Slice(i, width));
            }
        }
        for (; i < n; i++)
        {
            darg[i] = c0[i] * al + c1[i] * alsu + c2[i] * af + c3[i] * ad + c4[i] * aom
                    + c5[i] * alme + c6[i] * alve + c7[i] * alea + c8[i] * alma + c9[i] * alju
                    + c10[i] * alsa + c11[i] * alur + c12[i] * alne + c13[i] * apa;
        }
    }

    private static void AccumulatePlanetary(int n, ReadOnlySpan<double> sin, ReadOnlySpan<double> cos, out double dpsi, out double deps)
    {
        var psiSin = NutationTablesColumnMajor.PlPsiSin.AsSpan(0, n);
        var psiCos = NutationTablesColumnMajor.PlPsiCos.AsSpan(0, n);
        var epsSin = NutationTablesColumnMajor.PlEpsSin.AsSpan(0, n);
        var epsCos = NutationTablesColumnMajor.PlEpsCos.AsSpan(0, n);
        var width = System.Numerics.Vector<double>.Count;
        var psiAcc = System.Numerics.Vector<double>.Zero;
        var epsAcc = System.Numerics.Vector<double>.Zero;
        var i = n - width;
        if (System.Numerics.Vector.IsHardwareAccelerated && n >= width)
        {
            for (; i >= 0; i -= width)
            {
                var s = new System.Numerics.Vector<double>(sin.Slice(i, width));
                var c = new System.Numerics.Vector<double>(cos.Slice(i, width));
                psiAcc += new System.Numerics.Vector<double>(psiSin.Slice(i, width)) * s + new System.Numerics.Vector<double>(psiCos.Slice(i, width)) * c;
                epsAcc += new System.Numerics.Vector<double>(epsSin.Slice(i, width)) * s + new System.Numerics.Vector<double>(epsCos.Slice(i, width)) * c;
            }
            i += width;
        }
        else
        {
            i = n;
        }
        var psi = System.Numerics.Vector.Sum(psiAcc);
        var eps = System.Numerics.Vector.Sum(epsAcc);
        for (var k = i - 1; k >= 0; k--)
        {
            var s = sin[k];
            var c = cos[k];
            psi += psiSin[k] * s + psiCos[k] * c;
            eps += epsSin[k] * s + epsCos[k] * c;
        }
        dpsi = psi;
        deps = eps;
    }

    // ---- Woolard 1953 -------------------------------------------------------

    private static void CalcWoolard(double j, Span<double> nutlo)
    {
        var mjd = j - AstronomicalConstants.J1900;
        var t = mjd / JulianCentury;
        var t2 = t * t;
        var a = 100.0021358 * t;
        var b = 360.0 * (a - System.Math.Floor(a));
        var ls = 279.697 + 0.000303 * t2 + b;
        a = 1336.855231 * t;
        b = 360.0 * (a - System.Math.Floor(a));
        var ld = 270.434 - 0.001133 * t2 + b;
        a = 99.99736056000026 * t;
        b = 360.0 * (a - System.Math.Floor(a));
        var ms = 358.476 - 0.00015 * t2 + b;
        a = 13255523.59 * t;
        b = 360.0 * (a - System.Math.Floor(a));
        var md = 296.105 + 0.009192 * t2 + b;
        a = 5.372616667 * t;
        b = 360.0 * (a - System.Math.Floor(a));
        var nm = 259.183 + 0.002078 * t2 - b;
        var tls = 2 * ls * DegToRad;
        nm = nm * DegToRad;
        var tnm = 2 * nm;
        ms = ms * DegToRad;
        var tld = 2 * ld * DegToRad;
        md = md * DegToRad;
        var dpsi = (-17.2327 - 0.01737 * t) * System.Math.Sin(nm)
                   + (-1.2729 - 0.00013 * t) * System.Math.Sin(tls)
                   + 0.2088 * System.Math.Sin(tnm) - 0.2037 * System.Math.Sin(tld)
                   + (0.1261 - 0.00031 * t) * System.Math.Sin(ms)
                   + 0.0675 * System.Math.Sin(md)
                   - (0.0497 - 0.00012 * t) * System.Math.Sin(tls + ms)
                   - 0.0342 * System.Math.Sin(tld - nm)
                   - 0.0261 * System.Math.Sin(tld + md)
                   + 0.0214 * System.Math.Sin(tls - ms)
                   - 0.0149 * System.Math.Sin(tls - tld + md)
                   + 0.0124 * System.Math.Sin(tls - nm)
                   + 0.0114 * System.Math.Sin(tld - md);
        var deps = (9.21 + 0.00091 * t) * System.Math.Cos(nm)
                   + (0.5522 - 0.00029 * t) * System.Math.Cos(tls)
                   - 0.0904 * System.Math.Cos(tnm)
                   + 0.0884 * System.Math.Cos(tld)
                   + 0.0216 * System.Math.Cos(tls + ms)
                   + 0.0183 * System.Math.Cos(tld - nm)
                   + 0.0113 * System.Math.Cos(tld + md)
                   - 0.0093 * System.Math.Cos(tls - ms)
                   - 0.0066 * System.Math.Cos(tls - nm);
        nutlo[0] = dpsi / 3600.0 * DegToRad;
        nutlo[1] = deps / 3600.0 * DegToRad;
    }
}
