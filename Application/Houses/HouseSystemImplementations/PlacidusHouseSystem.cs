// Ported from swehouse.c#L1830-L1983 (default = case 'P', Placidus).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

/// <summary>
/// Placidus is the C library's default. Each of houses 11 / 12 / 2 / 3 is
/// found by a fixed-point iteration on the Asc1 projection with a pole
/// height that itself depends on the cusp's declination — the iterate-until-
/// converged loop at swehouse.c#L1851-L1864 (and three near-identical
/// neighbours).
/// </summary>
internal sealed class PlacidusHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Placidus;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    private const int NiterMax = 100;
    private const double PlacIterTol = 1.0 / 360_000.0; // VERY_SMALL_PLAC_ITER

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

        var a = System.Math.Asin(ctx.Tanfi * ctx.Tane) * rd;
        var fh1 = System.Math.Atan(System.Math.Sin(a / 3.0 * dr) / ctx.Tane) * rd;
        var fh2 = System.Math.Atan(System.Math.Sin(a * 2.0 / 3.0 * dr) / ctx.Tane) * rd;

        // House 11: ramc = th + 30, divisor = 3 (i.e. (a/3))
        if (!IteratePlacidus(ref ctx, ih: 11, rectasc: ctx.Th + 30, fh: fh1, divisor: 3.0)) return false;
        // House 12: ramc = th + 60, divisor = 1.5 (i.e. (2a/3))
        if (!IteratePlacidus(ref ctx, ih: 12, rectasc: ctx.Th + 60, fh: fh2, divisor: 1.5)) return false;
        // House 2: ramc = th + 120, divisor = 1.5
        if (!IteratePlacidus(ref ctx, ih: 2, rectasc: ctx.Th + 120, fh: fh2, divisor: 1.5)) return false;
        // House 3: ramc = th + 150, divisor = 3
        if (!IteratePlacidus(ref ctx, ih: 3, rectasc: ctx.Th + 150, fh: fh1, divisor: 3.0)) return false;

        return true;
    }

    /// <summary>
    /// Single-cusp iteration block extracted from the four near-identical
    /// fragments at swehouse.c#L1839-L1982. Returns true on convergence,
    /// false on max-iteration overshoot (caller falls back to Porphyry).
    /// </summary>
    private static bool IteratePlacidus(ref HouseComputeContext ctx, int ih, double rectasc, double fh, double divisor)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        rectasc = AngleMath.NormalizeDegrees(rectasc);
        var asc1 = HouseAscendantMath.Asc1(rectasc, fh, ctx.Sine, ctx.Cose);
        var tant = System.Math.Tan(System.Math.Asin(ctx.Sine * System.Math.Sin(asc1 * dr)) * rd * dr);

        if (System.Math.Abs(tant) < HouseAscendantMath.VerySmall)
        {
            ctx.Cusps[ih] = rectasc;
            if (ctx.DoHspeed) ctx.CuspSpeeds[ih] = ctx.ArmcSpeed;
            return true;
        }

        // initial pole height
        var f = System.Math.Atan(System.Math.Sin(System.Math.Asin(ctx.Tanfi * tant) / divisor) / tant) * rd;
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
            f = System.Math.Atan(System.Math.Sin(System.Math.Asin(ctx.Tanfi * tant) / divisor) / tant) * rd;
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
