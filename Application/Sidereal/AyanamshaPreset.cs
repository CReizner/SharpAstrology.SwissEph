// Ported from swisseph-master/sweph.h struct aya_init (line 347).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Frames;

namespace SharpAstrology.SwissEphemerides.Application.Sidereal;

/// <summary>
/// One row of the canonical ayanamsha table. Mirrors the C struct
/// <c>aya_init</c> (sweph.h#L347-L350).
/// </summary>
/// <param name="T0">Reference epoch (Julian Day) at which the ayanamsha
/// equals <paramref name="AyanT0"/>.</param>
/// <param name="AyanT0">Ayanamsha value (degrees) at <paramref name="T0"/>.</param>
/// <param name="T0IsUt">If <c>true</c>, <paramref name="T0"/> is given in UT
/// and ΔT must be added before precession.</param>
/// <param name="PrecOffset">Precession model the original ayanamsha was
/// defined against. <c>null</c> when no correction applies (column = 0 in
/// the C source). C's <c>-1</c> sentinel ("unclear or not investigated") is
/// also represented as <c>null</c>; the difference is documented in
/// <see cref="AyanamshaTable"/> comments where it matters.</param>
internal readonly record struct AyanamshaPreset(
    double T0,
    double AyanT0,
    bool T0IsUt,
    PrecessionModel? PrecOffset);

/// <summary>
/// Per-mode flavour for the star-anchored ayanamsha branch. Each value
/// determines the flag combination passed to <see cref="Stars.FixedStarService.Compute"/>
/// and how the resulting coordinate is interpreted. Mirrors the three
/// distinct code paths inside <c>swi_get_ayanamsa_ex</c>
/// (sweph.c#L3050-L3149).
/// </summary>
internal enum StarAnchoredKind
{
    /// <summary>
    /// True/Citra-class: ecliptic longitude with <c>SEFLG_NONUT</c>;
    /// optionally <c>TRUEPOS</c>/<c>NOABERR</c>/<c>NOGDEFL</c> are
    /// passed through if the caller requested them. The ayanamsha is
    /// <c>star_lon - target</c>.
    /// </summary>
    Ecliptic,

    /// <summary>
    /// Galactic-equator class: ecliptic longitude with
    /// <c>SEFLG_NONUT | SEFLG_TRUEPOS</c> forced (sweph.c#L3018), so the
    /// galactic-pole position is not contaminated by aberration or
    /// gravitational deflection. The ayanamsha is <c>star_lon - target</c>.
    /// </summary>
    EclipticTruePosition,

    /// <summary>
    /// Mid-Mula Wilhelm class: <c>SEFLG_NONUT | SEFLG_EQUATORIAL</c>
    /// (plus optional <c>TRUEPOS</c>/<c>NOABERR</c>/<c>NOGDEFL</c>),
    /// then the right ascension is projected onto the ecliptic via the
    /// <c>armc_to_mc</c> helper using the mean obliquity at <c>jdEt</c>.
    /// </summary>
    EquatorialArmcToMc,
}

/// <summary>
/// Per-mode anchor data for star-based ayanamshas. Sister record to
/// <see cref="AyanamshaPreset"/>; populated only for the twelve
/// <see cref="SiderealMode"/> values listed in
/// <see cref="AyanamshaTable.RequiresFixedStarSource"/>. Mirrors the
/// constants embedded in the per-mode <c>if</c>-branches of
/// <c>swi_get_ayanamsa_ex</c> (sweph.c#L3050-L3149).
/// </summary>
/// <param name="StarName">Catalogue lookup string passed to
/// <see cref="Stars.FixedStarService.Compute"/>. Verbatim from the C
/// source (e.g. <c>"Spica"</c>, <c>",zePsc"</c>, <c>",SgrA*"</c>).</param>
/// <param name="TargetLongitudeDeg">The ecliptic longitude the anchor
/// star is *defined* to occupy (degrees). The ayanamsha is
/// <c>star_lon - TargetLongitudeDeg</c>.</param>
/// <param name="Kind">Which flag/projection variant to use.</param>
internal readonly record struct StarAnchoredPreset(
    string StarName,
    double TargetLongitudeDeg,
    StarAnchoredKind Kind);
