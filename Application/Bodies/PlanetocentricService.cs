// Ported from swisseph-master/sweph.c swe_calc_pctr (lines 8042-8283).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Planetocentric body-position service. Mirrors the C entry point
/// <c>swe_calc_pctr</c> (sweph.c#L8042-L8283): the position and velocity
/// of <c>body</c> as seen from the center of <c>center</c>, instead of
/// from Earth, Sun, or the solar-system barycenter. Two true-position
/// barycentric J2000 fetches, light-time iteration, planetocenter
/// translation, gravitational deflection, annual aberration with the
/// center body's velocity, frame bias, precession, nutation, ecliptic
/// rotation, and optional sidereal projection.
/// </summary>
/// <remarks>
/// <para>
/// This API only accepts <see cref="CelestialBody"/> values. Asteroid bodies
/// (<c>SE_AST_OFFSET ≤ ipl</c>) and fictitious bodies are not in scope and
/// require asteroid-file infrastructure that is not currently wired into the
/// port (see milestone notes).
/// </para>
/// <para>
/// Lunar derived points (<see cref="CelestialBody.MeanNode"/>,
/// <see cref="CelestialBody.TrueNode"/>, <see cref="CelestialBody.MeanApogee"/>,
/// <see cref="CelestialBody.OsculatingApogee"/>,
/// <see cref="CelestialBody.InterpolatedApogee"/>,
/// <see cref="CelestialBody.InterpolatedPerigee"/>) are not valid as either
/// <c>body</c> or <c>center</c>; the C function would compute their underlying
/// barycentric XYZ but the result is astronomically meaningless. We reject
/// them with <see cref="EphemerisFlagsException"/>.
/// </para>
/// <para>
/// The two underlying <see cref="BodyService.Compute"/> calls use the locked
/// flag set <c>BARYCTR | J2000 | ICRS | TRUEPOS | EQUATORIAL | XYZ | SPEED |
/// NOABERR | NOGDEFL</c> (sweph.c#L8061-L8062). Source routing and the
/// SwissEph→Moshier file-not-found fall-back happen inside
/// <see cref="BodyService"/>.
/// </para>
/// <para>
/// Aberration is applied with the center body's barycentric velocity, not
/// Earth's (sweph.c#L8158). Gravitational deflection — a Sun-mass-only
/// formula — keeps Earth as the reference observer in the C library
/// (sweph.c#L3743-L3818); we mirror that. Mean obliquity and nutation are
/// recomputed per call instead of being read from the C library's
/// <c>swed.oec</c> / <c>swed.nut</c> cache (sweph.c#L8058 omitted).
/// </para>
/// </remarks>
public sealed class PlanetocentricService
{
    private readonly BodyService _bodies;
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides _models;
    private readonly SiderealService? _sidereal;

    private const EphemerisFlags SourceMask =
        EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph;

    /// <summary>
    /// Locked iflag set used for the two underlying barycentric J2000 fetches
    /// (sweph.c#L8061-L8062). Source bit is OR-ed in per call.
    /// </summary>
    private const EphemerisFlags RawBaryFlagsBase =
        EphemerisFlags.Barycentric
        | EphemerisFlags.J2000Equinox
        | EphemerisFlags.Icrs
        | EphemerisFlags.TruePosition
        | EphemerisFlags.Equatorial
        | EphemerisFlags.Cartesian
        | EphemerisFlags.Speed
        | EphemerisFlags.NoAberration
        | EphemerisFlags.NoGravDeflection;

    public PlanetocentricService(
        BodyService bodies,
        CalendarService calendar,
        AstronomicalModelOverrides? models = null,
        SiderealService? sidereal = null)
    {
        _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _models = models ?? AstronomicalModelOverrides.Default;
        _sidereal = sidereal;
    }

    /// <summary>
    /// UT (UT1) variant. Applies ΔT and dispatches to <see cref="Compute"/>.
    /// Mirrors the convention of <see cref="BodyService.ComputeUt"/>.
    /// </summary>
    public BodyState ComputeUt(CelestialBody body, CelestialBody center, JulianDay jdUt, EphemerisFlags flags)
    {
        var dt = _calendar.DeltaT(jdUt);
        return Compute(body, center, new JulianDay(jdUt.Value + dt), flags);
    }

