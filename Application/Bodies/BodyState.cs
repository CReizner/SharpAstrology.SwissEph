// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   PlanetocentricEclipticOfDate — swe_calc_pctr default frame
//   PlanetocentricJ2000Ecliptic  — swe_calc_pctr with SEFLG_J2000
//   PlanetocentricEquatorOfDate  — swe_calc_pctr with SEFLG_EQUATORIAL
//   PlanetocentricJ2000Equator   — swe_calc_pctr with SEFLG_J2000 | SEFLG_EQUATORIAL

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Result of a body-position calculation: cartesian position, velocity
/// and distance, plus the source that produced them and the reference
/// frame they live in.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are always cartesian. Linear quantities are in
/// astronomical units (AU); velocity components in AU per Terrestrial-
/// Time day. The interpretation of the axes (heliocentric vs.
/// geocentric, J2000 vs. of-date, ecliptic vs. equatorial) is given by
/// <see cref="Frame"/> — call sites or downstream services rotate /
/// translate as needed.
/// </para>
/// <para>
/// <see cref="Distance"/> is the magnitude of <see cref="Position"/> and
/// is supplied separately as a numerical convenience for callers that
/// only need range (e.g. light-time iteration).
/// </para>
/// </remarks>
/// <param name="Position">Cartesian position vector, AU.</param>
/// <param name="Velocity">Cartesian velocity vector, AU/day (TT).</param>
/// <param name="Distance">Magnitude of <paramref name="Position"/>, AU.</param>
/// <param name="Source">Back-end that produced the sample.</param>
/// <param name="Frame">Reference frame of the cartesian components.</param>
public readonly record struct BodyState(
    Vec3 Position,
    Vec3 Velocity,
    double Distance,
    EphemerisSource Source,
    BodyStateFrame Frame);

/// <summary>
/// Identifies the reference frame of a <see cref="BodyState"/>.
/// </summary>
public enum BodyStateFrame
{
    /// <summary>Heliocentric, J2000 ecliptic, cartesian (AU, AU/day).</summary>
    HeliocentricJ2000Ecliptic = 0,
    /// <summary>Geocentric, J2000 ecliptic, cartesian (AU, AU/day).</summary>
    GeocentricJ2000Ecliptic = 1,
    /// <summary>Geocentric, ecliptic-of-date, cartesian (AU, AU/day). Moshier moon raw output.</summary>
    GeocentricEclipticOfDate = 2,
    /// <summary>Heliocentric, J2000 equator (= ICRS axes), cartesian (AU, AU/day). SwissEph planet raw output.</summary>
    HeliocentricJ2000Equator = 3,
    /// <summary>Barycentric, J2000 equator, cartesian (AU, AU/day). SwissEph EMB raw output.</summary>
    BarycentricJ2000Equator = 4,
    /// <summary>Geocentric, J2000 equator, cartesian (AU, AU/day). SwissEph Moon raw output.</summary>
    GeocentricJ2000Equator = 5,
    /// <summary>Planetocentric (centered on a non-Earth body), ecliptic-of-date, cartesian (AU, AU/day).</summary>
    PlanetocentricEclipticOfDate = 6,
    /// <summary>Planetocentric (centered on a non-Earth body), J2000 ecliptic, cartesian (AU, AU/day).</summary>
    PlanetocentricJ2000Ecliptic = 7,
    /// <summary>Planetocentric (centered on a non-Earth body), equator-of-date, cartesian (AU, AU/day).</summary>
    PlanetocentricEquatorOfDate = 8,
    /// <summary>Planetocentric (centered on a non-Earth body), J2000 equator, cartesian (AU, AU/day).</summary>
    PlanetocentricJ2000Equator = 9,
}
