// Ported from swisseph-master/sweph.h `pla_diam[]` (line 313-333).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Body diameters in metres, indexed by the C-library ipl integer (Sun = 0,
/// Moon = 1, Mercury = 2, …, Vesta = 20). Mirrors <c>pla_diam[NDIAM]</c>
/// (sweph.h#L315-L333) verbatim. Used by <c>rise_set_fast</c> /
/// <c>swe_rise_trans</c> for the apparent-disc-radius term.
/// </summary>
internal static class PlanetDiameters
{
    /// <summary>Diameter table — index by C-library ipl. <c>0</c> means "no disc / not applicable".</summary>
    public static readonly double[] Meters =
    {
        1_392_000_000.0,    // 0  Sun
            3_475_000.0,    // 1  Moon
        2_439_400.0 * 2,    // 2  Mercury
        6_051_800.0 * 2,    // 3  Venus
        3_389_500.0 * 2,    // 4  Mars
       69_911_000.0 * 2,    // 5  Jupiter
       58_232_000.0 * 2,    // 6  Saturn
       25_362_000.0 * 2,    // 7  Uranus
       24_622_000.0 * 2,    // 8  Neptune
        1_188_300.0 * 2,    // 9  Pluto
        0, 0, 0, 0,         // 10..13 nodes / apogees
        6_371_008.4 * 2,    // 14 Earth
            271_370.0,      // 15 Chiron
            290_000.0,      // 16 Pholus
            939_400.0,      // 17 Ceres
            545_000.0,      // 18 Pallas
            246_596.0,      // 19 Juno
            525_400.0,      // 20 Vesta
    };
}
