// Ported from swisseph-master/swecl.c#L3237 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Attributes of a lunar eclipse. Mirrors the <c>attr[20]</c> output array
/// of <c>swe_lun_eclipse_how</c> / <c>swe_lun_eclipse_when</c>.
/// </summary>
/// <param name="UmbralMagnitude">
/// attr[0] — umbral magnitude (fraction of lunar diameter covered by the
/// umbra; 0 if no umbral phase).
/// </param>
/// <param name="PenumbralMagnitude">
/// attr[1] — penumbral magnitude (fraction of lunar diameter covered by
/// the penumbra).
/// </param>
/// <param name="MoonBodyAngularDistanceDeg">
/// attr[7] — 180° minus the geocentric Sun-Moon angular distance.
/// Equals the angular distance between the Moon and the antisolar point.
/// </param>
/// <param name="UmbralMagnitudeAlias">
/// attr[8] — duplicate of <see cref="UmbralMagnitude"/>; preserved for
/// faithful mirroring of the C output.
/// </param>
/// <param name="SarosSeriesNumber">attr[9] — Saros series number, or -99 999 999 if unknown.</param>
/// <param name="SarosSeriesMemberNumber">attr[10] — Saros series member, or -99 999 999 if unknown.</param>
/// <param name="MoonAzimuthDeg">
/// attr[4] — azimuth of the Moon at the observer (degrees from south,
/// clockwise). Zero when no observer is supplied (geocentric path).
/// </param>
/// <param name="MoonTrueAltitudeDeg">
/// attr[5] — true (geometric) altitude of the Moon above the observer
/// horizon. Zero in the geocentric path.
/// </param>
/// <param name="MoonApparentAltitudeDeg">
/// attr[6] — apparent (refracted) altitude of the Moon above the observer
/// horizon. Zero in the geocentric path.
/// </param>
public readonly record struct LunarEclipseAttributes(
    double UmbralMagnitude,
    double PenumbralMagnitude,
    double MoonBodyAngularDistanceDeg,
    double UmbralMagnitudeAlias,
    double SarosSeriesNumber,
    double SarosSeriesMemberNumber,
    double MoonAzimuthDeg = 0.0,
    double MoonTrueAltitudeDeg = 0.0,
    double MoonApparentAltitudeDeg = 0.0);
