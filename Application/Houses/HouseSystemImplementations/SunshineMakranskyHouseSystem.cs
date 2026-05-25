// Ported from swehouse.c#L1156-L1181 (case 'i') + #L2906-L3046
// (sunshine_solution_makransky).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class SunshineMakranskyHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.SunshineMakransky;
    public int CuspCount => 12;
    // toupper('i') == 'I' so the post-CalcH mirror also skips us.
    public bool SkipsDefaultMirror => true;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
            // Note: Makransky branch ('i') keeps MC unchanged.
        }
        ctx.Cusps[4] = AngleMath.NormalizeDegrees(ctx.Cusps[10] + 180);
        ctx.Cusps[7] = AngleMath.NormalizeDegrees(ctx.Cusps[1] + 180);

        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var lat = ctx.Fi;
        var ecl = ctx.Ekl;
        var dec = ctx.SunDeclination;

        Span<double> xh = stackalloc double[13];
        if (!SunshineTreindlHouseSystem.SunshineInit(lat, dec, xh))
        {
            ctx.Warning = "within polar circle, switched to Porphyry";
            ctx.FellBackToPorphyry = true;
            return false;
        }

        var sinlat = System.Math.Sin(lat * dr);
        var coslat = System.Math.Cos(lat * dr);
        var tanlat = System.Math.Tan(lat * dr);
        var tandec = System.Math.Tan(dec * dr);
        var sinecl = System.Math.Sin(ecl * dr);

        for (var ih = 1; ih <= 12; ih++)
        {
            if ((ih - 1) % 3 == 0) continue; // skip 1,4,7,10
            var md = System.Math.Abs(xh[ih]);
            double rah = (ih <= 6)
                ? AngleMath.NormalizeDegrees(ctx.Th + 180 + xh[ih])
                : AngleMath.NormalizeDegrees(ctx.Th + xh[ih]);
            if (lat < 0) rah = AngleMath.NormalizeDegrees(180 + rah);

            double zd;
            if (md == 90)
            {
                zd = 90.0 - System.Math.Atan(sinlat * tandec) * rd;
            }
            else
            {
                double aPoint;
                if (md < 90)
                {
                    aPoint = System.Math.Atan(coslat * System.Math.Tan(md * dr)) * rd;
                }
                else
                {
                    aPoint = System.Math.Atan(System.Math.Tan((md - 90) * dr) / coslat) * rd;
                }
                var bPoint = System.Math.Atan(tanlat * System.Math.Cos(md * dr)) * rd;
                var cPoint = (ih <= 6) ? bPoint + dec : bPoint - dec;
                var fPoint = System.Math.Atan(sinlat * System.Math.Sin(md * dr) * System.Math.Tan(cPoint * dr)) * rd;
                zd = aPoint + fPoint;
            }
            var pole = System.Math.Asin(System.Math.Sin(zd * dr) * sinlat) * rd;
            var q = System.Math.Asin(tandec * System.Math.Tan(pole * dr)) * rd;
            double w = (ih <= 3 || ih >= 11)
                ? AngleMath.NormalizeDegrees(rah - q)
                : AngleMath.NormalizeDegrees(rah + q);
            double cu;
            if (w == 90)
            {
                var rTerm = System.Math.Atan(System.Math.Sin(ecl * dr) * System.Math.Tan(pole * dr)) * rd;
                cu = (ih <= 3 || ih >= 11) ? 90 + rTerm : 90 - rTerm;
            }
            else if (w == 270)
            {
                var rTerm = System.Math.Atan(sinecl * System.Math.Tan(pole * dr)) * rd;
                cu = (ih <= 3 || ih >= 11) ? 270 - rTerm : 270 + rTerm;
            }
            else
            {
                var m = System.Math.Atan(System.Math.Abs(System.Math.Tan(pole * dr) / System.Math.Cos(w * dr))) * rd;
                double z;
                if (ih <= 3 || ih >= 11)
                    z = (w > 90 && w < 270) ? m - ecl : m + ecl;
                else
                    z = (w > 90 && w < 270) ? m + ecl : m - ecl;
                if (z == 90)
                {
                    cu = w < 180 ? 90 : 270;
                }
                else
                {
                    var rTerm = System.Math.Atan(System.Math.Abs(System.Math.Cos(m * dr) * System.Math.Tan(w * dr) / System.Math.Cos(z * dr))) * rd;
                    if (w < 90) cu = rTerm;
                    else if (w > 90 && w < 180) cu = 180 - rTerm;
                    else if (w > 180 && w < 270) cu = 180 + rTerm;
                    else cu = 360 - rTerm;
                    if (z > 90)
                    {
                        if (w < 90) cu = 180 - rTerm;
                        else if (w > 90 && w < 180) cu = +rTerm;
                        else if (w > 180 && w < 270) cu = 360 - rTerm;
                        else cu = 180 + rTerm;
                    }
                    if (lat < 0) cu = AngleMath.NormalizeDegrees(cu + 180);
                }
            }
            ctx.Cusps[ih] = cu;
        }
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
