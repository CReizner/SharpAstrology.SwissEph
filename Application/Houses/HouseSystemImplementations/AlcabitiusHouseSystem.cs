// Ported from swehouse.c#L1581-L1622 (case 'B' — Alcabitius semi-arc).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class AlcabitiusHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Alcabitius;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;

        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
            acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        }

        // declination of Ascendant
        var dek = System.Math.Asin(System.Math.Sin(ctx.Ac * dr) * ctx.Sine) * rd;
        var r = -ctx.Tanfi * System.Math.Tan(dek * dr);
        if (r > 1) r = 1;
        if (r < -1) r = -1;
        var sda = System.Math.Acos(r) * rd;   // semidiurnal arc on equator
        var sna = 180 - sda;                  // seminocturnal arc
        var sd3 = sda / 3.0;
        var sn3 = sna / 3.0;

        ctx.Cusps[11] = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(ctx.Th + sd3),     0, ctx.Sine, ctx.Cose);
        ctx.Cusps[12] = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(ctx.Th + 2 * sd3), 0, ctx.Sine, ctx.Cose);
        ctx.Cusps[2] = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(ctx.Th + 180 - 2 * sn3), 0, ctx.Sine, ctx.Cose);
        ctx.Cusps[3] = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(ctx.Th + 180 - sn3),     0, ctx.Sine, ctx.Cose);
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
