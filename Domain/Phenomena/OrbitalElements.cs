// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Seventeen Kepler-elements from <c>swe_get_orbital_elements</c>
/// (swecl.c#L5772-L5959). Heliocentric for Mercury..Pluto and Earth (the
/// EMB orbit is used internally), geocentric for the Moon. Reference frame
/// is J2000 ecliptic — angles in degrees, distances in AU, speeds per day,
/// times in TT Julian Days.
/// </summary>
/// <param name="SemiMajorAxisAu">a — semi-major axis (AU).</param>
/// <param name="Eccentricity">e — eccentricity (dimensionless).</param>
/// <param name="InclinationDeg">i — inclination to the J2000 ecliptic.</param>
/// <param name="LongitudeOfAscendingNodeDeg">Ω — longitude of ascending node.</param>
/// <param name="ArgumentOfPeriapsisDeg">ω — argument of periapsis (perihelion from node).</param>
/// <param name="LongitudeOfPeriapsisDeg">ϖ = Ω + ω — longitude of periapsis.</param>
/// <param name="MeanAnomalyDeg">M₀ — mean anomaly at the epoch.</param>
/// <param name="TrueAnomalyDeg">ν₀ — true anomaly at the epoch.</param>
/// <param name="EccentricAnomalyDeg">E₀ — eccentric anomaly at the epoch.</param>
/// <param name="MeanLongitudeDeg">L = ϖ + M₀ — mean longitude at the epoch.</param>
/// <param name="SiderealOrbitalPeriodSiderealYears">Sidereal orbital period in sidereal years (J2000-anchored).</param>
/// <param name="MeanDailyMotionDegPerDay">Mean daily motion in degrees/day.</param>
/// <param name="TropicalPeriodTropicalYears">Tropical orbital period in tropical years (J2000-anchored).</param>
/// <param name="SynodicPeriodDays">Synodic period in days. Negative for inner planets and the Moon. Zero for Earth.</param>
/// <param name="TimeOfPerihelionPassageJd">Julian Day (TT) of the most recent perihelion passage.</param>
/// <param name="PerihelionDistanceAu">a · (1 − e) — perihelion distance.</param>
/// <param name="AphelionDistanceAu">a · (1 + e) — aphelion distance.</param>
public readonly record struct OrbitalElements(
    double SemiMajorAxisAu,
    double Eccentricity,
    double InclinationDeg,
    double LongitudeOfAscendingNodeDeg,
    double ArgumentOfPeriapsisDeg,
    double LongitudeOfPeriapsisDeg,
    double MeanAnomalyDeg,
    double TrueAnomalyDeg,
    double EccentricAnomalyDeg,
    double MeanLongitudeDeg,
    double SiderealOrbitalPeriodSiderealYears,
    double MeanDailyMotionDegPerDay,
    double TropicalPeriodTropicalYears,
    double SynodicPeriodDays,
    double TimeOfPerihelionPassageJd,
    double PerihelionDistanceAu,
    double AphelionDistanceAu);
