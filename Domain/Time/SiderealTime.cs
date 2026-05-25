// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   SiderealTimeModel  — SEMOD_SIDT_* macros           (swephexp.h#L541-L545)
//     Iau1976          — SEMOD_SIDT_IAU_1976
//     Iau2006          — SEMOD_SIDT_IAU_2006
//     IersConv2010     — SEMOD_SIDT_IERS_CONV_2010
//     LongTerm         — SEMOD_SIDT_LONGTERM (C library default)
//   Hours              — swe_sidtime0                  (swephlib.c#L3464-L3556)
//   swe_sidtime entry  — swe_sidtime                   (swephlib.c#L3580-L3594)
//   SidtimeLongTerm    — sidtime_long_term             (swephlib.c#L3285-L3324)
//   SidtimeNonPolynomialPart — sidtime_non_polynomial_part (swephlib.c#L3413-L3450)
//   SidtLtermT0        — SIDT_LTERM_T0                 (swephlib.c#L3460)
//   SidtLtermT1        — SIDT_LTERM_T1                 (swephlib.c#L3461)
//   SidtLtermOfs0      — SIDT_LTERM_OFS0               (swephlib.c#L3462)
//   SidtLtermOfs1      — SIDT_LTERM_OFS1               (swephlib.c#L3463)
//   SiderealTimeNonPolynomialCoefficients — C′_s/C′_c  (swephlib.c#L3341-L3375)
//   SiderealTimeNonPolynomialArguments    —             (swephlib.c#L3378-L3412)

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Time;

/// <summary>
/// Sidereal-time model selector.
/// </summary>
internal enum SiderealTimeModel
{
    /// <summary>Greenwich Mean Sidereal Time per IAU 1976.</summary>
    Iau1976 = 1,

    /// <summary>IAU 2006 precession, "short" form.</summary>
    Iau2006 = 2,

    /// <summary>IERS Conventions 2010 with non-polynomial part.</summary>
    IersConv2010 = 3,

    /// <summary>
    /// Default model — IERS 2010 between 1850 and 2050, long-term Simon-et-al.
    /// formula (with precession + nutation) outside.
    /// </summary>
    LongTerm = 4,
}

/// <summary>
/// Apparent and mean sidereal time at Greenwich for the IERS-Conventions-2010 /
/// IAU-2006 / IAU-1976 polynomial paths. <see cref="SiderealTimeModel.LongTerm"/>
/// currently falls through to the IERS path inside [1850, 2050]; the genuine
/// long-term branch is wired up by callers that have nutation / precession
/// context available.
/// </summary>
internal static class SiderealTime
{
    private const double J2000 = AstronomicalConstants.J2000;
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double JulianCentury = AstronomicalConstants.JulianCentury;

    /// <summary>1 Jan 1850 in JD-UT — lower bound of the IERS-2010 polynomial.</summary>
    private const double SidtLtermT0 = 2_396_758.5;

    /// <summary>1 Jan 2050 in JD-UT — upper bound of the IERS-2010 polynomial.</summary>
    private const double SidtLtermT1 = 2_469_807.5;

    /// <summary>Sidereal-time offset at the 1850 boundary, hours.</summary>
    private const double SidtLtermOfs0 = 0.000378172 / 15.0;

    /// <summary>Sidereal-time offset at the 2050 boundary, hours.</summary>
    private const double SidtLtermOfs1 = 0.001385646 / 15.0;

