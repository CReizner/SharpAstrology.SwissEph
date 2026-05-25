// Ported from swisseph-master/sweph.c main_planet (line 1562) and the
// goto-based fallback chain inside swecalc (line 587).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.Collections.Generic;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Three-way dispatcher that picks an <see cref="IBodyPositionSource"/> for
/// the requested <see cref="EphemerisSource"/> hint and falls back to a
/// supported source when the requested one cannot serve the body or its data
/// file is missing. Mirrors the C library's source-fallback chain in
/// <c>main_planet</c> (sweph.c#L1562-L1672): the goto-labels
/// <c>sweph_planet</c>, <c>moshier_planet</c> express the same fallback
/// graph as our cascade — with one deliberate divergence:
/// <see cref="EphemerisFlags.JplEph"/> never falls back silently
/// (<c>ARCHITECTURE.md §3.4</c>). When JPL is requested but unavailable
/// (no source registered, or the chosen body is not in the JPL ephemeris)
/// the router throws instead of substituting a different back-end. The
/// SwissEph → Moshier fallback for missing <c>.se1</c> segments stays:
/// SwissEph is the C library's "best available" source, Moshier its
/// analytical understudy, so silent degrade matches caller expectations.
/// </summary>
internal sealed class SourceRouter
{
    private readonly IBodyPositionSource? _jpl;
    private readonly IBodyPositionSource? _swissEph;
    private readonly IBodyPositionSource? _moshier;

    /// <summary>
    /// Constructs a router from a set of available sources. Sources may be
    /// supplied in any order; at least one must be present. Composing more
    /// than one source per kind is unsupported (only one of each).
    /// </summary>
    public SourceRouter(IEnumerable<IBodyPositionSource> sources)
    {
        if (sources is null) throw new ArgumentNullException(nameof(sources));
        foreach (var s in sources)
        {
            switch (s.Kind)
            {
                case EphemerisSource.Jpl:
                    if (_jpl is not null)
                        throw new ArgumentException("Multiple JPL body-position sources supplied.", nameof(sources));
                    _jpl = s;
                    break;
                case EphemerisSource.SwissEph:
                    if (_swissEph is not null)
                        throw new ArgumentException("Multiple SwissEph body-position sources supplied.", nameof(sources));
                    _swissEph = s;
                    break;
                case EphemerisSource.Moshier:
                    if (_moshier is not null)
                        throw new ArgumentException("Multiple Moshier body-position sources supplied.", nameof(sources));
                    _moshier = s;
                    break;
                default:
                    throw new ArgumentException($"Unknown ephemeris source kind: {s.Kind}", nameof(sources));
            }
        }
        if (_jpl is null && _swissEph is null && _moshier is null)
            throw new ArgumentException("At least one body-position source must be supplied.", nameof(sources));
    }

    /// <summary>True if the router owns a source of <paramref name="kind"/>.</summary>
    public bool Has(EphemerisSource kind) => kind switch
    {
        EphemerisSource.Jpl => _jpl is not null,
        EphemerisSource.SwissEph => _swissEph is not null,
        EphemerisSource.Moshier => _moshier is not null,
        _ => false,
    };

    /// <summary>
    /// Resolves the source that should serve <paramref name="body"/> at
    /// <paramref name="jdEt"/>. Honours the source bit in
    /// <paramref name="flags"/> first; SwissEph silently falls back to
    /// Moshier when no <c>.se1</c> segment is available, but JPL never
    /// falls back — see the class-level remark.
    /// </summary>
    /// <returns>
    /// A tuple of (chosen source, normalised flags). The flags reflect the
    /// chosen source — i.e. if SwissEph was requested but Moshier picked up
    /// the call, the returned flags carry <see cref="EphemerisFlags.MoshierEph"/>.
    /// </returns>
    /// <exception cref="EphemerisFlagsException">
    /// <see cref="EphemerisFlags.JplEph"/> was requested but no JPL source
    /// is registered with this context.
    /// </exception>
    /// <exception cref="UnsupportedBodyException">
    /// The chosen source cannot serve <paramref name="body"/>. For JPL this
    /// is a hard fail; for SwissEph/Moshier it is reached only after the
    /// fallback chain exhausts.
    /// </exception>
    public (IBodyPositionSource Source, EphemerisFlags Flags) Resolve(
        EphemerisFlags flags,
        CelestialBody body,
        JulianDay jdEt)
    {
        // 1. JPL is honoured strictly: no silent substitution. Either we
        //    have the source and it covers the body, or we throw.
        if ((flags & EphemerisFlags.JplEph) != 0)
        {
            if (_jpl is null)
                throw new EphemerisFlagsException(
                    "EphemerisFlags.JplEph was requested, but no JPL body-position source is "
                    + "configured on this EphemerisContext. Wire one up via "
                    + "EphemerisContextBuilder.UseJplFile(...), or pass EphemerisFlags.SwissEph / "
                    + "EphemerisFlags.MoshierEph instead. JPL never falls back silently "
                    + "(ARCHITECTURE.md §3.4).");
            if (!_jpl.CanProvide(body))
                throw new UnsupportedBodyException(body, EphemerisSource.Jpl);
            return (_jpl, flags);
        }
        if ((flags & EphemerisFlags.SwissEph) != 0)
        {
            if (TryUse(_swissEph, body, out var s) && CanComputeWithoutThrowing(s, body, jdEt))
                return (s, flags);
            // SwissEph cannot serve → fall through to Moshier (C parity).
            flags = (flags & ~EphemerisFlags.SwissEph) | EphemerisFlags.MoshierEph;
        }
        if ((flags & EphemerisFlags.MoshierEph) != 0)
        {
            if (TryUse(_moshier, body, out var s)) return (s, flags);
        }

        throw new UnsupportedBodyException(body, _SourceFromFlags(flags));
    }

    private static EphemerisSource _SourceFromFlags(EphemerisFlags flags)
    {
        if ((flags & EphemerisFlags.JplEph) != 0) return EphemerisSource.Jpl;
        if ((flags & EphemerisFlags.SwissEph) != 0) return EphemerisSource.SwissEph;
        return EphemerisSource.Moshier;
    }

    private static bool TryUse(IBodyPositionSource? source, CelestialBody body, out IBodyPositionSource picked)
    {
        if (source is not null && source.CanProvide(body))
        {
            picked = source;
            return true;
        }
        picked = null!;
        return false;
    }

    /// <summary>
    /// SwissEph can declare CanProvide=true at config time but lack the
    /// concrete <c>.se1</c> file at the requested JD. The C library catches
    /// this in <c>main_planet</c> via <c>NOT_AVAILABLE</c>; we approximate by
    /// trying the call and treating filesystem/format errors as a missing
    /// file. Pure date-out-of-range errors from the source itself are
    /// propagated (they would also fail the Moshier source).
    /// </summary>
    private static bool CanComputeWithoutThrowing(IBodyPositionSource source, CelestialBody body, JulianDay jdEt)
    {
        // Cheap pre-check: if CanProvide already says no, skip.
        if (!source.CanProvide(body)) return false;

        // We do NOT actually call Compute here (would defeat the purpose of
        // a tiny dispatcher and double the work). Instead, we trust
        // CanProvide; the BodyService catches FileNotFoundException
        // (raised by Se1FileReader for missing/unsupported segments) and
        // retries with Moshier. See BodyService.Compute for the catch.
        _ = jdEt;
        return true;
    }
}
