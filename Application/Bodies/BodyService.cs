// Ported from swisseph-master/sweph.c swe_calc / swe_calc_ut entry points.
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.IO;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// High-level body-position service. Mirrors the C API entry points
/// <c>swe_calc</c> / <c>swe_calc_ut</c> (sweph.c#L309 and #L565) but in a
/// stateless, per-call form: no <c>swe_set_*</c> mutators, observer + flags
/// are passed every time. Composes the <see cref="SourceRouter"/> and
/// <see cref="CorrectionPipeline"/> internally.
/// </summary>
public sealed class BodyService
{
    private readonly SourceRouter _router;
    private readonly CorrectionPipeline _pipeline;
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides _models;
    private readonly SiderealService? _sidereal;

    internal BodyService(
        SourceRouter router,
        CalendarService calendar,
        AstronomicalModelOverrides? models = null,
        SiderealService? sidereal = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _models = models ?? AstronomicalModelOverrides.Default;
        _pipeline = new CorrectionPipeline(_models);
        _sidereal = sidereal;
    }

    /// <summary>
    /// Computes body position (and, when <see cref="EphemerisFlags.Speed"/>
    /// is set, velocity) at <paramref name="jdEt"/> Terrestrial Time.
    /// Mirrors <c>swe_calc</c>.
    /// </summary>
    /// <param name="body">The celestial body.</param>
    /// <param name="jdEt">Julian Day in TT.</param>
    /// <param name="flags">Frame / source / option flags.</param>
    /// <param name="observer">
    /// Required when <see cref="EphemerisFlags.Topocentric"/> is set;
    /// otherwise ignored. There is no global observer state.
    /// </param>
    public BodyState Compute(CelestialBody body, JulianDay jdEt, EphemerisFlags flags, ObserverLocation? observer = null)
    {
        var normalised = PlausibilityCheck.Normalize(flags, observer.HasValue);

        // Sun has neither heliocentric nor geocentric meaning; return zero
        // for the helio case (sweph.c#L830-L834).
        if (body == CelestialBody.Sun && (normalised & EphemerisFlags.Heliocentric) != 0)
        {
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.HeliocentricJ2000Ecliptic);
        }
        // Geocentric Earth is the zero vector in geocentric output too (sweph.c#L839-L843).
        if (body == CelestialBody.Earth
            && (normalised & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric)) == 0)
        {
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.GeocentricJ2000Ecliptic);
        }

        // Lunar node / apogee branches (sweph.c#L859-L967). The C library
        // dispatches these out of the planet pipeline because they are
        // derived geocentric quantities. The Moshier source ports both the
        // osculating-element engine (TrueNode / OsculatingApogee, via
        // ComputeLunarOsculatingPoint) and the mean-element engine
        // (MeanNode / MeanApogee, served by IBodyPositionSource directly).
        if (body == CelestialBody.TrueNode || body == CelestialBody.OsculatingApogee)
        {
            return ComputeLunarOsculatingPoint(body, jdEt, normalised);
        }
        if (body == CelestialBody.MeanNode || body == CelestialBody.MeanApogee)
        {
            return ComputeMeanLunarPoint(body, jdEt, normalised);
        }
        if (body == CelestialBody.InterpolatedApogee || body == CelestialBody.InterpolatedPerigee)
        {
            return ComputeInterpolatedLunarApside(body, jdEt, normalised);
        }

        // Fetch raw body state with one source. SwissEph CanProvide=true but
        // the actual file may be missing on disk; in that case the source
        // throws FileNotFoundException and we fall back to Moshier
        // (mirroring main_planet's NOT_AVAILABLE → moshier_planet path,
        // sweph.c#L1626-L1635).
        var (chosenSource, resolvedFlags) = _router.Resolve(normalised, body, jdEt);
        BodyState rawBody;
        try
        {
            rawBody = chosenSource.Compute(body, jdEt, resolvedFlags);
        }
        catch (FileNotFoundException) when (chosenSource.Kind == EphemerisSource.SwissEph && _router.Has(EphemerisSource.Moshier))
        {
            resolvedFlags = (resolvedFlags & ~EphemerisFlags.SwissEph) | EphemerisFlags.MoshierEph;
            (chosenSource, resolvedFlags) = _router.Resolve(resolvedFlags, body, jdEt);
            rawBody = chosenSource.Compute(body, jdEt, resolvedFlags);
        }

        // Earth & Sun bary states for the pipeline. These follow exactly the
        // same source/fallback chain as the body itself.
        var rawEarthCenter = ResolveAndCompute(CelestialBody.Earth, jdEt, resolvedFlags);
        var rawSun = ResolveSunState(jdEt, resolvedFlags, rawEarthCenter);

        // Topocentric: fold the geographic offset into "earth" so the
        // pipeline's xobs already includes it. This mirrors sweph.c#L2536:
        // xobs[i] = xobs[i] + pedp->x[i]. The unmodified Earth-center is kept
        // separate so the pipeline can lift geocentric raw frames (Moshier
        // Moon) to barycentric without double-counting the topo offset.
        var rawObserver = rawEarthCenter;
        if ((resolvedFlags & EphemerisFlags.Topocentric) != 0)
        {
            // observer is guaranteed non-null by PlausibilityCheck.
            var topoOffset = ComputeTopocentricOffsetJ2000Equator(jdEt, observer!.Value);
            rawObserver = AddVec3(rawEarthCenter, topoOffset);
        }

        var fetcher = new Refetcher(this, observer);
        var apparent = _pipeline.Apply(rawBody, rawObserver, rawEarthCenter, rawSun, in fetcher, body, jdEt, resolvedFlags);

        // ---- Sidereal step (sweph.c#L2811-L2837) -------------------------
        // Mirrors the "traditional algorithm" branch of app_pos_rest:
        // convert tropical ecliptic-of-date longitude/lat to polar, subtract
        // ayanamsha (and its time-derivative on speed), convert back to
        // cartesian. Only fires for the non-J2000, non-equatorial,
        // non-helio/bary geocentric ecliptic-of-date branch — the only one
        // where SEFLG_SIDEREAL produces a meaningful sidereal longitude in
        // the reference C library (the other projection variants are SSY /
        // ECL_T0 paths kept for M-XX completeness).
        if ((resolvedFlags & EphemerisFlags.Sidereal) != 0
            && (resolvedFlags & EphemerisFlags.Equatorial) == 0
            && (resolvedFlags & EphemerisFlags.J2000Equinox) == 0
            && (resolvedFlags & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric)) == 0)
        {
            apparent = ApplySidereal(apparent, jdEt, resolvedFlags);
        }

        return apparent;
    }

    /// <summary>
    /// Closure-free <see cref="CorrectionPipeline.IRefetchProvider"/>: the
    /// captured state (<see cref="BodyService"/> instance and optional
    /// observer) lives on the stack as a readonly struct, so the hot path
    /// allocates no display class / delegate objects per <see cref="Compute"/>
    /// call. The JIT monomorphises <see cref="CorrectionPipeline.Apply{T}"/>
    /// on this concrete type and inlines the two callbacks back into the
    /// pipeline.
    /// </summary>
    private readonly struct Refetcher : CorrectionPipeline.IRefetchProvider
    {
        private readonly BodyService _service;
        private readonly ObserverLocation? _observer;

        public Refetcher(BodyService service, ObserverLocation? observer)
        {
            _service = service;
            _observer = observer;
        }

        public BodyState RefetchBody(CelestialBody body, JulianDay jd, EphemerisFlags flags)
        {
            var (source, resolvedFlags) = _service._router.Resolve(flags, body, jd);
            return source.Compute(body, jd, resolvedFlags);
        }

        public bool HasEarthRefetch => true;

        public BodyState RefetchEarth(JulianDay jd, EphemerisFlags flags)
        {
            var earthAtT = _service.ResolveAndCompute(CelestialBody.Earth, jd, flags);
            if ((flags & EphemerisFlags.Topocentric) != 0 && _observer.HasValue)
            {
                var topo = _service.ComputeTopocentricOffsetJ2000Equator(jd, _observer.Value);
                earthAtT = AddVec3(earthAtT, topo);
            }
            return earthAtT;
        }
    }

    /// <summary>
    /// Computes body position from a UT (UT1) Julian Day. Mirrors
    /// <c>swe_calc_ut</c>: applies ΔT and dispatches to <see cref="Compute"/>.
    /// </summary>
    public BodyState ComputeUt(CelestialBody body, JulianDay jdUt, EphemerisFlags flags, ObserverLocation? observer = null)
    {
        var dt = _calendar.DeltaT(jdUt);
        return Compute(body, new JulianDay(jdUt.Value + dt), flags, observer);
    }

    private BodyState ResolveAndCompute(CelestialBody body, JulianDay jdEt, EphemerisFlags flags)
    {
        var (source, f) = _router.Resolve(flags, body, jdEt);
        return source.Compute(body, jdEt, f);
    }

    /// <summary>
    /// Compute the osculating ascending node (SE_TRUE_NODE) or osculating
    /// apogee (SE_OSCU_APOG) of the Moon. Mirrors the body-specific branches
    /// of <c>swe_calc</c> at sweph.c#L931-L967 followed by
    /// <c>lunar_osc_elem</c> (sweph.c#L5168). Routes through the configured
    /// <see cref="SourceRouter"/>: SwissEph and JPL Moon sources are honoured
    /// when available, with the same SwissEph→Moshier file-not-found fallback
    /// the planet pipeline uses (sweph.c#L5302-L5331).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The orbital-element math runs in geocentric ecliptic-of-date
    /// cartesian. The Moshier moon source emits the mean ecliptic natively;
    /// the SwissEph and JPL moon sources emit geocentric J2000-equator
    /// cartesian, which <see cref="MoonToEclipticOfDate"/> rotates into
    /// ecliptic-of-date via ICRS bias, J2000 → date precession and a
    /// mean-obliquity equator → ecliptic rotation — mirroring
    /// <c>swi_plan_for_osc_elem</c> (sweph.c#L5758-L5856). Unless
    /// <see cref="EphemerisFlags.NoNutation"/> is set, each lunar sample is
    /// additionally nutated into the true ecliptic-and-equinox of its epoch,
    /// exactly like the C original, so the resulting node honours nutation
    /// (the element math then needs no further correction, per the comment
    /// at sweph.c#L5468-L5472). Other apparent-position corrections
    /// (aberration, light-time, gravitational deflection) are not layered
    /// on top, matching the geometric semantics of
    /// <see cref="Phenomena.NodesAndApsidesService"/> and the
    /// SEFLG_TRUEPOS golden tests. Heliocentric and barycentric
    /// request bits short-circuit to zero (sweph.c#L860-L864).
    /// </para>
    /// </remarks>
    private BodyState ComputeLunarOsculatingPoint(CelestialBody body, JulianDay jdEt, EphemerisFlags normalisedFlags)
    {
        // Heliocentric / barycentric not allowed (sweph.c#L860-L864).
        if ((normalisedFlags & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric)) != 0)
        {
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
        }

        // Source dispatch. Mirrors the lunar_osc_elem switch at
        // sweph.c#L5252-L5354: honour the requested source bit, fall through
        // to Moshier when SwissEph .se1 is missing or out of range.
        var (source, resolvedFlags) = ResolveMoonSource(normalisedFlags, jdEt);

        var includeSpeed = (normalisedFlags & (EphemerisFlags.Speed | EphemerisFlags.Speed3)) != 0;
        var withNutation = (normalisedFlags & EphemerisFlags.NoNutation) == 0;
        var step = LunarOsculatingElements.StepDaysFor(source.Kind);

        // Fetch moon (pos+vel) at three epochs and rotate each into
        // geocentric ecliptic-of-date (true ecliptic unless NoNutation is
        // set) for the orbital-element math.
        var centerSample = FetchMoonInEclipticOfDate(source, jdEt, resolvedFlags, withNutation);
        (Vec3, Vec3) minusSample = default, plusSample = default;
        if (includeSpeed)
        {
            minusSample = FetchMoonInEclipticOfDate(source, new JulianDay(jdEt.Value - step), resolvedFlags, withNutation);
            plusSample = FetchMoonInEclipticOfDate(source, new JulianDay(jdEt.Value + step), resolvedFlags, withNutation);
        }

        var result = LunarOsculatingElements.Compute(
            minusSample,
            plusSample,
            centerSample,
            step,
            includeSpeed);

        var (pos, vel) = body == CelestialBody.TrueNode
            ? (result.NodePosition, result.NodeVelocity)
            : (result.ApogeePosition, result.ApogeeVelocity);

        return new BodyState(
            pos,
            vel,
            pos.Length,
            source.Kind,
            BodyStateFrame.GeocentricEclipticOfDate);
    }

    /// <summary>
    /// Resolve the moon source for the lunar osculating-elements path.
    /// Honours the source bit in <paramref name="flags"/>; on a SwissEph
    /// <see cref="FileNotFoundException"/> at the requested JD, falls back
    /// to Moshier — same pattern as <see cref="Compute"/>'s catch block.
    /// </summary>
    private (IBodyPositionSource Source, EphemerisFlags Flags) ResolveMoonSource(EphemerisFlags flags, JulianDay jdEt)
    {
        var (source, resolvedFlags) = _router.Resolve(flags, CelestialBody.Moon, jdEt);
        if (source.Kind == EphemerisSource.SwissEph && _router.Has(EphemerisSource.Moshier))
        {
            try
            {
                source.Compute(CelestialBody.Moon, jdEt, resolvedFlags | EphemerisFlags.Speed);
            }
            catch (FileNotFoundException)
            {
                resolvedFlags = (resolvedFlags & ~EphemerisFlags.SwissEph) | EphemerisFlags.MoshierEph;
                (source, resolvedFlags) = _router.Resolve(resolvedFlags, CelestialBody.Moon, jdEt);
            }
        }
        return (source, resolvedFlags);
    }

    /// <summary>
    /// Fetch the moon (with speed) from <paramref name="source"/> and
    /// rotate the cartesian state into geocentric ecliptic-of-date
    /// — the frame the <c>lunar_osc_elem</c> math expects.
    /// </summary>
    private (Vec3 Position, Vec3 Velocity) FetchMoonInEclipticOfDate(
        IBodyPositionSource source, JulianDay jd, EphemerisFlags resolvedFlags, bool withNutation)
    {
        var moon = source.Compute(CelestialBody.Moon, jd, resolvedFlags | EphemerisFlags.Speed);
        return MoonToEclipticOfDate(moon, jd.Value, withNutation);
    }

    /// <summary>
    /// Rotate a moon <see cref="BodyState"/> into geocentric
    /// ecliptic-of-date cartesian. Mirrors <c>swi_plan_for_osc_elem</c>
    /// (sweph.c#L5758-L5856): ICRS → J2000 frame bias, then J2000 → date
    /// precession (rotation only — the daily precession rate is not added
    /// to velocity per the C comment at sweph.c#L5772-L5773), then, when
    /// <paramref name="withNutation"/> is set, the nutation matrix of the
    /// sample epoch (rotation only on the speed vector too), then
    /// equator-of-date → ecliptic-of-date with mean obliquity, and finally
    /// the Δε rotation into the true ecliptic. The Moshier moon source
    /// already emits the mean ecliptic-of-date (it bypasses the
    /// J2000-equator round trip the C library runs in
    /// <c>ecldat_equ2000</c>), so that branch only needs the
    /// mean → true ecliptic lift when nutation is requested.
    /// </summary>
    private (Vec3 Position, Vec3 Velocity) MoonToEclipticOfDate(BodyState moon, double jdTt, bool withNutation)
    {
        if (moon.Frame == BodyStateFrame.GeocentricEclipticOfDate)
        {
            if (!withNutation)
            {
                return (moon.Position, moon.Velocity);
            }
            // Undoing the mean-obliquity rotation, applying the nutation
            // steps on the equator and re-running the ecliptic transform is
            // algebraically the C chain, which inserts the nutation matrix
            // between precession and the ecliptic rotation.
            Span<double> mpos = stackalloc double[3] { moon.Position.X, moon.Position.Y, moon.Position.Z };
            Span<double> mvel = stackalloc double[3] { moon.Velocity.X, moon.Velocity.Y, moon.Velocity.Z };
            var eps = Precession.MeanObliquity(jdTt, _models);
            FrameTransform.EclipticToEquatorial(mpos, eps);
            FrameTransform.EclipticToEquatorial(mvel, eps);
            NutateEquatorOfDateSample(mpos, mvel, jdTt, eps);
            return (new Vec3(mpos[0], mpos[1], mpos[2]), new Vec3(mvel[0], mvel[1], mvel[2]));
        }
        if (moon.Frame == BodyStateFrame.GeocentricJ2000Equator)
        {
            Span<double> state = stackalloc double[6];
            state[0] = moon.Position.X; state[1] = moon.Position.Y; state[2] = moon.Position.Z;
            state[3] = moon.Velocity.X; state[4] = moon.Velocity.Y; state[5] = moon.Velocity.Z;
            CatalogFrameTransforms.IcrsBias(state, includeSpeed: true, backward: false, _models.FrameBias);
            Span<double> pos = stackalloc double[3] { state[0], state[1], state[2] };
            Span<double> vel = stackalloc double[3] { state[3], state[4], state[5] };
            Precession.Apply(pos, AstronomicalConstants.J2000, jdTt, _models);
            Precession.Apply(vel, AstronomicalConstants.J2000, jdTt, _models);
            var meanEps = Precession.MeanObliquity(jdTt, _models);
            if (withNutation)
            {
                NutateEquatorOfDateSample(pos, vel, jdTt, meanEps);
            }
            else
            {
                FrameTransform.EquatorialToEcliptic(pos, meanEps);
                FrameTransform.EquatorialToEcliptic(vel, meanEps);
            }
            return (new Vec3(pos[0], pos[1], pos[2]), new Vec3(vel[0], vel[1], vel[2]));
        }
        throw new InvalidOperationException(
            $"ComputeLunarOsculatingPoint: moon source returned unsupported frame {moon.Frame}.");
    }

    /// <summary>
    /// The nutation tail of <c>swi_plan_for_osc_elem</c>
    /// (sweph.c#L5806-L5852) applied to a mean-equator-of-date sample:
    /// nutation matrix of the sample epoch on position and velocity
    /// (rotation only — the nutation rate is not added to the speed
    /// vector), mean-obliquity equator → ecliptic, then the Δε rotation
    /// into the true ecliptic-and-equinox of date.
    /// </summary>
    private void NutateEquatorOfDateSample(Span<double> pos, Span<double> vel, double jdTt, double meanEps)
    {
        var nut = Nutation.Compute(jdTt, _models);
        Nutation.Apply(pos, nut, meanEps);
        Nutation.Apply(vel, nut, meanEps);
        FrameTransform.EquatorialToEcliptic(pos, meanEps);
        FrameTransform.EquatorialToEcliptic(vel, meanEps);
        FrameTransform.EquatorialToEcliptic(pos, nut.DeltaEpsilonRad);
        FrameTransform.EquatorialToEcliptic(vel, nut.DeltaEpsilonRad);
    }

    /// <summary>
    /// Compute the mean lunar node (SE_MEAN_NODE) or mean lunar apogee
    /// (SE_MEAN_APOG, "Lilith" / Black Moon). Mirrors the body-specific
    /// branches at sweph.c#L859-L927: a Moshier-only path that bypasses the
    /// planet pipeline, returning geocentric ecliptic-of-date cartesian
    /// (true ecliptic unless <see cref="EphemerisFlags.NoNutation"/> is
    /// set, per the nutation step of <c>app_pos_rest</c>).
    /// Heliocentric / barycentric request bits short-circuit to zero (no
    /// helio/bary meaning, sweph.c#L860-L864 / #L899-L903). The polar →
    /// cartesian conversion and one-sided finite-difference speed are owned
    /// by <see cref="MoshierBodyPositionSource"/>; this method only enforces
    /// the helio/bary gate, forces a Moshier source and layers the
    /// nutation step on top.
    /// </summary>
    private BodyState ComputeMeanLunarPoint(CelestialBody body, JulianDay jdEt, EphemerisFlags normalisedFlags)
    {
        if ((normalisedFlags & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric)) != 0)
        {
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
        }

        if (!_router.Has(EphemerisSource.Moshier))
        {
            throw new UnsupportedBodyException(body, EphemerisSource.SwissEph);
        }
        var (source, _) = _router.Resolve(EphemerisFlags.MoshierEph, body, jdEt);
        if (source.Kind != EphemerisSource.Moshier)
        {
            throw new UnsupportedBodyException(body, source.Kind);
        }

        var sourceFlags = EphemerisFlags.MoshierEph
            | (normalisedFlags & (EphemerisFlags.Speed | EphemerisFlags.Speed3));
        var state = source.Compute(body, jdEt, sourceFlags);
        return NutateMeanEclipticPoint(state, jdEt, normalisedFlags);
    }

    /// <summary>
    /// Compute the interpolated lunar apogee (SE_INTP_APOG) or perigee
    /// (SE_INTP_PERG). Mirrors the body-specific branches at sweph.c#L971-L1015:
    /// a Moshier-only path that bypasses the planet pipeline, returning
    /// geocentric ecliptic-of-date cartesian (true ecliptic unless
    /// <see cref="EphemerisFlags.NoNutation"/> is set, per the nutation
    /// step of <c>app_pos_rest</c>). Helio/bary request bits
    /// short-circuit to zero (no helio/bary meaning, sweph.c#L972-L976).
    /// The Newton search and the central-difference 0.1-day speed step are
    /// owned by <see cref="IBodyPositionSource"/>; this method only enforces
    /// the helio/bary gate, forces a Moshier source and layers the
    /// nutation step on top.
    /// </summary>
    private BodyState ComputeInterpolatedLunarApside(CelestialBody body, JulianDay jdEt, EphemerisFlags normalisedFlags)
    {
        if ((normalisedFlags & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric)) != 0)
        {
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
        }

        if (!_router.Has(EphemerisSource.Moshier))
        {
            throw new UnsupportedBodyException(body, EphemerisSource.SwissEph);
        }
        var (source, _) = _router.Resolve(EphemerisFlags.MoshierEph, body, jdEt);
        if (source.Kind != EphemerisSource.Moshier)
        {
            throw new UnsupportedBodyException(body, source.Kind);
        }

        var sourceFlags = EphemerisFlags.MoshierEph
            | (normalisedFlags & (EphemerisFlags.Speed | EphemerisFlags.Speed3));
        var state = source.Compute(body, jdEt, sourceFlags);
        return NutateMeanEclipticPoint(state, jdEt, normalisedFlags);
    }

    /// <summary>
    /// Lifts an analytically derived lunar point from the mean to the true
    /// ecliptic-and-equinox of date — the nutation step of
    /// <c>app_pos_rest</c> (sweph.c#L2787-L2804) as it applies to the
    /// mean-node / mean-apogee / interpolated-apsides outputs, which arrive
    /// here in geocentric mean ecliptic-of-date cartesian (the C library
    /// rotates them onto the mean equator in <c>app_pos_etc_mean</c> first,
    /// sweph.c#L4330-L4332): rotate to the mean equator, run
    /// <c>swi_nutate</c> (nutation matrix; with speed also the
    /// nutation-rate term), rotate back with mean obliquity and finally by
    /// Δε. No-op when <see cref="EphemerisFlags.NoNutation"/> is set.
    /// </summary>
    private BodyState NutateMeanEclipticPoint(BodyState state, JulianDay jdEt, EphemerisFlags normalisedFlags)
    {
        if ((normalisedFlags & EphemerisFlags.NoNutation) != 0)
        {
            return state;
        }

        var includeSpeed = (normalisedFlags & (EphemerisFlags.Speed | EphemerisFlags.Speed3)) != 0;
        Span<double> s = stackalloc double[6];
        s[0] = state.Position.X; s[1] = state.Position.Y; s[2] = state.Position.Z;
        s[3] = state.Velocity.X; s[4] = state.Velocity.Y; s[5] = state.Velocity.Z;
        var pos = s.Slice(0, 3);
        var vel = s.Slice(3, 3);

        var meanEps = Precession.MeanObliquity(jdEt.Value, _models);
        var nut = Nutation.Compute(jdEt.Value, _models);
        FrameTransform.EclipticToEquatorial(pos, meanEps);
        FrameTransform.EclipticToEquatorial(vel, meanEps);
        if (includeSpeed)
        {
            Nutation.ApplyWithSpeed(s, jdEt.Value, backward: false, _models);
        }
        else
        {
            Nutation.Apply(pos, nut, meanEps);
        }
        FrameTransform.EquatorialToEcliptic(pos, meanEps);
        FrameTransform.EquatorialToEcliptic(vel, meanEps);
        FrameTransform.EquatorialToEcliptic(pos, nut.DeltaEpsilonRad);
        FrameTransform.EquatorialToEcliptic(vel, nut.DeltaEpsilonRad);

        return new BodyState(
            new Vec3(s[0], s[1], s[2]),
            new Vec3(s[3], s[4], s[5]),
            state.Distance,
            state.Source,
            state.Frame);
    }

    private BodyState ResolveSunState(JulianDay jdEt, EphemerisFlags flags, BodyState earth)
    {
        // For SwissEph/JPL we fetch barycentric Sun directly. For Moshier,
        // helio Sun is the origin; we synthesize a zero state in helio
        // ecliptic-J2000 (helio≡bary in Moshier).
        var srcBit = flags & (EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph);
        if (srcBit == EphemerisFlags.MoshierEph)
        {
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.HeliocentricJ2000Ecliptic);
        }
        // Barycentric Sun: SwissEph/JPL sources expose this via the SE_SUN
        // body request with SEFLG_BARYCTR. We bypass plausibility-check here
        // because the source layer accepts it directly.
        var (source, f) = _router.Resolve(flags | EphemerisFlags.Barycentric, CelestialBody.Sun, jdEt);
        // Cancel helio bit if it slipped through.
        f = (f & ~EphemerisFlags.Heliocentric) | EphemerisFlags.Barycentric;
        return source.Compute(CelestialBody.Sun, jdEt, f);
        // earth is unused here but kept to make the calling pattern explicit.
        // (BodyService passes both even though only earth participates in
        //  geocentrification.)
    }

    /// <summary>
    /// Subtract ayanamsha from longitude (and its time-derivative from
    /// longitude-speed). Mirrors the inline "traditional algorithm" branch
    /// of <c>app_pos_rest</c> at <c>sweph.c#L2820-L2835</c>. The pipeline
    /// state is in geocentric tropical ecliptic-of-date cartesian; we
    /// convert to polar, subtract the ayanamsha (and its speed), and
    /// convert back.
    /// </summary>
    private BodyState ApplySidereal(BodyState ecliptic, JulianDay jdEt, EphemerisFlags flags)
    {
        if (_sidereal is null)
            throw new EphemerisFlagsException(
                "SEFLG_SIDEREAL was set but no SiderealService was injected into BodyService.");

        var inSpeed = (flags & EphemerisFlags.Speed) != 0;

        // Cartesian → polar with speed.
        Span<double> cart = stackalloc double[6];
        cart[0] = ecliptic.Position.X;
        cart[1] = ecliptic.Position.Y;
        cart[2] = ecliptic.Position.Z;
        cart[3] = ecliptic.Velocity.X;
        cart[4] = ecliptic.Velocity.Y;
        cart[5] = ecliptic.Velocity.Z;

        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);

        // Subtract ayanamsha (deg → rad) from longitude / speed.
        var (daya, speed) = _sidereal.GetAyanamsaWithSpeed(jdEt, flags);
        polar[0] -= daya * AstronomicalConstants.DegToRad;
        if (inSpeed)
            polar[3] -= speed * AstronomicalConstants.DegToRad;

        // Polar → cartesian back. CartesianToPolarWithSpeed produced a
        // canonical polar tuple; we reverse it.
        Polar.PolarToCartesianWithSpeed(polar, cart);

        return new BodyState(
            new Vec3(cart[0], cart[1], cart[2]),
            inSpeed ? new Vec3(cart[3], cart[4], cart[5]) : default,
            ecliptic.Distance,
            ecliptic.Source,
            ecliptic.Frame);
    }

    private static BodyState AddVec3(BodyState a, BodyState b) =>
        new(
            new Vec3(a.Position.X + b.Position.X, a.Position.Y + b.Position.Y, a.Position.Z + b.Position.Z),
            new Vec3(a.Velocity.X + b.Velocity.X, a.Velocity.Y + b.Velocity.Y, a.Velocity.Z + b.Velocity.Z),
            0.0,
            a.Source,
            a.Frame);

    /// <summary>
    /// Geographic observer offset in barycentric J2000-equator coordinates
    /// (AU, AU/day). Delegates to the shared <see cref="TopocentricMath"/>
    /// helper so <c>FixedStarService</c> can reuse the same maths without
    /// taking a hard dependency on <c>BodyService</c>.
    /// </summary>
    private BodyState ComputeTopocentricOffsetJ2000Equator(JulianDay jdEt, ObserverLocation observer)
        => TopocentricMath.ObserverOffsetJ2000Equator(jdEt, observer, _calendar, _models);
}
