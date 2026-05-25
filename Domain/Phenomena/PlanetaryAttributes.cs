// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Apparent-phenomena bundle for a planet, mirroring the <c>attr[20]</c> output
/// of <c>swe_pheno</c>. Phase 1 fills the geometric channels (phase angle,
/// phase, elongation, apparent diameter, horizontal parallax) plus the
/// apparent magnitude using the Mallama 2018 / Vreijs lunar formulas.
/// </summary>
public readonly record struct PlanetaryAttributes(
    /// <summary>Sun-planet-Earth angle (degrees). Zero for Sun and Earth.</summary>
    double PhaseAngleDeg,
    /// <summary>Illuminated fraction of disc, range [0, 1]. Zero for Sun/Earth.</summary>
    double PhaseFraction,
    /// <summary>Sun-Earth-planet angle (degrees). Zero for Sun and Earth.</summary>
    double ElongationDeg,
    /// <summary>Apparent diameter (degrees). 180° if observer sits inside the body's radius.</summary>
    double ApparentDiameterDeg,
    /// <summary>Apparent magnitude (lower = brighter).</summary>
    double ApparentMagnitude,
    /// <summary>Geocentric or topocentric horizontal parallax (degrees). Moon only; zero otherwise.</summary>
    double HorizontalParallaxDeg);
