// Ported from swisseph-master/swehouse.c#L2216-L2876 (swe_house_pos).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal static class HousePositionMath
{
    private const double MilliArcSec = 1.0 / 3_600_000.0;

    /// <summary>
    /// Returns the house position of an ecliptic point in [1, 13) (or [1, 37)
    /// for Gauquelin sectors). Mirrors the C source's huge per-system switch.
    /// </summary>
    public static double Compute(HouseService svc, double armcDeg, double geolatDeg, double obliquityDeg,
        HouseSystem hsys, double lonDeg, double latDeg, double sunDeclinationDeg = 0.0)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;
        var sine = System.Math.Sin(obliquityDeg * dr);
        var cose = System.Math.Cos(obliquityDeg * dr);

        // Cusp check (swehouse.c#L2232-L2266): if input matches a cusp, return its index.
        Span<double> cuspsCheck = stackalloc double[37];
        Span<double> ascmcCheck = stackalloc double[8];
        svc.ComputeFromArmcInto(armcDeg, geolatDeg, obliquityDeg, hsys,
            cuspsCheck, ascmcCheck, default, default, sunDeclinationDeg);
        var ito = (hsys == HouseSystem.Gauquelin) ? 36 : 12;
        for (var i = 1; i <= 12; i++)
        {
            if (System.Math.Abs(AngleMath.DifferenceDegreesSigned(lonDeg, cuspsCheck[i])) < MilliArcSec && latDeg == 0)
                return i;
        }

        // (lon, lat) → (RA, Dec)
        var ra = lonDeg;
        var de = latDeg;
        HouseAscendantMath.RotateEqToEclSpherical(ref ra, ref de, -obliquityDeg);
        var mdd = AngleMath.NormalizeDegrees(ra - armcDeg);
        var mdn = AngleMath.NormalizeDegrees(mdd + 180);
        if (mdd >= 180) mdd -= 360;
        if (mdn >= 180) mdn -= 360;

        // ---- Per-system geometric position. -----------------------------
        switch (hsys)
        {
            case HouseSystem.EqualAriesAnchored:
                return lonDeg / 30.0 + 1;

            case HouseSystem.Equal:
            case HouseSystem.EqualMc:
            case HouseSystem.Vehlow:
            case HouseSystem.WholeSign:
            {
                var asc = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(armcDeg + 90), geolatDeg, sine, cose);
                var mc = HouseAscendantMath.ArmcToMc(armcDeg, obliquityDeg);
                asc = HouseAscendantMath.FixAscPolar(asc, armcDeg, obliquityDeg, geolatDeg);
                var x0 = AngleMath.NormalizeDegrees(lonDeg - asc);
                if (hsys == HouseSystem.Vehlow) x0 = AngleMath.NormalizeDegrees(x0 + 15);
                if (hsys == HouseSystem.WholeSign) x0 = AngleMath.NormalizeDegrees(x0 + (asc % 30.0));
                if (hsys == HouseSystem.EqualMc) x0 = AngleMath.NormalizeDegrees(lonDeg - mc - 90);
                x0 = AngleMath.NormalizeDegrees(x0 + MilliArcSec);
                return x0 / 30.0 + 1;
            }

            case HouseSystem.Porphyry:
            case HouseSystem.Sripati:
            case HouseSystem.Alcabitius:
            {
                var asc = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(armcDeg + 90), geolatDeg, sine, cose);
                var mc = HouseAscendantMath.ArmcToMc(armcDeg, obliquityDeg);
                asc = HouseAscendantMath.FixAscPolar(asc, armcDeg, obliquityDeg, geolatDeg);
                if (hsys is HouseSystem.Porphyry or HouseSystem.Sripati)
                {
                    var x0 = AngleMath.NormalizeDegrees(lonDeg - asc);
                    x0 = AngleMath.NormalizeDegrees(x0 + MilliArcSec);
                    double hpos;
                    if (x0 < 180) hpos = 1;
                    else { hpos = 7; x0 -= 180; }
                    var acmc = AngleMath.DifferenceDegreesSigned(asc, mc);
                    if (x0 < 180 - acmc) hpos += x0 * 3 / (180 - acmc);
                    else hpos += 3 + (x0 - 180 + acmc) * 3 / acmc;
                    if (hsys == HouseSystem.Sripati)
                    {
                        hpos += 0.5;
                        if (hpos > 12) hpos = 1;
                    }
                    return hpos;
                }
                else // Alcabitius
                {
                    var dek = System.Math.Asin(System.Math.Sin(asc * dr) * sine) * rd;
                    var tanfi = System.Math.Tan(geolatDeg * dr);
                    var r = -tanfi * System.Math.Tan(dek * dr);
                    if (r > 1) r = 1;
                    if (r < -1) r = -1;
                    var sda = System.Math.Acos(r) * rd;
                    var sna = 180 - sda;
                    double hpos;
                    if (mdd > 0)
                    {
                        if (mdd < sda) hpos = mdd * 90 / sda;
                        else hpos = 90 + (mdd - sda) * 90 / sna;
                    }
                    else
                    {
                        if (mdd > -sna) hpos = 360 + mdd * 90 / sna;
                        else hpos = 270 + (mdd + sna) * 90 / sda;
                    }
                    hpos = AngleMath.NormalizeDegrees(hpos - 90) / 30.0 + 1.0;
                    if (hpos >= 13.0) hpos -= 12;
                    return hpos;
                }
            }

            case HouseSystem.Meridian:
                return AngleMath.NormalizeDegrees(mdd - 90) / 30.0 + 1.0;

            case HouseSystem.Morinus:
            {
                var a = lonDeg;
                double hpos;
                if (System.Math.Abs(a - 90) > HouseAscendantMath.VerySmall
                    && System.Math.Abs(a - 270) > HouseAscendantMath.VerySmall)
                {
                    var tant = System.Math.Tan(a * dr);
                    hpos = System.Math.Atan(tant / cose) * rd;
                    if (a > 90 && a <= 270) hpos = AngleMath.NormalizeDegrees(hpos + 180);
                }
                else
                {
                    hpos = System.Math.Abs(a - 90) <= HouseAscendantMath.VerySmall ? 90 : 270;
                }
                hpos = AngleMath.NormalizeDegrees(hpos - armcDeg - 90);
                return hpos / 30.0 + 1;
            }

            case HouseSystem.Campanus:
            {
                var ra2 = AngleMath.NormalizeDegrees(mdd - 90);
                var de2 = de;
                HouseAscendantMath.RotateEqToEclSpherical(ref ra2, ref de2, -geolatDeg);
                ra2 = AngleMath.NormalizeDegrees(ra2 + MilliArcSec);
                return ra2 / 30.0 + 1;
            }

            case HouseSystem.Horizon:
            {
                var ra2 = AngleMath.NormalizeDegrees(mdd - 90);
                var de2 = de;
                HouseAscendantMath.RotateEqToEclSpherical(ref ra2, ref de2, 90 - geolatDeg);
                ra2 = AngleMath.NormalizeDegrees(ra2 + MilliArcSec);
                return ra2 / 30.0 + 1;
            }

            case HouseSystem.Regiomontanus:
            {
                double xp;
                if (System.Math.Abs(mdd) < HouseAscendantMath.VerySmall) xp = 270;
                else if (180 - System.Math.Abs(mdd) < HouseAscendantMath.VerySmall) xp = 90;
                else
                {
                    var lat = geolatDeg;
                    var deLocal = de;
                    if (90 - System.Math.Abs(lat) < HouseAscendantMath.VerySmall)
                        lat = lat > 0 ? 90 - HouseAscendantMath.VerySmall : -90 + HouseAscendantMath.VerySmall;
                    if (90 - System.Math.Abs(deLocal) < HouseAscendantMath.VerySmall)
                        deLocal = deLocal > 0 ? 90 - HouseAscendantMath.VerySmall : -90 + HouseAscendantMath.VerySmall;
                    var aTerm = System.Math.Tan(lat * dr) * System.Math.Tan(deLocal * dr) + System.Math.Cos(mdd * dr);
                    xp = AngleMath.NormalizeDegrees(System.Math.Atan(-aTerm / System.Math.Sin(mdd * dr)) * rd);
                    if (mdd < 0) xp += 180;
                    xp = AngleMath.NormalizeDegrees(xp);
                    xp = AngleMath.NormalizeDegrees(xp + MilliArcSec);
                }
                return xp / 30.0 + 1;
            }

            case HouseSystem.CarterPoliEquatorial:
            {
                var asc = HouseAscendantMath.Asc1(AngleMath.NormalizeDegrees(armcDeg + 90), geolatDeg, sine, cose);
                asc = HouseAscendantMath.FixAscPolar(asc, armcDeg, obliquityDeg, geolatDeg);
                double lonAsc = asc, latAsc = 0;
                HouseAscendantMath.RotateEqToEclSpherical(ref lonAsc, ref latAsc, -obliquityDeg);
                return AngleMath.NormalizeDegrees(ra - lonAsc) / 30.0 + 1;
            }

            case HouseSystem.PolichPage:
            {
                // Binary search per swehouse.c#L2745-L2801. Stripped of the
                // serr/circumpolar branches.
                var fh = geolatDeg;
                if (fh > 89.999) fh = 89.999;
                if (fh < -89.999) fh = -89.999;
                var raLocal = ra;
                var deLocal = de;
                if (deLocal > 90 - HouseAscendantMath.VerySmall) deLocal = 90 - HouseAscendantMath.VerySmall;
                if (deLocal < -90 + HouseAscendantMath.VerySmall) deLocal = -90 + HouseAscendantMath.VerySmall;
                var sinad = System.Math.Tan(deLocal * dr) * System.Math.Tan(fh * dr);
                if (sinad > 1) sinad = 1;
                if (sinad < -1) sinad = -1;
                var aSign = sinad + System.Math.Cos(mdd * dr);
                var isAboveHor = aSign >= 0;
                var mddLocal = AngleMath.NormalizeDegrees(mdd);
                if (!isAboveHor)
                {
                    raLocal = AngleMath.NormalizeDegrees(raLocal + 180);
                    deLocal = -deLocal;
                    mddLocal = AngleMath.NormalizeDegrees(mddLocal + 180);
                }
                if (mddLocal > 180) raLocal = AngleMath.NormalizeDegrees(armcDeg - mddLocal);
                var tanfi = System.Math.Tan(fh * dr);
                var ra0 = AngleMath.NormalizeDegrees(armcDeg + 90);
                Span<double> xeq = stackalloc double[3];
                Span<double> xp = stackalloc double[3];
                xp[1] = 1;
                xeq[1] = deLocal;
                var fac = 2.0;
                var nloop = 0;
                while (System.Math.Abs(xp[1]) > 1e-6 && nloop < 1000)
                {
                    if (xp[1] > 0) { fh = System.Math.Atan(System.Math.Tan(fh * dr) - tanfi / fac) * rd; ra0 -= 90 / fac; }
                    else { fh = System.Math.Atan(System.Math.Tan(fh * dr) + tanfi / fac) * rd; ra0 += 90 / fac; }
                    xeq[0] = AngleMath.NormalizeDegrees(raLocal - ra0);
                    var lon2 = xeq[0];
                    var lat2 = xeq[1];
                    HouseAscendantMath.RotateEqToEclSpherical(ref lon2, ref lat2, 90 - fh);
                    xp[0] = lon2;
                    xp[1] = lat2;
                    fac *= 2;
                    nloop++;
                }
                var hpos = AngleMath.NormalizeDegrees(ra0 - armcDeg);
                if (mddLocal > 180) hpos = AngleMath.NormalizeDegrees(-hpos);
                if (!isAboveHor) hpos = AngleMath.NormalizeDegrees(hpos + 180);
                return AngleMath.NormalizeDegrees(hpos - 90) / 30 + 1;
            }

            case HouseSystem.Placidus:
            case HouseSystem.Gauquelin:
            {
                double xp;
                if (90 - System.Math.Abs(de) <= System.Math.Abs(geolatDeg))
                {
                    if (de * geolatDeg < 0) xp = AngleMath.NormalizeDegrees(90 + mdn / 2);
                    else xp = AngleMath.NormalizeDegrees(270 + mdd / 2);
                }
                else
                {
                    var sinad = System.Math.Tan(de * dr) * System.Math.Tan(geolatDeg * dr);
                    var ad = System.Math.Asin(sinad) * rd;
                    var aSign = sinad + System.Math.Cos(mdd * dr);
                    var isAboveHor = aSign >= 0;
                    var sad = 90 + ad;
                    var san = 90 - ad;
                    xp = isAboveHor ? (mdd / sad + 3) * 90 : (mdn / san + 1) * 90;
                    xp = AngleMath.NormalizeDegrees(xp + MilliArcSec);
                }
                if (hsys == HouseSystem.Gauquelin)
                {
                    xp = 360 - xp;
                    return xp / 10.0 + 1;
                }
                return xp / 30.0 + 1;
            }

            // Fallbacks: simplified algorithm — locate house by linear interpolation.
            default:
            {
                Span<double> cusps = stackalloc double[37];
                Span<double> ascmc = stackalloc double[8];
                svc.ComputeFromArmcInto(armcDeg, geolatDeg, obliquityDeg, hsys,
                    cusps, ascmc, default, default, sunDeclinationDeg);
                return SimplifiedHpos(cusps, lonDeg, ito);
            }
        }
    }

    private static double SimplifiedHpos(ReadOnlySpan<double> hcusp, double lonDeg, int _)
    {
        // Mirrors swehouse.c#L2842-L2870: locate input within the
        // sequential-cusp arrangement, using linear interpolation.
        double d, c1 = 0, c2 = 0;
        int i = 1, j;
        if (AngleMath.DifferenceDegreesSigned(hcusp[6], hcusp[1]) > 0)
        {
            d = AngleMath.NormalizeDegrees(lonDeg - hcusp[1]);
            for (i = 1; i <= 12; i++)
            {
                j = i + 1;
                if (j > 12) c2 = 360;
                else c2 = AngleMath.NormalizeDegrees(hcusp[j] - hcusp[1]);
                if (d < c2) break;
            }
            c1 = AngleMath.NormalizeDegrees(hcusp[i] - hcusp[1]);
        }
        else
        {
            d = AngleMath.NormalizeDegrees(hcusp[1] - lonDeg);
            for (i = 1; i <= 12; i++)
            {
                j = i + 1;
                if (j > 12) c2 = 360;
                else c2 = AngleMath.NormalizeDegrees(hcusp[1] - hcusp[j]);
                if (d < c2) break;
            }
            c1 = AngleMath.NormalizeDegrees(hcusp[1] - hcusp[i]);
        }
        var hsize = c2 - c1;
        return hsize == 0 ? i : i + (d - c1) / hsize;
    }
}
