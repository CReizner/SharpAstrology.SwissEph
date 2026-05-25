// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Adapter that exposes <see cref="BodyService"/> as the internal
/// <see cref="ISunPositionProvider"/>. Used to break the
/// <see cref="CalendarService"/> ↔ <see cref="BodyService"/> circular
/// dependency: <c>CalendarService</c> only depends on the small internal
/// interface, the composition root wires this adapter in. Both the
/// adapter and the interface are <see langword="internal"/> because they
/// are an implementation detail of
/// <see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder.Build"/>,
/// not a public extension point.
/// </summary>
internal sealed class BodyServiceSunPositionProvider : ISunPositionProvider
{
    private readonly BodyService _bodies;

    public BodyServiceSunPositionProvider(BodyService bodies)
    {
        _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    }

    /// <summary>
    /// Returns the apparent equatorial right ascension of the Sun in
    /// degrees at <paramref name="jdEt"/> Terrestrial Time. Equivalent to
    /// a <c>swe_calc(jd, SE_SUN, SEFLG_EQUATORIAL, …)</c> call.
    /// </summary>
    public double ApparentRightAscensionDegrees(JulianDay jdEt)
    {
        var state = _bodies.Compute(CelestialBody.Sun, jdEt, EphemerisFlags.Equatorial);
        // BodyState in equatorial cartesian (J2000 or true-of-date depending
        // on flags). We requested without J2000 flag → true-of-date equator.
        // Right ascension = atan2(y, x) in degrees, normalised to [0, 360).
        var ra = System.Math.Atan2(state.Position.Y, state.Position.X) * Domain.Constants.AstronomicalConstants.RadToDeg;
        if (ra < 0) ra += 360.0;
        return ra;
    }
}
