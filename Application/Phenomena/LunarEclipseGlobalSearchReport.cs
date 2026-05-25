// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a global lunar-eclipse search (<c>swe_lun_eclipse_when</c>,
/// swecl.c#L3378). Mirrors the C output array <c>tret[0..7]</c> with typed
/// fields; nullable members are null when the corresponding phase does not
/// exist for this eclipse (e.g. <see cref="TotalityBeginTime"/> is null for
/// a partial or penumbral eclipse, and the partial pair is null for a pure
/// penumbral eclipse).
/// </summary>
/// <param name="EclipseType">
/// <see cref="EclipseTypeFlags.Total"/>, <see cref="EclipseTypeFlags.Partial"/>,
/// or <see cref="EclipseTypeFlags.Penumbral"/>.
/// </param>
/// <param name="MaximumTime">tret[0] — UT of geocentric maximum eclipse.</param>
/// <param name="PartialBeginTime">
/// tret[2] — UT at which the umbra first touches the Moon (partial begin).
/// Null for purely penumbral eclipses.
/// </param>
/// <param name="PartialEndTime">
/// tret[3] — UT at which the umbra leaves the Moon (partial end).
/// Null for purely penumbral eclipses.
/// </param>
/// <param name="TotalityBeginTime">
/// tret[4] — UT at which totality begins. Null when not a total eclipse.
/// </param>
/// <param name="TotalityEndTime">
/// tret[5] — UT at which totality ends. Null when not a total eclipse.
/// </param>
/// <param name="PenumbraBeginTime">tret[6] — UT at which the penumbra first touches the Moon.</param>
/// <param name="PenumbraEndTime">tret[7] — UT at which the penumbra leaves the Moon.</param>
public readonly record struct LunarEclipseGlobalSearchReport(
    EclipseTypeFlags EclipseType,
    JulianDay MaximumTime,
    JulianDay? PartialBeginTime,
    JulianDay? PartialEndTime,
    JulianDay? TotalityBeginTime,
    JulianDay? TotalityEndTime,
    JulianDay PenumbraBeginTime,
    JulianDay PenumbraEndTime);
