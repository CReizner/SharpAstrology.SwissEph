// Ported from swisseph-master/sweph.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: sweph.c
//   swe_fixstar2                       — lines 6818-6876
//   swe_fixstar2_ut                    — lines 6878-6898
//   swe_fixstar2_mag                   — lines 6911-6944
//   fixstar_calc_from_struct           — lines 6407-6669

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Stars;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Stars;

/// <summary>
/// High-level fixed-star service. Mirrors the C API entry points
/// <c>swe_fixstar2</c>, <c>swe_fixstar2_ut</c> and <c>swe_fixstar2_mag</c>
/// (<c>sweph.c#L6818</c>, <c>#L6878</c>, <c>#L6911</c>) in a stateless,
/// per-call form. The service does not mutate any global ephemeris
/// state — flags, observer information and ephemeris source are passed
/// every time. Mirrors the proper-motion + parallax + light-time +
/// aberration + deflection + precession + nutation pipeline of
/// <c>fixstar_calc_from_struct</c> (<c>sweph.c#L6407-L6669</c>) and the
/// catalogue-search wrapper of <c>swe_fixstar2</c>
/// (<c>sweph.c#L6818-L6876</c>).
/// </summary>
/// <remarks>
/// <para>The service is fully stateless. The C library's TLS caches
/// (<c>last_starname</c>, <c>last_stardata</c>) are not ported because
/// they are an unobservable optimisation — every call goes through
/// <see cref="IFixedStarCatalog.TryFind"/>, whose hot path is already
/// O(1) after the first dictionary build.</para>
/// <para>Topocentric (<see cref="EphemerisFlags.Topocentric"/>) folds the
/// observer offset into the parallax origin via
/// <see cref="TopocentricMath"/>; sidereal mode reuses the same
/// <see cref="SiderealService"/> hookup as <c>BodyService</c>. The JPL
/// Horizons compatibility flags
/// (<see cref="EphemerisFlags.JplHorizons"/> /
/// <see cref="EphemerisFlags.JplHorizonsApprox"/>) are silently dropped on
/// the Moshier path, mirroring <c>plaus_iflag</c> at
/// <c>sweph.c#L6111-L6112</c> which clears them whenever the ephemeris
/// source is not JPL or SwissEph. Cartesian output
/// (<see cref="EphemerisFlags.Cartesian"/>) skips the closing polar
/// projection and returns the J2000 equator-of-date / ecliptic-of-date
/// state vector directly.</para>
/// </remarks>
public sealed class FixedStarService
{
    private readonly IFixedStarCatalog _catalog;
    private readonly BodyService _bodyService;
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides _models;
    private readonly SiderealService? _sidereal;

    /// <summary>Time-step used for the t−dt evaluation of Earth and Sun
    /// state. Mirrors <c>PLAN_SPEED_INTV * 0.1 = 8.64e-6 d</c> at
    /// <c>sweph.c#L6416</c>.</summary>
    private const double EvalIntervalDays = 0.0001 * 0.1;

    /// <summary>Constructor.</summary>
    /// <param name="catalog">Lazily-loaded fixed-star catalogue (the
    /// concrete reader lives in Infrastructure).</param>
    /// <param name="bodyService"><see cref="BodyService"/> used to fetch
    /// the Earth / Sun raw state at <c>tjd</c> and <c>tjd − dt</c>. The
    /// service must be able to handle <see cref="EphemerisFlags.MoshierEph"/>.</param>
    /// <param name="calendar">ΔT provider used by <see cref="ComputeUt"/>.</param>
    /// <param name="models">Astronomical model overrides (precession /
    /// nutation / frame-bias). Defaults to the C-library defaults
    /// (Vondrák 2011 + IAU 2000B + IAU 2006).</param>
    /// <param name="sidereal">Optional sidereal service. Required only
    /// when callers pass <see cref="EphemerisFlags.Sidereal"/>; otherwise
    /// may be null.</param>
    /// <remarks>
    /// The constructor is <see langword="internal"/>: the catalogue
    /// abstraction is a composition-root concern (see
    /// <see cref="IFixedStarCatalog"/>), so the public way to obtain a
    /// configured service is <c>EphemerisContext.FixedStars</c> after
    /// wiring a catalogue via
    /// <see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder.UseFixedStarCatalog(string)"/>.
    /// </remarks>
    internal FixedStarService(
        IFixedStarCatalog catalog,
        BodyService bodyService,
        CalendarService calendar,
        AstronomicalModelOverrides? models = null,
        SiderealService? sidereal = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bodyService = bodyService ?? throw new ArgumentNullException(nameof(bodyService));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _models = models ?? AstronomicalModelOverrides.Default;
        _sidereal = sidereal;
    }

