// Ported from swisseph-master/swecl.c#L3237 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Geometry of a lunar eclipse, as produced by the static
/// <c>lun_eclipse_how</c> helper (swecl.c#L3237) and emitted via the
/// <c>dcore[10]</c> array.
/// </summary>
/// <param name="ShadowAxisDistanceFromSelenocenterAu">
/// dcore[0] — perpendicular distance of the shadow axis from the Moon's
/// centre projected onto the fundamental plane, AU.
/// </param>
/// <param name="UmbraDiameterAu">
/// dcore[1] — diameter of the Earth's umbra at the Moon's distance, AU
/// (already corrected for atmosphere and NASA-fit factor 0.99405).
/// </param>
/// <param name="PenumbraDiameterAu">
/// dcore[2] — diameter of the Earth's penumbra at the Moon's distance, AU
/// (already corrected for atmosphere and NASA-fit factor 0.98813).
/// </param>
/// <param name="UmbraConeCosine">
/// dcore[3] — cosine of the half-angle of the umbral cone (cosf1).
/// </param>
/// <param name="PenumbraConeCosine">
/// dcore[4] — cosine of the half-angle of the penumbral cone (cosf2).
/// </param>
public readonly record struct LunarEclipseGeometry(
    double ShadowAxisDistanceFromSelenocenterAu,
    double UmbraDiameterAu,
    double PenumbraDiameterAu,
    double UmbraConeCosine,
    double PenumbraConeCosine);
