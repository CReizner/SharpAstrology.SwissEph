// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Geometry helpers for the outer-planet acronychal (visibility-limit) heliacal
// branch. Mirrors get_asc_obl at swehel.c#L2452 — the rest of the search lives
// in HeliacalService.Search.cs since it depends on body / vis-limit services.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

internal static class AcronychalGeometry
{
    /// <summary>
    /// Ascensio (or descensio) obliqua of a body with right ascension
    /// <paramref name="raDeg"/> and declination <paramref name="decDeg"/>
    /// observed from latitude <paramref name="latDeg"/>. Mirrors
    /// <c>get_asc_obl</c> at swehel.c#L2452 starting after the equatorial
    /// position is fetched. Returns <see langword="null"/> when the body is
    /// circumpolar (the C function returns -2 with a serr message).
    /// </summary>
    /// <param name="descObl">
    /// <c>true</c> for descensio obliqua (setting side), <c>false</c> for
    /// ascensio obliqua (rising side).
    /// </param>
    public static double? AscensioObliqua(double raDeg, double decDeg, double latDeg, bool descObl)
    {
        var adp = Math.Tan(latDeg * AstronomicalConstants.DegToRad)
                  * Math.Tan(decDeg * AstronomicalConstants.DegToRad);
        if (Math.Abs(adp) > 1.0) return null;
        adp = Math.Asin(adp) * AstronomicalConstants.RadToDeg;
        var aop = descObl ? raDeg + adp : raDeg - adp;
        return AngleMath.NormalizeDegrees(aop);
    }
}
