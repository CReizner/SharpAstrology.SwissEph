// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Per-observer attributes of a lunar occultation of a body other than the
/// Sun. Mirrors the <c>attr[20]</c> output of <c>swe_lun_occult_where</c> /
/// <c>swe_lun_occult_when_loc</c>. Field names are body-neutral —
/// <c>Body</c> refers to the planet (or star) being occulted by the Moon.
/// The Saros / NASA-magnitude entries used for solar eclipses are not
/// populated here; the C source emits them only when <c>ipl == SE_SUN</c>
/// (swecl.c#L1124).
/// </summary>
/// <param name="DiameterFractionCovered">
/// attr[0] — fraction of the body's diameter covered by the Moon. For
/// occultations of a small planet by the much larger lunar disc this can
/// run to large multiples (the planet is fully behind the Moon long after
/// one body-diameter is covered).
/// </param>
/// <param name="DiameterRatioMoonOverBody">
/// attr[1] — ratio of lunar apparent diameter to the occulted body's
/// apparent diameter.
/// </param>
/// <param name="DiscFractionObscured">
/// attr[2] — fraction of the body's disc area obscured by the Moon. Like
/// <see cref="DiameterFractionCovered"/>, exceeds 1 once the body is fully
/// behind the lunar disc.
/// </param>
/// <param name="CoreShadowDiameterKm">
/// attr[3] — diameter of the core (umbral) shadow at the place of maximum
/// occultation, kilometres. Negative for total occultations, positive for
/// annular.
/// </param>
/// <param name="BodyAzimuthDeg">
/// attr[4] — azimuth of the occulted body, degrees from south, clockwise.
/// </param>
/// <param name="BodyTrueAltitudeDeg">
/// attr[5] — true (geometric) altitude of the body above the horizon.
/// </param>
/// <param name="BodyApparentAltitudeDeg">
/// attr[6] — apparent (refracted) altitude of the body above the horizon.
/// </param>
/// <param name="MoonBodyAngularDistanceDeg">
/// attr[7] — angular distance between Moon and the occulted body.
/// </param>
public readonly record struct LunarOccultationAttributes(
    double DiameterFractionCovered,
    double DiameterRatioMoonOverBody,
    double DiscFractionObscured,
    double CoreShadowDiameterKm,
    double BodyAzimuthDeg,
    double BodyTrueAltitudeDeg,
    double BodyApparentAltitudeDeg,
    double MoonBodyAngularDistanceDeg);
