// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference driver: /tmp/heliacal_b_ref.c.
//
// Note (cf. AtmosphericModel): under SIMULATE_VICTORVB the high-precision
// branch of swehel.c#L563 (`SunRA`) is dead — `SunRaSeasonal` mirrors the
// shipped seasonal-only formula at swehel.c#L578-L580.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;
using SharpAstrology.SwissEphemerides.Domain.Time.Calendar;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Schaefer (2000) sky-brightness components: night-glow <c>Bn</c>, moon
/// <c>Bm</c>, twilight <c>Btwi</c>, daylight <c>Bday</c>, light-pollution
/// <c>Bcity</c>, and the orchestrator <c>Bsky</c>. All brightnesses are in
/// nLambert. Pure helpers — no caches, no implicit observer state.
/// </summary>
internal static class SkyBrightnessModel
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;
    private const double Erg2NL = HeliacalConstants.Erg2NL;
    private const double MoonDistKm = HeliacalConstants.MoonDistanceKm;
    private const double EarthRaKm = HeliacalConstants.EarthEquatorialRadius / 1000.0;
    // swehel.c#L1191 declares `double lunar_radius = 0.25 * DEGTORAD` and then
    // compares it against `RM` which is in degrees — the multiplication is a
    // unit bug in the upstream. The guard is effectively dead unless the
    // observer is within ≈16 arcsec of the moon centre. Mirroring the bug is
    // necessary for golden compatibility.
    private const double LunarRadiusClampDeg = 0.25 * DegToRad;
    private const double M0 = -11.05;   // sky-glow zero-mag reference (swehel.c)
    private const double MS = -26.74;   // apparent solar magnitude

    /// <summary>
    /// Sun right-ascension under <c>SIMULATE_VICTORVB</c> — the seasonal
    /// approximation from swehel.c#L578-L580. The shipped C library uses this
    /// branch unconditionally; the high-precision <c>swe_calc</c> branch is
    /// inactive (see <see cref="AtmosphericModel"/> header note).
    /// </summary>
    public static double SunRaSeasonalDeg(JulianDay jdUt)
    {
        var date = JulianDayConversion.ToCalendarDate(jdUt, CalendarSystem.Gregorian);
        var ra = (date.Month + (date.Day - 1) / 30.4 - 3.69) * 30.0;
        ra %= 360.0;
        if (ra < 0) ra += 360.0;
        return ra;
    }

    /// <summary>
    /// Lunar phase angle in degrees (0 = new, 180 = full). Mirrors
    /// <c>MoonPhase</c> at swehel.c#L1172. The 0.95° term is Reijs' empirical
    /// parallax correction.
    /// </summary>
    public static double MoonPhase(double altMDeg, double aziMDeg, double altSDeg, double aziSDeg)
    {
        const double moonAvgPar = 0.95;
        var altM = altMDeg * DegToRad;
        var altS = altSDeg * DegToRad;
        var aziM = aziMDeg * DegToRad;
        var aziS = aziSDeg * DegToRad;
        var dPar = moonAvgPar * DegToRad;
        var arg = Math.Cos(aziS - aziM - dPar) * Math.Cos(altM + dPar) * Math.Cos(altS)
                  + Math.Sin(altS) * Math.Sin(altM + dPar);
        return 180.0 - Math.Acos(arg) / DegToRad;
    }

    /// <summary>
    /// Apparent moon magnitude at the given Earth–Moon distance and phase
    /// angle (degrees). Mirrors <c>MoonsBrightness</c> at swehel.c#L1159.
    /// </summary>
    public static double MoonsBrightness(double distanceKm, double phaseDeg)
        => -21.62
           + 5.0 * Math.Log10(distanceKm / EarthRaKm)
           + 0.026 * Math.Abs(phaseDeg)
           + 0.000000004 * Math.Pow(phaseDeg, 4);

    /// <summary>
    /// Natural night-sky background <c>Bn</c> (nL). Mirrors swehel.c#L1073.
    /// Apparent altitudes below 10° are clamped (Vistas in Astronomy p. 343);
    /// the seasonal/sunspot factor varies with the calendar date.
    /// </summary>
    public static double Bn(
        double altODeg, JulianDay jdUt, double altSDeg, double sunRaDeg,
        double latDeg, double heightMeters, AtmosphericConditions atm,
        bool useHighPrecision)
    {
        var presE = AtmosphericModel.PresEFromPresS(atm.TemperatureCelsius, atm.PressureMbar, heightMeters);
        var tempE = AtmosphericModel.TempEFromTempS(atm.TemperatureCelsius, heightMeters,
                                                     HeliacalConstants.LapseStandardAtmosphere);
        var appAltO = AtmosphericModel.AppAltFromTopoAlt(altODeg, tempE, presE, useHighPrecision);
        if (appAltO < 10.0) appAltO = 10.0;
        var zend = (90.0 - appAltO) * DegToRad;

        var date = JulianDayConversion.ToCalendarDate(jdUt, CalendarSystem.Gregorian);
        const double b0 = 0.0000000000001; // Schaefer 2000, p. 128
        var bna = b0 * (1.0 + 0.3 * Math.Cos(
            6.283 * (date.Year + ((date.Day - 1) / 30.4 + date.Month - 1) / 12.0 - 1990.33) / 11.1));

        var kX = AtmosphericModel.DeltaMagnitudes(altODeg, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        var sinz = Math.Sin(zend);
        var bnb = bna * (0.4 + 0.6 / Math.Sqrt(1.0 - 0.96 * sinz * sinz)) * Math.Pow(10.0, -0.4 * kX);
        return Math.Max(bnb, 0.0) * Erg2NL;
    }

    /// <summary>
    /// Moon-scattered sky brightness <c>Bm</c> (nL). Mirrors swehel.c#L1186.
    /// Returns 0 when the moon is below horizon (≤ -0.26°) or when the
    /// observer line of sight is the moon itself.
    /// </summary>
    public static double Bm(
        double altODeg, double aziODeg, double altMDeg, double aziMDeg,
        double altSDeg, double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters, AtmosphericConditions atm,
        bool useHighPrecision)
    {
        if (altODeg == altMDeg && aziODeg == aziMDeg) return 0.0; // observer == moon
        if (altMDeg <= -0.26) return 0.0;                          // moon below horizon

        var rmDeg = AtmosphericModel.DistanceAngleRad(
            altODeg * DegToRad, aziODeg * DegToRad,
            altMDeg * DegToRad, aziMDeg * DegToRad) * RadToDeg;
        if (rmDeg <= LunarRadiusClampDeg) rmDeg = LunarRadiusClampDeg; // observer "behind" moon clamp (see constant note)

        var kxm = AtmosphericModel.DeltaMagnitudes(altMDeg, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        var kx = AtmosphericModel.DeltaMagnitudes(altODeg, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        var c3 = Math.Pow(10.0, -0.4 * kxm);
        var fm = 62_000_000.0 / (rmDeg * rmDeg)
                  + Math.Pow(10.0, 6.15 - rmDeg / 40.0)
                  + Math.Pow(10.0, 5.36) * (1.06 + Math.Pow(Math.Cos(rmDeg * DegToRad), 2));
        var bm = fm * c3 + 440_000.0 * (1.0 - c3);
        var phase = MoonPhase(altMDeg, aziMDeg, altSDeg, aziSDeg);
        var mm = MoonsBrightness(MoonDistKm, phase);
        bm *= Math.Pow(10.0, -0.4 * (mm - M0 + 43.27));
        bm *= 1.0 - Math.Pow(10.0, -0.4 * kx);
        return Math.Max(bm, 0.0) * Erg2NL;
    }

    /// <summary>
    /// Twilight sky brightness <c>Btwi</c> (nL). Mirrors swehel.c#L1218.
    /// Used when the sun's true altitude is below -3°.
    /// </summary>
    public static double Btwi(
        double altODeg, double aziODeg, double altSDeg, double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters, AtmosphericConditions atm,
        bool useHighPrecision)
    {
        var presE = AtmosphericModel.PresEFromPresS(atm.TemperatureCelsius, atm.PressureMbar, heightMeters);
        var tempE = AtmosphericModel.TempEFromTempS(atm.TemperatureCelsius, heightMeters,
                                                     HeliacalConstants.LapseStandardAtmosphere);
        var appAltO = AtmosphericModel.AppAltFromTopoAlt(altODeg, tempE, presE, useHighPrecision);
        var zendO = 90.0 - appAltO;
        var rsDeg = AtmosphericModel.DistanceAngleRad(
            altODeg * DegToRad, aziODeg * DegToRad,
            altSDeg * DegToRad, aziSDeg * DegToRad) * RadToDeg;
        var kx = AtmosphericModel.DeltaMagnitudes(altODeg, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        var k = AtmosphericModel.Kt(altSDeg, sunRaDeg, latDeg, heightMeters,
                                     atm.TemperatureCelsius, atm.RelativeHumidityPercent,
                                     atm.MeteorologicalRangeKm, extType: 4);

        var btwi = Math.Pow(10.0, -0.4 * (MS - M0 + 32.5 - altSDeg - zendO / (360.0 * k)));
        btwi *= 100.0 / rsDeg * (1.0 - Math.Pow(10.0, -0.4 * kx));
        return Math.Max(btwi, 0.0) * Erg2NL;
    }

    /// <summary>
    /// Daylight sky brightness <c>Bday</c> (nL). Mirrors swehel.c#L1246.
    /// Used when the sun's true altitude is above +4°.
    /// </summary>
    public static double Bday(
        double altODeg, double aziODeg, double altSDeg, double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters, AtmosphericConditions atm,
        bool useHighPrecision)
    {
        var rsDeg = AtmosphericModel.DistanceAngleRad(
            altODeg * DegToRad, aziODeg * DegToRad,
            altSDeg * DegToRad, aziSDeg * DegToRad) * RadToDeg;
        var kxs = AtmosphericModel.DeltaMagnitudes(altSDeg, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        var kx = AtmosphericModel.DeltaMagnitudes(altODeg, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        var c4 = Math.Pow(10.0, -0.4 * kxs);
        var fs = 62_000_000.0 / (rsDeg * rsDeg)
                  + Math.Pow(10.0, 6.15 - rsDeg / 40.0)
                  + Math.Pow(10.0, 5.36) * (1.06 + Math.Pow(Math.Cos(rsDeg * DegToRad), 2));
        var bday = fs * c4 + 440_000.0 * (1.0 - c4);
        bday *= Math.Pow(10.0, -0.4 * (MS - M0 + 43.27));
        bday *= 1.0 - Math.Pow(10.0, -0.4 * kx);
        return Math.Max(bday, 0.0) * Erg2NL;
    }

    /// <summary>
    /// Light-pollution baseline <c>Bcity</c> (nL). The C version takes a
    /// pressure parameter for symmetry with the other components but ignores
    /// it (swehel.c#L1271 explicitly silences the unused-warning); we omit
    /// the parameter.
    /// </summary>
    public static double Bcity(double valueNL)
        => Math.Max(valueNL, 0.0);

    /// <summary>
    /// Total sky brightness at the line of sight (nL). Mirrors swehel.c#L1279
    /// — twilight branch when AltS &lt; -3, daytime branch when AltS &gt; 4,
    /// the minimum of both in between (Schaefer's transition heuristic).
    /// Adds <see cref="Bm"/>, <see cref="Bcity"/>, and <see cref="Bn"/>
    /// only when the cumulative <c>Bsky</c> is below their respective
    /// "5% of total" thresholds.
    /// </summary>
    public static double Bsky(
        double altODeg, double aziODeg, double altMDeg, double aziMDeg,
        JulianDay jdUt, double altSDeg, double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters, AtmosphericConditions atm,
        bool useHighPrecision)
    {
        double bsky;
        if (altSDeg < -3.0)
        {
            bsky = Btwi(altODeg, aziODeg, altSDeg, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        }
        else if (altSDeg > 4.0)
        {
            bsky = Bday(altODeg, aziODeg, altSDeg, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);
        }
        else
        {
            bsky = Math.Min(
                Bday(altODeg, aziODeg, altSDeg, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision),
                Btwi(altODeg, aziODeg, altSDeg, aziSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision));
        }

        // Bm contributes only when current Bsky is small enough that Bm could matter.
        if (bsky < 200_000_000.0)
            bsky += Bm(altODeg, aziODeg, altMDeg, aziMDeg, altSDeg, aziSDeg, sunRaDeg,
                       latDeg, heightMeters, atm, useHighPrecision);

        if (altSDeg <= 0.0) bsky += Bcity(0.0);

        if (bsky < 5_000.0)
            bsky += Bn(altODeg, jdUt, altSDeg, sunRaDeg, latDeg, heightMeters, atm, useHighPrecision);

        return bsky;
    }
}
