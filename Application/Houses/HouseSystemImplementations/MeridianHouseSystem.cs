// Ported from swehouse.c#L1485-L1516 (case 'X' — Meridian / axial-rotation).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class MeridianHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Meridian;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var a = ctx.Th;
        for (var i = 1; i <= 12; i++)
        {
            var j = i + 10;
            if (j > 12) j -= 12;
            a = AngleMath.NormalizeDegrees(a + 30);
            double cusp;
            if (System.Math.Abs(a - 90) > HouseAscendantMath.VerySmall
                && System.Math.Abs(a - 270) > HouseAscendantMath.VerySmall)
            {
                var tant = System.Math.Tan(a * dr);
                cusp = System.Math.Atan(tant / ctx.Cose) * rd;
                if (a > 90 && a <= 270) cusp = AngleMath.NormalizeDegrees(cusp + 180);
            }
            else
            {
                cusp = System.Math.Abs(a - 90) <= HouseAscendantMath.VerySmall ? 90 : 270;
            }
            ctx.Cusps[j] = AngleMath.NormalizeDegrees(cusp);
        }
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0) ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
