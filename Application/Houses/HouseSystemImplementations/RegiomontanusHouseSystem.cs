// Ported from swehouse.c#L1381-L1409 (case 'R').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class RegiomontanusHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Regiomontanus;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        // pole heights via tan(φ)·0.5 and tan(φ)·cos(30°)
        var fh1 = System.Math.Atan(ctx.Tanfi * 0.5) * rd;
        var fh2 = System.Math.Atan(ctx.Tanfi * System.Math.Cos(30.0 * dr)) * rd;

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

        // Within polar circle, when MC sinks below horizon and AC swaps to
        // western hemisphere, all non-meridian cusps add 180° (clockwise).
        if (System.Math.Abs(ctx.Fi) >= 90 - ctx.Ekl)
        {
            var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
            if (acmc < 0)
            {
                ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
                ctx.Mc = AngleMath.NormalizeDegrees(ctx.Mc + 180);
                for (var i = 1; i <= 12; i++)
                {
                    if (i >= 4 && i < 10) continue;
                    ctx.Cusps[i] = AngleMath.NormalizeDegrees(ctx.Cusps[i] + 180);
                }
            }
        }
        return true;
    }
}
