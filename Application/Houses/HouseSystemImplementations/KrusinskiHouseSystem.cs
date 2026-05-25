// Ported from swehouse.c#L1731-L1805 (case 'U' — Krusinski-Pisa-Goelzer).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class KrusinskiHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.KrusinskiPisaGoelzer;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;

        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0) ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);

        // The C code uses local 3-vector (lon, lat=0, r=1), then applies
        // a sequence of swe_cotrans / rotations. Use stack-local vector.
        Span<double> x = stackalloc double[3];
        x[0] = ctx.Ac; x[1] = 0.0; x[2] = 1.0;
        // A1. Transform into equatorial coords (rotate by -ε).
        SwissCotrans(x, -ctx.Ekl);
        // A2. Rotate by -(th-90)
        x[0] -= ctx.Th - 90;
        // A3. Transform into horizontal coords (rotate by -(90-fi))
        SwissCotrans(x, -(90 - ctx.Fi));
        var krHorizonLon = x[0];
        // A4. Rotate by -x[0]: x[0] = 0
        x[0] = 0;
        // A5. Transform into asc-zenith great circle (rotate by -90)
        SwissCotrans(x, -90);

        for (var i = 0; i < 6; i++)
        {
            x[0] = 30.0 * i;
            x[1] = 0.0;
            x[2] = 1.0; // C source doesn't reset x[2], but every cotrans renormalises
            // B1. Transform back into horizontal (rotate by 90)
            SwissCotrans(x, 90);
            // B2. Rotate back: x[0] += krHorizonLon
            x[0] += krHorizonLon;
            // B3. Transform into equatorial (rotate by 90-fi)
            SwissCotrans(x, 90 - ctx.Fi);
            // B4. Rotate by (th-90)
            x[0] = AngleMath.NormalizeDegrees(x[0] + (ctx.Th - 90));
            // B5. Project to ecliptic longitude.
            var cusp = System.Math.Atan(System.Math.Tan(x[0] * dr) / System.Math.Cos(ctx.Ekl * dr)) * rd;
            if (x[0] > 90 && x[0] <= 270) cusp = AngleMath.NormalizeDegrees(cusp + 180);
            cusp = AngleMath.NormalizeDegrees(cusp);
            ctx.Cusps[i + 1] = cusp;
            ctx.Cusps[i + 7] = AngleMath.NormalizeDegrees(cusp + 180);
        }
        return true;
    }

    /// <summary>
    /// Polar swe_cotrans: rotates the (lon, lat) pair by <paramref name="epsDeg"/>
    /// around the X-axis, normalised back to spherical coords. C signature:
    /// <c>swe_cotrans(double *xpo, double *xpn, double eps)</c>.
    /// </summary>
    private static void SwissCotrans(Span<double> x, double epsDeg)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var lonRad = x[0] * dr;
        var latRad = x[1] * dr;
        var cl = System.Math.Cos(lonRad);
        var sl = System.Math.Sin(lonRad);
        var cb = System.Math.Cos(latRad);
        var sb = System.Math.Sin(latRad);
        var px = cl * cb;
        var py = sl * cb;
        var pz = sb;
        var ce = System.Math.Cos(epsDeg * dr);
        var se = System.Math.Sin(epsDeg * dr);
        var py2 = py * ce + pz * se;
        var pz2 = -py * se + pz * ce;
        var rxy = System.Math.Sqrt(px * px + py2 * py2);
        x[0] = AngleMath.NormalizeDegrees(System.Math.Atan2(py2, px) * rd);
        x[1] = System.Math.Atan2(pz2, rxy) * rd;
    }
}