    /// <summary>
    /// Computes the apparent position of a fixed star at terrestrial-time
    /// Julian Day <paramref name="jdEt"/>. Mirrors <c>swe_fixstar2</c>.
    /// </summary>
    /// <param name="starName">Catalogue lookup string. Accepts the three
    /// formats the C library recognises: traditional name (case-insensitive,
    /// whitespace-tolerant), <c>",bayer"</c> Bayer/Flamsteed designation
    /// (case-sensitive after the comma), or 1-based sequential index.</param>
    /// <param name="jdEt">Julian Day in TT.</param>
    /// <param name="flags">Frame / source / option flags. Speed is forced
    /// internally (<c>fixstar_calc_from_struct</c> at <c>sweph.c#L6420</c>);
    /// the speed components in the result are zeroed when the caller did
    /// not request them.</param>
    /// <param name="observer">Geographic observer position. Required when
    /// <paramref name="flags"/> contains <see cref="EphemerisFlags.Topocentric"/>;
    /// ignored otherwise.</param>
    /// <returns>Apparent position with magnitude carried over from the catalogue.</returns>
    public FixedStarPosition Compute(
        string starName,
        JulianDay jdEt,
        EphemerisFlags flags = EphemerisFlags.MoshierEph,
        ObserverLocation? observer = null)
    {
        if (starName is null) throw new ArgumentNullException(nameof(starName));

        if ((flags & EphemerisFlags.Topocentric) != 0 && !observer.HasValue)
            throw new EphemerisFlagsException(
                "SEFLG_TOPOCTR requires an ObserverLocation parameter.");

        if (!_catalog.TryFind(starName, out var match))
            throw new System.Collections.Generic.KeyNotFoundException(
                $"Fixed star \"{starName}\" not found in catalogue.");

        return ComputeFromMatch(match, jdEt, flags, observer);
    }

    /// <summary>
    /// Computes the apparent position from a UT (UT1) Julian Day. Mirrors
    /// <c>swe_fixstar2_ut</c>: applies ΔT and dispatches to <see cref="Compute"/>.
    /// </summary>
    public FixedStarPosition ComputeUt(
        string starName,
        JulianDay jdUt,
        EphemerisFlags flags = EphemerisFlags.MoshierEph,
        ObserverLocation? observer = null)
    {
        var deltaT = _calendar.DeltaT(jdUt);
        return Compute(starName, new JulianDay(jdUt.Value + deltaT), flags, observer);
    }

    /// <summary>
    /// Returns the catalogue V magnitude for the named star. Mirrors
    /// <c>swe_fixstar2_mag</c> at <c>sweph.c#L6911</c>.
    /// </summary>
    /// <param name="starName">Catalogue lookup string (same formats as
    /// <see cref="Compute"/>).</param>
    /// <returns>Apparent V magnitude.</returns>
    public double GetMagnitude(string starName)
    {
        if (starName is null) throw new ArgumentNullException(nameof(starName));
        if (!_catalog.TryFind(starName, out var match))
            throw new System.Collections.Generic.KeyNotFoundException(
                $"Fixed star \"{starName}\" not found in catalogue.");
        return match.Star.Magnitude;
    }

