// Ported from swisseph-master/swecl.c:75-86 + sweph.h:283-284 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   SunDiameterMeters    — DSUN macro             (swecl.c#L80)
//   MoonDiameterMeters   — DMOON macro            (swecl.c#L82)
//   EarthDiameterMeters  — DEARTH macro           (swecl.c#L83)
//   SunDiameterAu        — DSUN
//   MoonDiameterAu       — DMOON
//   EarthDiameterAu      — DEARTH
//   SunRadiusAu          — RSUN
//   MoonRadiusAu         — RMOON
//   EarthRadiusAu        — REARTH
//   EarthOblateness      — EARTH_OBLATENESS       (sweph.h)
//   SarosCycleDays       — SAROS_CYCLE            (swecl.c#L114)
//   BodyDiameterMeters   — pla_diam[] table       (sweph.h#L313-L333). Note:
//                          pla_diam[SE_MOON] = 3_475_000 m differs from DMOON =
//                          3_476_300 m used in eclipse geometry; the occultation
//                          routines hard-wire RMOON regardless of the table entry.

using SharpAstrology.SwissEphemerides.Domain.Constants;

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Bodies' physical diameters and radii used by the eclipse / occultation
/// machinery, normalised to Astronomical Units. Earth oblateness follows AA 2006 K6.
/// </summary>
internal static class EclipseConstants
{
    private const double Aunit = AstronomicalConstants.AstronomicalUnitMeters;

    /// <summary>Sun's diameter in metres (1 392 000 km).</summary>
    public const double SunDiameterMeters = 1_392_000_000.0;

    /// <summary>Moon's diameter in metres (3 476.3 km).</summary>
    public const double MoonDiameterMeters = 3_476_300.0;

    /// <summary>Earth's equatorial diameter in metres (2 × 6 378 140).</summary>
    public const double EarthDiameterMeters = 6_378_140.0 * 2.0;

    /// <summary>Sun's diameter in AU.</summary>
    public const double SunDiameterAu = SunDiameterMeters / Aunit;

    /// <summary>Moon's diameter in AU.</summary>
    public const double MoonDiameterAu = MoonDiameterMeters / Aunit;

    /// <summary>Earth's equatorial diameter in AU.</summary>
    public const double EarthDiameterAu = EarthDiameterMeters / Aunit;

    /// <summary>Sun's radius in AU.</summary>
    public const double SunRadiusAu = SunDiameterAu / 2.0;

    /// <summary>Moon's radius in AU.</summary>
    public const double MoonRadiusAu = MoonDiameterAu / 2.0;

    /// <summary>Earth's equatorial radius in AU.</summary>
    public const double EarthRadiusAu = EarthDiameterAu / 2.0;

    /// <summary>Earth's reference radius used for fundamental-plane geometry (6 378.140 km / AU).</summary>
    public const double EarthReferenceRadiusAu = 6_378_140.0 / Aunit;

    /// <summary>Earth oblateness (1 / 298.25642), AA 2006 K6.</summary>
    public const double EarthOblateness = 1.0 / 298.25642;

    /// <summary>Length of one Saros cycle in days.</summary>
    public const double SarosCycleDays = 6585.3213;

    /// <summary>
    /// Returns the body's physical diameter in metres. Returns 0 for massless
    /// points (lunar nodes / apogees) and any unknown body; callers must treat
    /// 0 as "no disc — point body".
    /// </summary>
    public static double BodyDiameterMeters(int seBodyId) => seBodyId switch
    {
        0 => 1_392_000_000.0,    // Sun
        1 => 3_475_000.0,        // Moon
        2 => 2_439_400.0 * 2.0,  // Mercury
        3 => 6_051_800.0 * 2.0,  // Venus
        4 => 3_389_500.0 * 2.0,  // Mars
        5 => 69_911_000.0 * 2.0, // Jupiter
        6 => 58_232_000.0 * 2.0, // Saturn
        7 => 25_362_000.0 * 2.0, // Uranus
        8 => 24_622_000.0 * 2.0, // Neptune
        9 => 1_188_300.0 * 2.0,  // Pluto
        14 => 6_371_008.4 * 2.0, // Earth
        15 => 271_370.0,         // Chiron
        16 => 290_000.0,         // Pholus
        17 => 939_400.0,         // Ceres
        18 => 545_000.0,         // Pallas
        19 => 246_596.0,         // Juno
        20 => 525_400.0,         // Vesta
        _ => 0.0,
    };

    /// <summary>Body radius in AU — convenience wrapper around <see cref="BodyDiameterMeters"/>.</summary>
    public static double BodyRadiusAu(int seBodyId) => BodyDiameterMeters(seBodyId) / 2.0 / Aunit;
}
