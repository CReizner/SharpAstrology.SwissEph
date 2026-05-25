// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a local solar-eclipse query (<c>swe_sol_eclipse_how</c>) — the
/// observer-specific view of an eclipse at a given UT.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/>. The <c>Visible</c> bit is set
/// when the body is above the horizon (with refraction/dip allowance).
/// Returns <see cref="EclipseTypeFlags.None"/> if the observer is outside
/// the eclipse cone at this instant.
/// </param>
/// <param name="Attributes">Per-observer attributes (magnitude, obscuration, azimuth, altitude, …).</param>
public readonly record struct SolarEclipseLocalReport(
    EclipseTypeFlags EclipseType,
    SolarEclipseAttributes Attributes);
