// Ported from swehouse.c#L1623-L1730 (case 'G' — 36 Gauquelin sectors).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class GauquelinHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Gauquelin;
    public int CuspCount => 36;
    public bool SkipsDefaultMirror => true; // sets cusps[1..36] itself

    private const int NiterMax = 100;
    private const double PlacIterTol = 1.0 / 360_000.0;

    public bool Compute(ref HouseComputeContext ctx)
    {
        for (var i = 1; i <= 36; i++)
        {
            ctx.Cusps[i] = 0;
            if (ctx.DoHspeed) ctx.CuspSpeeds[i] = 0;
        }
        if (System.Math.Abs(ctx.Fi) >= 90 - ctx.Ekl)
        {
            ctx.Warning = "within polar circle, switched to Porphyry";
            ctx.FellBackToPorphyry = true;
            return false;
        }

        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var a = System.Math.Asin(ctx.Tanfi * ctx.Tane) * rd;

        // Fourth/second quarter (ih = 2..9)
        for (var ih = 2; ih <= 9; ih++)
        {
            var ih2 = 10 - ih;
            var fh1 = System.Math.Atan(System.Math.Sin(a * ih2 / 9.0 * dr) / ctx.Tane) * rd;
            var rectasc = AngleMath.NormalizeDegrees(90.0 / 9.0 * ih2 + ctx.Th);
            if (!IterateGauquelin(ref ctx, ih, rectasc, fh1, ih2)) return false;
            ctx.Cusps[ih + 18] = AngleMath.NormalizeDegrees(ctx.Cusps[ih] + 180);
            if (ctx.DoHspeed) ctx.CuspSpeeds[ih + 18] = ctx.CuspSpeeds[ih];
        }
        // First/third quarter (ih = 29..36)
        for (var ih = 29; ih <= 36; ih++)
        {
            var ih2 = ih - 28;
            var fh1 = System.Math.Atan(System.Math.Sin(a * ih2 / 9.0 * dr) / ctx.Tane) * rd;
            var rectasc = AngleMath.NormalizeDegrees(180 - ih2 * 90.0 / 9.0 + ctx.Th);
            if (!IterateGauquelin(ref ctx, ih, rectasc, fh1, ih2)) return false;
            ctx.Cusps[ih - 18] = AngleMath.NormalizeDegrees(ctx.Cusps[ih] + 180);
            if (ctx.DoHspeed) ctx.CuspSpeeds[ih - 18] = ctx.CuspSpeeds[ih];
        }

        ctx.Cusps[1] = ctx.Ac;
        ctx.Cusps[10] = ctx.Mc;
        ctx.Cusps[19] = AngleMath.NormalizeDegrees(ctx.Ac + 180);
        ctx.Cusps[28] = AngleMath.NormalizeDegrees(ctx.Mc + 180);
        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[1] = ctx.AcSpeed;
            ctx.CuspSpeeds[10] = ctx.McSpeed;
            ctx.CuspSpeeds[19] = ctx.AcSpeed;
            ctx.CuspSpeeds[28] = ctx.McSpeed;
        }
        return true;
    }

    private static bool IterateGauquelin(ref HouseComputeContext ctx, int ih, double rectasc, double fh1, int ih2)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;

        var asc1 = HouseAscendantMath.Asc1(rectasc, fh1, ctx.Sine, ctx.Cose);
        var tant = System.Math.Tan(System.Math.Asin(ctx.Sine * System.Math.Sin(asc1 * dr)) * rd * dr);

        if (System.Math.Abs(tant) < HouseAscendantMath.VerySmall)
        {
            ctx.Cusps[ih] = rectasc;
            if (ctx.DoHspeed) ctx.CuspSpeeds[ih] = ctx.ArmcSpeed;
            return true;
        }

        var f = System.Math.Atan(System.Math.Sin(System.Math.Asin(ctx.Tanfi * tant) * ih2 / 9.0) / tant) * rd;
        ctx.Cusps[ih] = HouseAscendantMath.Asc1(rectasc, f, ctx.Sine, ctx.Cose);

        var cuspsv = 0.0;
        var i = 1;
        for (; i <= NiterMax; i++)
        {
            tant = System.Math.Tan(System.Math.Asin(ctx.Sine * System.Math.Sin(ctx.Cusps[ih] * dr)) * rd * dr);
            if (System.Math.Abs(tant) < HouseAscendantMath.VerySmall)
            {
                ctx.Cusps[ih] = rectasc;
                if (ctx.DoHspeed) ctx.CuspSpeeds[ih] = ctx.ArmcSpeed;
                return true;
            }
            f = System.Math.Atan(System.Math.Sin(System.Math.Asin(ctx.Tanfi * tant) * ih2 / 9.0) / tant) * rd;
            ctx.Cusps[ih] = HouseAscendantMath.Asc1(rectasc, f, ctx.Sine, ctx.Cose);
            if (i > 1 && System.Math.Abs(AngleMath.DifferenceDegreesSigned(ctx.Cusps[ih], cuspsv)) < PlacIterTol)
                break;
            cuspsv = ctx.Cusps[ih];
        }
        if (i >= NiterMax)
        {
            ctx.Warning = "very close to polar circle, switched to Porphyry";
            ctx.FellBackToPorphyry = true;
            return false;
        }
        if (ctx.DoHspeed)
            ctx.CuspSpeeds[ih] = HouseAscendantMath.AscDash(rectasc, f, ctx.Sine, ctx.Cose);
        return true;
    }
}
