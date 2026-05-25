// Ported from swisseph-master/swephexp.h:307-331 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Eclipse-type and visibility bit flags. Mirrors the <c>SE_ECL_*</c>
/// constants in <c>swephexp.h</c>. Used both as request filters
/// (<see cref="Total"/>, <see cref="Annular"/>, …) and as return-flag bit
/// sets for visibility / contact-time information.
/// </summary>
[Flags]
public enum EclipseTypeFlags : int
{
    /// <summary>No eclipse / no flags set.</summary>
    None = 0,

    /// <summary>SE_ECL_CENTRAL — central eclipse (shadow axis touches earth).</summary>
    Central = 1,
    /// <summary>SE_ECL_NONCENTRAL — non-central eclipse (axis misses, partial seen).</summary>
    NonCentral = 2,
    /// <summary>SE_ECL_TOTAL — total eclipse.</summary>
    Total = 4,
    /// <summary>SE_ECL_ANNULAR — annular eclipse (Moon fully inside Sun).</summary>
    Annular = 8,
    /// <summary>SE_ECL_PARTIAL — partial eclipse.</summary>
    Partial = 16,
    /// <summary>SE_ECL_ANNULAR_TOTAL — hybrid annular/total eclipse.</summary>
    AnnularTotal = 32,
    /// <summary>SE_ECL_HYBRID — alias for <see cref="AnnularTotal"/>.</summary>
    Hybrid = AnnularTotal,
    /// <summary>SE_ECL_PENUMBRAL — penumbral eclipse (lunar only).</summary>
    Penumbral = 64,

    /// <summary>SE_ECL_ALLTYPES_SOLAR — every solar-eclipse-type bit.</summary>
    AllTypesSolar = Central | NonCentral | Total | Annular | Partial | AnnularTotal,
    /// <summary>SE_ECL_ALLTYPES_LUNAR — every lunar-eclipse-type bit.</summary>
    AllTypesLunar = Total | Partial | Penumbral,

    /// <summary>SE_ECL_VISIBLE — eclipse is above the horizon at the observer.</summary>
    Visible = 128,
    /// <summary>SE_ECL_MAX_VISIBLE — moment of maximum is visible.</summary>
    MaxVisible = 256,
    /// <summary>SE_ECL_PARTBEG_VISIBLE — start of partial phase visible.</summary>
    PartialBeginVisible = 512,
    /// <summary>SE_ECL_TOTBEG_VISIBLE — start of total phase visible.</summary>
    TotalBeginVisible = 1024,
    /// <summary>SE_ECL_TOTEND_VISIBLE — end of total phase visible.</summary>
    TotalEndVisible = 2048,
    /// <summary>SE_ECL_PARTEND_VISIBLE — end of partial phase visible.</summary>
    PartialEndVisible = 4096,
    /// <summary>SE_ECL_PENUMBBEG_VISIBLE — start of penumbral phase visible.</summary>
    PenumbralBeginVisible = 8192,
    /// <summary>SE_ECL_PENUMBEND_VISIBLE — end of penumbral phase visible.</summary>
    PenumbralEndVisible = 16384,
    /// <summary>SE_ECL_OCC_BEG_DAYLIGHT — occultation begins during the day (alias of PenumbBeg).</summary>
    OccultationBeginDaylight = 8192,
    /// <summary>SE_ECL_OCC_END_DAYLIGHT — occultation ends during the day (alias of PenumbEnd).</summary>
    OccultationEndDaylight = 16384,
    /// <summary>SE_ECL_ONE_TRY — search-control bit: only one trial step.</summary>
    OneTry = 32 * 1024,
}
