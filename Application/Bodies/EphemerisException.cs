// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Common base for all errors raised by ephemeris computations.
/// Mirrors the C-side <c>serr[256]</c> error reporting in a typed,
/// idiomatic .NET shape.
/// </summary>
public abstract class EphemerisException : Exception
{
    protected EphemerisException(string message) : base(message) { }
    protected EphemerisException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a body cannot be served by the requested ephemeris source.
/// </summary>
public sealed class UnsupportedBodyException : EphemerisException
{
    public UnsupportedBodyException(CelestialBody body, EphemerisSource source)
        : base($"Body {body} is not supported by ephemeris source {source}.")
    {
        Body = body;
        EphemerisSource = source;
    }

    public CelestialBody Body { get; }
    public EphemerisSource EphemerisSource { get; }
}

/// <summary>
/// Thrown when the requested Julian Day lies outside the supported time range
/// of the chosen ephemeris source.
/// </summary>
public sealed class EphemerisDateOutOfRangeException : EphemerisException
{
    public EphemerisDateOutOfRangeException(JulianDay requested, JulianDay min, JulianDay max, EphemerisSource source)
        : base($"JD {requested.Value:F2} is outside {source} range [{min.Value:F2} .. {max.Value:F2}].")
    {
        Requested = requested;
        MinAvailable = min;
        MaxAvailable = max;
        EphemerisSource = source;
    }

    public JulianDay Requested { get; }
    public JulianDay MinAvailable { get; }
    public JulianDay MaxAvailable { get; }
    public EphemerisSource EphemerisSource { get; }
}

/// <summary>
/// Thrown when an <see cref="EphemerisFlags"/> combination is internally
/// inconsistent (e.g. <c>Heliocentric | Barycentric</c>) or the call is
/// missing data the requested flags require (e.g.
/// <see cref="EphemerisFlags.Topocentric"/> without an
/// <see cref="ObserverLocation"/>). Mirrors the C library's
/// <c>plaus_iflag</c> validation and the inline "geographic position has not
/// been set" branch of <c>swi_get_observer</c>.
/// </summary>
public sealed class EphemerisFlagsException : EphemerisException
{
    public EphemerisFlagsException(string message) : base(message) { }
}
