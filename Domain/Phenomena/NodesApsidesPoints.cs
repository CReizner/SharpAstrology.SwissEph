// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Result of <c>swe_nod_aps</c>: longitude, latitude, distance plus speeds for
/// the four orbital points. Coordinates are in ecliptic-of-date polar form
/// (degrees, AU); speeds are degrees/day and AU/day.
/// </summary>
public readonly record struct NodesApsidesPoints(
    OrbitalPoint AscendingNode,
    OrbitalPoint DescendingNode,
    OrbitalPoint Perihelion,
    OrbitalPoint Aphelion);

/// <summary>
/// Position + speed of a single orbital point (node or apsis). Lon/Lat in
/// degrees, distance in AU; speeds per day.
/// </summary>
public readonly record struct OrbitalPoint(
    double LongitudeDeg,
    double LatitudeDeg,
    double DistanceAu,
    double LongitudeSpeedDegPerDay,
    double LatitudeSpeedDegPerDay,
    double DistanceSpeedAuPerDay);
