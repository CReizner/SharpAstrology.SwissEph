// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris): mirrors the #define block at
// swehel.c#L76-L153 verbatim. Per-constant origin:
//   CelsiusToKelvinOffset           — swehel.c#L131
//   LowestAppAltDeg                 — swehel.c#L142
//   LapseStandardAtmosphere         — swehel.c#L138
//   LapseDryAdiabatic               — swehel.c#L139
//   ScaleHeightRayleigh             — swehel.c#L124 (Su 2003)
//   ScaleHeightWater                — swehel.c#L123 (Ricchiazzi 1997)
//   ScaleHeightAerosol              — swehel.c#L125 (Su 2003)
//   ScaleHeightOzone                — swehel.c#L126 (Schaefer 2000)
//   EarthEquatorialRadius           — swehel.c#L116 (WGS84)
//   EarthPolarRadius                — swehel.c#L117 (WGS84)
//   AstroMagnitudesToTau            — swehel.c#L127
//   AirmassReferencePressure        — swehel.c#L971 / #L982
//   ScotopicThresholdCva            — swehel.c#L185 / #L262
//   ScotopicThresholdOpticFactor    — swehel.c#L262
//   PupilReferenceAgeYears          — swehel.c#L238 (Garstang 2000)
//   MoonDistanceKm                  — swehel.c#L122
//   Erg2NL                          — swehel.c#L120-L121
//   BNightReferenceNL               — swehel.c#L78
//   BNightFactor                    — swehel.c#L79
//   LunarRadiusDeg                  — swehel.c#L1191
//   ScotopicThresholdVisLimMagn     — swehel.c#L1403
//   AvgRadiusMoonDeg                — swehel.c#L112 (Yallop, 15.541/60)
//   BisectionEpsilonDeg             — swehel.c#L145
//   TopoArcVisionisNoCrossingDeg    — swehel.c#L1593
//   GeoAltitudeMinMeters            — sweph.h#L199
//   GeoAltitudeMaxMeters            — sweph.h#L198
//   TjdInvalid                      — TJD_INVALID at swephexp.h#L450
//   TimeStepDefaultMinutes          — swehel.c#L85
//   LocalMinStepMinutes             — swehel.c#L86
//   MaxTryHours                     — swehel.c#L84

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Numerical constants used by the heliacal-phenomena machinery
/// (limiting magnitude, arcus visionis, sky brightness model).
/// </summary>
internal static class HeliacalConstants
{
    /// <summary>Celsius-to-Kelvin offset.</summary>
    public const double CelsiusToKelvinOffset = 273.15;

    /// <summary>Lowest apparent altitude provided by the refraction model, in degrees.</summary>
    public const double LowestAppAltDeg = -3.5;

    /// <summary>Standard-atmosphere lapse rate, K/m.</summary>
    public const double LapseStandardAtmosphere = 0.0065;

    /// <summary>Dry-adiabatic lapse rate, K/m.</summary>
    public const double LapseDryAdiabatic = 0.0098;

    /// <summary>Rayleigh scale height, metres (Su 2003).</summary>
    public const double ScaleHeightRayleigh = 8515.0;

    /// <summary>Water-vapour scale height, metres (Ricchiazzi 1997).</summary>
    public const double ScaleHeightWater = 3000.0;

    /// <summary>Aerosol scale height, metres (Su 2003).</summary>
    public const double ScaleHeightAerosol = 3745.0;

    /// <summary>Ozone scale height, metres (Schaefer 2000).</summary>
    public const double ScaleHeightOzone = 20000.0;

    /// <summary>WGS84 equatorial radius, metres.</summary>
    public const double EarthEquatorialRadius = 6_378_136.6;

    /// <summary>WGS84 polar radius, metres.</summary>
    public const double EarthPolarRadius = 6_356_752.314;

    /// <summary><c>ln(10^0.4) = 0.921034…</c> — magnitude → optical-depth conversion.</summary>
    public const double AstroMagnitudesToTau = 0.921034037197618;

    /// <summary>Reference pressure for airmass / X-extinction normalisation, mbar.</summary>
    public const double AirmassReferencePressure = 1013.0;

    /// <summary>Brightness threshold below which CVA / OpticFactor switch to scotopic vision, in nL.</summary>
    public const double ScotopicThresholdCva = 1394.0;

    /// <summary>Brightness threshold for scotopic switch in OpticFactor, nL.</summary>
    public const double ScotopicThresholdOpticFactor = 1645.0;

    /// <summary>Reference pupil-diameter age (Garstang 2000), used as <c>Pst</c> baseline.</summary>
    public const double PupilReferenceAgeYears = 23.0;

    /// <summary>Mean Earth–Moon distance used by the lunar brightness term, kilometres.</summary>
    public const double MoonDistanceKm = 384_410.4978;

    /// <summary>nLambert ↔ erg conversion: 1 erg ≙ <c>1/1.02e-15</c> nL.</summary>
    public const double Erg2NL = 1.0 / 1.02e-15;

    /// <summary>Reference night-sky brightness used by the scotopic-flag fuzzy band, nL.</summary>
    public const double BNightReferenceNL = 1479.0;

    /// <summary>Multiplicative tolerance for the night-sky scotopic-flag band.</summary>
    public const double BNightFactor = 1.0;

    /// <summary>Apparent moon-disc radius used as the lower bound for the observer-to-moon angular distance, degrees.</summary>
    public const double LunarRadiusDeg = 0.25;

    /// <summary>Threshold below which the visual-limit-magnitude solver selects scotopic constants, nL.</summary>
    public const double ScotopicThresholdVisLimMagn = 1645.0;

    /// <summary>Mean apparent lunar disc radius (Yallop), degrees.</summary>
    public const double AvgRadiusMoonDeg = 15.541 / 60.0;

    /// <summary>Bisection convergence epsilon for the topocentric arcus-visionis solver, degrees.</summary>
    public const double BisectionEpsilonDeg = 0.001;

    /// <summary>Out-of-range marker returned when bisection brackets do not contain the magnitude crossing.</summary>
    public const double TopoArcVisionisNoCrossingDeg = 99.0;

    /// <summary>Minimum observer altitude (m) accepted by the heliacal entry-points.</summary>
    public const double GeoAltitudeMinMeters = -500.0;

    /// <summary>Maximum observer altitude (m) accepted by the heliacal entry-points.</summary>
    public const double GeoAltitudeMaxMeters = 25_000.0;

    /// <summary>"#NA!" sentinel for invalid Julian Days returned by the heliacal-phenomena machinery.</summary>
    public const double TjdInvalid = 99_999_999.0;

    /// <summary>Walkthrough time-step in the heliacal pheno walker, in minutes.</summary>
    public const double TimeStepDefaultMinutes = 1.0;

    /// <summary>Local-minimum verification step in the walkthrough, in minutes.</summary>
    public const double LocalMinStepMinutes = 8.0;

    /// <summary>Maximum walkthrough span in hours.</summary>
    public const double MaxTryHours = 4.0;
}
