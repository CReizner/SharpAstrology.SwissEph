// Ported from swisseph-master/swephexp.h (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Time;

/// <summary>
/// Calendar system selector. Mirrors the C constants <c>SE_JUL_CAL = 0</c> and
/// <c>SE_GREG_CAL = 1</c> from <c>swephexp.h</c>.
/// </summary>
public enum CalendarSystem
{
    Julian = 0,
    Gregorian = 1,
}
