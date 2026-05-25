// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Flags for the rise/transit finder. Mirrors the <c>SE_CALC_*</c> /
/// <c>SE_BIT_*</c> bits at swephexp.h#L336-L361.
/// </summary>
[Flags]
public enum RiseTransitFlags
{
    /// <summary><c>SE_CALC_RISE</c>.</summary>
    Rise = 1,
    /// <summary><c>SE_CALC_SET</c>.</summary>
    Set = 2,
    /// <summary><c>SE_CALC_MTRANSIT</c> (upper meridian transit).</summary>
    UpperMeridianTransit = 4,
    /// <summary><c>SE_CALC_ITRANSIT</c> (lower meridian transit).</summary>
    LowerMeridianTransit = 8,
    /// <summary><c>SE_BIT_GEOCTR_NO_ECL_LAT</c>: ignore ecliptic latitude (Hindu use).</summary>
    GeocentricNoEclipticLatitude = 128,
    /// <summary><c>SE_BIT_DISC_CENTER</c>: rise/set of disc centre, not upper limb.</summary>
    DiscCenter = 256,
    /// <summary><c>SE_BIT_NO_REFRACTION</c>: skip refraction in the rise/set check.</summary>
    NoRefraction = 512,
    /// <summary><c>SE_BIT_CIVIL_TWILIGHT</c>: civil twilight (Sun -6°).</summary>
    CivilTwilight = 1024,
    /// <summary><c>SE_BIT_NAUTIC_TWILIGHT</c>: nautical twilight (Sun -12°).</summary>
    NauticalTwilight = 2048,
    /// <summary><c>SE_BIT_ASTRO_TWILIGHT</c>: astronomical twilight (Sun -18°).</summary>
    AstronomicalTwilight = 4096,
    /// <summary><c>SE_BIT_DISC_BOTTOM</c>: rise/set of lower limb (e.g. Hindu).</summary>
    DiscBottom = 8192,
    /// <summary><c>SE_BIT_FIXED_DISC_SIZE</c>: neglect distance on disc size.</summary>
    FixedDiscSize = 16384,
    /// <summary><c>SE_BIT_FORCE_SLOW_METHOD</c>: force the slow (full-iteration) algorithm.</summary>
    ForceSlowMethod = 32768,
    /// <summary><c>SE_BIT_HINDU_RISING = DiscCenter | NoRefraction | GeocentricNoEclipticLatitude</c>.</summary>
    HinduRising = DiscCenter | NoRefraction | GeocentricNoEclipticLatitude,
}
