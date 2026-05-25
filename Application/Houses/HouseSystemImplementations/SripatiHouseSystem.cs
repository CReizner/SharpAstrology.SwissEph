// Ported from swehouse.c#L1410-L1431 (case 'S').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class SripatiHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Sripati;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        // Uses Porphyry sectors but takes the *middle* of each sector as the cusp.
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        }
        var q1 = 180 - acmc;
        var s1 = q1 / 3.0;
        var s4 = acmc / 3.0;
        ctx.Cusps[1] = AngleMath.NormalizeDegrees(ctx.Ac - s4 * 0.5);
        ctx.Cusps[2] = AngleMath.NormalizeDegrees(ctx.Ac + s1 * 0.5);
        ctx.Cusps[3] = AngleMath.NormalizeDegrees(ctx.Ac + s1 * 1.5);
        ctx.Cusps[10] = AngleMath.NormalizeDegrees(ctx.Mc - s1 * 0.5);
        ctx.Cusps[11] = AngleMath.NormalizeDegrees(ctx.Mc + s4 * 0.5);
        ctx.Cusps[12] = AngleMath.NormalizeDegrees(ctx.Mc + s4 * 1.5);
        ctx.DoInterpol = ctx.DoHspeed; // speeds via finite difference, mirrors swehouse.c#L1430
        return true;
    }
}
