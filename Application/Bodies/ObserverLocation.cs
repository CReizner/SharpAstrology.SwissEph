// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Mirrors the (geolon, geolat, geoalt) tuple set via swe_set_topo
// (sweph.c around line 7240). Pass per call, never mutated globally.

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Geographic observer position used for topocentric body calculations and
/// (later) house cusps. East-positive longitude, north-positive latitude,
/// height above mean sea level.
/// </summary>
/// <param name="LongitudeDegrees">East-positive geographic longitude (degrees).</param>
/// <param name="LatitudeDegrees">North-positive geographic latitude (degrees).</param>
/// <param name="HeightMeters">Height above the reference ellipsoid (metres).</param>
public readonly record struct ObserverLocation(
    double LongitudeDegrees,
    double LatitudeDegrees,
    double HeightMeters);
