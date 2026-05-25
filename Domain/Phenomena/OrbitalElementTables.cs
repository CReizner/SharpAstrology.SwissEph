// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   Mean orbital-element tables and planet-mass reciprocals — verbatim port of
//   the static tables in swecl.c#L5001-L5063.
//   IplToElem        — ipl_to_elem      (swecl.c#L5063)
//   AscendingNode    — swecl.c#L5001
//   Perihelion       — swecl.c#L5011
//   Inclination      — swecl.c#L5021
//   Eccentricity     — swecl.c#L5031
//   SemiMajorAxis    — swecl.c#L5041
//   PlanetMassRatios — swecl.c#L5052

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Static tables of mean orbital elements (J2000-based polynomial coefficients)
/// for Mercury through Neptune, used by the mean-points branch of the nodes /
/// apsides solver. Indexed via <see cref="IplToElem"/> from a body identifier.
/// </summary>
internal static class OrbitalElementTables
{
    /// <summary>Number of bodies covered by the per-planet tables (Mercury..Neptune).</summary>
    public const int Count = 8;

    /// <summary>
    /// Maps a body identifier (SE_SUN..SE_EARTH = 14) to the row index inside
    /// the per-planet element tables. SE_SUN and SE_EARTH both route to the
    /// Earth row. Length 15.
    /// </summary>
    public static readonly int[] IplToElem =
    {
        // SE_SUN=0, MOON=1, MERCURY=2, VENUS=3, MARS=4, JUPITER=5,
        // SATURN=6, URANUS=7, NEPTUNE=8, PLUTO=9, MEAN_NODE=10,
        // TRUE_NODE=11, MEAN_APOG=12, OSCU_APOG=13, EARTH=14
        2, 0, 0, 1, 3, 4, 5, 6, 7, 0, 0, 0, 0, 0, 2,
    };

    /// <summary>Longitude of ascending node, polynomial in T (Julian centuries since J2000).</summary>
    public static readonly double[][] AscendingNode =
    {
        new[] {  48.330893,  1.1861890,  0.00017587,  0.000000211 }, // Mercury
        new[] {  76.679920,  0.9011190,  0.00040665, -0.000000080 }, // Venus
        new[] {   0.0,       0.0,        0.0,         0.0         }, // Earth
        new[] {  49.558093,  0.7720923,  0.00001605,  0.000002325 }, // Mars
        new[] { 100.464441,  1.0209550,  0.00040117,  0.000000569 }, // Jupiter
        new[] { 113.665524,  0.8770970, -0.00012067, -0.000002380 }, // Saturn
        new[] {  74.005947,  0.5211258,  0.00133982,  0.000018516 }, // Uranus
        new[] { 131.784057,  1.1022057,  0.00026006, -0.000000636 }, // Neptune
    };

    /// <summary>Longitude of perihelion (degrees, T in Julian centuries).</summary>
    public static readonly double[][] Perihelion =
    {
        new[] {  77.456119,  1.5564775,  0.00029589,  0.000000056 },
        new[] { 131.563707,  1.4022188, -0.00107337, -0.000005315 },
        new[] { 102.937348,  1.7195269,  0.00045962,  0.000000499 },
        new[] { 336.060234,  1.8410331,  0.00013515,  0.000000318 },
        new[] {  14.331309,  1.6126668,  0.00103127, -0.000004569 },
        new[] {  93.056787,  1.9637694,  0.00083757,  0.000004899 },
        new[] { 173.005159,  1.4863784,  0.00021450,  0.000000433 },
        new[] {  48.123691,  1.4262677,  0.00037918, -0.000000003 },
    };

    /// <summary>Orbital inclination (degrees, T in Julian centuries).</summary>
    public static readonly double[][] Inclination =
    {
        new[] {  7.004986,  0.0018215, -0.00001809,  0.000000053 },
        new[] {  3.394662,  0.0010037, -0.00000088, -0.000000007 },
        new[] {  0.0,       0.0,        0.0,         0.0         },
        new[] {  1.849726, -0.0006010,  0.00001276, -0.000000006 },
        new[] {  1.303270, -0.0054966,  0.00000465, -0.000000004 },
        new[] {  2.488878, -0.0037363, -0.00001516,  0.000000089 },
        new[] {  0.773196,  0.0007744,  0.00003749, -0.000000092 },
        new[] {  1.769952, -0.0093082, -0.00000708,  0.000000028 },
    };

    /// <summary>Eccentricity (dimensionless, T in Julian centuries).</summary>
    public static readonly double[][] Eccentricity =
    {
        new[] {  0.20563175,  0.000020406, -0.0000000284, -0.00000000017 },
        new[] {  0.00677188, -0.000047766,  0.0000000975,  0.00000000044 },
        new[] {  0.01670862, -0.000042037, -0.0000001236,  0.00000000004 },
        new[] {  0.09340062,  0.000090483, -0.0000000806, -0.00000000035 },
        new[] {  0.04849485,  0.000163244, -0.0000004719, -0.00000000197 },
        new[] {  0.05550862, -0.000346818, -0.0000006456,  0.00000000338 },
        new[] {  0.04629590, -0.000027337,  0.0000000790,  0.00000000025 },
        new[] {  0.00898809,  0.000006408, -0.0000000008, -0.00000000005 },
    };

    /// <summary>Semi-major axis (AU, T in Julian centuries).</summary>
    public static readonly double[][] SemiMajorAxis =
    {
        new[] {  0.387098310, 0.0,           0.0,            0.0 },
        new[] {  0.723329820, 0.0,           0.0,            0.0 },
        new[] {  1.000001018, 0.0,           0.0,            0.0 },
        new[] {  1.523679342, 0.0,           0.0,            0.0 },
        new[] {  5.202603191, 0.0000001913,  0.0,            0.0 },
        new[] {  9.554909596, 0.0000021389,  0.0,            0.0 },
        new[] { 19.218446062, -0.0000000372, 0.00000000098,  0.0 },
        new[] { 30.110386869, -0.0000001663, 0.00000000069,  0.0 },
    };

    /// <summary>Sun-to-planet mass ratios (Mercury .. Pluto).</summary>
    public static readonly double[] PlanetMassRatios =
    {
        6_023_600.0,    // Mercury
          408_523.719,  // Venus
          328_900.5,    // Earth and Moon
        3_098_703.59,   // Mars
            1_047.348644, // Jupiter
            3_497.9018,   // Saturn
           22_902.98,     // Uranus
           19_412.26,     // Neptune
       136_566_000.0,    // Pluto
    };
}
