// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Three-value result of <c>swe_heliacal_angle</c>: the optimum object
/// altitude, the corresponding arc visionis, and the implied sun altitude.
/// Mirrors the <c>dret[3]</c> output at swehel.c#L1689-L1691.
/// </summary>
/// <param name="ObjectAltitudeDeg">
/// Object altitude (deg) at which the arc visionis is minimal — i.e. the
/// altitude where the body becomes most easily visible. Mirrors <c>dret[0]</c>.
/// </param>
/// <param name="ArcVisionisDeg">
/// Arc visionis (deg) at <see cref="ObjectAltitudeDeg"/> — the altitude
/// difference between object and sun required for visibility. Mirrors
/// <c>dret[1]</c>.
/// </param>
/// <param name="SunAltitudeDeg">
/// Implied sun altitude (deg) at the moment of best visibility =
/// <see cref="ObjectAltitudeDeg"/> − <see cref="ArcVisionisDeg"/>. Mirrors
/// <c>dret[2]</c>.
/// </param>
public readonly record struct HeliacalAngleResult(
    double ObjectAltitudeDeg,
    double ArcVisionisDeg,
    double SunAltitudeDeg);
