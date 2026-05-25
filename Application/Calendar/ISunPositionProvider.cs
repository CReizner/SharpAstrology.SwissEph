// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Calendar;

/// <summary>
/// Composition-root-only abstraction the <see cref="CalendarService"/> uses
/// to fetch the apparent equatorial right-ascension of the Sun (degrees),
/// as required by Equation-of-Time and LMT↔LAT. The interface is
/// <see langword="internal"/> by design: it is not a public extension
/// point but the dependency-breaker that lets
/// <see cref="CalendarService"/> stay decoupled from
/// <see cref="Bodies.BodyService"/> (otherwise: BodyService → CalendarService
/// → BodyService is a constructor cycle). The single shipped implementation
/// is <see cref="Bodies.BodyServiceSunPositionProvider"/>; consumers wire
/// it up through <see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder.Build"/>.
/// </summary>
internal interface ISunPositionProvider
{
    /// <summary>
    /// Returns the apparent equatorial right ascension of the Sun in
    /// degrees at <paramref name="jdEt"/> Terrestrial Time. Mirrors a
    /// <c>swe_calc(jd, SE_SUN, SEFLG_EQUATORIAL, …)</c> call.
    /// </summary>
    double ApparentRightAscensionDegrees(JulianDay jdEt);
}
