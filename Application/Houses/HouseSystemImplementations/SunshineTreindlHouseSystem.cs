// Ported from swehouse.c#L1156-L1181 (case 'I') + #L3048-L3143 (sunshine_solution_treindl)
// + #L2878-L2904 (sunshine_init).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class SunshineTreindlHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.SunshineTreindl;
    public int CuspCount => 12;
    // Sunshine fills cusps[1..12] itself; default mirror is skipped (toupper(hsy)=='I').
    public bool SkipsDefaultMirror => true;

    private const bool KeepMcSouth = false; // SUNSHINE_KEEP_MC_SOUTH

    public bool Compute(ref HouseComputeContext ctx)
    {
        // Polar swap mirroring swehouse.c#L1158-L1167.
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
            // SUNSHINE_KEEP_MC_SOUTH==0 → also flip the MC for the 'I' branch.
            // (The if () guards 'I' specifically; 'i' keeps MC.)
            if (!KeepMcSouth)
            {
                ctx.Mc = AngleMath.NormalizeDegrees(ctx.Mc + 180);
                ctx.Cusps[10] = ctx.Mc;
            }
        }
        ctx.Cusps[4] = AngleMath.NormalizeDegrees(ctx.Cusps[10] + 180);
        ctx.Cusps[7] = AngleMath.NormalizeDegrees(ctx.Cusps[1] + 180);

        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var lat = ctx.Fi;
        var ecl = ctx.Ekl;
        var dec = ctx.SunDeclination;

        var sinlat = System.Math.Sin(lat * dr);
        var coslat = System.Math.Cos(lat * dr);
        var cosdec = System.Math.Cos(dec * dr);
        var tandec = System.Math.Tan(dec * dr);
        var sinecl = System.Math.Sin(ecl * dr);
        var cosecl = System.Math.Cos(ecl * dr);

        Span<double> xh = stackalloc double[13];
        SunshineInit(lat, dec, xh);

        var mcdec = System.Math.Atan(System.Math.Sin(ctx.Th * dr) * System.Math.Tan(ecl * dr)) * rd;
        var mcUnderHorizon = System.Math.Abs(lat - mcdec) > 90;
        if (mcUnderHorizon && KeepMcSouth)
        {
            for (var ihx = 2; ihx <= 12; ihx++) xh[ihx] = -xh[ihx];
        }

        var retval = true;
        for (var ih = 1; ih <= 12; ih++)
        {
            if ((ih - 1) % 3 == 0) continue; // skip 1,4,7,10
            var xhs = 2 * System.Math.Asin(cosdec * System.Math.Sin(xh[ih] / 2.0 * dr)) * rd;
            var cosa = tandec * System.Math.Tan(xhs / 2.0 * dr);
            var alph = System.Math.Acos(cosa) * rd;
            double alpha2, b;
            if (ih > 7)
            {
                alpha2 = 180 - alph;
                b = 90 - lat + dec;
            }
            else
            {
                alpha2 = alph;
                b = 90 - lat - dec;
            }
            var cosc = System.Math.Cos(xhs * dr) * System.Math.Cos(b * dr)
                     + System.Math.Sin(xhs * dr) * System.Math.Sin(b * dr) * System.Math.Cos(alpha2 * dr);
            var c = System.Math.Acos(cosc) * rd;
            if (c < 1e-6)
            {
                ctx.Warning = $"Sunshine house {ih} c={c:G3} very small";
                retval = false;
            }
            var sinzd = System.Math.Sin(xhs * dr) * System.Math.Sin(alpha2 * dr) / System.Math.Sin(c * dr);
            var zd = System.Math.Asin(sinzd) * rd;
            var rax = System.Math.Atan(coslat * System.Math.Tan(zd * dr)) * rd;
            var pole = System.Math.Asin(sinzd * sinlat) * rd;
            double aRamc;
            if (ih <= 6)
            {
                pole = -pole;
                aRamc = AngleMath.NormalizeDegrees(rax + ctx.Th + 180);
            }
            else
            {
                aRamc = AngleMath.NormalizeDegrees(ctx.Th + rax);
            }
            ctx.Cusps[ih] = HouseAscendantMath.Asc1(aRamc, pole, sinecl, cosecl);
        }

        if (mcUnderHorizon && !KeepMcSouth)
        {
            for (var ih = 2; ih <= 12; ih++)
            {
                if ((ih - 1) % 3 == 0) continue;
                ctx.Cusps[ih] = AngleMath.NormalizeDegrees(ctx.Cusps[ih] + 180);
            }
        }
        ctx.DoInterpol = ctx.DoHspeed;
        return retval;
    }

    /// <summary>
    /// Mirrors <c>sunshine_init</c> at swehouse.c#L2878. Returns true on
    /// non-circumpolar Sun.
    /// </summary>
    internal static bool SunshineInit(double lat, double dec, Span<double> xh)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var arg = System.Math.Tan(dec * dr) * System.Math.Tan(lat * dr);
        double ad = arg switch
        {
            >= 1 => 90 - HouseAscendantMath.VerySmall,
            <= -1 => -90 + HouseAscendantMath.VerySmall,
            _ => System.Math.Asin(arg) * rd,
        };
        var nsa = 90 - ad;
        var dsa = 90 + ad;
        xh[2] = -2 * nsa / 3.0;
        xh[3] = -1 * nsa / 3.0;
        xh[5] = 1 * nsa / 3.0;
        xh[6] = 2 * nsa / 3.0;
        xh[8] = -2 * dsa / 3.0;
        xh[9] = -1 * dsa / 3.0;
        xh[11] = 1 * dsa / 3.0;
        xh[12] = 2 * dsa / 3.0;
        return System.Math.Abs(arg) < 1.0;
    }
}
