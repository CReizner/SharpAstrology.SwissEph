// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Fundamental-plane geometry of a solar eclipse / lunar occultation, as
/// produced by the static <c>eclipse_where</c> helper (swecl.c#L640) and
/// emitted via the <c>dcore[10]</c> array.
/// </summary>
/// <param name="CoreShadowDiameterAtMaxKm">
/// dcore[0] — diameter of the core shadow at the place of maximum
/// eclipse, kilometres. Negative for total eclipses, positive for annular.
/// </param>
/// <param name="PenumbraDiameterAtMaxKm">
/// dcore[1] — diameter of the penumbra at the place of maximum eclipse, km.
/// </param>
/// <param name="ShadowAxisDistanceFromGeocenterKm">
/// dcore[2] — perpendicular distance of the shadow axis from the geocentre
/// projected onto the fundamental plane, km.
/// </param>
/// <param name="CoreShadowDiameterFundamentalPlaneKm">
/// dcore[3] — core-shadow diameter on the fundamental plane, km.
/// </param>
/// <param name="PenumbraDiameterFundamentalPlaneKm">
/// dcore[4] — penumbra diameter on the fundamental plane, km.
/// </param>
/// <param name="CoreShadowConeCosine">
/// dcore[5] — cosine of the half-angle of the umbral cone (cosf1).
/// </param>
/// <param name="PenumbraConeCosine">
/// dcore[6] — cosine of the half-angle of the penumbral cone (cosf2).
/// </param>
public readonly record struct SolarEclipseGeometry(
    double CoreShadowDiameterAtMaxKm,
    double PenumbraDiameterAtMaxKm,
    double ShadowAxisDistanceFromGeocenterKm,
    double CoreShadowDiameterFundamentalPlaneKm,
    double PenumbraDiameterFundamentalPlaneKm,
    double CoreShadowConeCosine,
    double PenumbraConeCosine);
