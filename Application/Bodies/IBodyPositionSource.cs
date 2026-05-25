// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// One ephemeris back-end (JPL DE binary, Swiss Ephemeris <c>.se1</c>
/// segments, or the Moshier analytical theory). The body service routes
/// between configured sources based on the call's
/// <see cref="EphemerisFlags"/> and the source's
/// <see cref="CanProvide(CelestialBody)"/> answer; most callers use the
/// service rather than implementing or invoking this interface directly.
/// </summary>
/// <remarks>
/// Implementations live in the Infrastructure project. Returned
/// <see cref="BodyState"/> values are raw — no light-time, aberration or
/// frame conversions have been applied. The body service applies those
/// corrections per call.
/// </remarks>
internal interface IBodyPositionSource
{
    /// <summary>The source kind this instance represents.</summary>
    EphemerisSource Kind { get; }

    /// <summary>
    /// Indicates whether this source can produce a sample for
    /// <paramref name="body"/>. Returns <see langword="false"/> for
    /// bodies served by other channels (e.g. asteroids by ID,
    /// fixed-stars by name).
    /// </summary>
    /// <param name="body">The celestial body in question.</param>
    /// <returns><see langword="true"/> if the source supports it.</returns>
    bool CanProvide(CelestialBody body);

    /// <summary>
    /// Computes a raw <see cref="BodyState"/> for the body at
    /// <paramref name="jdEt"/>.
    /// </summary>
    /// <param name="body">Body to compute.</param>
    /// <param name="jdEt">Julian Day in Terrestrial Time.</param>
    /// <param name="flags">
    /// The full caller-supplied flag set. Implementations may inspect
    /// the source-selection / heliocentric-vs-geocentric / J2000 /
    /// speed bits; remaining bits (aberration, deflection, frame
    /// conversion) are honoured in the body service's correction
    /// pipeline rather than here.
    /// </param>
    /// <returns>A raw, source-native body state.</returns>
    /// <exception cref="UnsupportedBodyException">
    /// <paramref name="body"/> is not in this source's supported set.
    /// </exception>
    /// <exception cref="EphemerisDateOutOfRangeException">
    /// <paramref name="jdEt"/> is outside this source's supported time
    /// range.
    /// </exception>
    BodyState Compute(CelestialBody body, JulianDay jdEt, EphemerisFlags flags);
}
