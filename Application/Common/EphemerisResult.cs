// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Application.Common;

/// <summary>
/// Generic result wrapper for ephemeris routines that may emit a warning
/// without failing — for example, <c>swe_sol_eclipse_when_glob</c> writes
/// "no eclipse found in the search window" into <c>serr</c> while still
/// returning a flag value. A null <see cref="Warning"/> means "clean run".
/// </summary>
/// <typeparam name="T">Result payload type.</typeparam>
/// <param name="Value">The result value.</param>
/// <param name="Warning">
/// Non-fatal warning text, or <c>null</c> if the operation produced no
/// warning. Mirrors the C library's <c>serr</c> buffer convention but
/// without exposing the buffer to callers.
/// </param>
public readonly record struct EphemerisResult<T>(T Value, string? Warning)
{
    /// <summary>Constructs a clean result (no warning).</summary>
    public static EphemerisResult<T> Ok(T value) => new(value, null);

    /// <summary>Constructs a result with a non-fatal warning attached.</summary>
    public static EphemerisResult<T> WithWarning(T value, string warning) => new(value, warning);

    /// <summary><c>true</c> when a warning is attached.</summary>
    public bool HasWarning => Warning is not null;

    /// <summary>
    /// Returns <see cref="Value"/> when the result is clean; throws
    /// <see cref="InvalidOperationException"/> with the warning text when
    /// <see cref="HasWarning"/> is <c>true</c>. Use this when the caller
    /// has already established that a warning is impossible for the
    /// invocation (e.g. a golden test with known-good inputs); ordinary
    /// callers should branch on <see cref="HasWarning"/> instead.
    /// </summary>
    public T Unwrap() =>
        Warning is null
            ? Value
            : throw new InvalidOperationException(Warning);
}
