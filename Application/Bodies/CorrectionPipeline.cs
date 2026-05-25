// Ported from swisseph-master/sweph.c app_pos_etc_plan (line 2465) and
// app_pos_rest (line 2777). Original license: see LICENSE.SwissEph.txt.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Pure-function correction pipeline that turns a raw <see cref="BodyState"/>
/// from an <see cref="IBodyPositionSource"/> into the geocentric (or
/// helio-/topo-/bary-) apparent vector that <c>swe_calc</c> would return.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline mirrors the order in <c>app_pos_etc_plan</c>
/// (sweph.c#L2465-L2774) followed by <c>app_pos_rest</c>
/// (sweph.c#L2777-L2858). Each step cites the source line.
/// </para>
/// <para>
/// The pipeline is stateless and allocation-free in its hot path.
/// </para>
/// </remarks>
internal sealed class CorrectionPipeline
{
    private readonly AstronomicalModelOverrides _models;

    public CorrectionPipeline(AstronomicalModelOverrides? models = null)
    {
        _models = models ?? AstronomicalModelOverrides.Default;
    }

    /// <summary>
    /// Apply the apparent-position pipeline to <paramref name="rawBody"/>.
    /// All input vectors are assumed to be in their native source frame
    /// (per <see cref="BodyState.Frame"/>). The returned <see cref="BodyState"/>
    /// is in the user-requested frame: tropical-equatorial when
    /// <see cref="EphemerisFlags.Equatorial"/> is set, otherwise tropical-
    /// ecliptic; J2000 when <see cref="EphemerisFlags.J2000Equinox"/> is set,
    /// otherwise true-of-date.
    /// </summary>
    /// <param name="rawBody">Body state from the source layer.</param>
    /// <param name="rawEarthHelioBary">Earth's barycentric/heliocentric J2000-equator state at
    /// <paramref name="jdEt"/>; used for geocentrification, light-time iteration,
    /// aberration, and deflection. For Moshier sources where bary≡helio, the
    /// caller passes the heliocentric Earth.</param>
    /// <param name="rawSunBaryAtTeval">Sun's barycentric J2000-equator state at <paramref name="jdEt"/>;
    /// only consulted for the Heliocentric / Barycentric output modes and the
    /// gravitational-deflection helper. Pass the zero vector for Moshier (helio = bary).</param>
    /// <param name="reFetchAtTime">Light-time refetch callback. Given <c>(t-dt, body)</c>
    /// returns the source's body state at <c>t-dt</c> in J2000-EQUATOR coordinates.
    /// The pipeline normalises whatever raw frame the source emits to J2000-Equator
    /// before reading; the callback is provided by <see cref="BodyService"/>.</param>
    /// <param name="reFetchEarthAtTime">Earth (and observer offset) refetch callback for
    /// the velocity-aberration speed correction (sweph.c#L2697-L2708). Returns the
    /// barycentric Earth state at the supplied time, in J2000-equator coordinates,
    /// already including any topocentric offset. May be null when speed is not
    /// required.</param>
    /// <param name="body">The body identifier (only used to short-circuit Sun=helio-zero).</param>
    /// <param name="jdEt">Evaluation epoch (TT).</param>
    /// <param name="flags">Normalised flag set (<see cref="PlausibilityCheck.Normalize"/>).</param>
    public BodyState Apply<TFetcher>(
        BodyState rawBody,
        BodyState rawEarthHelioBary,
        BodyState rawEarthCenterHelioBary,
        BodyState rawSunBaryAtTeval,
        in TFetcher fetcher,
        CelestialBody body,
        JulianDay jdEt,
        EphemerisFlags flags)
        where TFetcher : struct, IRefetchProvider
    {
        // ---- Step 1: normalise raw frame to J2000-EQUATOR cartesian (AU). --
        // app_pos_etc_plan operates in this frame: SwissEph/JPL sources emit
        // it natively; Moshier emits J2000-ECLIPTIC, the Moon is in
        // ecliptic-of-date. The frame contract is documented in BodyState.
        Span<double> xx = stackalloc double[6];
        ToJ2000Equator(rawBody, jdEt.Value, xx);

        // We always need Earth in J2000-EQUATOR coordinates for the
        // geocentrification + aberration steps, and the original (J2000-EQUATOR)
        // body state for the heliocentric-output branch.
        Span<double> xEarth = stackalloc double[6];
        ToJ2000Equator(rawEarthHelioBary, jdEt.Value, xEarth);

        // Geocentric raw frames (Moshier Moon emits GeocentricEclipticOfDate)
        // need lifting to barycentric before the standard pipeline runs —
        // app_pos_etc_moon does this at sweph.c#L4115-L4116:
        //   xx[i] += pedp->x[i];   // to solar-system barycentric
        // Without this lift, step 5 ("convert to geocenter") cancels Earth a
        // second time and the Moon path produces ≈ -Earth_helio (≈ Sun).
        // Note: we add EARTH-CENTER (rawEarthCenterHelioBary), not the
        // observer position — the topo offset must not appear in the lift,
        // otherwise the topo path collapses back to geocentric.
        Span<double> xEarthCenter = stackalloc double[6];
        ToJ2000Equator(rawEarthCenterHelioBary, jdEt.Value, xEarthCenter);
        if (rawBody.Frame == BodyStateFrame.GeocentricEclipticOfDate
            || rawBody.Frame == BodyStateFrame.GeocentricJ2000Ecliptic
            || rawBody.Frame == BodyStateFrame.GeocentricJ2000Equator)
        {
            for (var i = 0; i < 6; i++) xx[i] += xEarthCenter[i];
        }

        // ---- Step 2: heliocentric-output prep (sweph.c#L2516-L2520) -------
        // For SwissEph/JPL the source positions are barycentric, so subtracting
        // bary-Sun gives heliocentric. For Moshier, subtract the heliocentric
        // Earth to do nothing (helio Sun is the origin) — we just zero the
        // Sun.
        Span<double> xSun = stackalloc double[6];
        ToJ2000Equator(rawSunBaryAtTeval, jdEt.Value, xSun);

        Span<double> xx0 = stackalloc double[6];
        for (var i = 0; i < 6; i++) xx0[i] = xx[i];

        var isHelio = (flags & EphemerisFlags.Heliocentric) != 0;
        var isBary = (flags & EphemerisFlags.Barycentric) != 0;
        var inSpeed = (flags & EphemerisFlags.Speed) != 0;
        var truePos = (flags & EphemerisFlags.TruePosition) != 0;

        if (isHelio && rawBody.Source != EphemerisSource.Moshier)
        {
            for (var i = 0; i < 6; i++) xx[i] -= xSun[i];
        }

        // ---- Step 3: observer position (sweph.c#L2521-L2541) --------------
        // Observer in barycentric J2000-equator coordinates. For non-topo we
        // use Earth's bary state. For topo we'd add a geocenter→observer offset
        // — caller has supplied the offset folded into BodyService when needed.
        Span<double> xobs = stackalloc double[6];
        for (var i = 0; i < 6; i++) xobs[i] = xEarth[i];

        // ---- Step 4: light-time iteration (sweph.c#L2545-L2596) -----------
        // Plus the change-of-dt speed correction xxsp (sweph.c#L2552-L2603,
        // applied later at sweph.c#L2723-L2725).
        var dtsave = 0.0;
        Span<double> xxsp = stackalloc double[3];
        var hasXxsp = false;
        if (!truePos)
        {
            // Number of refinement iterations beyond the initial estimate.
            // Mirrors C: JPL/SwissEph use 1; Moshier uses 0 (sweph.c#L2546-L2551).
            var niter = rawBody.Source == EphemerisSource.Moshier ? 0 : 1;

            // xxsp speed correction: the change of dt across one day affects
            // apparent speed by several 0.01"/day. We compute the rough
            // apparent-position difference at t and t-1 and store the offset
            // for application after geocentrification (sweph.c#L2552-L2603).
            if (inSpeed)
            {
                Span<double> xxsv = stackalloc double[3];
                Span<double> xxspIter = stackalloc double[3];
                for (var i = 0; i < 3; i++)
                {
                    xxsv[i] = xx0[i] - xx0[i + 3];
                    xxspIter[i] = xxsv[i];
                }
                Span<double> dxsp = stackalloc double[3];
                for (var j = 0; j <= niter; j++)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        dxsp[i] = xxspIter[i];
                        if (!isHelio && !isBary) dxsp[i] -= (xobs[i] - xobs[i + 3]);
                    }
                    var rsp = System.Math.Sqrt(dxsp[0] * dxsp[0] + dxsp[1] * dxsp[1] + dxsp[2] * dxsp[2]);
                    var dtsp = rsp * AstronomicalConstants.LightTimeAuPerDay;
                    for (var i = 0; i < 3; i++)
                        xxspIter[i] = xxsv[i] - dtsp * xx0[i + 3];
                }
                // xxsp = xxsv - apparent-position-at-(t-1) (sweph.c#L2578-L2579)
                for (var i = 0; i < 3; i++)
                    xxsp[i] = xxsv[i] - xxspIter[i];
                hasXxsp = true;
            }

