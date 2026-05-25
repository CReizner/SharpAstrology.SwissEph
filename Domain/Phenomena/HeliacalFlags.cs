// Ported from swisseph-master/swephexp.h:434-449 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   HeliacalFlags — SE_HELFLAG_* macros (swephexp.h#L434-L449). The four AvKind
//                   members map onto SE_HELFLAG_AVKIND_* and are mutually exclusive.
//   LongSearch    — MAX_COUNT_SYNPER_MAX synodic periods (swehel.c#L111)
//   AvKindMask    — swephexp.h#L449

using System;

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Behavioural flags for the heliacal-phenomena routines. Multiple
/// <c>AvKind*</c> values select different arcus-vis search methods and are
/// mutually exclusive.
/// </summary>
[Flags]
public enum HeliacalFlags
{
    /// <summary>No flags. Defaults: max 5 synodic periods, low precision, naked-eye observer.</summary>
    None = 0,

    /// <summary>Search up to the long-search synodic-period cap.</summary>
    LongSearch = 128,

    /// <summary>Use full-nutation apparent positions (slower).</summary>
    HighPrecision = 256,

    /// <summary>Honour the four telescope/optic fields of <c>ObserverParameters</c>.</summary>
    OpticalParameters = 512,

    /// <summary>Skip the per-event "best/first/last visibility" detail computation.</summary>
    NoDetails = 1024,

    /// <summary>Search only one synodic period (mutually exclusive with <see cref="LongSearch"/>).</summary>
    SearchOnePeriod = 1 << 11,

    /// <summary>Treat the sky as fully dark (skip Sun-altitude lookup).</summary>
    VisLimDark = 1 << 12,

    /// <summary>Ignore the Moon's contribution to sky brightness.</summary>
    VisLimNoMoon = 1 << 13,

    /// <summary>Force photopic-vision branch regardless of brightness.</summary>
    VisLimPhotopic = 1 << 14,

    /// <summary>Force scotopic-vision branch regardless of brightness.</summary>
    VisLimScotopic = 1 << 15,

    /// <summary>Search method: Reijs' arcus-visionis (sun altitude).</summary>
    AvKindVR = 1 << 16,

    /// <summary>Search method: photometric (PTO).</summary>
    AvKindPto = 1 << 17,

    /// <summary>Search method: minimum sun altitude -7° (acronychal heuristic).</summary>
    AvKindMin7 = 1 << 18,

    /// <summary>Search method: minimum sun altitude -9° (acronychal heuristic).</summary>
    AvKindMin9 = 1 << 19,

    /// <summary>Mask of all four <c>AvKind*</c> bits.</summary>
    AvKindMask = AvKindVR | AvKindPto | AvKindMin7 | AvKindMin9,
}