    /// <summary>
    /// Compute <paramref name="body"/> at <paramref name="jdEt"/> TT in a
    /// frame centered on <paramref name="center"/>. Mirrors
    /// <c>swe_calc_pctr</c> with TT input.
    /// </summary>
    /// <param name="body">The body whose position is wanted.</param>
    /// <param name="center">The body that defines the coordinate origin.</param>
    /// <param name="jdEt">Julian Day in TT.</param>
    /// <param name="flags">Frame / source / option flags.</param>
    /// <exception cref="EphemerisFlagsException">
    /// <see cref="EphemerisFlags.Heliocentric"/>,
    /// <see cref="EphemerisFlags.Barycentric"/> or
    /// <see cref="EphemerisFlags.Topocentric"/> is set;
    /// <paramref name="body"/> equals <paramref name="center"/>;
    /// or either argument is a derived lunar point.
    /// </exception>
    public BodyState Compute(CelestialBody body, CelestialBody center, JulianDay jdEt, EphemerisFlags flags)
    {
        // ---- Step 1: validation (sweph.c#L8050-L8054 plus port-specific decisions) ----
        if (body == center)
            throw new EphemerisFlagsException(
                $"body and center must not be identical (both are {body}).");
        if ((flags & (EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric | EphemerisFlags.Topocentric)) != 0)
            throw new EphemerisFlagsException(
                "PlanetocentricService.Compute: Heliocentric, Barycentric, and Topocentric flags are mutually exclusive with planetocentric output.");
        RejectDerivedPoint(body, nameof(body));
        RejectDerivedPoint(center, nameof(center));

        // ---- Step 2: normalise flags (sweph.c#L8055-L8059) -----------------
        // Strip helio/bary defensively before plausibility-check (the C code
        // also strips them at line 8059 after plaus_iflag).
        var input = flags & ~(EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric);
        var normalised = PlausibilityCheck.Normalize(input, hasObserver: false);
        normalised &= ~(EphemerisFlags.Heliocentric | EphemerisFlags.Barycentric);
        var epheflag = normalised & SourceMask;

        // sweph.c#L8058 calls swe_calc(SE_ECL_NUT) just to populate the global
        // obliquity/nutation cache. The C# port has no global cache; mean
        // obliquity and nutation are recomputed per call. No action needed.

        // ---- Step 3: two barycentric J2000-equator fetches at t (sweph.c#L8061-L8068) ----
        var rawFlags = epheflag | RawBaryFlagsBase;
        var ctrState = _bodies.Compute(center, jdEt, rawFlags);
        var bodyState = _bodies.Compute(body, jdEt, rawFlags);

        Span<double> xxctr = stackalloc double[6];
        WriteState(ctrState, xxctr);
        Span<double> xx = stackalloc double[6];
        WriteState(bodyState, xx);
        Span<double> xx0 = stackalloc double[6];
        xx.CopyTo(xx0);

        // ---- Step 4: light-time iteration (sweph.c#L8076-L8125) ------------
        var truePos = (normalised & EphemerisFlags.TruePosition) != 0;
        var inSpeed = (normalised & EphemerisFlags.Speed) != 0;
        Span<double> xxctr2 = stackalloc double[6];
        xxctr.CopyTo(xxctr2);
        Span<double> xxsp = stackalloc double[3];
        var hasXxsp = false;
        var dtsaveForDefl = 0.0;
        var tApparent = jdEt.Value;
        // niter=1 in C → loop runs j=0,1 (two passes), independent of source.
        const int niter = 1;
        if (!truePos)
        {
            // xxsp speed-correction precomputation (sweph.c#L8079-L8104).
            if (inSpeed)
            {
                Span<double> xxsv = stackalloc double[3];
                Span<double> xxspIter = stackalloc double[3];
                for (var i = 0; i < 3; i++)
                {
                    xxsv[i] = xx[i] - xx[i + 3];
                    xxspIter[i] = xxsv[i];
                }
                Span<double> dx = stackalloc double[3];
                for (var j = 0; j <= niter; j++)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        dx[i] = xxspIter[i];
                        dx[i] -= (xxctr[i] - xxctr[i + 3]);
                    }
                    var rsp = System.Math.Sqrt(dx[0] * dx[0] + dx[1] * dx[1] + dx[2] * dx[2]);
                    var dtsp = rsp * AstronomicalConstants.LightTimeAuPerDay;
                    for (var i = 0; i < 3; i++)
                        xxspIter[i] = xxsv[i] - dtsp * xx0[i + 3];
                }
                for (var i = 0; i < 3; i++)
                    xxsp[i] = xxsv[i] - xxspIter[i];
                hasXxsp = true;
            }