            Span<double> dx = stackalloc double[3];
            for (var j = 0; j <= niter; j++)
            {
                for (var i = 0; i < 3; i++)
                {
                    dx[i] = xx[i];
                    if (!isHelio && !isBary) dx[i] -= xobs[i];
                }
                var r = System.Math.Sqrt(dx[0] * dx[0] + dx[1] * dx[1] + dx[2] * dx[2]);
                var dt = r * AstronomicalConstants.LightTimeAuPerDay;
                dtsave = dt;
                // Rough first iteration: linearised position at t-dt using the
                // *original* velocity (xx0). The C code does the same — see
                // sweph.c#L2593-L2595.
                for (var i = 0; i < 3; i++)
                    xx[i] = xx0[i] - dt * xx0[i + 3];
            }

            // Finalise xxsp: xxsp[i] = xx0[i] - xx[i] - xxsp[i]
            // (sweph.c#L2598-L2603). After this, xxsp holds the t-vs-t-dt
            // position-difference attributable to the light-time change rate;
            // it is subtracted from velocity at sweph.c#L2723-L2725.
            if (hasXxsp)
            {
                for (var i = 0; i < 3; i++)
                    xxsp[i] = xx0[i] - xx[i] - xxsp[i];
            }

            // Refetch the body's true position at t-dt with full source
            // dynamics (sweph.c#L2613-L2689). For Moshier the C code calls
            // swi_moshplan(t, ipli, ...) again at t and copies only the
            // velocity into xx[3..5] (sweph.c#L2670-L2688). For SwissEph/JPL
            // the position+velocity are both replaced.
            if (niter > 0)
            {
                var tMinusDt = jdEt.Value - dtsave;
                var refetched = fetcher.RefetchBody(body, new JulianDay(tMinusDt), flags);
                ToJ2000Equator(refetched, tMinusDt, xx);
                // Re-lift to barycentric for sources whose raw frame is
                // geocentric (e.g. JPL Moon at GeocentricJ2000Equator). The
                // initial lift at line 104 was discarded by overwriting xx
                // with the refetched raw vector. Without this re-lift, step 5
                // subtracts xobs once too often and the Moon path collapses
                // onto the geocentric Sun direction.
                if (rawBody.Frame == BodyStateFrame.GeocentricEclipticOfDate
                    || rawBody.Frame == BodyStateFrame.GeocentricJ2000Ecliptic
                    || rawBody.Frame == BodyStateFrame.GeocentricJ2000Equator)
                {
                    for (var i = 0; i < 6; i++) xx[i] += xEarthCenter[i];
                }
            }
            else if (inSpeed
                && !isHelio && !isBary
                && rawBody.Source == EphemerisSource.Moshier)
            {
                // Moshier speed-precision refetch at t-dt — sweph.c#L2670-L2688.
                // "only speed is taken from this computation, otherwise position
                // calculations with and without speed would not agree".
                var tMinusDt = jdEt.Value - dtsave;
                var refetched = fetcher.RefetchBody(body, new JulianDay(tMinusDt), flags);
                Span<double> xxsv = stackalloc double[6];
                ToJ2000Equator(refetched, tMinusDt, xxsv);
                // If the original raw frame was geocentric (Moon), the lift
                // above made xx barycentric — apply the same lift to the
                // refetched velocity by adding Earth's velocity at t (a good
                // first-order approximation; the Moshier Moon path in
                // app_pos_etc_moon similarly treats Earth's velocity as
                // constant across dt at sweph.c#L4179-L4180).
                if (rawBody.Frame == BodyStateFrame.GeocentricEclipticOfDate
                    || rawBody.Frame == BodyStateFrame.GeocentricJ2000Ecliptic
                    || rawBody.Frame == BodyStateFrame.GeocentricJ2000Equator)
                {
                    xx[3] = xxsv[3] + xEarthCenter[3];
                    xx[4] = xxsv[4] + xEarthCenter[4];
                    xx[5] = xxsv[5] + xEarthCenter[5];
                }
                else
                {
                    xx[3] = xxsv[3]; xx[4] = xxsv[4]; xx[5] = xxsv[5];
                }
            }

