// Ported from swehouse.c#L1182-L1249 (case 'J').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class SavardAHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.SavardA;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var sinfi = System.Math.Sin(ctx.Fi * dr);
        var cosfi = System.Math.Cos(ctx.Fi * dr);

        double xs1, xs2;
        if (System.Math.Abs(ctx.Fi) < HouseAscendantMath.VerySmall)
        {
            xs2 = 1.0 / 3.0;
            xs1 = 2.0 / 3.0;
        }
        else
        {
            xs2 = System.Math.Sin(ctx.Fi / 3.0 * dr) / sinfi;
            xs1 = System.Math.Sin(2 * ctx.Fi / 3.0 * dr) / sinfi;
        }
        xs2 = System.Math.Asin(xs2) * rd;
        xs1 = System.Math.Asin(xs1) * rd;

        double xh1, xh2;
        if (cosfi == 0.0)
        {
            xh1 = xh2 = ctx.Fi > 0 ? 90 : 270;
        }
        else
        {
            xh1 = System.Math.Atan(System.Math.Tan(xs1 * dr) / cosfi) * rd;
            xh2 = System.Math.Atan(System.Math.Tan(xs2 * dr) / cosfi) * rd;
        }
        var fh1 = System.Math.Asin(sinfi * System.Math.Sin((90 - xs1) * dr)) * rd;
        var fh2 = System.Math.Asin(sinfi * System.Math.Sin((90 - xs2) * dr)) * rd;

        ctx.Cusps[12] = HouseAscendantMath.Asc1(ctx.Th + 90 - xh2, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[11] = HouseAscendantMath.Asc1(ctx.Th + 90 - xh1, fh1, ctx.Sine, ctx.Cose);
        ctx.Cusps[2] = HouseAscendantMath.Asc1(ctx.Th + 90 + xh2, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[3] = HouseAscendantMath.Asc1(ctx.Th + 90 + xh1, fh1, ctx.Sine, ctx.Cose);

        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[11] = HouseAscendantMath.AscDash(ctx.Th + 90 - xh1, fh1, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[12] = HouseAscendantMath.AscDash(ctx.Th + 90 - xh2, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[3] = HouseAscendantMath.AscDash(ctx.Th + 90 + xh1, fh1, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[2] = HouseAscendantMath.AscDash(ctx.Th + 90 + xh2, fh2, ctx.Sine, ctx.Cose);
        }

        if (System.Math.Abs(ctx.Fi) >= 90 - ctx.Ekl)
        {
            var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
            if (acmc < 0)
            {
                ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
                ctx.Mc = AngleMath.NormalizeDegrees(ctx.Mc + 180);
                for (var i = 1; i <= 12; i++)
                {
                    if (i >= 4 && i < 10) continue;
                    ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[i] + 180);
                }
            }
        }
        return true;
    }
}