    /// <summary>
    /// Greenwich apparent / mean sidereal time in <b>hours</b> for a given UT,
    /// when the obliquity of the ecliptic and nutation have been computed
    /// independently.
    /// </summary>
    /// <param name="jdUt">Julian Day in UT.</param>
    /// <param name="trueObliquityDegrees">
    /// True (apparent) obliquity of the ecliptic in degrees (mean ε + Δε).
    /// Pass <c>0</c> together with <paramref name="nutationLongitudeDegrees"/> = 0
    /// to obtain Greenwich Mean Sidereal Time without the equation of the
    /// equinoxes.
    /// </param>
    /// <param name="nutationLongitudeDegrees">Nutation in longitude (Δψ) in degrees.</param>
    /// <param name="tidalAcceleration">Optional tidal-acceleration override forwarded to <see cref="DeltaT"/>.</param>
    /// <param name="model">Sidereal-time polynomial selector. Defaults to <see cref="SiderealTimeModel.LongTerm"/>.</param>
    public static double Hours(JulianDay jdUt, double trueObliquityDegrees, double nutationLongitudeDegrees,
        double? tidalAcceleration = null, SiderealTimeModel model = SiderealTimeModel.LongTerm)
    {
        var tjd = jdUt.Value;
        // Long-term branch outside [1850, 2050]: faithful port of
        // sidtime_long_term (swephlib.c#L3285-L3324).
        if (model == SiderealTimeModel.LongTerm
            && (tjd <= SidtLtermT0 || tjd >= SidtLtermT1))
        {
            var gmstHours = SidtimeLongTerm(jdUt, trueObliquityDegrees, nutationLongitudeDegrees, tidalAcceleration);
            if (tjd <= SidtLtermT0) gmstHours -= SidtLtermOfs0;
            else if (tjd >= SidtLtermT1) gmstHours -= SidtLtermOfs1;
            if (gmstHours >= 24) gmstHours -= 24;
            if (gmstHours < 0) gmstHours += 24;
            return gmstHours;
        }

        // Split JD into 0h-UT day and seconds since 0h UT, see swephlib.c#L3487-L3498.
        var jd0 = Math.Floor(tjd);
        var secs = tjd - jd0;
        if (secs < 0.5)
        {
            jd0 -= 0.5;
            secs += 0.5;
        }
        else
        {
            jd0 += 0.5;
            secs -= 0.5;
        }
        secs *= 86400.0;
        var tu = (jd0 - J2000) / JulianCentury; // UT1 in centuries after J2000

        double gmst, msday;

        if (model is SiderealTimeModel.IersConv2010 or SiderealTimeModel.LongTerm)
        {
            // ERA-based expression for GMST based on IAU 2006 precession.
            // swephlib.c#L3500-L3510
            var jdrel = tjd - J2000;
            var deltaT = DeltaT.InDays(jdUt, tidalAcceleration);
            var tt = (tjd + deltaT - J2000) / JulianCentury;
            gmst = AngleMath.NormalizeDegrees((0.7790572732640 + 1.00273781191135448 * jdrel) * 360.0);
            gmst += (0.014506 + tt * (4612.156534 + tt * (1.3915817 + tt * (-0.00000044 + tt * (-0.000029956 + tt * -0.0000000368))))) / 3600.0;
            var dadd = SidtimeNonPolynomialPart(tt);
            gmst = AngleMath.NormalizeDegrees(gmst + dadd);
            gmst = gmst / 15.0 * 3600.0;
        }
        else if (model == SiderealTimeModel.Iau2006)
        {
            // swephlib.c#L3512-L3518
            var deltaT = DeltaT.InDays(new JulianDay(jd0), tidalAcceleration);
            var tt = (jd0 + deltaT - J2000) / JulianCentury;
            gmst = (((-0.000000002454 * tt - 0.00000199708) * tt - 0.0000002926) * tt + 0.092772110) * tt * tt
                   + 307.4771013 * (tt - tu) + 8640184.79447825 * tu + 24110.5493771;
            msday = 1 + ((((-0.000000012270 * tt - 0.00000798832) * tt - 0.0000008778) * tt + 0.185544220) * tt + 8640184.79447825) / (86400.0 * JulianCentury);
            gmst += msday * secs;
        }
        else
        {
            // IAU 1976 (swephlib.c#L3520-L3525)
            gmst = ((-6.2e-6 * tu + 9.3104e-2) * tu + 8640184.812866) * tu + 24110.54841;
            msday = 1.0 + ((-1.86e-5 * tu + 0.186208) * tu + 8640184.812866) / (86400.0 * JulianCentury);
            gmst += msday * secs;
        }

        // Equation of the equinoxes — apparent sidereal time.
        // swephlib.c#L3527-L3531
        var eqeq = 240.0 * nutationLongitudeDegrees * Math.Cos(trueObliquityDegrees * DegToRad);
        gmst += eqeq;
        gmst -= 86400.0 * Math.Floor(gmst / 86400.0);
        return gmst / 3600.0;
    }

    /// <summary>
    /// Greenwich Mean Sidereal Time in hours for a given UT (no nutation
    /// applied). Equivalent to calling <see cref="Hours"/> with
    /// <c>trueObliquityDegrees</c> and <c>nutationLongitudeDegrees</c> set to
    /// zero.
    /// </summary>
    public static double MeanGreenwichHours(JulianDay jdUt, double? tidalAcceleration = null,
        SiderealTimeModel model = SiderealTimeModel.LongTerm)
        => Hours(jdUt, 0.0, 0.0, tidalAcceleration, model);

