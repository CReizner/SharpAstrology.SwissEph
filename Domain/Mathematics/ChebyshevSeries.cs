// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Evaluation of Chebyshev series of the first kind on <c>[-1, +1]</c>.
/// Mirrors the C functions <c>swi_echeb</c> and <c>swi_edcheb</c>, both based on
/// Communications of the ACM algorithm 446 (Broucke 1973). These two functions
/// are the inner loop of every <c>.se1</c> / <c>.eph</c> position evaluation.
/// </summary>
internal static class ChebyshevSeries
{
    /// <summary>
    /// Evaluates <c>Σ cⱼ Tⱼ(x)</c> at <paramref name="x"/> ∈ <c>[-1, +1]</c>.
    /// Allocation-free; the coefficients are read from a span.
    /// </summary>
    public static double Evaluate(double x, ReadOnlySpan<double> coefficients)
    {
        if (coefficients.IsEmpty) return 0.0;

        var x2 = x * 2.0;
        var br = 0.0;
        var brp2 = 0.0;
        var brpp = 0.0;
        for (var j = coefficients.Length - 1; j >= 0; j--)
        {
            brp2 = brpp;
            brpp = br;
            br = x2 * brpp - brp2 + coefficients[j];
        }

        return (br - brp2) * 0.5;
    }

    /// <summary>
    /// Evaluates the derivative <c>d/dx Σ cⱼ Tⱼ(x)</c> at <paramref name="x"/>.
    /// </summary>
    public static double EvaluateDerivative(double x, ReadOnlySpan<double> coefficients)
    {
        if (coefficients.Length < 2) return 0.0;

        var x2 = x * 2.0;
        var bf = 0.0;
        var bj = 0.0;
        var xjp2 = 0.0;
        var xjpl = 0.0;
        var bjp2 = 0.0;
        var bjpl = 0.0;
        for (var j = coefficients.Length - 1; j >= 1; j--)
        {
            var dj = (double)(j + j);
            var xj = coefficients[j] * dj + xjp2;
            bj = x2 * bjpl - bjp2 + xj;
            bf = bjp2;
            bjp2 = bjpl;
            bjpl = bj;
            xjp2 = xjpl;
            xjpl = xj;
        }

        return (bj - bf) * 0.5;
    }
}
