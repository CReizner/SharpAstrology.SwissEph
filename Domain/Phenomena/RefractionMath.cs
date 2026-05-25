// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   Refrac                — swe_refrac              (swecl.c#L2887)  — Meeus branch
//   RefracExtended        — swe_refrac_extended     (swecl.c#L3035)
//   CalcAstronomicalRefr  — calc_astronomical_refr  (swecl.c#L3124)  — Sinclair (active #else branch)
//   CalcDip               — calc_dip                (swecl.c#L3158)  — Thom 1973 / Reijs 2000
//   Direction.TrueToApparent — SE_TRUE_TO_APP = 0
//   Direction.ApparentToTrue — SE_APP_TO_TRUE = 1
//   DefaultLapseRate      — const_lapse_rate
//   RefractionExtendedResult — dret[4] of swe_refrac_extended

using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Atmospheric-refraction primitives. Pure functions in degrees / millibars / Celsius.
/// </summary>
public static class RefractionMath
{
    /// <summary>Mean tropospheric lapse rate, K/m.</summary>
    public const double DefaultLapseRate = 0.0065;

    private const double DegToRad = AstronomicalConstants.DegToRad;

    /// <summary>Direction flag for <see cref="Refrac"/> / <see cref="RefracExtended"/>.</summary>
    public enum Direction
    {
        /// <summary>True altitude → apparent altitude.</summary>
        TrueToApparent = 0,
        /// <summary>Apparent altitude → true altitude.</summary>
        ApparentToTrue = 1,
    }

    /// <summary>
    /// Refraction conversion (Meeus formulation). Inputs/outputs in degrees;
    /// pressure in mbar, temperature in °C.
    /// </summary>
    public static double Refrac(double inAltDeg, double atPressMbar, double atTempC, Direction direction)
    {
        var ptFactor = atPressMbar / 1010.0 * 283.0 / (273.0 + atTempC);
        if (direction == Direction.TrueToApparent)
        {
            var trualt = inAltDeg;
            double refr;
            if (trualt > 15)
            {
                var a = System.Math.Tan((90 - trualt) * DegToRad);
                refr = (58.276 * a - 0.0824 * a * a * a) * ptFactor / 3600.0;
            }
            else if (trualt > -5)
            {
                var a = trualt + 10.3 / (trualt + 5.11);
                refr = a + 1e-10 >= 90 ? 0 : 1.02 / System.Math.Tan(a * DegToRad);
                refr *= ptFactor / 60.0;
            }
            else
            {
                refr = 0;
            }
            var appalt = trualt;
            if (appalt + refr > 0) appalt += refr;
            return appalt;
        }
        else
        {
            var appalt = inAltDeg;
            var a = appalt + 7.31 / (appalt + 4.4);
            double refr;
            if (a + 1e-10 >= 90)
            {
                refr = 0;
            }
            else
            {
                refr = 1.00 / System.Math.Tan(a * DegToRad);
                refr -= 0.06 * System.Math.Sin(14.7 * refr + 13);
            }
            refr *= ptFactor / 60.0;
            var trualt = appalt;
            if (appalt - refr > 0) trualt = appalt - refr;
            return trualt;
        }
    }

    /// <summary>
    /// Output bundle from <see cref="RefracExtended"/>: true altitude, apparent
    /// altitude, refraction (degrees) and dip of the horizon (degrees).
    /// </summary>
    public readonly record struct RefractionExtendedResult(
        double TrueAltitudeDeg,
        double ApparentAltitudeDeg,
        double RefractionDeg,
        double HorizonDipDeg)
    {
        /// <summary>True iff the body is above the (ideal) horizon.</summary>
        public bool AboveHorizon => TrueAltitudeDeg != ApparentAltitudeDeg;
    }

    /// <summary>
    /// Refraction conversion with horizon-dip handling. Returns the converted
    /// altitude (true→apparent or vice versa) and fills <paramref name="result"/>
    /// with the per-component output. Geometric observer altitude
    /// (<paramref name="geoAltMeters"/>) is required for the horizon-dip term.
    /// </summary>
    public static double RefracExtended(
        double inAltDeg,
        double geoAltMeters,
        double atPressMbar,
        double atTempC,
        double lapseRate,
        Direction direction,
        out RefractionExtendedResult result)
    {
        var dip = CalcDip(geoAltMeters, atPressMbar, atTempC, lapseRate);
        var inalt = inAltDeg;
        if (inalt > 90) inalt = 180 - inalt;

        if (direction == Direction.TrueToApparent)
        {
            if (inalt < -10)
            {
                result = new RefractionExtendedResult(inalt, inalt, 0, dip);
                return inalt;
            }
            var y = inalt;
            double d = 0.0;
            double yy0 = 0;
            var d0 = d;
            for (var i = 0; i < 5; i++)
            {
                d = CalcAstronomicalRefr(y, atPressMbar, atTempC);
                var n = y - yy0;
                yy0 = d - d0 - n;
                if (n != 0.0 && yy0 != 0.0)
                    n = y - n * (inalt + d - y) / yy0;
                else
                    n = inalt + d;
                yy0 = y;
                d0 = d;
                y = n;
            }
            var refr = d;
            if (inalt + refr < dip)
            {
                result = new RefractionExtendedResult(inalt, inalt, 0, dip);
                return inalt;
            }
            result = new RefractionExtendedResult(inalt, inalt + refr, refr, dip);
            return inalt + refr;
        }
        else
        {
            var refr = CalcAstronomicalRefr(inalt, atPressMbar, atTempC);
            var trualt = inalt - refr;
            if (inalt > dip)
                result = new RefractionExtendedResult(trualt, inalt, refr, dip);
            else
                result = new RefractionExtendedResult(inalt, inalt, 0, dip);
            return inalt >= dip ? trualt : inalt;
        }
    }

    /// <summary>
    /// Sinclair atmospheric-refraction formula.
    /// </summary>
    public static double CalcAstronomicalRefr(double inAltDeg, double atPressMbar, double atTempC)
    {
        double r;
        if (inAltDeg > 17.904104638432)
        {
            r = 0.97 / System.Math.Tan(inAltDeg * DegToRad);
        }
        else
        {
            r = (34.46 + 4.23 * inAltDeg + 0.004 * inAltDeg * inAltDeg)
                / (1 + 0.505 * inAltDeg + 0.0845 * inAltDeg * inAltDeg);
        }
        return ((atPressMbar - 80) / 930
                / (1 + 0.00008 * (r + 39) * (atTempC - 10))
                * r) / 60.0;
    }

    /// <summary>
    /// Dip of the horizon, in degrees (negative). Formula based on Thom 1973 / Reijs 2000.
    /// </summary>
    public static double CalcDip(double geoAltMeters, double atPressMbar, double atTempC, double lapseRate)
    {
        var krefr = (0.0342 + lapseRate) / (0.154 * 0.0238);
        var d = 1 - 1.8480 * krefr * atPressMbar
            / (273.15 + atTempC) / (273.15 + atTempC);
        return -180.0 / AstronomicalConstants.Pi
               * System.Math.Acos(1 / (1 + geoAltMeters / AstronomicalConstants.EarthRadiusMeters))
               * System.Math.Sqrt(d);
    }
}
