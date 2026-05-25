// Ported from swehouse.c#L1083-L1155 (case 'H').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class HorizonHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Horizon;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;

        // The C source mutates fi and th locally before restoring them later.
        // Keep that pattern verbatim — both are scalars.
        var fi = ctx.Fi;
        var th = ctx.Th;

        if (fi > 0) fi = 90 - fi;
        else fi = -90 - fi;

        if (System.Math.Abs(System.Math.Abs(fi) - 90) < HouseAscendantMath.VerySmall)
            fi = fi < 0 ? -90 + HouseAscendantMath.VerySmall : 90 - HouseAscendantMath.VerySmall;

        th = AngleMath.NormalizeDegrees(th + 180);
        var sinFi = System.Math.Sin(fi * dr);
        var fh1 = System.Math.Asin(sinFi / 2.0) * rd;
        var fh2 = System.Math.Asin(System.Math.Sqrt(3.0) / 2.0 * sinFi) * rd;
        var cosfi = System.Math.Cos(fi * dr);

        double xh1, xh2;
        if (cosfi == 0.0)
        {
            xh1 = xh2 = fi > 0 ? 90 : 270;
        }
        else
        {
            xh1 = System.Math.Atan(System.Math.Sqrt(3.0) / cosfi) * rd;
            xh2 = System.Math.Atan(1.0 / System.Math.Sqrt(3.0) / cosfi) * rd;
        }

        ctx.Cusps[11] = HouseAscendantMath.Asc1(th + 90 - xh1, fh1, ctx.Sine, ctx.Cose);
        ctx.Cusps[12] = HouseAscendantMath.Asc1(th + 90 - xh2, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[1] = HouseAscendantMath.Asc1(th + 90, fi, ctx.Sine, ctx.Cose);
        ctx.Cusps[2] = HouseAscendantMath.Asc1(th + 90 + xh2, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[3] = HouseAscendantMath.Asc1(th + 90 + xh1, fh1, ctx.Sine, ctx.Cose);

        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[11] = HouseAscendantMath.AscDash(th + 90 - xh1, fh1, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[12] = HouseAscendantMath.AscDash(th + 90 - xh2, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[1] = HouseAscendantMath.AscDash(th + 90, fi, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[2] = HouseAscendantMath.AscDash(th + 90 + xh2, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[3] = HouseAscendantMath.AscDash(th + 90 + xh1, fh1, ctx.Sine, ctx.Cose);
        }

        if (System.Math.Abs(fi) >= 90 - ctx.Ekl)
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

        for (var i = 1; i <= 3; i++)
            ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[i] + 180);
        for (var i = 11; i <= 12; i++)
            ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[i] + 180);

        // Restore-time fi/th tweak (swehouse.c#L1146-L1150) is purely local —
        // we don't re-use ctx.Fi / ctx.Th below. The trailing AC swap (L1151)
        // mirrors the C source.
        var acmc2 = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc2 < 0) ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
        return true;
    }
}