            // dt and t (apparent) (sweph.c#L8106-L8117).
            Span<double> dx2 = stackalloc double[3];
            for (var j = 0; j <= niter; j++)
            {
                for (var i = 0; i < 3; i++)
                {
                    dx2[i] = xx[i];
                    dx2[i] -= xxctr[i];
                }
                var r = System.Math.Sqrt(dx2[0] * dx2[0] + dx2[1] * dx2[1] + dx2[2] * dx2[2]);
                var dt = r * AstronomicalConstants.LightTimeAuPerDay;
                tApparent = jdEt.Value - dt;
                dtsaveForDefl = dt;
                for (var i = 0; i < 3; i++)
                    xx[i] = xx0[i] - dt * xx0[i + 3];
            }
            // Finalise xxsp = xx0 - xx_rough - xxsp (sweph.c#L8119-L8122).
            if (hasXxsp)
            {
                for (var i = 0; i < 3; i++)
                    xxsp[i] = xx0[i] - xx[i] - xxsp[i];
            }

            // Refetch both center and body at t-dt with full source dynamics
            // (sweph.c#L8123-L8124).
            var jdMinus = new JulianDay(tApparent);
            var ctrMinus = _bodies.Compute(center, jdMinus, rawFlags);
            var bodyMinus = _bodies.Compute(body, jdMinus, rawFlags);
            WriteState(ctrMinus, xxctr2);
            WriteState(bodyMinus, xx);
        }

        // ---- Step 5: translate to planetocenter (sweph.c#L8129-L8143) ------
        // Helio/bary branches were stripped above, so this always fires.
        for (var i = 0; i < 6; i++)
            xx[i] -= xxctr[i];
        if (!truePos && inSpeed && hasXxsp)
        {
            for (var i = 0; i < 3; i++)
                xx[3 + i] -= xxsp[i];
        }
        if (!inSpeed)
        {
            xx[3] = 0.0; xx[4] = 0.0; xx[5] = 0.0;
        }

        // ---- Step 6: gravitational deflection (sweph.c#L8150-L8152) -------
        // The C library's swi_deflect_light keeps Earth as the reference
        // observer (sweph.c#L3753-L3771), even when called from swe_calc_pctr.
        // Mirror that for parity. The Earth helio state is taken at t-dt
        // (matches the state cached by the most recent swe_calc fetch in C).
        if (!truePos && (normalised & EphemerisFlags.NoGravDeflection) == 0)
        {
            Span<double> earthHelio = stackalloc double[6];
            ResolveEarthHelioJ2000Equator(new JulianDay(tApparent), epheflag, earthHelio);
            ReadOnlySpan<double> earthHelioVel = earthHelio.Slice(3, 3);
            GravitationalDeflection.Apply(xx, earthHelio, earthHelioVel, inSpeed);
        }

        // ---- Step 7: annual aberration (sweph.c#L8156-L8168) --------------
        // The observer here is the planetocenter — pass xxctr (center body's
        // barycentric J2000 state at time t) so Aberration.Apply uses
        // xxctr[3..5] as observer velocity.
        if (!truePos && (normalised & EphemerisFlags.NoAberration) == 0)
        {
            ReadOnlySpan<double> ctrVel = xxctr.Slice(3, 3);
            Aberration.Apply(xx, ctrVel, inSpeed);
            if (inSpeed)
            {
                // Center-velocity drift between t and t-dt
                // (sweph.c#L8164-L8167).
                for (var i = 0; i < 3; i++)
                    xx[3 + i] += xxctr[3 + i] - xxctr2[3 + i];
            }
        }
        if (!inSpeed)
        {
            xx[3] = 0.0; xx[4] = 0.0; xx[5] = 0.0;
        }

        // ---- Step 8: ICRS → J2000 frame bias (sweph.c#L8173-L8175) --------
        // C# port treats all sources as denum >= 403 (Decision 6.10 in milestone).
        if ((normalised & EphemerisFlags.Icrs) == 0)
        {
            CatalogFrameTransforms.IcrsBias(xx, includeSpeed: true, backward: false, _models.FrameBias);
        }

        // J2000-equator state is now in xx. (xxsv save at sweph.c#L8177-L8178
        // is only needed for the SE_SIDBIT_ECL_T0 / SE_SIDBIT_SSY_PLANE
        // sidereal branches, which are out of scope this milestone.)

        // ---- Step 9: precession J2000 → date (sweph.c#L8182-L8189) --------
        var precessNow = (normalised & EphemerisFlags.J2000Equinox) == 0;
        if (precessNow)
        {
            Span<double> pos = stackalloc double[3] { xx[0], xx[1], xx[2] };
            Precession.Apply(pos, AstronomicalConstants.J2000, jdEt.Value, _models);
            xx[0] = pos[0]; xx[1] = pos[1]; xx[2] = pos[2];
            if (inSpeed)
            {
                Precession.ApplySpeed(xx, AstronomicalConstants.J2000, jdEt.Value, _models);
            }
        }

        // ---- Step 10: nutation (sweph.c#L8193-L8194) ----------------------
        var meanEpsAtJ = Precession.MeanObliquity(precessNow ? jdEt.Value : AstronomicalConstants.J2000, _models);
        NutationAngles? nutAngles = null;
        if ((normalised & EphemerisFlags.NoNutation) == 0)
        {
            var nut = Nutation.Compute(jdEt.Value, _models);
            nutAngles = nut;
            if (inSpeed)
            {
                Nutation.ApplyWithSpeed(xx, jdEt.Value, backward: false, _models);
            }
            else
            {
                Span<double> pos = stackalloc double[3] { xx[0], xx[1], xx[2] };
                Nutation.Apply(pos, nut, meanEpsAtJ, backward: false);
                xx[0] = pos[0]; xx[1] = pos[1]; xx[2] = pos[2];
            }
        }

        // At this point xx is equatorial: J2000-equator if J2000 set,
        // true-of-date equator otherwise.

        // ---- Step 11: equatorial → ecliptic (sweph.c#L8203-L8210) ---------
        var wantEquatorial = (normalised & EphemerisFlags.Equatorial) != 0;
        if (!wantEquatorial)
        {
            var trueEps = meanEpsAtJ + (nutAngles?.DeltaEpsilonRad ?? 0.0);
            Span<double> pos = stackalloc double[3] { xx[0], xx[1], xx[2] };
            FrameTransform.EquatorialToEcliptic(pos, trueEps);
            xx[0] = pos[0]; xx[1] = pos[1]; xx[2] = pos[2];
            if (inSpeed)
            {
                Span<double> vel = stackalloc double[3] { xx[3], xx[4], xx[5] };
                FrameTransform.EquatorialToEcliptic(vel, trueEps);
                xx[3] = vel[0]; xx[4] = vel[1]; xx[5] = vel[2];
            }
        }

        // ---- Step 12: sidereal projection — traditional algorithm only ----
        // (sweph.c#L8217-L8243). Only the ecliptic-of-date, non-J2000,
        // non-equatorial branch fires; this matches BodyService.ApplySidereal.
        // SE_SIDBIT_ECL_T0 / SE_SIDBIT_SSY_PLANE are out of scope this
        // milestone (per design note 5.15).
        BodyState result = BuildResult(xx, normalised, wantEquatorial, precessNow, bodyState.Source);
        if ((normalised & EphemerisFlags.Sidereal) != 0
            && !wantEquatorial
            && (normalised & EphemerisFlags.J2000Equinox) == 0)
        {
            result = ApplySidereal(result, jdEt, normalised);
        }
        return result;
    }

    private static void RejectDerivedPoint(CelestialBody body, string argName)
    {
        switch (body)
        {
            case CelestialBody.MeanNode:
            case CelestialBody.TrueNode:
            case CelestialBody.MeanApogee:
            case CelestialBody.OsculatingApogee:
            case CelestialBody.InterpolatedApogee:
            case CelestialBody.InterpolatedPerigee:
                throw new EphemerisFlagsException(
                    $"{argName}={body} is a derived geocentric point; it cannot serve as a planetocentric body or center.");
        }
    }

    private static void WriteState(BodyState state, Span<double> dest)
    {
        dest[0] = state.Position.X;
        dest[1] = state.Position.Y;
        dest[2] = state.Position.Z;
        dest[3] = state.Velocity.X;
        dest[4] = state.Velocity.Y;
        dest[5] = state.Velocity.Z;
    }

    /// <summary>
    /// Earth's heliocentric J2000-equator state. Mirrors swi_deflect_light's
    /// (sweph.c#L3753-L3771) "earth helio = earth bary − sun bary" reduction,
    /// using the same RawBaryFlagsBase fetch the rest of this service uses.
    /// For Moshier, Sun bary collapses to zero (helio ≡ bary in Moshier), so
    /// the reduction simplifies to Earth's raw heliocentric state.
    /// </summary>
    private void ResolveEarthHelioJ2000Equator(JulianDay jd, EphemerisFlags epheflag, Span<double> dest)
    {
        var rawFlags = epheflag | RawBaryFlagsBase;
        var earth = _bodies.Compute(CelestialBody.Earth, jd, rawFlags);
        var sun = _bodies.Compute(CelestialBody.Sun, jd, rawFlags);
        dest[0] = earth.Position.X - sun.Position.X;
        dest[1] = earth.Position.Y - sun.Position.Y;
        dest[2] = earth.Position.Z - sun.Position.Z;
        dest[3] = earth.Velocity.X - sun.Velocity.X;
        dest[4] = earth.Velocity.Y - sun.Velocity.Y;
        dest[5] = earth.Velocity.Z - sun.Velocity.Z;
    }

    private BodyState BuildResult(
        ReadOnlySpan<double> xx,
        EphemerisFlags flags,
        bool wantEquatorial,
        bool precessed,
        EphemerisSource source)
    {
        var inSpeed = (flags & EphemerisFlags.Speed) != 0;
        BodyStateFrame frame = wantEquatorial
            ? (precessed ? BodyStateFrame.PlanetocentricEquatorOfDate : BodyStateFrame.PlanetocentricJ2000Equator)
            : (precessed ? BodyStateFrame.PlanetocentricEclipticOfDate : BodyStateFrame.PlanetocentricJ2000Ecliptic);
        var pos = new Vec3(xx[0], xx[1], xx[2]);
        var vel = inSpeed ? new Vec3(xx[3], xx[4], xx[5]) : default;
        return new BodyState(
            pos,
            vel,
            System.Math.Sqrt(xx[0] * xx[0] + xx[1] * xx[1] + xx[2] * xx[2]),
            source,
            frame);
    }

    /// <summary>
    /// Subtract ayanamsha (and its time-derivative) from longitude and
    /// longitude-speed of the planetocentric tropical ecliptic-of-date
    /// cartesian state. Mirrors the inline traditional-algorithm branch of
    /// <c>swe_calc_pctr</c> at <c>sweph.c#L8226-L8242</c> (which is the same
    /// algorithm <see cref="BodyService.ApplySidereal"/> uses for the
    /// geocentric path).
    /// </summary>
    private BodyState ApplySidereal(BodyState ecliptic, JulianDay jdEt, EphemerisFlags flags)
    {
        if (_sidereal is null)
            throw new EphemerisFlagsException(
                "SEFLG_SIDEREAL was set but no SiderealService was injected into PlanetocentricService.");

        var inSpeed = (flags & EphemerisFlags.Speed) != 0;

        Span<double> cart = stackalloc double[6];
        cart[0] = ecliptic.Position.X;
        cart[1] = ecliptic.Position.Y;
        cart[2] = ecliptic.Position.Z;
        cart[3] = ecliptic.Velocity.X;
        cart[4] = ecliptic.Velocity.Y;
        cart[5] = ecliptic.Velocity.Z;

        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);

        var (daya, speed) = _sidereal.GetAyanamsaWithSpeed(jdEt, flags);
        polar[0] -= daya * AstronomicalConstants.DegToRad;
        if (inSpeed)
            polar[3] -= speed * AstronomicalConstants.DegToRad;

        Polar.PolarToCartesianWithSpeed(polar, cart);

        return new BodyState(
            new Vec3(cart[0], cart[1], cart[2]),
            inSpeed ? new Vec3(cart[3], cart[4], cart[5]) : default,
            ecliptic.Distance,
            ecliptic.Source,
            ecliptic.Frame);
    }
}
