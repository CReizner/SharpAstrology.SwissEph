// Ported from swehouse.c#L1432-L1458 (case 'T' — "topocentric" / Polich-Page).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class PolichPageHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.PolichPage;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var rd = AstronomicalConstants.RadToDeg;
        var fh1 = System.Math.Atan(ctx.Tanfi / 3.0) * rd;
        var fh2 = System.Math.Atan(ctx.Tanfi * 2.0 / 3.0) * rd;

        ctx.Cusps[11] = HouseAscendantMath.Asc1(30 + ctx.Th, fh1, ctx.Sine, ctx.Cose);
        ctx.Cusps[12] = HouseAscendantMath.Asc1(60 + ctx.Th, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[2] = HouseAscendantMath.Asc1(120 + ctx.Th, fh2, ctx.Sine, ctx.Cose);
        ctx.Cusps[3] = HouseAscendantMath.Asc1(150 + ctx.Th, fh1, ctx.Sine, ctx.Cose);

        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[11] = HouseAscendantMath.AscDash(30 + ctx.Th, fh1, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[12] = HouseAscendantMath.AscDash(60 + ctx.Th, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[2] = HouseAscendantMath.AscDash(120 + ctx.Th, fh2, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[3] = HouseAscendantMath.AscDash(150 + ctx.Th, fh1, ctx.Sine, ctx.Cose);
        }

        // Polich-Page polar branch shifts ALL 12 cusps by 180° (no skip range).
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
        return true;
    }
}
