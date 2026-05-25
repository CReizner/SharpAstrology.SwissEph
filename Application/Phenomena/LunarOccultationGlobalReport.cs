// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a global lunar-occultation query (<c>swe_lun_occult_where</c>).
/// Carries the eclipse-type flag set, the geographic position where the
/// occultation shadow axis touches Earth (or where the partial obscuration
/// is maximum if the axis misses), per-observer attributes at that
/// location, and the fundamental-plane shadow geometry.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/> describing the occultation
/// (e.g. <c>Total | Central</c>, <c>Total | NonCentral</c>, or
/// <c>Partial | NonCentral</c>). When the Moon entirely covers a small
/// planetary disc the flag is <see cref="EclipseTypeFlags.Total"/>; when
/// the planet appears as a dot inside the bright limb the flag is
/// <see cref="EclipseTypeFlags.Annular"/>.
/// </param>
/// <param name="MaximumLocation">
/// Geographic position where the occultation is maximum. Longitude is
/// east-positive, latitude is north-positive. Altitude is set to 0.
/// </param>
/// <param name="Attributes">Attributes (magnitude, obscuration, …) at <see cref="MaximumLocation"/>.</param>
/// <param name="Geometry">Fundamental-plane shadow geometry.</param>
public readonly record struct LunarOccultationGlobalReport(
    EclipseTypeFlags EclipseType,
    GeographicLocation MaximumLocation,
    LunarOccultationAttributes Attributes,
    SolarEclipseGeometry Geometry);
