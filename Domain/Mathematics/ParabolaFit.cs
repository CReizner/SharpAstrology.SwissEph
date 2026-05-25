// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Mathematics;

/// <summary>
/// Three-point parabola helpers used by the eclipse / occultation extremum
/// searches. Treats three equispaced samples as <c>y(−1)=y0, y(0)=y1, y(1)=y2</c>
/// and returns offsets in units of <paramref name="dx"/> measured from the
/// <em>third</em> sample (caller convention <c>(x − 1) · dx</c>, so a final
/// <c>+dx</c> increment can be undone without an extra branch).
/// </summary>
internal static class ParabolaFit
{
    /// <summary>
    /// Vertex of the parabola through three equispaced samples. Mirrors
    /// <c>find_maximum</c> in <c>swecl.c#L4133</c>.
    /// </summary>
    /// <param name="y0">Sample at <c>x = −1</c>.</param>
    /// <param name="y1">Sample at <c>x = 0</c>.</param>
    /// <param name="y2">Sample at <c>x = +1</c>.</param>
    /// <param name="dx">Sample spacing.</param>
    /// <param name="dxOffset">Vertex offset from the third sample, scaled by <paramref name="dx"/>.</param>
    /// <param name="yPeak">Function value at the vertex.</param>
    public static void FindMaximum(double y0, double y1, double y2, double dx, out double dxOffset, out double yPeak)
    {
        var b = (y2 - y0) / 2.0;
        var a = (y2 + y0) / 2.0 - y1;
        var x = -b / (2.0 * a);
        dxOffset = (x - 1.0) * dx;
        yPeak = (4.0 * a * y1 - b * b) / (4.0 * a);
    }

    /// <summary>
    /// Real roots of the parabola through three equispaced samples. Mirrors
    /// <c>find_zero</c> in <c>swecl.c#L4148</c>. Returns false when the
    /// discriminant is negative.
    /// </summary>
    /// <param name="y0">Sample at <c>x = −1</c>.</param>
    /// <param name="y1">Sample at <c>x = 0</c>.</param>
    /// <param name="y2">Sample at <c>x = +1</c>.</param>
    /// <param name="dx">Sample spacing.</param>
    /// <param name="dx1">First root offset from the third sample, scaled by <paramref name="dx"/>.</param>
    /// <param name="dx2">Second root offset, same convention.</param>
    /// <returns><c>true</c> if real roots exist.</returns>
    public static bool FindZero(double y0, double y1, double y2, double dx, out double dx1, out double dx2)
    {
        var c = y1;
        var b = (y2 - y0) / 2.0;
        var a = (y2 + y0) / 2.0 - c;
        var disc = b * b - 4.0 * a * c;
        if (disc < 0)
        {
            dx1 = 0; dx2 = 0;
            return false;
        }
        var sq = System.Math.Sqrt(disc);
        var x1 = (-b + sq) / (2.0 * a);
        var x2 = (-b - sq) / (2.0 * a);
        dx1 = (x1 - 1.0) * dx;
        dx2 = (x2 - 1.0) * dx;
        return true;
    }
}
