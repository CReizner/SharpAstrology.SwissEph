// Ported from swisseph-master/swephlib.h and sweph.h (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   TwoPi                       — TWOPI                (swephlib.h)
//   DegToRad                    — DEGTORAD             (swephlib.h)
//   RadToDeg                    — RADTODEG             (swephlib.h)
//   LightTimeAuPerDay           — AUNIT / CLIGHT / 86400
//   TidalAccelerationDefault    — SE_TIDAL_DEFAULT
//   EarthRadiusMeters           — EARTH_RADIUS         (sweph.h)
//   HelioGravConst              — HELGRAVCONST         (sweph.h)
//   GeoGravConst                — GEOGCONST            (sweph.h)
//   EarthMoonMassRatio          — EARTH_MOON_MRAT      (sweph.h)
//   MoonMeanDistanceMeters      — MOON_MEAN_DIST       (sweph.h)
//   MoonMeanInclinationDeg      — MOON_MEAN_INCL       (sweph.h)
//   MoonMeanEccentricity        — MOON_MEAN_ECC        (sweph.h)
//   NodeCalcIntervalDays        — NODE_CALC_INTV       (sweph.h)

namespace SharpAstrology.SwissEphemerides.Domain.Constants;

/// <summary>
/// Astronomical and mathematical constants used across the Swiss Ephemeris port.
/// </summary>
internal static class AstronomicalConstants
{
    /// <summary>π.</summary>
    public const double Pi = System.Math.PI;

    /// <summary>2π.</summary>
    public const double TwoPi = 2.0 * System.Math.PI;

    /// <summary>Conversion factor degrees → radians.</summary>
    public const double DegToRad = 0.0174532925199432957692369076848861271;

    /// <summary>Conversion factor radians → degrees.</summary>
    public const double RadToDeg = 57.2957795130823208767981548141051703;

    /// <summary>Astronomical Unit in metres (IAU 2009/2012).</summary>
    public const double AstronomicalUnitMeters = 149_597_870_700.0;

    /// <summary>Astronomical Unit in kilometres.</summary>
    public const double AstronomicalUnitKilometers = AstronomicalUnitMeters / 1000.0;

    /// <summary>Speed of light in metres per second.</summary>
    public const double SpeedOfLightMeters = 299_792_458.0;

    /// <summary>Light-time for one Astronomical Unit, in days.</summary>
    public const double LightTimeAuPerDay = AstronomicalUnitMeters / SpeedOfLightMeters / 86_400.0;

    /// <summary>Julian date of the J2000.0 epoch (TT).</summary>
    public const double J2000 = 2_451_545.0;

    /// <summary>Julian date of the B1950.0 epoch.</summary>
    public const double B1950 = 2_433_282.42345905;

    /// <summary>Julian date of the J1900.0 epoch.</summary>
    public const double J1900 = 2_415_020.0;

    /// <summary>Julian centuries (36 525 days), the time unit of fundamental ephemeris polynomials.</summary>
    public const double JulianCentury = 36_525.0;

    /// <summary>Standard tidal acceleration of the Moon used for ΔT.</summary>
    public const double TidalAccelerationDefault = -25.8;

    /// <summary>Number of seconds per day.</summary>
    public const double SecondsPerDay = 86_400.0;

    /// <summary>Mean Earth radius in metres (AA 2006 K6).</summary>
    public const double EarthRadiusMeters = 6_378_136.6;

    /// <summary>Heliocentric gravitational constant G·M(sun), m³/s² (AA 2006 K6).</summary>
    public const double HelioGravConst = 1.32712440017987e+20;

    /// <summary>Geocentric gravitational constant G·M(earth), m³/s² (AA 1996 K6).</summary>
    public const double GeoGravConst = 3.98600448e+14;

    /// <summary>Earth/Moon mass ratio (AA 2006 K7).</summary>
    public const double EarthMoonMassRatio = 1.0 / 0.0123000383;

    /// <summary>Mean lunar geocentric distance in metres (AA 1996 F2).</summary>
    public const double MoonMeanDistanceMeters = 384_400_000.0;

    /// <summary>Mean lunar orbital inclination in degrees (AA 1996 D2).</summary>
    public const double MoonMeanInclinationDeg = 5.1453964;

    /// <summary>Mean lunar orbital eccentricity (AA 1996 F2).</summary>
    public const double MoonMeanEccentricity = 0.054900489;

    /// <summary>Time interval used for finite-difference node speed calculation, in days.</summary>
    public const double NodeCalcIntervalDays = 0.0001;
}
