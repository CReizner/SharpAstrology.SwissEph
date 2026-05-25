// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Per-observer attributes of a solar eclipse / lunar occultation. Mirrors
/// the <c>attr[20]</c> output array of <c>swe_sol_eclipse_*</c> /
/// <c>swe_lun_occult_*</c>. Indices reproduced as named members.
/// </summary>
/// <param name="DiameterFractionCovered">
/// attr[0] — fraction of the body's diameter covered by the Moon (eclipse magnitude).
/// For total/annular eclipses this resolves to NASA-style magnitude
/// (= ratio of diameters).
/// </param>
/// <param name="DiameterRatioMoonOverBody">
/// attr[1] — ratio of lunar diameter to the eclipsed body's apparent diameter.
/// </param>
/// <param name="DiscFractionObscured">
/// attr[2] — fraction of the body's disc area obscured by the Moon (obscuration).
/// </param>
/// <param name="CoreShadowDiameterKm">
/// attr[3] — diameter of the core shadow at the place of maximum eclipse, km.
/// </param>
/// <param name="SunAzimuthDeg">
/// attr[4] — azimuth of the eclipsed body, degrees from south, clockwise.
/// </param>
/// <param name="SunTrueAltitudeDeg">
/// attr[5] — true (geometric) altitude of the body above the horizon.
/// </param>
/// <param name="SunApparentAltitudeDeg">
/// attr[6] — apparent (refracted) altitude of the body above the horizon.
/// </param>
/// <param name="MoonBodyAngularDistanceDeg">
/// attr[7] — angular distance between Moon and the eclipsed body.
/// </param>
/// <param name="MagnitudeNasa">
/// attr[8] — magnitude in the convention of NASA (= attr[0] for partial,
/// attr[1] for total/annular eclipses).
/// </param>
/// <param name="SarosSeriesNumber">attr[9] — Saros series number, or -99 999 999 if unknown.</param>
/// <param name="SarosSeriesMemberNumber">attr[10] — Saros series member, or -99 999 999 if unknown.</param>
public readonly record struct SolarEclipseAttributes(
    double DiameterFractionCovered,
    double DiameterRatioMoonOverBody,
    double DiscFractionObscured,
    double CoreShadowDiameterKm,
    double SunAzimuthDeg,
    double SunTrueAltitudeDeg,
    double SunApparentAltitudeDeg,
    double MoonBodyAngularDistanceDeg,
    double MagnitudeNasa,
    double SarosSeriesNumber,
    double SarosSeriesMemberNumber);
