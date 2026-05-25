// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Identifies which underlying ephemeris source produced a
/// <see cref="BodyState"/>. Mirrors the C-side three-way split between
/// JPL DE files, Swiss Ephemeris <c>.se1</c> compressed files and
/// Moshier's file-less analytical theory.
/// </summary>
public enum EphemerisSource
{
    /// <summary>Source not yet determined or not applicable.</summary>
    Unknown = 0,
    /// <summary>JPL DE ephemeris (<c>.eph</c> file).</summary>
    Jpl = 1,
    /// <summary>Swiss Ephemeris compressed file (<c>.se1</c>).</summary>
    SwissEph = 2,
    /// <summary>Moshier analytical theory (file-less).</summary>
    Moshier = 3,
}
