// Ported from swisseph-master/sweph.c plaus_iflag (line 6066).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Validates and normalises an <see cref="EphemerisFlags"/> value before it
/// enters the body-position pipeline. Mirrors <c>plaus_iflag</c>
/// (<c>sweph.c#L6066-L6148</c>).
/// </summary>
/// <remarks>
/// The C library quietly clears incompatible bits and does not report errors
/// for them (e.g. <c>HELCTR | TOPOCTR</c> drops the helio bit). We follow
/// the same forgiving normalisation, but additionally throw
/// <see cref="EphemerisFlagsException"/> for flag combinations that are
/// ambiguous in this port — multiple ephemeris-source bits set at once
/// (the C code resolves last-wins which is brittle), or
/// <see cref="EphemerisFlags.Topocentric"/> without an observer.
/// </remarks>
internal static class PlausibilityCheck
{
    private const EphemerisFlags SourceMask =
        EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph;

    /// <summary>
    /// Returns a normalised flag set. <paramref name="hasObserver"/> reports
    /// whether the caller supplied an <see cref="ObserverLocation"/>, which
    /// is required iff <see cref="EphemerisFlags.Topocentric"/> is on.
    /// </summary>
    /// <exception cref="EphemerisFlagsException">When the flags are internally inconsistent.</exception>
    public static EphemerisFlags Normalize(EphemerisFlags flags, bool hasObserver)
    {
        // SEFLG_SIDEREAL is applied by BodyService after the
        // apparent-position pipeline, not pre-rejected here.

        // Source-flag conflicts must be explicit. The C code silently picks
        // the last-set bit; we treat it as a programming error because the
        // caller cannot know which one wins in a multi-source service.
        var sourceBits = flags & SourceMask;
        if (System.Numerics.BitOperations.PopCount((uint)sourceBits) > 1)
            throw new EphemerisFlagsException(
                "More than one ephemeris-source bit set (JplEph / SwissEph / MoshierEph).");

        // Topocentric requires an observer.
        if ((flags & EphemerisFlags.Topocentric) != 0 && !hasObserver)
            throw new EphemerisFlagsException(
                "SEFLG_TOPOCTR requires an ObserverLocation parameter.");

        // -------- normalisations from plaus_iflag (sweph.c#L6076-L6101) --------

        // Topocentric overrides helio/bary.
        if ((flags & EphemerisFlags.Topocentric) != 0)
            flags &= ~(EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric);
        // Bary overrides helio.
        if ((flags & EphemerisFlags.Barycentric) != 0)
            flags &= ~EphemerisFlags.Heliocentric;
        // helio/bary turn off aberration + deflection.
        if ((flags & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric)) != 0)
            flags |= EphemerisFlags.NoAberration | EphemerisFlags.NoGravDeflection;
        // J2000: no nutation.
        if ((flags & EphemerisFlags.J2000Equinox) != 0)
            flags |= EphemerisFlags.NoNutation;
        // Sidereal also forces no-nutation on the body — mirrors plaus_iflag
        // (sweph.c#L6094-L6097). The ayanamsha is then subtracted from the
        // mean-ecliptic longitude. This is what makes the C library's sidereal
        // output identical with and without an explicit SEFLG_NONUT.
        if ((flags & EphemerisFlags.Sidereal) != 0)
            flags |= EphemerisFlags.NoNutation;
        // True position: no aberration, no deflection.
        if ((flags & EphemerisFlags.TruePosition) != 0)
            flags |= EphemerisFlags.NoAberration | EphemerisFlags.NoGravDeflection;
        // SEFLG_SPEED3 is collapsed into SEFLG_SPEED. The legacy 3-position
        // estimator is not separately implemented in this port — every raw
        // source already returns high-precision velocity unconditionally —
        // so a standalone Speed3 must be promoted to Speed before the
        // pipeline runs, otherwise CorrectionPipeline / PlanetocentricService /
        // FixedStarService (all of which check Speed only) would silently
        // drop the velocity components. Any Speed3 — alone or combined with
        // Speed — therefore normalises to Speed here.
        if ((flags & EphemerisFlags.Speed3) != 0)
            flags = (flags & ~EphemerisFlags.Speed3) | EphemerisFlags.Speed;

        // Default source = SwissEph if none specified (sweph.c#L6107).
        if ((flags & SourceMask) == 0)
            flags |= EphemerisFlags.SwissEph;

        return flags;
    }
}
