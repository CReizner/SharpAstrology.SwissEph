// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System.Diagnostics.CodeAnalysis;

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Result of a heliacal-event search. Mirrors the three-value <c>dret</c>
/// returned by <c>swe_heliacal_ut</c> at swehel.c#L3385: the moment the
/// object first becomes visible, the optimum (best-magnitude) moment, and
/// the moment visibility ends.
/// </summary>
/// <param name="VisibilityStartJdUt">
/// <c>dret[0]</c>: Julian Day (UT) at which the heliacal event begins —
/// the object first crosses the limiting-magnitude threshold.
/// </param>
/// <param name="OptimumVisibilityJdUt">
/// <c>dret[1]</c>: Julian Day (UT) of best visibility (highest
/// <c>Vmag − ObjectMag</c>). Zero if <see cref="HeliacalFlags.NoDetails"/>
/// suppresses the refinement step.
/// </param>
/// <param name="VisibilityEndJdUt">
/// <c>dret[2]</c>: Julian Day (UT) at which visibility ends. Zero when
/// suppressed.
/// </param>
[Experimental("SE0001")]
public readonly record struct HeliacalEvent(
    double VisibilityStartJdUt,
    double OptimumVisibilityJdUt,
    double VisibilityEndJdUt);
