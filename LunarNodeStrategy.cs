// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides;

/// <summary>
/// Selects which lunar-node flavour <see cref="SwissEphemerides"/> returns
/// for <see cref="SharpAstrology.Enums.Planets.NorthNode"/> and the derived
/// <see cref="SharpAstrology.Enums.Planets.SouthNode"/>. The
/// <c>SharpAstrology.Base</c> <c>Planets</c> enum exposes a single
/// node entry, so the choice between mean and true (osculating) nodes
/// is made once at context construction time via
/// <see cref="EphemerisContextBuilder.WithLunarNodeStrategy"/>.
/// </summary>
/// <remarks>
/// The lower-level <see cref="EphemerisContext.Bodies"/> service always
/// exposes both flavours through
/// <see cref="SharpAstrology.SwissEphemerides.Application.Bodies.CelestialBody.MeanNode"/>
/// and
/// <see cref="SharpAstrology.SwissEphemerides.Application.Bodies.CelestialBody.TrueNode"/>;
/// this strategy only governs the
/// <see cref="SharpAstrology.Interfaces.IEphemerides"/> adapter surface.
/// </remarks>
public enum LunarNodeStrategy
{
    /// <summary>
    /// Resolve <c>NorthNode</c> to the true (osculating) lunar node —
    /// the C library's <c>SE_TRUE_NODE</c>. This is the default. The
    /// adapter leaves the source choice to the configured
    /// <see cref="EphemerisContext"/>, so SwissEph/JPL sources are used
    /// when available and otherwise fall back normally.
    /// </summary>
    TrueNode = 0,

    /// <summary>
    /// Resolve <c>NorthNode</c> to the mean lunar node — the C library's
    /// <c>SE_MEAN_NODE</c>. This currently uses the analytical Moshier
    /// mean-element series even in a SwissEph/JPL-capable context, matching
    /// the lower-level body service's implementation.
    /// </summary>
    MeanNode = 1,
}
