// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Outcome of a local lunar-occultation search
/// (<c>swe_lun_occult_when_loc</c>, swecl.c#L2071). Mirrors the
/// <c>tret[0..6]</c> array plus the per-observer attribute set.
/// </summary>
/// <param name="EclipseType">
/// Combined <see cref="EclipseTypeFlags"/>. The visibility bits
/// (<c>Visible</c>, <c>MaxVisible</c>, <c>PartialBeginVisible</c>=1stV,
/// <c>TotalBeginVisible</c>=2ndV, <c>TotalEndVisible</c>=3rdV,
/// <c>PartialEndVisible</c>=4thV, <c>OccultationBeginDaylight</c>,
/// <c>OccultationEndDaylight</c>) are populated when the corresponding
/// contact is above the horizon at the observer.
/// </param>
/// <param name="MaximumTime">tret[0] — UT of the local maximum.</param>
/// <param name="FirstContactTime">
/// tret[1] — UT of first contact (penumbra begin). Null only when not
/// applicable (star occultations: equal to <see cref="SecondContactTime"/>).
/// </param>
/// <param name="SecondContactTime">
/// tret[2] — UT of second contact (totality begin). Null for purely
/// partial occultations.
/// </param>
/// <param name="ThirdContactTime">
/// tret[3] — UT of third contact (totality end). Null for partial.
/// </param>
/// <param name="FourthContactTime">
/// tret[4] — UT of fourth contact (penumbra end).
/// </param>
/// <param name="BodyRiseDuringOccultationTime">
/// tret[5] — UT of body rise between first and fourth contact, or
/// <c>null</c> if the body remains above (or below) the horizon for the
/// whole occultation.
/// </param>
/// <param name="BodySetDuringOccultationTime">
/// tret[6] — UT of body set between first and fourth contact, or
/// <c>null</c> if no horizon crossing occurs.
/// </param>
/// <param name="Attributes">Per-observer attributes evaluated at the local maximum.</param>
public readonly record struct LunarOccultationLocalSearchReport(
    EclipseTypeFlags EclipseType,
    JulianDay MaximumTime,
    JulianDay? FirstContactTime,
    JulianDay? SecondContactTime,
    JulianDay? ThirdContactTime,
    JulianDay? FourthContactTime,
    JulianDay? BodyRiseDuringOccultationTime,
    JulianDay? BodySetDuringOccultationTime,
    LunarOccultationAttributes Attributes);
