// Ported from swehouse.c#L994-L1010 (case 'A' / 'E').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class EqualHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Equal;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        // Within polar circle, swap AC if it landed on the wrong side.
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
        }
        for (var i = 2; i <= 12; i++)
            ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[1] + (i - 1) * 30.0);

        if (ctx.DoHspeed)
        {
            for (var i = 1; i <= 12; i++)
                ctx.CuspSpeeds[i] = ctx.AcSpeed;
        }
        return true;
    }
}
