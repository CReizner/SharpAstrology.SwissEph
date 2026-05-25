// Ported from swehouse.c#L1273-L1300 (case 'L').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class PullenSinusoidalDeltaHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.PullenSinusoidalDelta;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
            acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        }
        var q1 = 180 - acmc;
        var d = (acmc - 90.0) / 4.0;
        if (acmc <= 30)
        {
            ctx.Cusps[11] = ctx.Cusps[12] = AngleMath.NormalizeDegrees(ctx.Mc + acmc / 2.0);
        }
        else
        {
            ctx.Cusps[11] = AngleMath.NormalizeDegrees(ctx.Mc + 30 + d);
            ctx.Cusps[12] = AngleMath.NormalizeDegrees(ctx.Mc + 60 + 3 * d);
        }
        d = (q1 - 90.0) / 4.0;
        if (q1 <= 30)
        {
            ctx.Cusps[2] = ctx.Cusps[3] = AngleMath.NormalizeDegrees(ctx.Ac + q1 / 2.0);
        }
        else
        {
            ctx.Cusps[2] = AngleMath.NormalizeDegrees(ctx.Ac + 30 + d);
            ctx.Cusps[3] = AngleMath.NormalizeDegrees(ctx.Ac + 60 + 3 * d);
        }
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