    private FixedStarPosition ComputeFromMatch(
        FixedStarMatch match,
        JulianDay jdEt,
        EphemerisFlags flags,
        ObserverLocation? observer)
    {
        var iflagInput = flags;
        // SEFLG_SPEED is forced internally; sweph.c#L6420.
        flags |= EphemerisFlags.Speed;
        flags = PlausibilityCheck.Normalize(flags, hasObserver: observer.HasValue);

        var moshier = (flags & EphemerisFlags.MoshierEph) != 0;
        if (!moshier)
            throw new NotSupportedException(
                "FixedStarService currently only supports SEFLG_MOSEPH; the SwissEph and JPL fixed-star paths are not yet wired up.");

        // SEFLG_JPLHOR / SEFLG_JPLHOR_APPROX are silently dropped on the
        // Moshier path. Mirrors plaus_iflag at sweph.c#L6111-L6112: "SEFLG_JPLHOR
        // only with JPL and Swiss Ephemeris" — for any other ephemeris source
        // the C library clears both bits without warning. Empirically this
        // makes JPLHOR / JPLHOR_APPROX a no-op for fixstar Moshier output
        // (verified via /tmp/fixstar_jplhor_ref.c: every JPLHOR* case is
        // bit-identical with the no-flag baseline).
        flags &= ~(EphemerisFlags.JplHorizons | EphemerisFlags.JplHorizonsApprox);

        var inSpeedRequested = (iflagInput & EphemerisFlags.Speed) != 0;
        var truePos = (flags & EphemerisFlags.TruePosition) != 0;
        var helio = (flags & EphemerisFlags.Heliocentric) != 0;
        var bary = (flags & EphemerisFlags.Barycentric) != 0;
        var equatorial = (flags & EphemerisFlags.Equatorial) != 0;
        var icrs = (flags & EphemerisFlags.Icrs) != 0;
        var j2000Output = (flags & EphemerisFlags.J2000Equinox) != 0;
        var noNut = (flags & EphemerisFlags.NoNutation) != 0;
        var noAberr = (flags & EphemerisFlags.NoAberration) != 0;
        var noDeflect = (flags & EphemerisFlags.NoGravDeflection) != 0;
        var sidereal = (flags & EphemerisFlags.Sidereal) != 0;
        var radians = (flags & EphemerisFlags.Radians) != 0;
        var topocentric = (flags & EphemerisFlags.Topocentric) != 0;
        var cartesianOutput = (flags & EphemerisFlags.Cartesian) != 0;

        var star = match.Star;
        var tjd = jdEt.Value;

        // Days since reference epoch. sweph.c#L6459-L6463.
        var t = star.Epoch == FixedStarEpoch.B1950
            ? tjd - AstronomicalConstants.B1950
            : tjd - AstronomicalConstants.J2000;

        // Position+motion vector (polar, radians). sweph.c#L6464-L6477.
        const double parsecToAu = 206264.8062471; // PARSEC_TO_AUNIT, sweph.h#L288
        Span<double> x = stackalloc double[6];
        x[0] = star.RightAscensionRad;
        x[1] = star.DeclinationRad;
        // Heuristic distance from parallax; mirrors sweph.c#L6467-L6474.
        var rdist = star.ParallaxRad == 0.0
            ? 1.0e9
            : 1.0 / (star.ParallaxRad * AstronomicalConstants.RadToDeg * 3600.0) * parsecToAu;
        x[2] = rdist;
        x[3] = star.RaProperMotionRad / AstronomicalConstants.JulianCentury;
        x[4] = star.DecProperMotionRad / AstronomicalConstants.JulianCentury;
        x[5] = star.RadialVelocityAuPerCentury / AstronomicalConstants.JulianCentury;

        // Polar → cartesian (with speed). sweph.c#L6479.
        Polar.PolarToCartesianWithSpeed(x, x);

        // FK4 (B1950) → FK5 (J2000) catalogue alignment. sweph.c#L6483-L6487.
        if (star.Epoch == FixedStarEpoch.B1950)
        {
            CatalogFrameTransforms.Fk4ToFk5(x, AstronomicalConstants.B1950);
            // swi_precess(x  , B1950, 0, J_TO_J2000) for position
            // swi_precess(x+3, B1950, 0, J_TO_J2000) for velocity (separately, no dpre).
            Span<double> pos = stackalloc double[3];
            pos[0] = x[0]; pos[1] = x[1]; pos[2] = x[2];
            Precession.Apply(pos, AstronomicalConstants.B1950, AstronomicalConstants.J2000, _models);
            x[0] = pos[0]; x[1] = pos[1]; x[2] = pos[2];
            Span<double> vel = stackalloc double[3];
            vel[0] = x[3]; vel[1] = x[4]; vel[2] = x[5];
            Precession.Apply(vel, AstronomicalConstants.B1950, AstronomicalConstants.J2000, _models);
            x[3] = vel[0]; x[4] = vel[1]; x[5] = vel[2];
        }

        // FK5→ICRS plus ICRS→J2000 bias for catalogue records that have
        // an explicit equinox (1950 / 2000). Records with epoch=ICRS skip
        // both. sweph.c#L6490-L6496.
        if (star.Epoch != FixedStarEpoch.Icrs)
        {
            CatalogFrameTransforms.IcrsToFk5(x, includeSpeed: true, backward: true);
            // Moshier denum is hard-coded to 403 → bias is always applied.
            CatalogFrameTransforms.IcrsBias(x, includeSpeed: true, backward: false, model: _models.FrameBias);
        }

        // Earth and Sun barycentric raw vectors at tjd and tjd−dt
        // (Moshier helio == bary). sweph.c#L6501-L6508.
        Span<double> xEarth = stackalloc double[6];
        Span<double> xEarthDt = stackalloc double[6];
        Span<double> xSun = stackalloc double[6];
        Span<double> xSunDt = stackalloc double[6];

        var needEarth = !bary && (!helio || !moshier);
        if (needEarth)
        {
            FetchRawEarth(jdEt, flags, xEarth);
            FetchRawEarth(new JulianDay(tjd - EvalIntervalDays), flags, xEarthDt);
            // For Moshier, helio Sun is at the origin → all zeros (matches C).
            // For SwissEph/JPL we'd need a separate Sun fetch.
            xSun.Clear();
            xSunDt.Clear();
        }
        else
        {
            xEarth.Clear(); xEarthDt.Clear(); xSun.Clear(); xSunDt.Clear();
        }

        // Observer = Earth, plus the geographic offset under SEFLG_TOPOCTR.
        // sweph.c#L6513-L6522: swi_get_observer is evaluated at both tjd and
        // tjd-dt, then added to xearth / xearth_dt. For Moshier the result is
        // the geocentric Earth + observer offset (helio == bary).
        Span<double> xObs = stackalloc double[6];
        Span<double> xObsDt = stackalloc double[6];
        xEarth.CopyTo(xObs);
        xEarthDt.CopyTo(xObsDt);
        if (topocentric && observer.HasValue)
        {
            var topo = TopocentricMath.ObserverOffsetJ2000Equator(jdEt, observer.Value, _calendar, _models);
            var topoDt = TopocentricMath.ObserverOffsetJ2000Equator(
                new JulianDay(tjd - EvalIntervalDays), observer.Value, _calendar, _models);
            xObs[0] += topo.Position.X;   xObs[1] += topo.Position.Y;   xObs[2] += topo.Position.Z;
            xObs[3] += topo.Velocity.X;   xObs[4] += topo.Velocity.Y;   xObs[5] += topo.Velocity.Z;
            xObsDt[0] += topoDt.Position.X; xObsDt[1] += topoDt.Position.Y; xObsDt[2] += topoDt.Position.Z;
            xObsDt[3] += topoDt.Velocity.X; xObsDt[4] += topoDt.Velocity.Y; xObsDt[5] += topoDt.Velocity.Z;
        }

        // Decide xpo (the "parallax origin"). sweph.c#L6534-L6546.
        Span<double> xpo = stackalloc double[6];
        Span<double> xpoDt = stackalloc double[6];
        bool hasXpo;
        if ((helio && moshier) || bary)
        {
            // No parallax for heliocentric+Moshier or barycentric.
            hasXpo = false;
        }
        else if (helio)
        {
            xSun.CopyTo(xpo);
            xSunDt.CopyTo(xpoDt);
            hasXpo = true;
        }
        else
        {
            xObs.CopyTo(xpo);
            xObsDt.CopyTo(xpoDt);
            hasXpo = true;
        }

        // Apply proper motion (and, for the parallax branch, the observer
        // displacement). sweph.c#L6547-L6557.
        if (!hasXpo)
        {
            for (var i = 0; i < 3; i++) x[i] += t * x[i + 3];
        }
        else
        {
            for (var i = 0; i < 3; i++)
            {
                x[i] += t * x[i + 3];
                x[i] -= xpo[i];
                x[i + 3] -= xpo[i + 3];
            }
        }

        // Relativistic deflection. sweph.c#L6561-L6563.
        if (!truePos && !noDeflect)
        {
            // Earth helio = xEarth - xSun (Moshier: xSun=0). Velocity via
            // index 3..5 of the same buffer.
            Span<double> earthHelio = stackalloc double[6];
            for (var i = 0; i < 6; i++) earthHelio[i] = xEarth[i] - xSun[i];
            GravitationalDeflection.Apply(
                x, earthHelio, earthHelio.Slice(3, 3), includeSpeedCorrection: true);
        }

        // Annual aberration. sweph.c#L6568-L6569.
        if (!truePos && !noAberr)
        {
            ApplyAberrationEx(x, xpo, xpoDt, EvalIntervalDays, includeSpeed: true);
        }

        // ICRS → J2000 bias for the apparent position. sweph.c#L6570-L6573.
        // Moshier denum 403 → applied unless ICRS or BARYCTR-without-bias.
        if (!icrs)
        {
            CatalogFrameTransforms.IcrsBias(x, includeSpeed: true, backward: false, model: _models.FrameBias);
        }

        // Save J2000 cartesian for the sidereal projection. sweph.c#L6575-L6576.
        Span<double> xxsv = stackalloc double[6];
        x.CopyTo(xxsv);

        // Precession J2000 → date (skipped for J2000 output).
        // sweph.c#L6580-L6587.
        var oblqDateUsed = !j2000Output;
        if (!j2000Output)
        {
            Span<double> pos = stackalloc double[3];
            pos[0] = x[0]; pos[1] = x[1]; pos[2] = x[2];
            Precession.Apply(pos, AstronomicalConstants.J2000, tjd, _models);
            x[0] = pos[0]; x[1] = pos[1]; x[2] = pos[2];
            // swi_precess_speed handles the dpre rate term.
            Precession.ApplySpeed(x, AstronomicalConstants.J2000, tjd, _models);
        }

        // Mean obliquity ε (used for ecliptic projection). sweph.c#L6585-L6587.
        var meanEpsRad = oblqDateUsed
            ? Precession.MeanObliquity(tjd, _models)
            : Precession.MeanObliquity(AstronomicalConstants.J2000, _models);

        NutationAngles? nutAngles = null;
        // Nutation. sweph.c#L6591-L6592.
        if (!noNut)
        {
            var nut = Nutation.Compute(tjd, _models);
            nutAngles = nut;
            Nutation.ApplyWithSpeed(x, tjd, backward: false, _models);
        }

        // Ecliptic transform. sweph.c#L6602-L6611.
        if (!equatorial)
        {
            // Position uses true ε if nutated, mean ε otherwise — the C
            // library applies the rotation by mean ε first and then a
            // separate rotation by Δε via swi_coortrf2(... snut, cnut)
            // equivalent to a single rotation by ε + Δε.
            Span<double> pos = stackalloc double[3];
            pos[0] = x[0]; pos[1] = x[1]; pos[2] = x[2];
            FrameTransform.EquatorialToEcliptic(pos, meanEpsRad);
            x[0] = pos[0]; x[1] = pos[1]; x[2] = pos[2];
            Span<double> vel = stackalloc double[3];
            vel[0] = x[3]; vel[1] = x[4]; vel[2] = x[5];
            FrameTransform.EquatorialToEcliptic(vel, meanEpsRad);
            x[3] = vel[0]; x[4] = vel[1]; x[5] = vel[2];

            if (!noNut && nutAngles is { } nut)
            {
                // Apply Δε rotation around the X axis (matches
                // swi_coortrf2(x, snut, cnut) at sweph.c#L6606-L6610). We
                // rotate by +Δε on the position and on the velocity
                // separately.
                var snut = System.Math.Sin(nut.DeltaEpsilonRad);
                var cnut = System.Math.Cos(nut.DeltaEpsilonRad);
                RotateAroundX(ref x[1], ref x[2], snut, cnut);
                RotateAroundX(ref x[4], ref x[5], snut, cnut);
            }
        }

        // Sidereal projection (traditional algorithm). sweph.c#L6616-L6643.
        if (sidereal && !equatorial && !j2000Output && !helio && !bary)
        {
            if (_sidereal is null)
                throw new EphemerisFlagsException(
                    "SEFLG_SIDEREAL was set but no SiderealService was injected into FixedStarService.");

            Span<double> polar = stackalloc double[6];
            Polar.CartesianToPolarWithSpeed(x, polar);
            var (daya, ayanaSpeed) = _sidereal.GetAyanamsaWithSpeed(jdEt, flags);
            polar[0] -= daya * AstronomicalConstants.DegToRad;
            polar[3] -= ayanaSpeed * AstronomicalConstants.DegToRad;
            Polar.PolarToCartesianWithSpeed(polar, x);
        }

        // Cartesian → polar (skipped for SEFLG_XYZ). sweph.c#L6647-L6648.
        Span<double> output = stackalloc double[6];
        if (cartesianOutput)
        {
            x.CopyTo(output);
        }
        else
        {
            Polar.CartesianToPolarWithSpeed(x, output);
        }

        // Radians → degrees. sweph.c#L6652-L6657. The C library only converts
        // for the polar-output path; XYZ stays in AU and AU/day regardless of
        // SEFLG_RADIANS.
        if (!radians && !cartesianOutput)
        {
            output[0] *= AstronomicalConstants.RadToDeg;
            output[1] *= AstronomicalConstants.RadToDeg;
            output[3] *= AstronomicalConstants.RadToDeg;
            output[4] *= AstronomicalConstants.RadToDeg;
        }

        // Zero speeds when the original (user-facing) iflag did not
        // include SEFLG_SPEED. sweph.c#L6660-L6663.
        if (!inSpeedRequested)
        {
            output[3] = output[4] = output[5] = 0.0;
        }

        // Distance: polar Z is the line-of-sight distance; for XYZ it's the
        // magnitude of the cartesian position. The polar pipeline produces
        // the same number, so reading from x[] (pre-polar) keeps both paths
        // exact at floating-point precision.
        var distance = cartesianOutput
            ? System.Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2])
            : output[2];

        return new FixedStarPosition(
            match.CanonicalName,
            new Vec3(output[0], output[1], output[2]),
            new Vec3(output[3], output[4], output[5]),
            distance,
            star.Magnitude);
    }

    private static void RotateAroundX(ref double y, ref double z, double sinDelta, double cosDelta)
    {
        // Mirrors swi_coortrf2(x, x, sineps, coseps):
        //   y' = y * cos + z * sin
        //   z' = -y * sin + z * cos
        var yi = y;
        var zi = z;
        y = yi * cosDelta + zi * sinDelta;
        z = -yi * sinDelta + zi * cosDelta;
    }

    /// <summary>
    /// Aberration with explicit Earth state at <c>t</c> and <c>t − dt</c>.
    /// Mirrors <c>swi_aberr_light_ex</c> (<c>sweph.c#L3672-L3692</c>); the
    /// existing <see cref="Aberration"/> helper uses
    /// <c>swi_aberr_light</c> (<c>sweph.c#L3699</c>) which differs in how
    /// the speed correction is built. The "_ex" variant is the one
    /// <c>fixstar_calc_from_struct</c> picks at <c>sweph.c#L6569</c>.
    /// </summary>
    private static void ApplyAberrationEx(
        Span<double> x, ReadOnlySpan<double> xe, ReadOnlySpan<double> xeDt,
        double dt, bool includeSpeed)
    {
        Span<double> xxs = stackalloc double[6];
        for (var i = 0; i < 6; i++) xxs[i] = x[i];

        // First step: aberration with Earth velocity at t.
        AberrPositionOnly(x, xe);

        if (!includeSpeed) return;

        // Build xx2 = position at t-dt (linearised) and apply aberration
        // with Earth velocity at t-dt; the velocity correction is the
        // forward finite difference of those two aberrated positions.
        Span<double> xx2 = stackalloc double[6];
        for (var i = 0; i < 3; i++)
            xx2[i] = xxs[i] - dt * xxs[i + 3];
        for (var i = 3; i < 6; i++) xx2[i] = 0.0;
        AberrPositionOnly(xx2, xeDt);
        for (var i = 0; i < 3; i++)
            x[i + 3] = (x[i] - xx2[i]) / dt;
    }

    private static void AberrPositionOnly(Span<double> x, ReadOnlySpan<double> xe)
    {
        // sweph.c#L3647-L3663.
        const double au = AstronomicalConstants.AstronomicalUnitMeters;
        const double cLight = AstronomicalConstants.SpeedOfLightMeters;
        const double secPerDay = AstronomicalConstants.SecondsPerDay;
        Span<double> v = stackalloc double[3];
        for (var i = 0; i < 3; i++) v[i] = xe[i + 3] / secPerDay / cLight * au;
        var v2 = v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
        var bInv = System.Math.Sqrt(1 - v2);
        var ru = System.Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2]);
        var f1 = (x[0] * v[0] + x[1] * v[1] + x[2] * v[2]) / ru;
        var f2 = 1.0 + f1 / (1.0 + bInv);
        for (var i = 0; i < 3; i++)
            x[i] = (bInv * x[i] + f2 * ru * v[i]) / (1.0 + f1);
    }

    /// <summary>
    /// Returns Earth's barycentric (helio for Moshier) raw position+velocity
    /// in J2000 equatorial coordinates. Uses the <see cref="BodyService"/>'s
    /// TruePosition + Heliocentric + J2000Equinox + Equatorial + NoNutation +
    /// NoAberration + NoGravDeflection flag combination, which short-
    /// circuits the apparent-position pipeline and returns the source's
    /// raw heliocentric vector.
    /// </summary>
    private void FetchRawEarth(JulianDay jd, EphemerisFlags flags, Span<double> destination)
    {
        var earthFlags = (flags & EphemerisFlags.MoshierEph)
                        | EphemerisFlags.Heliocentric
                        | EphemerisFlags.Speed
                        | EphemerisFlags.J2000Equinox
                        | EphemerisFlags.Equatorial
                        | EphemerisFlags.NoNutation
                        | EphemerisFlags.NoAberration
                        | EphemerisFlags.NoGravDeflection
                        | EphemerisFlags.TruePosition;
        var earth = _bodyService.Compute(CelestialBody.Earth, jd, earthFlags);
        destination[0] = earth.Position.X;
        destination[1] = earth.Position.Y;
        destination[2] = earth.Position.Z;
        destination[3] = earth.Velocity.X;
        destination[4] = earth.Velocity.Y;
        destination[5] = earth.Velocity.Z;
    }
}
