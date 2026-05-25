// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   AtmosphericConditions — datm[4] array (swehel.c#L3348-L3360)
//   Auto sentinel         — triggers default_heliacal_parameters auto-fill
//   RelativeHumidityPercent — surface-clamping at swehel.c#L1340-L1341 is dead code
//                             because SIMULATE_VICTORVB is defined; value flows through unchanged
//   MeteorologicalRangeKm  — VR < 0 is treated identically to VR == 0 by the C guard

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Atmospheric inputs for the heliacal-phenomena routines. Surface-level values;
/// the model derives observer-altitude pressure / temperature internally via
/// <see cref="HeliacalConstants.LapseStandardAtmosphere"/>.
/// </summary>
/// <param name="PressureMbar">Surface atmospheric pressure in millibars (=hPa). Default 1013.25 hPa.
/// If zero, a value is estimated from the observer altitude.</param>
/// <param name="TemperatureCelsius">Surface temperature, °C. Default 15 °C; estimated from altitude if pressure is zero.</param>
/// <param name="RelativeHumidityPercent">Relative humidity, percent (0–100). Default 40.</param>
/// <param name="MeteorologicalRangeKm">
/// Meteorological visibility range:
/// <list type="bullet">
/// <item><description>≥ 1: meteorological range in km (default 40 km).</description></item>
/// <item><description>0 &lt; value &lt; 1: total atmospheric coefficient <c>ktot</c> (typical default 0.25).</description></item>
/// <item><description>= 0: <c>ktot</c> is computed from the other atmospheric parameters (Schaefer formula).</description></item>
/// <item><description>= -1: same as 0 in this port.</description></item>
/// </list>
/// </param>
public readonly record struct AtmosphericConditions(
    double PressureMbar,
    double TemperatureCelsius,
    double RelativeHumidityPercent,
    double MeteorologicalRangeKm)
{
    /// <summary>All-zero sentinel — triggers the default auto-fill path.</summary>
    public static AtmosphericConditions Auto => default;
}
