// Ported from swehouse.c#L1301-L1309 (case 'N').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class EqualAriesAnchoredHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.EqualAriesAnchored;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0) ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
        for (var i = 1; i <= 12; i++)
            ctx.Cusps[i] = (i - 1) * 30.0;
        return true;
    }
}
