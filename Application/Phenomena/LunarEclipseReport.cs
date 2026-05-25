// Ported from swisseph-master/swecl.c#L3237 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a lunar-eclipse query (<c>swe_lun_eclipse_how</c>).
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/>. One of
/// <see cref="EclipseTypeFlags.Total"/>, <see cref="EclipseTypeFlags.Partial"/>,
/// <see cref="EclipseTypeFlags.Penumbral"/>, or <see cref="EclipseTypeFlags.None"/>
/// when no lunar eclipse is in progress.
/// </param>
/// <param name="Attributes">Magnitudes and the Sun-Moon angular distance.</param>
/// <param name="Geometry">Fundamental-plane geometry of the Earth's shadow at the Moon's distance.</param>
public readonly record struct LunarEclipseReport(
    EclipseTypeFlags EclipseType,
    LunarEclipseAttributes Attributes,
    LunarEclipseGeometry Geometry);
