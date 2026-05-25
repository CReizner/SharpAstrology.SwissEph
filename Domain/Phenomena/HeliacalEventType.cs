// Ported from swisseph-master/swephexp.h (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// Heliacal event-type selector (the <c>TypeEvent</c> int32 argument to
/// <c>swe_heliacal_pheno_ut</c> / <c>swe_heliacal_ut</c>). Values mirror
/// <c>SE_HELIACAL_*</c> at swephexp.h#L426-L432.
/// </summary>
public enum HeliacalEventType
{
    /// <summary>
    /// First morning visibility — rising side of conjunction.
    /// <c>SE_HELIACAL_RISING</c> = <c>SE_MORNING_FIRST</c> = 1.
    /// </summary>
    MorningFirst = 1,

    /// <summary>
    /// Last evening visibility — setting side of conjunction.
    /// <c>SE_HELIACAL_SETTING</c> = <c>SE_EVENING_LAST</c> = 2.
    /// </summary>
    EveningLast = 2,

    /// <summary>
    /// First evening visibility (Earth side of conjunction).
    /// <c>SE_EVENING_FIRST</c> = 3.
    /// </summary>
    EveningFirst = 3,

    /// <summary>
    /// Last morning visibility (Earth side of conjunction).
    /// <c>SE_MORNING_LAST</c> = 4.
    /// </summary>
    MorningLast = 4,
}
