// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a local lunar-eclipse search (<c>swe_lun_eclipse_when_loc</c>,
/// swecl.c#L3633). Mirrors the C output array <c>tret[0..9]</c> with typed
/// fields plus the per-observer attributes at maximum and the visibility
/// flag set returned in C's <c>retflag</c>.
///
/// Phases that fall below the horizon are nulled out: e.g. when only the
/// partial-end and penumbra-end are visible above the horizon, the partial-
/// begin / totality / penumbra-begin entries are null and tret[8]
/// (<see cref="MoonriseDuringEclipseTime"/>) carries the moment the Moon
/// rose during the eclipse.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/> — eclipse type (Total / Partial /
/// Penumbral) plus Visible / *Visible bits per phase.
/// </param>
/// <param name="MaximumTime">
/// tret[0] — UT of maximum eclipse at the observer (or, when the Moon
/// rises / sets during the eclipse and the geocentric maximum is below
/// the horizon, the horizon-crossing time instead).
/// </param>
/// <param name="PartialBeginTime">
/// tret[2] — UT of partial-phase begin (umbra first touches Moon). Null
/// for purely penumbral eclipses or when this contact is below the horizon.
/// </param>
/// <param name="PartialEndTime">
/// tret[3] — UT of partial-phase end. Null for purely penumbral eclipses
/// or when this contact is below the horizon.
/// </param>
/// <param name="TotalityBeginTime">
/// tret[4] — UT of totality begin. Null for non-total eclipses or when
/// this contact is below the horizon.
/// </param>
/// <param name="TotalityEndTime">
/// tret[5] — UT of totality end. Null for non-total eclipses or when
/// this contact is below the horizon.
/// </param>
/// <param name="PenumbraBeginTime">
/// tret[6] — UT of penumbra begin. Null when this contact is below the
/// horizon.
/// </param>
/// <param name="PenumbraEndTime">
/// tret[7] — UT of penumbra end. Null when this contact is below the
/// horizon.
/// </param>
/// <param name="MoonriseDuringEclipseTime">
/// tret[8] — UT at which the Moon rises while the eclipse is in progress;
/// null when the Moon is already up at the first eclipse contact.
/// </param>
/// <param name="MoonsetDuringEclipseTime">
/// tret[9] — UT at which the Moon sets while the eclipse is in progress;
/// null when the Moon stays above the horizon through the last contact.
/// </param>
/// <param name="Attributes">Per-observer attributes (umbral / penumbral
/// magnitudes, az/alt, …) at <see cref="MaximumTime"/>.</param>
public readonly record struct LunarEclipseLocalSearchReport(
    EclipseTypeFlags EclipseType,
    JulianDay MaximumTime,
    JulianDay? PartialBeginTime,
    JulianDay? PartialEndTime,
    JulianDay? TotalityBeginTime,
    JulianDay? TotalityEndTime,
    JulianDay? PenumbraBeginTime,
    JulianDay? PenumbraEndTime,
    JulianDay? MoonriseDuringEclipseTime,
    JulianDay? MoonsetDuringEclipseTime,
    LunarEclipseAttributes Attributes);
