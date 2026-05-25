// Ported from swehouse.c#L1011-L1027 (case 'D').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class EqualMcHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.EqualMc;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0) ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
        ctx.Cusps[10] = ctx.Mc;
        for (var i = 11; i <= 12; i++)
            ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[10] + (i - 10) * 30.0);
        for (var i = 1; i <= 9; i++)
            ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[10] + (i + 2) * 30.0);
        if (ctx.DoHspeed)
        {
            for (var i = 1; i <= 12; i++)
                ctx.CuspSpeeds[i] = ctx.McSpeed;
        }
        return true;
    }
}
