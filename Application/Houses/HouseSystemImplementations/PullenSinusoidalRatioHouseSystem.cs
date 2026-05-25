// Ported from swehouse.c#L1336-L1380 (case 'Q').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class PullenSinusoidalRatioHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.PullenSinusoidalRatio;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        const double third = 1.0 / 3.0;
        var two23 = System.Math.Pow(2.0 * 2.0, third);

        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
            acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        }

        var q = acmc;
        if (q > 90) q = 180 - q;

        double x, xr, xr3, xr4;
        if (q < 1e-30)
        {
            x = xr = xr3 = 0;
            xr4 = 180;
        }
        else
        {
            var c = (180 - q) / q;
            var csq = c * c;
            var ccr = System.Math.Pow(csq - c, third);
            var cqx = System.Math.Sqrt(two23 * ccr + 1.0);
            var r1 = 0.5 * cqx;
            var r2 = 0.5 * System.Math.Sqrt(-2 * (1 - 2 * c) / cqx - two23 * ccr + 2);
            var r = r1 + r2 - 0.5;
            x = q / (2 * r + 1);
            xr = r * x;
            xr3 = xr * r * r;
            xr4 = xr3 * r;
        }

        if (acmc > 90)
        {
            ctx.Cusps[11] = AngleMath.NormalizeDegrees(ctx.Mc + xr3);
            ctx.Cusps[12] = AngleMath.NormalizeDegrees(ctx.Cusps[11] + xr4);
            ctx.Cusps[2] = AngleMath.NormalizeDegrees(ctx.Ac + xr);
            ctx.Cusps[3] = AngleMath.NormalizeDegrees(ctx.Cusps[2] + x);
        }
        else
        {
            ctx.Cusps[11] = AngleMath.NormalizeDegrees(ctx.Mc + xr);
            ctx.Cusps[12] = AngleMath.NormalizeDegrees(ctx.Cusps[11] + x);
            ctx.Cusps[2] = AngleMath.NormalizeDegrees(ctx.Ac + xr3);
            ctx.Cusps[3] = AngleMath.NormalizeDegrees(ctx.Cusps[2] + xr4);
        }
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
