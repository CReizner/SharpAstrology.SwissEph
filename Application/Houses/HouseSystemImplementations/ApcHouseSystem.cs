// Ported from swehouse.c#L782-L825 (apc_sector) and #L1806-L1829 (case 'Y').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class ApcHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Apc;
    public int CuspCount => 12;
    // APC populates cusps[1..12] itself. The post-CalcH mirror block at
    // swehouse.c#L1985 explicitly skips 'Y'.
    public bool SkipsDefaultMirror => true;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;

        for (var i = 1; i <= 12; i++)
            ctx.Cusps[i] = ApcSector(i, ctx.Fi * dr, ctx.Ekl * dr, ctx.Th * dr);

        // C-source comment: "the MC provided by apc_sector() near latitude 90
        // is not accurate" — override with the geometric MC we already have.
        ctx.Cusps[10] = ctx.Mc;
        ctx.Cusps[4] = AngleMath.NormalizeDegrees(ctx.Mc + 180);

        if (System.Math.Abs(ctx.Fi) >= 90 - ctx.Ekl)
        {
            var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
            if (acmc < 0)
            {
                ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
                ctx.Mc = AngleMath.NormalizeDegrees(ctx.Mc + 180);
                for (var i = 1; i <= 12; i++)
                    ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[i] + 180);
            }
        }
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }

    /// <summary>
    /// Verbatim port of <c>apc_sector</c> at swehouse.c#L782-L825. All
    /// arguments in radians; returns degrees.
    /// </summary>
    private static double ApcSector(int n, double ph, double e, double az)
    {
        var rd = AstronomicalConstants.RadToDeg;
        const double verySmall = HouseAscendantMath.VerySmall;
        var isBelowHor = false;
        double kv, dasc;

        if (System.Math.Abs(ph * rd) > 90 - verySmall)
        {
            kv = 0;
            dasc = 0;
        }
        else
        {
            kv = System.Math.Atan(
                System.Math.Tan(ph) * System.Math.Tan(e) * System.Math.Cos(az)
                / (1 + System.Math.Tan(ph) * System.Math.Tan(e) * System.Math.Sin(az)));
            if (System.Math.Abs(ph * rd) < verySmall)
            {
                dasc = (90 - verySmall) * AstronomicalConstants.DegToRad;
                if (ph < 0) dasc = -dasc;
            }
            else
            {
                dasc = System.Math.Atan(System.Math.Sin(kv) / System.Math.Tan(ph));
            }
        }

        int k;
        if (n < 8)
        {
            isBelowHor = true;
            k = n - 1;
        }
        else
        {
            k = n - 13;
        }

        var halfPi = System.Math.PI / 2.0;
        double a = isBelowHor
            ? kv + az + halfPi + k * (halfPi - kv) / 3.0
            : kv + az + halfPi + k * (halfPi + kv) / 3.0;
        a = AngleMath.NormalizeRadians(a);

        var dret = System.Math.Atan2(
            System.Math.Tan(dasc) * System.Math.Tan(ph) * System.Math.Sin(az) + System.Math.Sin(a),
            System.Math.Cos(e) * (System.Math.Tan(dasc) * System.Math.Tan(ph) * System.Math.Cos(az) + System.Math.Cos(a))
            + System.Math.Sin(e) * System.Math.Tan(ph) * System.Math.Sin(az - a));
        return AngleMath.NormalizeDegrees(dret * rd);
    }
}
