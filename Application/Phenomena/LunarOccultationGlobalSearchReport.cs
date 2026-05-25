// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a global lunar-occultation search
/// (<c>swe_lun_occult_when_glob</c>, swecl.c#L1572). Mirrors the
/// <c>tret[0..7]</c> array of the C call.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/> describing the occultation
/// classification (e.g. <c>Total | Central</c>, <c>Total | NonCentral</c>,
/// <c>Partial | NonCentral</c>, or <c>AnnularTotal</c> for hybrid solar
/// cases).
/// </param>
/// <param name="MaximumTime">tret[0] — UT of the geocentric maximum.</param>
/// <param name="LocalApparentNoonTime">
/// tret[1] — UT of local apparent noon (the moment when Moon and
/// occulted body have the same right ascension), <c>null</c> when no
/// transit happens during the occultation phases.
/// </param>
/// <param name="PartialBeginTime">tret[2] — UT of partial-phase begin (penumbra contact).</param>
/// <param name="PartialEndTime">tret[3] — UT of partial-phase end.</param>
/// <param name="TotalityBeginTime">
/// tret[4] — UT of totality begin (umbra contact). Null for purely
/// partial occultations.
/// </param>
/// <param name="TotalityEndTime">tret[5] — UT of totality end. Null for partial.</param>
/// <param name="CenterlineBeginTime">
/// tret[6] — UT when the shadow centreline first touches Earth. Null
/// for non-central occultations.
/// </param>
/// <param name="CenterlineEndTime">tret[7] — UT when the centreline leaves Earth.</param>
public readonly record struct LunarOccultationGlobalSearchReport(
    EclipseTypeFlags EclipseType,
    JulianDay MaximumTime,
    JulianDay? LocalApparentNoonTime,
    JulianDay? PartialBeginTime,
    JulianDay? PartialEndTime,
    JulianDay? TotalityBeginTime,
    JulianDay? TotalityEndTime,
    JulianDay? CenterlineBeginTime,
    JulianDay? CenterlineEndTime);