    /// <summary>
    /// Long-term sidereal time used outside [1850, 2050]. Returns hours.
    /// </summary>
    private static double SidtimeLongTerm(JulianDay jdUt, double epsilonDeg, double nutLongitudeDeg,
        double? tidalAcceleration)
    {
        var tjdUt = jdUt.Value;
        var dlt = AstronomicalConstants.AstronomicalUnitMeters / AstronomicalConstants.SpeedOfLightMeters / AstronomicalConstants.SecondsPerDay;
        var dtDays = DeltaT.InDays(jdUt, tidalAcceleration);
        var tjdEt = tjdUt + dtDays;
        var t = (tjdEt - J2000) / 365250.0;
        var t2 = t * t;
        var t3 = t * t2;
        // Mean longitude of Earth, J2000 (degrees).
        var dlon = 100.46645683 + (1295977422.83429 * t - 2.04411 * t2 - 0.00523 * t3) / 3600.0;
        dlon = AngleMath.NormalizeDegrees(dlon - dlt * 360.0 / 365.2425);
        Span<double> xs = stackalloc double[3];
        xs[0] = dlon * DegToRad; xs[1] = 0; xs[2] = 1;
        // To mean equator J2000 — rotate by -ε(J2000+ΔT(J2000)).
        var dtJ2000 = DeltaT.InDays(new JulianDay(J2000), tidalAcceleration);
        var oblJ2000 = Precession.MeanObliquity(J2000 + dtJ2000);
        PolarToCartesian(xs);
        Coordtrf(xs, -oblJ2000);
        // Precess to mean equinox of date.
        Precession.Apply(xs, J2000, tjdEt);
        // True obliquity & nutation of date.
        var oblDate = Precession.MeanObliquity(tjdEt);
        var nut = Nutation.Compute(tjdEt);
        var oblTrueDeg = oblDate * AstronomicalConstants.RadToDeg + nut.DeltaEpsilonRad * AstronomicalConstants.RadToDeg;
        var nutLonDeg = nut.DeltaPsiRad * AstronomicalConstants.RadToDeg;
        // To ecliptic of date.
        Coordtrf(xs, oblDate);
        CartesianToPolar(xs);
        var lonDeg = xs[0] * AstronomicalConstants.RadToDeg;
        var dhour = (tjdUt - 0.5 - System.Math.Floor(tjdUt - 0.5)) * 360.0;
        if (epsilonDeg == 0)
            lonDeg += nutLonDeg * System.Math.Cos(oblTrueDeg * DegToRad);
        else
            lonDeg += nutLongitudeDeg * System.Math.Cos(epsilonDeg * DegToRad);
        lonDeg = AngleMath.NormalizeDegrees(lonDeg + dhour);
        return lonDeg / 15.0;
    }

    private static void PolarToCartesian(Span<double> v)
    {
        var lon = v[0];
        var lat = v[1];
        var r = v[2];
        var cosLat = System.Math.Cos(lat);
        v[0] = r * cosLat * System.Math.Cos(lon);
        v[1] = r * cosLat * System.Math.Sin(lon);
        v[2] = r * System.Math.Sin(lat);
    }

    private static void CartesianToPolar(Span<double> v)
    {
        var x = v[0];
        var y = v[1];
        var z = v[2];
        if (x == 0 && y == 0 && z == 0)
        {
            v[0] = v[1] = v[2] = 0;
            return;
        }
        var rxy = x * x + y * y;
        var r = System.Math.Sqrt(rxy + z * z);
        rxy = System.Math.Sqrt(rxy);
        var lon = System.Math.Atan2(y, x);
        if (lon < 0) lon += AstronomicalConstants.TwoPi;
        var lat = rxy == 0
            ? (z >= 0 ? AstronomicalConstants.Pi / 2 : -AstronomicalConstants.Pi / 2)
            : System.Math.Atan(z / rxy);
        v[0] = lon;
        v[1] = lat;
        v[2] = r;
    }

