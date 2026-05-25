// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Computation method for <see cref="Application.Phenomena.NodesAndApsidesService"/>.
/// Mirrors the <c>SE_NODBIT_*</c> bit field used by <c>swe_nod_aps</c>
/// (swephexp.h). Combine via bitwise OR.
/// </summary>
[System.Flags]
public enum NodesApsidesMethod
{
    /// <summary>Mean nodes/apsides — uses J2000 polynomial element tables. Default.</summary>
    Mean = 0,

    /// <summary>Osculating nodes/apsides — derived from the momentary Kepler ellipse.</summary>
    Osculating = 1,

    /// <summary>For osculating points beyond Jupiter, use barycentric ellipse instead of heliocentric.</summary>
    OsculatingBarycentric = 2,

    /// <summary>For asteroids, return the Mean nodes despite the body normally requiring osculating.</summary>
    MeanForceAsteroid = 4,

    /// <summary>Return the focal point of the orbital ellipse instead of the apsides.</summary>
    FocalPoint = 256,
}
