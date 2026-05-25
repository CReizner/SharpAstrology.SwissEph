// Ported from swehouse.c#L1028-L1082 (case 'C').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class CampanusHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Campanus;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var sinFi = System.Math.Sin(ctx.Fi * dr);
        var cosfi = System.Math.Cos(ctx.Fi * dr);

        // Pole heights for the prime-vertical 30°/60° points (swehouse.c#L1036-L1039).
        var fh1 = System.Math.Asin(sinFi / 2.0) * rd;
        var fh2 = System.Math.Asin(System.Math.Sqrt(3.0) / 2.0 * sinFi) * rd;

        double xh1, xh2;
        if (cosfi == 0.0)
        {
            xh1 = xh2 = ctx.Fi > 0 ? 90 : 270;
        }
        else
        {
            xh1 = System.Math.Atan(System.Math.Sqrt(3.0) / cosfi) * rd;
            xh2 = System.Math.Atan(1.0 / System.Math.Sqrt(3.0) / cosfi) * rd;
        }

        ctx.Cusps[11] = HouseAscendantMath.Asc1(ctx.Th + 90 - xh1, fh1, ctx.Sine, ctx.Cose);
        ctx.Cusps[12] = HouseAscendantMath.Asc1(ctx.Th + 90 - xh2, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[2] = HouseAscendantMath.Asc1(ctx.Th + 90 + xh2, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[3] = HouseAscendantMath.Asc1(ctx.Th + 90 + xh1, fh1, ctx.Sine, ctx.Cose);

        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[11] = HouseAscendantMath.AscDash(ctx.Th + 90 - xh1, fh1, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[12] = HouseAscendantMath.AscDash(ctx.Th + 90 - xh2, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[2] = HouseAscendantMath.AscDash(ctx.Th + 90 + xh2, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[3] = HouseAscendantMath.AscDash(ctx.Th + 90 + xh1, fh1, ctx.Sine, ctx.Cose);
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
