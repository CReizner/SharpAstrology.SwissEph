// Ported from swehouse.c#L1310-L1335 (case 'O' / "porphyry:" label).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class PorphyryHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Porphyry;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        ApplyPorphyry(ref ctx);
        return true;
    }

    /// <summary>
    /// Helper used both by Porphyry itself and by the polar-fallback branch
    /// of Placidus / Koch / Gauquelin / Sunshine. Sets cusps[1..3, 10..12]
    /// from AC and MC after divisible-quadrant trisection.
    /// </summary>
    public static void ApplyPorphyry(ref HouseComputeContext ctx)
    {
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
            acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        }
        ctx.Cusps[1] = ctx.Ac;   // may have been clobbered if defaulting from Gauquelin
        ctx.Cusps[10] = ctx.Mc;
        ctx.Cusps[2] = AngleMath.NormalizeDegrees(ctx.Ac + (180 - acmc) / 3.0);
        ctx.Cusps[3] = AngleMath.NormalizeDegrees(ctx.Ac + (180 - acmc) / 3.0 * 2.0);
        ctx.Cusps[11] = AngleMath.NormalizeDegrees(ctx.Mc + acmc / 3.0);
        ctx.Cusps[12] = AngleMath.NormalizeDegrees(ctx.Mc + acmc / 3.0 * 2.0);
        if (ctx.DoHspeed)
        {
            var q1Speed = ctx.AcSpeed - ctx.McSpeed;
            ctx.CuspSpeeds[1] = ctx.AcSpeed;
            ctx.CuspSpeeds[10] = ctx.McSpeed;
            ctx.CuspSpeeds[2] = ctx.AcSpeed - q1Speed / 3.0;
            ctx.CuspSpeeds[3] = ctx.AcSpeed - q1Speed / 3.0 * 2.0;
            ctx.CuspSpeeds[11] = ctx.AcSpeed + q1Speed / 3.0;
            ctx.CuspSpeeds[12] = ctx.AcSpeed + q1Speed / 3.0 * 2.0;
        }
    }
}
