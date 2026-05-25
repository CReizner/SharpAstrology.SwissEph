// Ported from swisseph-master/sweph.h struct plantbl (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Moshier;

/// <summary>
/// C# equivalent of the C <c>struct plantbl</c> declared at
/// <c>swisseph-master/sweph.h:698-706</c>. Carries the truncated Fourier
/// expansion of one planet's heliocentric J2000 ecliptic coordinates as
/// fitted by Moshier to JPL DE404. Backed by <see cref="ReadOnlySpan{T}"/>
/// pointing at PE-image RVA blobs — no heap allocation.
/// </summary>
internal readonly ref struct MoshierPlanetTable
{
    /// <summary>
    /// Number of harmonics required for each of the nine fundamental Laplace-style
    /// arguments (Mercury through Pluto, in Simon-et-al order). A zero entry
    /// means the corresponding argument is unused.
    /// </summary>
    public required ReadOnlySpan<sbyte> MaxHarmonic { get; init; }

    /// <summary>Highest power of T (Julian millennia from J2000) in any term.</summary>
    public required int MaxPowerOfT { get; init; }

    /// <summary>Header rows describing each Fourier term: number of arguments + harmonic/planet pairs.</summary>
    public required ReadOnlySpan<sbyte> ArgTbl { get; init; }

    /// <summary>Cosine/sine amplitudes for ecliptic longitude. Units: arc-seconds.</summary>
    public required ReadOnlySpan<double> LonTbl { get; init; }

    /// <summary>Cosine/sine amplitudes for ecliptic latitude. Units: arc-seconds.</summary>
    public required ReadOnlySpan<double> LatTbl { get; init; }

    /// <summary>Cosine/sine amplitudes for radial distance. Units: AU times the planet's mean distance.</summary>
    public required ReadOnlySpan<double> RadTbl { get; init; }

    /// <summary>The planet's mean heliocentric distance in AU (constant offset for the radial expansion).</summary>
    public required double Distance { get; init; }
}
