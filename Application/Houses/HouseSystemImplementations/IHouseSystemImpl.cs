// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

/// <summary>
/// Strategy contract for one house system. <see cref="HouseService"/> sets
/// up the <see cref="HouseComputeContext"/> with sin/cos/tan and the
/// initial Asc/MC, then dispatches to one implementation per system.
/// </summary>
internal interface IHouseSystemImpl
{
    HouseSystem Identifier { get; }

    /// <summary>Number of cusp slots filled (12 for most systems, 36 for Gauquelin).</summary>
    int CuspCount { get; }

    /// <summary>
    /// True if the implementation already populates cusps[4..9] (or [1..36]
    /// for Gauquelin); false if it relies on the standard
    /// <c>cusp[i+3] = cusp[i+9] + 180</c> mirror at swehouse.c#L1985-L1991.
    /// Currently true for Gauquelin (G), APC (Y), and Sunshine (I/i).
    /// </summary>
    bool SkipsDefaultMirror { get; }

    /// <summary>
    /// Fills the cusps. Returns <c>false</c> when the system aborts due to
    /// a polar-circle constraint and asks <see cref="HouseService"/> to fall
    /// back to Porphyry (mirrors the <c>goto porphyry</c> branches in
    /// <c>CalcH</c>).
    /// </summary>
    bool Compute(ref HouseComputeContext ctx);
}