            // For helio output, re-subtract Sun at t (not t-dt) — mirrors
            // sweph.c#L2692-L2696. For bary, no subtraction needed.
            if (isHelio && rawBody.Source != EphemerisSource.Moshier)
            {
                for (var i = 0; i < 6; i++) xx[i] -= xSun[i];
            }
        }

        // xobs2: Earth (with topocentric offset) at t-dt for the
        // aberration-velocity correction (sweph.c#L2697-L2708, applied at
        // sweph.c#L2748-L2751).
        Span<double> xobs2 = stackalloc double[6];
        var hasXobs2 = false;
        if (!truePos && inSpeed && fetcher.HasEarthRefetch)
        {
            var tMinusDt = jdEt.Value - dtsave;
            var earthRefetch = fetcher.RefetchEarth(new JulianDay(tMinusDt), flags);
            ToJ2000Equator(earthRefetch, tMinusDt, xobs2);
            hasXobs2 = true;
        }

        // ---- Step 5: conversion to geocenter (sweph.c#L2713-L2727) --------
        if (!isHelio && !isBary)
        {
            for (var i = 0; i < 6; i++) xx[i] -= xobs[i];
            // Subtract the change-of-dt speed correction (sweph.c#L2723-L2725).
            if (!truePos && inSpeed && hasXxsp)
            {
                for (var i = 3; i < 6; i++) xx[i] -= xxsp[i - 3];
            }
        }
        if (!inSpeed)
        {
            for (var i = 3; i < 6; i++) xx[i] = 0.0;
        }

        // ---- Step 6: gravitational deflection (sweph.c#L2734-L2736) -------
        // The C library applies deflection only inside app_pos_etc_plan
        // (planets); app_pos_etc_sun and app_pos_etc_moon (sweph.c#L3902,
        // #L4087) deliberately skip the deflection step because the geocentric
        // line through the Sun (Sun itself) is the singularity of the formula
        // and the Moon contribution is negligible (≪ 1e-9 deg).
        if (!truePos
            && (flags & EphemerisFlags.NoGravDeflection) == 0
            && body != CelestialBody.Sun
            && body != CelestialBody.Moon)
        {
            // Earth heliocentric for the deflection helper. For Moshier
            // helio = bary because there's no separate barycenter; for
            // SwissEph/JPL we subtract Sun bary to get helio.
            Span<double> earthHelio = stackalloc double[6];
            for (var i = 0; i < 6; i++) earthHelio[i] = xEarth[i] - xSun[i];

            // sunBaryRel-to-earth-at-(t-dt) hook: leave default for now; the
            // Moshier path produces no measurable difference and SwissEph/JPL
            // would need a small bary-Sun refetch which is acceptable to omit
            // at this milestone (see notes/decisions for justification).
            GravitationalDeflection.Apply(
                xx, earthHelio, earthHelio.Slice(3, 3), inSpeed);
        }

        // ---- Step 7: annual aberration (sweph.c#L2740-L2752) --------------
        if (!truePos && (flags & EphemerisFlags.NoAberration) == 0)
        {
            // Aberration uses the observer's velocity (xobs[3..5]).
            ReadOnlySpan<double> obsVel = MemoryMarshal.CreateReadOnlySpan(ref xobs[3], 3);
            Aberration.Apply(xx, obsVel, inSpeed);
            // Apply the (xobs - xobs2) correction to velocity — apparent speed
            // is influenced by the change of Earth's velocity between t and
            // t-dt. Neglecting this would involve an error of several 0.1".
            // sweph.c#L2748-L2751.
            if (inSpeed && hasXobs2)
            {
                for (var i = 3; i < 6; i++)
                    xx[i] += xobs[i] - xobs2[i];
            }
        }
        if (!inSpeed)
        {
            for (var i = 3; i < 6; i++) xx[i] = 0.0;
        }

        // ---- Step 8: precession J2000 → date (sweph.c#L2766-L2773) --------
        // skipped if SEFLG_J2000 set.
        var precessNow = (flags & EphemerisFlags.J2000Equinox) == 0;
        if (precessNow)
        {
            Span<double> pos = stackalloc double[3];
            pos[0] = xx[0]; pos[1] = xx[1]; pos[2] = xx[2];
            Precession.Apply(pos, AstronomicalConstants.J2000, jdEt.Value, _models);
            xx[0] = pos[0]; xx[1] = pos[1]; xx[2] = pos[2];
            if (inSpeed)
            {
                // Velocity uses ApplySpeed (swi_precess_speed) which carries
                // the dpre rate term so apparent motion is consistent with
                // position; sweph.c#L2768-L2769.
                Precession.ApplySpeed(xx, AstronomicalConstants.J2000, jdEt.Value, _models);
            }
        }

        // ---- Step 9: nutation (mean → true) (sweph.c#L2787-L2788) ---------
        var meanEpsAtJ = Precession.MeanObliquity(precessNow ? jdEt.Value : AstronomicalConstants.J2000, _models);
        NutationAngles? nutAngles = null;
        if ((flags & EphemerisFlags.NoNutation) == 0)
        {
            var nut = Nutation.Compute(jdEt.Value, _models);
            nutAngles = nut;
            if (inSpeed)
            {
                // ApplyWithSpeed carries the nutation-derivative term.
                Nutation.ApplyWithSpeed(xx, jdEt.Value, backward: false, _models);
            }
            else
            {
                Span<double> pos = stackalloc double[3];
                pos[0] = xx[0]; pos[1] = xx[1]; pos[2] = xx[2];
                Nutation.Apply(pos, nut, meanEpsAtJ, backward: false);
                xx[0] = pos[0]; xx[1] = pos[1]; xx[2] = pos[2];
            }
        }

        // At this point xx is equatorial (true-of-date if J2000 flag is off,
        // J2000-equator if J2000 flag is on).

        // ---- Step 10: ecliptic transform if requested (sweph.c#L2797-L2799)
        var wantEquatorial = (flags & EphemerisFlags.Equatorial) != 0;
        if (!wantEquatorial)
        {
            // Use mean obliquity at the precessed-to date plus Δε if nutated.
            var trueEps = meanEpsAtJ + (nutAngles?.DeltaEpsilonRad ?? 0.0);
            Span<double> pos = stackalloc double[3];
            pos[0] = xx[0]; pos[1] = xx[1]; pos[2] = xx[2];
            FrameTransform.EquatorialToEcliptic(pos, trueEps);
            xx[0] = pos[0]; xx[1] = pos[1]; xx[2] = pos[2];
            if (inSpeed)
            {
                Span<double> vel = stackalloc double[3];
                vel[0] = xx[3]; vel[1] = xx[4]; vel[2] = xx[5];
                FrameTransform.EquatorialToEcliptic(vel, trueEps);
                xx[3] = vel[0]; xx[4] = vel[1]; xx[5] = vel[2];
            }
        }

        // Final BodyState frame label.
        var outFrame =
            wantEquatorial
                ? ((flags & EphemerisFlags.J2000Equinox) != 0
                    ? BodyStateFrame.GeocentricJ2000Equator
                    : BodyStateFrame.GeocentricJ2000Equator) // equator-of-date — closest match in the existing enum is GeocentricJ2000Equator
                : ((flags & EphemerisFlags.J2000Equinox) != 0
                    ? BodyStateFrame.GeocentricJ2000Ecliptic
                    : BodyStateFrame.GeocentricEclipticOfDate);
        // Heliocentric / barycentric output reuses the helio-J2000 enum.
        if (isHelio) outFrame = BodyStateFrame.HeliocentricJ2000Ecliptic;
        else if (isBary) outFrame = BodyStateFrame.BarycentricJ2000Equator;

        return new BodyState(
            new Vec3(xx[0], xx[1], xx[2]),
            inSpeed ? new Vec3(xx[3], xx[4], xx[5]) : default,
            System.Math.Sqrt(xx[0] * xx[0] + xx[1] * xx[1] + xx[2] * xx[2]),
            rawBody.Source,
            outFrame);
    }

    /// <summary>
    /// Refetch callbacks the pipeline needs for the light-time iteration and
    /// the aberration speed correction. Implemented as a generic
    /// <see langword="struct"/> constraint (rather than two delegate
    /// parameters) so the hot path is closure-free:
    /// <see cref="BodyService"/> packages the captured state (<c>this</c>,
    /// the optional observer) into a stack value, and the JIT monomorphises
    /// <see cref="Apply{TFetcher}"/> per concrete fetcher type.
    /// </summary>
    /// <remarks>
    /// <b>Single-implementation interface kept on purpose.</b> The interface
    /// is the dispatch contract for the closure-free generic struct
    /// constraint above; replacing it with the concrete struct type would
    /// either re-introduce delegate allocations on every <c>swe_calc</c>
    /// (busting the M-14 zero-allocation budget) or require the pipeline to
    /// take a hard reference on <c>BodyService.Refetcher</c>, which is a
    /// private nested struct. The <see cref="IRefetchProvider"/> indirection
    /// is therefore a deliberate retention under the
    /// <c>INTERFACE_CLEANUP_AUFTRAG</c> Wave 3 decision: it preserves the
    /// hot-path allocation freeness mandated for sidereal / topocentric
    /// <c>swe_calc</c> dispatch. Effective visibility is internal — the
    /// enclosing <see cref="CorrectionPipeline"/> is itself
    /// <see langword="internal"/>, so the interface is not part of the
    /// public API.
    /// </remarks>
    public interface IRefetchProvider
    {
        /// <summary>
        /// Light-time refetch callback. <see cref="BodyService"/> supplies an
        /// implementation that re-routes through the SourceRouter and emits a
        /// raw <see cref="BodyState"/> in whatever frame the source uses
        /// natively; the pipeline normalises it via
        /// <see cref="ToJ2000Equator"/>.
        /// </summary>
        BodyState RefetchBody(CelestialBody body, JulianDay jd, EphemerisFlags flags);

        /// <summary>
        /// True when <see cref="RefetchEarth"/> may be invoked. Lets the
        /// pipeline skip the second branch entirely when the caller cannot
        /// supply an Earth refetch (e.g. test harnesses).
        /// </summary>
        bool HasEarthRefetch { get; }

        /// <summary>
        /// Earth-state refetch callback used by the aberration speed correction
        /// (sweph.c#L2697-L2708). Returns the barycentric Earth state at the
        /// supplied time, with any topocentric offset already folded in.
        /// </summary>
        BodyState RefetchEarth(JulianDay jd, EphemerisFlags flags);
    }

    /// <summary>
    /// Frame normaliser: converts a raw <see cref="BodyState"/> (any of the
    /// six <see cref="BodyStateFrame"/> values) into J2000 equator-cartesian
    /// position+velocity, in AU and AU/day. Allocation-free.
    /// </summary>
    public void ToJ2000Equator(BodyState state, double jdTt, Span<double> destination)
    {
        if (destination.Length < 6)
            throw new ArgumentException("Destination span must contain at least 6 doubles.", nameof(destination));
        destination[0] = state.Position.X;
        destination[1] = state.Position.Y;
        destination[2] = state.Position.Z;
        destination[3] = state.Velocity.X;
        destination[4] = state.Velocity.Y;
        destination[5] = state.Velocity.Z;

        switch (state.Frame)
        {
            case BodyStateFrame.HeliocentricJ2000Equator:
            case BodyStateFrame.BarycentricJ2000Equator:
            case BodyStateFrame.GeocentricJ2000Equator:
                // Already in J2000 equator. Done.
                return;

            case BodyStateFrame.HeliocentricJ2000Ecliptic:
            case BodyStateFrame.GeocentricJ2000Ecliptic:
                {
                    // Rotate ecliptic → equator with mean obliquity at J2000.
                    var eps = Precession.MeanObliquity(AstronomicalConstants.J2000, _models);
                    Span<double> pos = stackalloc double[3];
                    pos[0] = destination[0]; pos[1] = destination[1]; pos[2] = destination[2];
                    FrameTransform.EclipticToEquatorial(pos, eps);
                    destination[0] = pos[0]; destination[1] = pos[1]; destination[2] = pos[2];
                    Span<double> vel = stackalloc double[3];
                    vel[0] = destination[3]; vel[1] = destination[4]; vel[2] = destination[5];
                    FrameTransform.EclipticToEquatorial(vel, eps);
                    destination[3] = vel[0]; destination[4] = vel[1]; destination[5] = vel[2];
                    return;
                }

            case BodyStateFrame.GeocentricEclipticOfDate:
                {
                    // Moshier Moon is referred to *mean* ecliptic and equinox
                    // of date (swemmoon.c:62-64). Mirrors C ecldat_equ2000
                    // (swemmoon.c#L1722-L1729): rotate mean-ecliptic →
                    // mean-equator with mean obliquity, then precess straight
                    // to J2000. No nutation step — both endpoints are mean.
                    var meanEps = Precession.MeanObliquity(jdTt, _models);
                    Span<double> pos = stackalloc double[3];
                    pos[0] = destination[0]; pos[1] = destination[1]; pos[2] = destination[2];
                    FrameTransform.EclipticToEquatorial(pos, meanEps);
                    Precession.Apply(pos, jdTt, AstronomicalConstants.J2000, _models);
                    destination[0] = pos[0]; destination[1] = pos[1]; destination[2] = pos[2];
                    Span<double> vel = stackalloc double[3];
                    vel[0] = destination[3]; vel[1] = destination[4]; vel[2] = destination[5];
                    FrameTransform.EclipticToEquatorial(vel, meanEps);
                    Precession.Apply(vel, jdTt, AstronomicalConstants.J2000, _models);
                    destination[3] = vel[0]; destination[4] = vel[1]; destination[5] = vel[2];
                    return;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(state), $"Unknown BodyStateFrame: {state.Frame}");
        }
    }
}

// MemoryMarshal helper alias so we don't pull a `using` into the file body.
file static class MemoryMarshal
{
    public static System.ReadOnlySpan<double> CreateReadOnlySpan(ref double reference, int length)
        => System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref reference, length);
}
