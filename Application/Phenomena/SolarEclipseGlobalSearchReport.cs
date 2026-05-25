// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a global solar-eclipse search (<c>swe_sol_eclipse_when_glob</c>,
/// swecl.c#L1185). Mirrors the C output array <c>tret[0..7]</c> with typed
/// fields; nullable members are null when the corresponding phase does not
/// exist for this eclipse (e.g. <see cref="TotalityBeginTime"/> is null for a
/// purely partial eclipse).
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/> describing the classification at
/// maximum (e.g. <c>Total | Central</c> or <c>Partial | NonCentral</c>).
/// </param>
/// <param name="MaximumTime">tret[0] — UT of geocentric maximum eclipse.</param>
/// <param name="LocalApparentNoonTime">
/// tret[1] — UT at which the eclipse occurs at local apparent noon (Sun-Moon
/// conjunction in right ascension); null when no transit happens between
/// partial begin and end. Mirrors C's <c>tret[1] == 0</c> sentinel.
/// </param>
/// <param name="PartialBeginTime">tret[2] — UT at which the eclipse touches the Earth (P1).</param>
/// <param name="PartialEndTime">tret[3] — UT at which the eclipse leaves the Earth (P4).</param>
/// <param name="TotalityBeginTime">
/// tret[4] — UT at which totality / annularity begins (U1). Null for purely
/// partial eclipses.
/// </param>
/// <param name="TotalityEndTime">
/// tret[5] — UT at which totality / annularity ends (U4). Null for purely
/// partial eclipses.
/// </param>
/// <param name="CenterLineBeginTime">
/// tret[6] — UT at which the centerline / shadow axis touches the Earth.
/// Null for non-central eclipses.
/// </param>
/// <param name="CenterLineEndTime">
/// tret[7] — UT at which the centerline / shadow axis leaves the Earth.
/// Null for non-central eclipses.
/// </param>
public readonly record struct SolarEclipseGlobalSearchReport(
    EclipseTypeFlags EclipseType,
    JulianDay MaximumTime,
    JulianDay? LocalApparentNoonTime,
    JulianDay PartialBeginTime,
    JulianDay PartialEndTime,
    JulianDay? TotalityBeginTime,
    JulianDay? TotalityEndTime,
    JulianDay? CenterLineBeginTime,
    JulianDay? CenterLineEndTime);
