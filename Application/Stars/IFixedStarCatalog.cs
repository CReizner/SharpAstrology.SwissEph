// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Stars;

namespace SharpAstrology.SwissEphemerides.Application.Stars;

/// <summary>
/// Application-layer abstraction over the Swiss Ephemeris fixed-star
/// catalogue. Mirrors the C library's <c>load_all_fixed_stars</c> +
/// <c>search_star_in_list</c> + <c>get_builtin_star</c> trio
/// (<c>sweph.c#L6324</c>, <c>#L6674</c>, <c>#L6750</c>) with stateless
/// per-call lookup semantics. The contract is deliberately minimal: the
/// concrete implementation owns the file I/O and lazy-parsing strategy.
/// </summary>
/// <remarks>
/// <see langword="internal"/> by design: the interface preserves the
/// Application → Infrastructure project-direction split (so
/// <see cref="FixedStarService"/> here in Application does not pull
/// in a hard reference on the Infrastructure-side
/// <c>FixedStarCatalogReader</c>), but it is not a public extension
/// point — consumers wire a catalogue up via
/// <see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder.UseFixedStarCatalog(string)"/>
/// or its <see cref="System.IO.Stream"/>-factory overload.
/// </remarks>
internal interface IFixedStarCatalog
{
    /// <summary>
    /// Resolves a search string to a star record. Implementations must
    /// honour the three lookup formats the C library accepts:
    /// traditional name (case-insensitive, whitespace-tolerant),
    /// <c>",bayer"</c> Bayer designation, and 1-based sequential index.
    /// Returns <c>false</c> when no entry matches; the caller is
    /// responsible for emitting an error.
    /// </summary>
    /// <param name="searchName">User-supplied lookup string.</param>
    /// <param name="result">On success: the matched star and the
    /// canonical <c>"trad,bayer"</c> name.</param>
    /// <returns>True when a record was found.</returns>
    bool TryFind(string searchName, out FixedStarMatch result);
}

/// <summary>
/// Result of a successful <see cref="IFixedStarCatalog.TryFind"/>
/// call: the matched <see cref="FixedStar"/> together with the canonical
/// <c>"trad,bayer"</c> name string written back into the input pointer
/// of the C library's <c>swe_fixstar2</c> entry point. Internal because
/// it is only produced and consumed by composition-root machinery; the
/// public fixed-star surface is <see cref="FixedStarService"/>.
/// </summary>
/// <param name="Star">The matched catalogue record.</param>
/// <param name="CanonicalName">Combined <c>"trad,bayer"</c> name.</param>
internal readonly record struct FixedStarMatch(FixedStar Star, string CanonicalName);
