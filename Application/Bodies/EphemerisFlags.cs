// Ported from swisseph-master/swephexp.h:186-218 (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Bit flags controlling a body-position computation. Numeric values
/// match the C library's <c>SEFLG_*</c> constants verbatim, so a flag
/// integer crossing a P/Invoke boundary works in either direction.
/// Combine with bitwise <c>|</c>; <see cref="None"/> means "default
/// settings" — typically tropical longitude, mean equinox of date, no
/// speed.
/// </summary>
[Flags]
public enum EphemerisFlags : int
{
    /// <summary>No flags set. Defaults to SwissEph + tropical mean-of-date.</summary>
    None = 0,

    /// <summary>SEFLG_JPLEPH — use JPL DE ephemeris file.</summary>
    JplEph = 1,
    /// <summary>SEFLG_SWIEPH — use Swiss Ephemeris (.se1) files.</summary>
    SwissEph = 2,
    /// <summary>SEFLG_MOSEPH — use Moshier analytical ephemeris (file-less).</summary>
    MoshierEph = 4,

    /// <summary>SEFLG_HELCTR — heliocentric position.</summary>
    Heliocentric = 8,
    /// <summary>SEFLG_TRUEPOS — true (instantaneous) position, no light-time.</summary>
    TruePosition = 16,
    /// <summary>SEFLG_J2000 — J2000 equinox / no precession to date.</summary>
    J2000Equinox = 32,
    /// <summary>SEFLG_NONUT — no nutation.</summary>
    NoNutation = 64,
    /// <summary>
    /// SEFLG_SPEED3 — legacy 3-position speed estimator. This port has no
    /// separate low-precision speed implementation, so PlausibilityCheck
    /// normalises any Speed3 (alone or combined with Speed) to plain
    /// <see cref="Speed"/> before the pipeline runs. Kept on the enum for
    /// numeric parity with the C library's iflag bit values.
    /// </summary>
    Speed3 = 128,
    /// <summary>SEFLG_SPEED — high-precision velocity.</summary>
    Speed = 256,
    /// <summary>SEFLG_NOGDEFL — no gravitational deflection by the Sun.</summary>
    NoGravDeflection = 512,
    /// <summary>SEFLG_NOABERR — no annual aberration.</summary>
    NoAberration = 1024,
    /// <summary>Astrometric position = neither aberration nor deflection.</summary>
    Astrometric = NoGravDeflection | NoAberration,

    /// <summary>SEFLG_EQUATORIAL — equatorial spherical coordinates.</summary>
    Equatorial = 2 * 1024,
    /// <summary>SEFLG_XYZ — cartesian (x,y,z) instead of (lon,lat,r).</summary>
    Cartesian = 4 * 1024,
    /// <summary>SEFLG_RADIANS — output in radians.</summary>
    Radians = 8 * 1024,
    /// <summary>SEFLG_BARYCTR — barycentric position.</summary>
    Barycentric = 16 * 1024,
    /// <summary>SEFLG_TOPOCTR — topocentric position.</summary>
    Topocentric = 32 * 1024,
    /// <summary>SEFLG_SIDEREAL — sidereal (ayanamsha-corrected) longitude.</summary>
    Sidereal = 64 * 1024,
    /// <summary>SEFLG_ICRS — ICRS frame.</summary>
    Icrs = 128 * 1024,
    /// <summary>SEFLG_DPSIDEPS_1980 — use IAU 1980 nutation Δψ/Δε.</summary>
    DPsiDEps1980 = 256 * 1024,
    /// <summary>SEFLG_JPLHOR — JPL Horizons compatibility.</summary>
    JplHorizons = DPsiDEps1980,
    /// <summary>SEFLG_JPLHOR_APPROX — approximate JPL Horizons compatibility.</summary>
    JplHorizonsApprox = 512 * 1024,
    /// <summary>SEFLG_CENTER_BODY — return the center of the body, not the surface.</summary>
    CenterOfBody = 1024 * 1024,
}
