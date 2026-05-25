// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a global solar-eclipse query (<c>swe_sol_eclipse_where</c>).
/// Carries the eclipse-type flag set, the geographic location where the
/// shadow axis touches the Earth (or the place of maximum partial cover if
/// the axis misses), the attributes at that location, and the
/// fundamental-plane geometry.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/> describing the eclipse
/// classification (e.g. <c>Total | Central</c> or <c>Partial | NonCentral</c>).
/// </param>
/// <param name="MaximumLocation">
/// Geographic position where the eclipse is maximum. Longitude is
/// east-positive, latitude is north-positive. Altitude is set to 0.
/// Set to (0, 0, 0) when no eclipse is found.
/// </param>
/// <param name="Attributes">Attributes (magnitude, obscuration, …) at <see cref="MaximumLocation"/>.</param>
/// <param name="Geometry">Fundamental-plane shadow geometry.</param>
public readonly record struct SolarEclipseGlobalReport(
    EclipseTypeFlags EclipseType,
    GeographicLocation MaximumLocation,
    SolarEclipseAttributes Attributes,
    SolarEclipseGeometry Geometry);
