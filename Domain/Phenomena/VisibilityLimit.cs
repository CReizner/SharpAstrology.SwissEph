// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Result of <c>swe_vis_limit_mag</c>: visual-limiting-magnitude evaluation
/// for a celestial body at a given moment, plus the object/sun/moon horizon
/// state used in the calculation. Mirrors the eight-double <c>dret</c> array
/// at swehel.c#L1527-L1535 plus the scotopic-flag bitmask returned via the
/// function value.
/// </summary>
/// <param name="LimitingMagnitude">
/// Faintest stellar magnitude an observer of given physiology can detect at
/// the body's line of sight under the prevailing sky-brightness conditions.
/// Returns -100 when the body is below the local horizon.
/// </param>
/// <param name="TopocentricAltitudeDeg">Topocentric altitude of the body, degrees.</param>
/// <param name="AzimuthDeg">Azimuth (north = 0, east = 90), degrees.</param>
/// <param name="SunAltitudeDeg">
/// Topocentric altitude of the Sun (or -90 if dark-sky mode is forced via
/// <see cref="HeliacalFlags.VisLimDark"/>).
/// </param>
/// <param name="SunAzimuthDeg">Azimuth of the Sun (0 if dark-sky mode is forced).</param>
/// <param name="MoonAltitudeDeg">
/// Topocentric altitude of the Moon (or -90 when the body itself is the
/// Moon, or when <see cref="HeliacalFlags.VisLimDark"/> /
/// <see cref="HeliacalFlags.VisLimNoMoon"/> is set).
/// </param>
/// <param name="MoonAzimuthDeg">Azimuth of the Moon (0 in the suppressed-moon cases).</param>
/// <param name="ObjectMagnitude">Apparent magnitude of the body itself (from <c>swe_pheno_ut</c>).</param>
/// <param name="ScotopicFlag">
/// Bitmask: bit 0 set when scotopic vision was selected, bit 1 set when the
/// sky background is in the boundary band (within
/// <see cref="HeliacalConstants.BNightFactor"/> of
/// <see cref="HeliacalConstants.BNightReferenceNL"/>). Mirrors the return
/// value of <c>swe_vis_limit_mag</c>.
/// </param>
/// <param name="IsObjectAboveHorizon">
/// <c>false</c> when the body sits below the local horizon and the
/// remaining fields carry the C library's sentinel values (-100/-2).
/// </param>
public readonly record struct VisibilityLimit(
    double LimitingMagnitude,
    double TopocentricAltitudeDeg,
    double AzimuthDeg,
    double SunAltitudeDeg,
    double SunAzimuthDeg,
    double MoonAltitudeDeg,
    double MoonAzimuthDeg,
    double ObjectMagnitude,
    int ScotopicFlag,
    bool IsObjectAboveHorizon);
