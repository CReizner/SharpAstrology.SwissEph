// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ObserverParameters — dobs[6] array (swehel.c#L3361-L3373)
//   Auto sentinel      — triggers the C default-parameter logic; HeliacalFlags.OpticalParameters
//                        additionally honours fields 2–5

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Observer-side inputs for the heliacal-phenomena routines. All fields zero
/// triggers the default-parameter auto-fill (age=36, SN=1, binocular, naked eye);
/// <see cref="HeliacalFlags"/>.<c>OpticalParameters</c> additionally honours the
/// optical fields.
/// </summary>
/// <param name="AgeYears">Observer age in years (default 36 — experienced ancient observer; optimum 23).</param>
/// <param name="SnellenRatio">Visual acuity (Snellen ratio, default 1).</param>
/// <param name="IsBinocular">True for binocular vision, false for monocular (Schaefer's <c>Fb</c> = 1.41 when monocular).</param>
/// <param name="OpticMagnification">Telescope magnification (1 = naked eye; only honoured with <see cref="HeliacalFlags.OpticalParameters"/>).</param>
/// <param name="ApertureMm">Optical aperture (telescope diameter) in mm.</param>
/// <param name="Transmission">Optical transmission factor (0–1).</param>
public readonly record struct ObserverParameters(
    double AgeYears,
    double SnellenRatio,
    bool IsBinocular,
    double OpticMagnification,
    double ApertureMm,
    double Transmission)
{
    /// <summary>All-zero sentinel — triggers the default auto-fill path (age=36, SN=1, binocular, naked eye).</summary>
    public static ObserverParameters Auto => default;
}
