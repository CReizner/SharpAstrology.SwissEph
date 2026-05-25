// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.Ephemerides;

/// <summary>
/// Selects the underlying ephemeris source used by
/// <see cref="SwissEphemeridesService"/>. Mirrors the public surface of the
/// legacy <c>SharpAstrology.SwissEph 0.3.0</c> package so that existing
/// consumer code compiles and runs unchanged.
/// </summary>
/// <remarks>
/// Internally the values map to <see cref="EphemerisContextBuilder"/>
/// configuration:
/// <list type="bullet">
///   <item><description><see cref="Moshier"/> — analytical Moshier series only (no files).</description></item>
///   <item><description><see cref="Swiss"/> — Astrodienst <c>.se1</c> files.</description></item>
///   <item><description><see cref="Jpl"/> — JPL DE binary ephemeris (e.g. <c>de441.eph</c>).</description></item>
/// </list>
/// </remarks>
public enum EphType
{
    Moshier,
    Swiss,
    Jpl,
}