    private static void Coordtrf(Span<double> v, double epsRad)
    {
        var sin = System.Math.Sin(epsRad);
        var cos = System.Math.Cos(epsRad);
        var y = v[1] * cos + v[2] * sin;
        var z = -v[1] * sin + v[2] * cos;
        v[1] = y;
        v[2] = z;
    }

    /// <summary>
    /// Non-polynomial part of the IERS Conventions 2010 sidereal-time expression.
    /// Returns degrees that are added to GMST. <paramref name="tt"/> is TT in
    /// Julian centuries from J2000.
    /// </summary>
    public static double SidtimeNonPolynomialPart(double tt)
    {
        Span<double> delm = stackalloc double[14];
        delm[0] = AngleMath.NormalizeRadians(2.35555598 + 8328.6914269554 * tt);
        delm[1] = AngleMath.NormalizeRadians(6.24006013 + 628.301955 * tt);
        delm[2] = AngleMath.NormalizeRadians(1.627905234 + 8433.466158131 * tt);
        delm[3] = AngleMath.NormalizeRadians(5.198466741 + 7771.3771468121 * tt);
        delm[4] = AngleMath.NormalizeRadians(2.18243920 - 33.757045 * tt);
        delm[5] = AngleMath.NormalizeRadians(4.402608842 + 2608.7903141574 * tt);
        delm[6] = AngleMath.NormalizeRadians(3.176146697 + 1021.3285546211 * tt);
        delm[7] = AngleMath.NormalizeRadians(1.753470314 + 628.3075849991 * tt);
        delm[8] = AngleMath.NormalizeRadians(6.203480913 + 334.0612426700 * tt);
        delm[9] = AngleMath.NormalizeRadians(0.599546497 + 52.9690962641 * tt);
        delm[10] = AngleMath.NormalizeRadians(0.874016757 + 21.3299104960 * tt);
        delm[11] = AngleMath.NormalizeRadians(5.481293871 + 7.4781598567 * tt);
        delm[12] = AngleMath.NormalizeRadians(5.321159000 + 3.8127774000 * tt);
        delm[13] = (0.02438175 + 0.00000538691 * tt) * tt;

        var dadd = -0.87 * Math.Sin(delm[4]) * tt;
        for (var i = 0; i < SidTermCount; i++)
        {
            double darg = 0;
            var argRow = i * SidArgCount;
            for (var j = 0; j < SidArgCount; j++)
            {
                darg += s_stfarg[argRow + j] * delm[j];
            }
            dadd += s_stcf[i * 2] * Math.Sin(darg) + s_stcf[i * 2 + 1] * Math.Cos(darg);
        }
        return dadd / (3600.0 * 1_000_000.0);
    }

    private const int SidTermCount = 33;
    private const int SidArgCount = 14;

    /// <summary>Sidereal-time non-polynomial coefficients (C′_s, C′_c) for the IERS 2010 series.</summary>
    private static readonly double[] s_stcf =
    {
        2640.96, -0.39,
        63.52, -0.02,
        11.75, 0.01,
        11.21, 0.01,
        -4.55, 0.00,
        2.02, 0.00,
        1.98, 0.00,
        -1.72, 0.00,
        -1.41, -0.01,
        -1.26, -0.01,
        -0.63, 0.00,
        -0.63, 0.00,
        0.46, 0.00,
        0.45, 0.00,
        0.36, 0.00,
        -0.24, -0.12,
        0.32, 0.00,
        0.28, 0.00,
        0.27, 0.00,
        0.26, 0.00,
        -0.21, 0.00,
        0.19, 0.00,
        0.18, 0.00,
        -0.10, 0.05,
        0.15, 0.00,
        -0.14, 0.00,
        0.14, 0.00,
        -0.14, 0.00,
        0.14, 0.00,
        0.13, 0.00,
        -0.11, 0.00,
        0.11, 0.00,
        0.11, 0.00,
    };

    /// <summary>Non-polynomial-argument matrix for the IERS 2010 series.</summary>
    private static readonly int[] s_stfarg =
    {
        0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, -2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, -2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, -2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 2, -2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 2, -2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 4, -4, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 1, -1, 1, 0, -8, 12, 0, 0, 0, 0, 0, 0,
        0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 2, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 2, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, -2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, -2, 2, -3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, -2, 2, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 8, -13, 0, 0, 0, 0, 0, -1,
        0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        2, 0, -2, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 0, -2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 2, -2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 0, -2, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 4, -2, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 2, -2, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, -2, 0, -3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 0, -2, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

}
