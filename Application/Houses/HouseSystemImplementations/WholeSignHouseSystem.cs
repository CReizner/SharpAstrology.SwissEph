// Ported from swehouse.c#L1474-L1484 (case 'W').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class WholeSignHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.WholeSign;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
        }
        // C source uses fmod (not %) — both round toward zero on non-negative inputs.
        ctx.Cusps[1] = ctx.Ac - (ctx.Ac % 30.0);
        for (var i = 2; i <= 12; i++)
            ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[1] + (i - 1) * 30.0);
        return true;
    }
}
