// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a local solar-eclipse search (<c>swe_sol_eclipse_when_loc</c>,
/// swecl.c#L2019). Mirrors the C output array <c>tret[0..6]</c> with typed
/// fields, the per-observer attributes at maximum, and the visibility flag
/// set returned in C's <c>retflag</c>.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/> describing the classification
/// (e.g. <c>Total | Visible | MaxVisible | PartialBeginVisible</c>).
/// </param>
/// <param name="MaximumTime">tret[0] — UT of maximum eclipse at the observer
/// (or, when the maximum falls below the horizon and the eclipse only
/// becomes visible at sunrise/sunset, that horizon-crossing time instead).</param>
/// <param name="PartialBeginTime">tret[1] — UT of first contact (P1).</param>
/// <param name="TotalityBeginTime">
/// tret[2] — UT of second contact (U1, totality / annularity begin); null
/// for purely partial eclipses.
/// </param>
/// <param name="TotalityEndTime">
/// tret[3] — UT of third contact (U4); null for purely partial eclipses.
/// </param>
/// <param name="PartialEndTime">tret[4] — UT of fourth contact (P4).</param>
/// <param name="SunriseDuringEclipseTime">
/// tret[5] — UT at which the Sun rises while the eclipse is in progress;
/// null when the Sun is already up at first contact.
/// </param>
/// <param name="SunsetDuringEclipseTime">
/// tret[6] — UT at which the Sun sets while the eclipse is in progress;
/// null when the Sun stays above the horizon through fourth contact.
/// </param>
/// <param name="Attributes">Per-observer attributes (magnitude, obscuration,
/// horizon coords, …) at <see cref="MaximumTime"/>.</param>
public readonly record struct SolarEclipseLocalSearchReport(
    EclipseTypeFlags EclipseType,
    JulianDay MaximumTime,
    JulianDay PartialBeginTime,
    JulianDay? TotalityBeginTime,
    JulianDay? TotalityEndTime,
    JulianDay PartialEndTime,
    JulianDay? SunriseDuringEclipseTime,
    JulianDay? SunsetDuringEclipseTime,
    SolarEclipseAttributes Attributes);
