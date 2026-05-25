// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Stars;

/// <summary>
/// Catalog reference epoch for a fixed-star record. Mirrors the third
/// field of a <c>sefstars.txt</c> record (<c>1950</c>, <c>2000</c>, or
/// <c>ICRS</c>). Drives the FK4→FK5 / FK5→ICRS / bias steps in the
/// proper-motion propagation pipeline at <c>sweph.c#L6459-L6496</c>.
/// </summary>
public enum FixedStarEpoch
{
    /// <summary>ICRS — modern catalogue. Stored as <c>epoch = 0</c> in the
    /// C library after <c>atof("ICRS")</c>; no FK5 conversion or bias is
    /// applied to the catalogue position.</summary>
    Icrs = 0,

    /// <summary>B1950 — old FK4 catalogue. Triggers FK4→FK5 conversion plus
    /// precession from B1950 to J2000 in <c>fixstar_calc_from_struct</c>.</summary>
    B1950 = 1950,

    /// <summary>J2000 — FK5 catalogue. Triggers FK5→ICRS conversion plus
    /// frame-bias rotation back to J2000 in
    /// <c>fixstar_calc_from_struct</c>.</summary>
    J2000 = 2000,
}
