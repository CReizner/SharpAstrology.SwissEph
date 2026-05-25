// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Body identifier mirroring the <c>SE_*</c> constants from
/// <c>swephexp.h</c> for the in-base set. Numeric values match the C
/// constants verbatim so that tests, cached files, and downstream
/// dispatching remain interchangeable. Asteroid IDs (≥
/// <c>SE_AST_OFFSET = 10000</c>) are not part of this enum and are
/// represented as raw <see cref="int"/> bodyIds in the lower-level APIs.
/// </summary>
public enum CelestialBody
{
    /// <summary>SE_SUN = 0.</summary>
    Sun = 0,
    /// <summary>SE_MOON = 1.</summary>
    Moon = 1,
    /// <summary>SE_MERCURY = 2.</summary>
    Mercury = 2,
    /// <summary>SE_VENUS = 3.</summary>
    Venus = 3,
    /// <summary>SE_MARS = 4.</summary>
    Mars = 4,
    /// <summary>SE_JUPITER = 5.</summary>
    Jupiter = 5,
    /// <summary>SE_SATURN = 6.</summary>
    Saturn = 6,
    /// <summary>SE_URANUS = 7.</summary>
    Uranus = 7,
    /// <summary>SE_NEPTUNE = 8.</summary>
    Neptune = 8,
    /// <summary>SE_PLUTO = 9.</summary>
    Pluto = 9,
    /// <summary>SE_MEAN_NODE = 10.</summary>
    MeanNode = 10,
    /// <summary>SE_TRUE_NODE = 11.</summary>
    TrueNode = 11,
    /// <summary>SE_MEAN_APOG = 12.</summary>
    MeanApogee = 12,
    /// <summary>SE_OSCU_APOG = 13.</summary>
    OsculatingApogee = 13,
    /// <summary>SE_EARTH = 14.</summary>
    Earth = 14,
    /// <summary>SE_CHIRON = 15.</summary>
    Chiron = 15,
    /// <summary>SE_PHOLUS = 16.</summary>
    Pholus = 16,
    /// <summary>SE_CERES = 17.</summary>
    Ceres = 17,
    /// <summary>SE_PALLAS = 18.</summary>
    Pallas = 18,
    /// <summary>SE_JUNO = 19.</summary>
    Juno = 19,
    /// <summary>SE_VESTA = 20.</summary>
    Vesta = 20,
    /// <summary>SE_INTP_APOG = 21.</summary>
    InterpolatedApogee = 21,
    /// <summary>SE_INTP_PERG = 22.</summary>
    InterpolatedPerigee = 22,
}
