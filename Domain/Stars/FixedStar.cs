// Ported from swisseph-master/sweph.h struct fixed_star (line 773).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Stars;

/// <summary>
/// Immutable catalogue record for one fixed star. Mirrors
/// <c>struct fixed_star</c> (<c>sweph.h#L773</c>) after the unit
/// conversions performed in <c>fixstar_cut_string</c>
/// (<c>sweph.c#L6211-L6306</c>): all angular quantities are in radians,
/// proper motions in radians per Julian century, radial velocity in AU
/// per century, parallax in radians.
/// </summary>
/// <param name="TraditionalName">Traditional star name (e.g. <c>Aldebaran</c>);
///   may be empty for records that only carry a Bayer/Flamsteed designation.</param>
/// <param name="BayerDesignation">Bayer/Flamsteed designation
///   (e.g. <c>alTau</c>); always present.</param>
/// <param name="Epoch">Catalogue equinox; controls the FK4/FK5/ICRS branch
///   in <c>fixstar_calc_from_struct</c>.</param>
/// <param name="RightAscensionRad">Right ascension at catalogue epoch (rad).</param>
/// <param name="DeclinationRad">Declination at catalogue epoch (rad).</param>
/// <param name="RaProperMotionRad">Proper motion in RA (rad / Julian century).
///   The C library divides the catalogue value by <c>cos(δ₀)</c> here so the
///   stored quantity is the raw RA-rate, not a great-circle rate.</param>
/// <param name="DecProperMotionRad">Proper motion in declination (rad / Julian century).</param>
/// <param name="RadialVelocityAuPerCentury">Radial velocity in AU per Julian century.</param>
/// <param name="ParallaxRad">Annual parallax (rad). Zero for stars without
///   measured parallax — the calc pipeline substitutes <c>1e9 AU</c>.</param>
/// <param name="Magnitude">Apparent V magnitude.</param>
public readonly record struct FixedStar(
    string TraditionalName,
    string BayerDesignation,
    FixedStarEpoch Epoch,
    double RightAscensionRad,
    double DeclinationRad,
    double RaProperMotionRad,
    double DecProperMotionRad,
    double RadialVelocityAuPerCentury,
    double ParallaxRad,
    double Magnitude);
