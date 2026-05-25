// Ported from swehouse.c#L1250-L1272 (case 'K').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class KochHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Koch;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        if (System.Math.Abs(ctx.Fi) >= 90 - ctx.Ekl)
        {
            ctx.Warning = "within polar circle, switched to Porphyry";
            ctx.FellBackToPorphyry = true;
            return false;
        }

        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;

        var sina = System.Math.Sin(ctx.Mc * dr) * ctx.Sine / System.Math.Cos(ctx.Fi * dr);
        if (sina > 1) sina = 1;
        if (sina < -1) sina = -1;
        var cosa = System.Math.Sqrt(1 - sina * sina);
        var c = System.Math.Atan(ctx.Tanfi / cosa) * rd;
        var ad3 = System.Math.Asin(System.Math.Sin(c * dr) * sina) * rd / 3.0;

        ctx.Cusps[11] = HouseAscendantMath.Asc1(ctx.Th + 30 - 2 * ad3, ctx.Fi, ctx.Sine, ctx.Cose);
        ctx.Cusps[12] = HouseAscendantMath.Asc1(ctx.Th + 60 - ad3, ctx.Fi, ctx.Sine, ctx.Cose);
        ctx.Cusps[2] = HouseAscendantMath.Asc1(ctx.Th + 120 + ad3, ctx.Fi, ctx.Sine, ctx.Cose);
        ctx.Cusps[3] = HouseAscendantMath.Asc1(ctx.Th + 150 + 2 * ad3, ctx.Fi, ctx.Sine, ctx.Cose);

        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[11] = HouseAscendantMath.AscDash(ctx.Th + 30 - 2 * ad3, ctx.Fi, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[12] = HouseAscendantMath.AscDash(ctx.Th + 60 - ad3, ctx.Fi, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[2] = HouseAscendantMath.AscDash(ctx.Th + 120 + ad3, ctx.Fi, ctx.Sine, ctx.Cose);
            ctx.CuspSpeeds[3] = HouseAscendantMath.AscDash(ctx.Th + 150 + 2 * ad3, ctx.Fi, ctx.Sine, ctx.Cose);
        }
        return true;
    }
}
