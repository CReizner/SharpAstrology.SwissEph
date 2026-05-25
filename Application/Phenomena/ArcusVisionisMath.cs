// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference driver: /tmp/heliacal_c_ref.c (sed-stripped statics from swehel.c).
// Mirrors swehel.c#L1562-L1810: TopoArcVisionis, HeliacalAngle, WidthMoon,
// LengthMoon, qYallop, crossing, x2min, funct2.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Arcus-visionis math from swehel.c — the bisection helpers
/// <c>TopoArcVisionis</c> / <c>HeliacalAngle</c> plus the Yallop crescent
/// helpers (<c>WidthMoon</c>, <c>LengthMoon</c>, <c>qYallop</c>) and the
/// search-loop quadratic-fit pieces (<c>x2min</c>, <c>funct2</c>,
/// <c>crossing</c>) used by <see cref="HeliacalService"/>.
/// </summary>
internal static class ArcusVisionisMath
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double Epsilon = HeliacalConstants.BisectionEpsilonDeg;
    private const double NoCrossing = HeliacalConstants.TopoArcVisionisNoCrossingDeg;
    private const double AvgRadiusMoonDeg = HeliacalConstants.AvgRadiusMoonDeg;

    /// <summary>
    /// Bisection over the assumed sun altitude that brings the visual
    /// limiting magnitude up to <paramref name="magn"/>. Mirrors swehel.c#L1562.
    /// Returns the arc-visionis altitude (object alt − sun alt at which the
    /// object becomes visible) in degrees, or <see cref="HeliacalConstants.TopoArcVisionisNoCrossingDeg"/>
    /// when the magnitude crossing is not bracketed in [0°, 45°].
    /// </summary>
    public static double TopoArcVisionis(
        double magn,
        ObserverParameters observer,
        double altODeg, double aziODeg,
        double altMDeg, double aziMDeg,
        JulianDay jdUt,
        double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters,
        AtmosphericConditions atm,
        HeliacalFlags helFlags)
    {
        double xR = 0.0;
        double xL = 45.0;
        double yL = magn - VisLimMagn(observer, altODeg, aziODeg, altMDeg, aziMDeg,
                                      jdUt, altODeg - xL, aziSDeg, sunRaDeg,
                                      latDeg, heightMeters, atm, helFlags);
        double yR = magn - VisLimMagn(observer, altODeg, aziODeg, altMDeg, aziMDeg,
                                      jdUt, altODeg - xR, aziSDeg, sunRaDeg,
                                      latDeg, heightMeters, atm, helFlags);

        double xm;
        if ((yL * yR) <= 0.0)
        {
            while (Math.Abs(xR - xL) > Epsilon)
            {
                xm = (xR + xL) / 2.0;
                var altSi = altODeg - xm;
                var ym = magn - VisLimMagn(observer, altODeg, aziODeg, altMDeg, aziMDeg,
                                            jdUt, altSi, aziSDeg, sunRaDeg,
                                            latDeg, heightMeters, atm, helFlags);
                if ((yL * ym) > 0.0)
                {
                    xL = xm;
                    yL = ym;
                }
                else
                {
                    xR = xm;
                    yR = ym;
                }
            }
            xm = (xR + xL) / 2.0;
        }
        else
        {
            xm = NoCrossing;
        }
        if (xm < altODeg) xm = altODeg;
        return xm;
    }

    /// <summary>
    /// Three-value heliacal-angle search: minimum-arc altitude (Xm),
    /// arc visionis at that altitude (Ym), and the sun altitude Xm−Ym.
    /// Mirrors swehel.c#L1636. Uses a coarse 1°-grid scan over [2°, 20°]
    /// followed by a directional bisection with a 0.025° derivative probe.
    /// </summary>
    public static (double XmDeg, double YmDeg, double SunAltDeg) HeliacalAngle(
        double magn,
        ObserverParameters observer,
        double aziODeg,
        double altMDeg, double aziMDeg,
        JulianDay jdUt,
        double aziSDeg,
        double latDeg, double heightMeters,
        AtmosphericConditions atm,
        HeliacalFlags helFlags)
    {
        // PLSV branch (swehel.c#L1643) is dead in shipped lib — PLSV is #define'd 0.
        var sunRaDeg = SkyBrightnessModel.SunRaSeasonalDeg(jdUt);

        // Coarse scan over altitude 2..20° for the global minimum.
        double xMin = 0.0;
        double yMin = 10000.0;
        for (var x = 2.0; x <= 20.0; x += 1.0)
        {
            var arc = TopoArcVisionis(magn, observer, x, aziODeg, altMDeg, aziMDeg,
                                      jdUt, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, helFlags);
            if (arc < yMin)
            {
                yMin = arc;
                xMin = x;
            }
        }

        // Bracket and bisect with directional derivative probe.
        var xL = xMin - 1.0;
        var xR = xMin + 1.0;
        var yR = TopoArcVisionis(magn, observer, xR, aziODeg, altMDeg, aziMDeg,
                                 jdUt, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, helFlags);
        var yL = TopoArcVisionis(magn, observer, xL, aziODeg, altMDeg, aziMDeg,
                                 jdUt, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, helFlags);

        const double deltaX = 0.025;
        double xm, ym;
        while (Math.Abs(xR - xL) > 0.1)
        {
            xm = (xR + xL) / 2.0;
            var xmd = xm + deltaX;
            ym = TopoArcVisionis(magn, observer, xm, aziODeg, altMDeg, aziMDeg,
                                 jdUt, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, helFlags);
            var ymd = TopoArcVisionis(magn, observer, xmd, aziODeg, altMDeg, aziMDeg,
                                      jdUt, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, helFlags);
            if (ym >= ymd)
            {
                xL = xm;
                yL = ym;
            }
            else
            {
                xR = xm;
                yR = ym;
            }
        }
        xm = (xR + xL) / 2.0;
        ym = (yR + yL) / 2.0;
        return (xm, ym, xm - ym);
    }

    /// <summary>
    /// Yallop (1998) crescent width [deg]. <paramref name="parallax"/> is the
    /// lunar topocentric parallax in degrees. Mirrors swehel.c#L1715.
    /// </summary>
    public static double WidthMoon(double altODeg, double aziODeg, double altSDeg, double aziSDeg, double parallaxDeg)
    {
        var geoAltO = altODeg + parallaxDeg;
        return 0.27245 * parallaxDeg
               * (1.0 + Math.Sin(geoAltO * DegToRad) * Math.Sin(parallaxDeg * DegToRad))
               * (1.0 - Math.Cos((altSDeg - geoAltO) * DegToRad) * Math.Cos((aziSDeg - aziODeg) * DegToRad));
    }

    /// <summary>
    /// Sultan (2005) crescent length [deg], given crescent width
    /// <paramref name="widthDeg"/> and lunar disc diameter
    /// <paramref name="diaMoonDeg"/> (0 → defaults to <c>2 ·
    /// <see cref="HeliacalConstants.AvgRadiusMoonDeg"/></c>). Mirrors
    /// swehel.c#L1726.
    /// </summary>
    public static double LengthMoon(double widthDeg, double diaMoonDeg)
    {
        if (diaMoonDeg == 0.0) diaMoonDeg = AvgRadiusMoonDeg * 2.0;
        var wMin = widthDeg * 60.0;
        var dMin = diaMoonDeg * 60.0;
        return (dMin - 0.3 * (dMin + wMin) / 2.0 / wMin) / 60.0;
    }

    /// <summary>
    /// Yallop visibility quality factor q. <paramref name="widthDeg"/> is the
    /// crescent width from <see cref="WidthMoon"/>, <paramref name="geoArcvDeg"/>
    /// the geocentric arc visionis. Mirrors swehel.c#L1741.
    /// </summary>
    public static double QYallop(double widthDeg, double geoArcvDeg)
    {
        var w = widthDeg * 60.0;
        return (geoArcvDeg - (11.8371 - 6.3226 * w + 0.7319 * w * w - 0.1018 * w * w * w)) / 10.0;
    }

    /// <summary>
    /// Linear-crossing point of two segments A→B and C→D parameterised over
    /// x ∈ [0, 1]. Mirrors swehel.c#L1753. Returns +∞/-∞/NaN when the segments
    /// are parallel (denominator zero) — the caller filters those.
    /// </summary>
    public static double Crossing(double a, double b, double c, double d)
    {
        return (c - a) / ((b - a) - (d - c));
    }

    /// <summary>
    /// x-coordinate of the minimum of the quadratic that passes through
    /// (1, A), (0, B), (-1, C). Mirrors swehel.c#L1791. Returns 0 when the
    /// three points are colinear (term=0).
    /// </summary>
    public static double X2Min(double a, double b, double c)
    {
        var term = a + c - 2.0 * b;
        if (term == 0.0) return 0.0;
        return -(a - c) / 2.0 / term;
    }

    /// <summary>
    /// Quadratic that interpolates (1, A), (0, B), (-1, C), evaluated at
    /// <paramref name="x"/>. Mirrors swehel.c#L1807.
    /// </summary>
    public static double Funct2(double a, double b, double c, double x)
    {
        return (a + c - 2.0 * b) / 2.0 * x * x + (a - c) / 2.0 * x + b;
    }

    /// <summary>
    /// Convenience wrapper: <see cref="VisibilityLimitMath.Compute"/> projected
    /// to the magnitude scalar (the bisection in <see cref="TopoArcVisionis"/>
    /// only needs the magnitude). Pure helper.
    /// </summary>
    private static double VisLimMagn(
        ObserverParameters observer,
        double altODeg, double aziODeg, double altMDeg, double aziMDeg,
        JulianDay jdUt, double altSDeg, double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters,
        AtmosphericConditions atm, HeliacalFlags helFlags)
    {
        var (mag, _) = VisibilityLimitMath.Compute(observer, altODeg, aziODeg, altMDeg, aziMDeg,
                                                    jdUt, altSDeg, aziSDeg, sunRaDeg,
                                                    latDeg, heightMeters, atm, helFlags);
        return mag;
    }
}
