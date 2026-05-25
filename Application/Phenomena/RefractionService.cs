// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Atmospheric-refraction conversion service. Mirrors the C entry points
/// <c>swe_refrac</c>, <c>swe_refrac_extended</c> and <c>swe_set_lapse_rate</c>
/// (swecl.c#L2887 / #L3035 / #L2986). A thin, stateless adapter over
/// <see cref="RefractionMath"/>; thread-safe. The "global" lapse rate of
/// <c>swe_set_lapse_rate</c> is exposed per call as the <c>lapseRate</c>
/// argument with default <see cref="RefractionMath.DefaultLapseRate"/>; all
/// other pressure / temperature inputs are passed per call.
/// </summary>
public sealed class RefractionService
{
    /// <summary>
    /// Converts true ↔ apparent altitude using the Meeus formula. Mirrors
    /// <c>swe_refrac</c>. Pressure in mbar (hPa), temperature in °C.
    /// </summary>
    public double Refrac(double inAltDeg, double atPressMbar, double atTempC, RefractionMath.Direction direction)
        => RefractionMath.Refrac(inAltDeg, atPressMbar, atTempC, direction);

    /// <summary>
    /// Sinclair-based extended refraction: handles negative apparent altitudes
    /// and observer altitude above sea level. Mirrors
    /// <c>swe_refrac_extended</c>.
    /// </summary>
    public double RefracExtended(
        double inAltDeg,
        double geoAltMeters,
        double atPressMbar,
        double atTempC,
        RefractionMath.Direction direction,
        out RefractionMath.RefractionExtendedResult result,
        double lapseRate = RefractionMath.DefaultLapseRate)
        => RefractionMath.RefracExtended(inAltDeg, geoAltMeters, atPressMbar, atTempC, lapseRate, direction, out result);
}
