// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Result of <c>swe_orbit_max_min_true_distance</c> (swecl.c#L6159-L6276).
/// Maximum and minimum are derived from the osculating Kepler ellipse(s) at
/// the requested epoch — heliocentric uses the body's own ellipse, geocentric
/// scans the body's ellipse against the Earth-Moon-barycentre ellipse. The
/// "true" distance is the actual heliocentric or geocentric range at that
/// epoch.
/// </summary>
/// <param name="MaximumAu">Maximum possible distance over the osculating ellipse(s), AU.</param>
/// <param name="MinimumAu">Minimum possible distance over the osculating ellipse(s), AU.</param>
/// <param name="TrueAu">True distance at <c>jdEt</c>, AU.</param>
public readonly record struct DistanceExtrema(
    double MaximumAu,
    double MinimumAu,
    double TrueAu);
