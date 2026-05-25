// Ported from swisseph-master/sweph.c swe_set_sid_mode (line 2861),
// swi_get_ayanamsa_ex / swe_get_ayanamsa_ex (line 2930), get_aya_correction
// (line 2960), swi_get_ayanamsa_with_speed (line 3210), swe_get_ayanamsa_ex_ut
// (line 3226), swe_get_ayanamsa_name (line 7127).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Stars;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Sidereal;

/// <summary>
/// Computes the ayanamsha (precession-of-equinoxes offset) for a given
/// Julian Day in either Terrestrial or Universal Time. Mirrors the C-side
/// API <c>swe_set_sid_mode</c> / <c>swe_get_ayanamsa_ex</c> /
/// <c>swe_get_ayanamsa_ex_ut</c>, but as an injectable, per-context service
/// with no global state.
/// </summary>
/// <remarks>
/// <para>
/// The configuration is mutated through <see cref="SetMode"/>, which is
/// <see langword="internal"/> by design: only composition roots
/// (<see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder"/>)
/// flip the mode, and only before the owning context is published. There
/// is no global <c>swed.sidd</c> equivalent — the state lives on the
/// service instance, and the assembly-private mutator keeps the
/// "build-time only" discipline visible at the type surface (cf.
/// <c>ARCHITECTURE.md §3.5</c>).
/// </para>
/// <para>
/// <b>Thread-safety</b>: a published <see cref="SiderealService"/> is safe
/// for concurrent reads. <see cref="SetMode"/> is not safe to call
/// concurrently with reads or other writes — it is restricted to the
/// composition root for exactly that reason. To switch modes at runtime,
/// build a second <see cref="SharpAstrology.SwissEphemerides.EphemerisContext"/>.
/// </para>
/// <para>
/// Star-based modes
/// (<see cref="AyanamshaTable.RequiresFixedStarSource"/>) need an
/// <see cref="FixedStarService"/> wired in via the constructor or
/// the <see cref="AttachFixedStarService"/> hook (the latter exists
/// to break the construction-time cycle when the fixed-star service
/// itself depends on this <see cref="SiderealService"/> for its
/// own sidereal-projection path). When no source is configured,
/// star-anchored modes throw <see cref="NotSupportedException"/>
/// with a clear configuration hint.
/// </para>
/// </remarks>
public sealed class SiderealService
{
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides _models;
    private FixedStarService? _fixedStarService;
    private SiderealConfiguration _configuration;
    /// <summary>
    /// Pre-cloned <see cref="AstronomicalModelOverrides"/> with the
    /// preset's defining precession model substituted in. Cached when
    /// <see cref="_configuration"/> changes so the precession-correction
    /// path in <see cref="ComputePrecessionCorrection"/> avoids the
    /// per-call <c>with</c>-record allocation that would otherwise leak
    /// into the M-14 zero-allocation hot-path budget for every sidereal
    /// <c>swe_calc</c>.
    /// </summary>
    private AstronomicalModelOverrides? _definingModelsForCorrection;

    public SiderealService(
        CalendarService calendar,
        AstronomicalModelOverrides? models = null,
        FixedStarService? fixedStarService = null)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _models = models ?? AstronomicalModelOverrides.Default;
        _fixedStarService = fixedStarService;
        AssignConfiguration(SiderealConfiguration.Default);
    }

    /// <summary>
    /// Wire (or replace) the fixed-star service after construction.
    /// Used by composition roots where <see cref="FixedStarService"/>
    /// itself takes a <see cref="SiderealService"/> dependency, which
    /// would otherwise be a constructor-time cycle. The first call must
    /// happen before any star-anchored sidereal mode is queried.
    /// </summary>
    /// <param name="fixedStarService">The service to attach.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fixedStarService"/> is null.</exception>
    public void AttachFixedStarService(FixedStarService fixedStarService)
    {
        _fixedStarService = fixedStarService ?? throw new ArgumentNullException(nameof(fixedStarService));
    }

    /// <summary>Currently active configuration.</summary>
    public SiderealConfiguration Configuration => _configuration;

    /// <summary>
    /// Set the active sidereal mode. Mirrors <c>swe_set_sid_mode</c>
    /// (sweph.c#L2861-L2928).
    /// </summary>
    /// <remarks>
    /// <b>Build-time configuration only.</b> The mutator is
    /// <see langword="internal"/>: only the composition root
    /// (<see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder"/>)
    /// flips the mode, before the owning
    /// <see cref="SharpAstrology.SwissEphemerides.EphemerisContext"/> is
    /// published. Mutating after publication would race with concurrent
    /// readers and is unsupported by the per-context thread-safety
    /// contract. To switch modes at runtime, build a second context.
    /// </remarks>
    /// <param name="mode">Predefined mode or <see cref="SiderealMode.UserDefined"/>.</param>
    /// <param name="t0">User T0 (Julian Day). Only consulted when
    /// <paramref name="mode"/> is <see cref="SiderealMode.UserDefined"/>.</param>
    /// <param name="ayanT0">User ayanamsha at <paramref name="t0"/>, degrees.
    /// Only consulted when <paramref name="mode"/> is <see cref="SiderealMode.UserDefined"/>.</param>
    /// <param name="flags">Modifier flags (<see cref="SiderealFlags"/>).</param>
    internal void SetMode(SiderealMode mode, double t0 = 0, double ayanT0 = 0, SiderealFlags flags = SiderealFlags.None)
    {
        // Standard equinoxes always use ECL_T0 projection
        // (sweph.c#L2870-L2879).
        if (mode is SiderealMode.J2000 or SiderealMode.J1900 or SiderealMode.B1950 or SiderealMode.SkydramMardyks)
            flags |= SiderealFlags.EclipticOfT0;

        if (mode == SiderealMode.UserDefined)
        {
            var t0IsUt = (flags & SiderealFlags.UserT0IsUt) != 0;
            AssignConfiguration(new SiderealConfiguration(mode, flags, t0, ayanT0, t0IsUt, PrecOffset: null));
        }
        else
        {
            // Reject anything that is not a known predefined mode (mirrors
            // sweph.c#L2897-L2898 — fall back to Fagan/Bradley).
            if (!IsPredefined(mode))
                mode = SiderealMode.FaganBradley;
            var preset = AyanamshaTable.Presets[(int)mode];
            AssignConfiguration(new SiderealConfiguration(
                mode,
                flags,
                preset.T0,
                preset.AyanT0,
                preset.T0IsUt,
                preset.PrecOffset));
        }
    }

    /// <summary>
    /// Returns the display name of a predefined ayanamsha. Mirrors
    /// <c>swe_get_ayanamsa_name</c> (sweph.c#L7127).
    /// </summary>
    /// <returns>The C-library display name, or <c>null</c> for
    /// <see cref="SiderealMode.UserDefined"/> / out-of-range values.</returns>
    public string? GetName(SiderealMode mode)
    {
        if (!IsPredefined(mode))
            return null;
        return AyanamshaTable.Names[(int)mode];
    }

    /// <summary>
    /// Compute the ayanamsha at <paramref name="jdEt"/> (TT) in degrees.
    /// Mirrors <c>swe_get_ayanamsa_ex</c> (sweph.c#L2930). Includes the
    /// nutation-in-longitude term unless <see cref="EphemerisFlags.NoNutation"/>
    /// is set.
    /// </summary>
    public double GetAyanamsa(JulianDay jdEt, EphemerisFlags flags = EphemerisFlags.None)
    {
        var daya = ComputeBareAyanamsa(jdEt, flags);
        // swe_get_ayanamsa_ex adds Δψ when nutation is requested
        // (sweph.c#L2935-L2944).
        if ((flags & EphemerisFlags.NoNutation) == 0)
        {
            var nut = Nutation.Compute(jdEt.Value, _models);
            daya += nut.DeltaPsiRad * AstronomicalConstants.RadToDeg;
        }
        return daya;
    }

    /// <summary>
    /// Compute the ayanamsha at <paramref name="jdUt"/> (UT) in degrees.
    /// Mirrors <c>swe_get_ayanamsa_ex_ut</c> (sweph.c#L3226).
    /// </summary>
    public double GetAyanamsaUt(JulianDay jdUt, EphemerisFlags flags = EphemerisFlags.None)
    {
        var deltaT = _calendar.DeltaT(jdUt);
        return GetAyanamsa(new JulianDay(jdUt.Value + deltaT), flags);
    }

    /// <summary>
    /// Compute ayanamsha and its time-derivative (deg/day) at
    /// <paramref name="jdEt"/>. Mirrors <c>swi_get_ayanamsa_with_speed</c>
    /// (sweph.c#L3210). The speed is a centred finite difference over a
    /// 0.001-day interval, matching the C library exactly.
    /// </summary>
    /// <remarks>
    /// The C implementation uses a backward difference (t and t-0.001).
    /// We mirror that exactly so signed numerical agreement is bit-identical.
    /// </remarks>
    public (double Ayanamsha, double Speed) GetAyanamsaWithSpeed(JulianDay jdEt, EphemerisFlags flags = EphemerisFlags.None)
    {
        const double tIntv = 0.001;
        // C code: nutation is intentionally skipped here (the bare-ayanamsha
        // path is what the calc pipeline subtracts). The nutation is applied
        // separately by the pipeline via the standard nutation step.
        // swi_get_ayanamsa_ex internally forces SEFLG_NONUT (sweph.c#L3014).
        var bareFlags = flags | EphemerisFlags.NoNutation;
        var daya = ComputeBareAyanamsa(jdEt, bareFlags);
        var dayaPrev = ComputeBareAyanamsa(new JulianDay(jdEt.Value - tIntv), bareFlags);
        var speed = (daya - dayaPrev) / tIntv;
        return (daya, speed);
    }

    /// <summary>
    /// Subtract ayanamsha from a tropical ecliptic-of-date longitude/speed
    /// pair. Mirrors the inline <c>app_pos_rest</c> step at
    /// <c>sweph.c#L2820-L2835</c> (the "traditional algorithm" branch).
    /// </summary>
    /// <remarks>
    /// Both <paramref name="longitudeRad"/> and <paramref name="longitudeSpeedRadPerDay"/>
    /// are in radians. The result is normalised into [0, 2π).
    /// </remarks>
    internal (double LongitudeRad, double LongitudeSpeedRadPerDay) ApplyToLongitude(
        double longitudeRad,
        double longitudeSpeedRadPerDay,
        JulianDay jdEt,
        EphemerisFlags flags)
    {
        var (daya, speed) = GetAyanamsaWithSpeed(jdEt, flags);
        var lon = AngleMath.NormalizeRadians(longitudeRad - daya * AstronomicalConstants.DegToRad);
        var lonSpeed = longitudeSpeedRadPerDay - speed * AstronomicalConstants.DegToRad;
        return (lon, lonSpeed);
    }

    private static bool IsPredefined(SiderealMode mode)
    {
        var v = (int)mode;
        return v >= 0 && v < AyanamshaTable.PredefinedCount;
    }

    /// <summary>
    /// Bare ayanamsha computation without nutation (NONUT-equivalent).
    /// Mirrors the body of <c>swi_get_ayanamsa_ex</c> (sweph.c#L3002-L3208)
    /// minus the fixed-star branches and the nutation top-up that the public
    /// wrapper adds.
    /// </summary>
    private double ComputeBareAyanamsa(JulianDay jdEt, EphemerisFlags flags)
    {
        if (AyanamshaTable.TryGetStarAnchoredPreset(_configuration.Mode, out var starPreset))
            return ComputeStarAnchoredAyanamsa(jdEt, flags, starPreset);

        var t0 = _configuration.T0;
        if (_configuration.T0IsUt)
            t0 += _calendar.DeltaT(new JulianDay(t0));

        Span<double> x = stackalloc double[3];
        double resultDeg;

        if ((_configuration.Flags & SiderealFlags.EclipticOfDate) == 0)
        {
            // Traditional ECL_T0 branch (sweph.c#L3143-L3173).
            // Vernal point (tjd) → J2000 → t0 → ecliptic-of-t0 → polar.
            x[0] = 1; x[1] = 0; x[2] = 0;
            if (jdEt.Value != AstronomicalConstants.J2000)
                Precession.Apply(x, jdEt.Value, AstronomicalConstants.J2000, _models);
            Precession.Apply(x, AstronomicalConstants.J2000, t0, _models);
            var eps = Precession.MeanObliquity(t0, _models);
            FrameTransform.EquatorialToEcliptic(x, eps);
            // To polar; longitude is x[0] of the polar form. We compute it
            // directly to avoid the Vec3 round-trip allocation.
            var lonRad = System.Math.Atan2(x[1], x[0]);
            resultDeg = -lonRad * AstronomicalConstants.RadToDeg + _configuration.AyanT0;
        }
        else
        {
            // ECL_DATE branch (sweph.c#L3174-L3202).
            // Start from the ayan_t0 vector at t0, project equatorial via
            // -eps_t0, precess t0 → J2000 → tjd, project ecliptic-of-date.
            var ayanT0Rad = AngleMath.NormalizeRadians(_configuration.AyanT0 * AstronomicalConstants.DegToRad);
            // Polar (lon=ayanT0Rad, lat=0, r=1) → cartesian.
            x[0] = System.Math.Cos(ayanT0Rad);
            x[1] = System.Math.Sin(ayanT0Rad);
            x[2] = 0;
            var epsT0 = Precession.MeanObliquity(t0, _models);
            // Equatorial of t0 — coortrf with -eps (i.e. ecliptic→equator).
            FrameTransform.EclipticToEquatorial(x, epsT0);
            if (t0 != AstronomicalConstants.J2000)
                Precession.Apply(x, t0, AstronomicalConstants.J2000, _models);
            Precession.Apply(x, AstronomicalConstants.J2000, jdEt.Value, _models);
            var epsTjd = Precession.MeanObliquity(jdEt.Value, _models);
            FrameTransform.EquatorialToEcliptic(x, epsTjd);
            var lonRad = System.Math.Atan2(x[1], x[0]);
            resultDeg = lonRad * AstronomicalConstants.RadToDeg;
        }

        // get_aya_correction (sweph.c#L2960-L3000) — applied unless
        // NO_PREC_OFFSET is set, the preset has no defined precession model,
        // or t0 is exactly J2000.
        var corr = ComputePrecessionCorrection(flags);
        return Degnorm(resultDeg - corr);
    }

    /// <summary>
    /// Star-anchored branch of <c>swi_get_ayanamsa_ex</c>
    /// (sweph.c#L3050-L3149). Pins a chosen ecliptic longitude to a
    /// chosen fixed star at the requested epoch:
    /// <c>ayanamsha = star_lon_tropical_at(jdEt) − target</c>. Mirrors
    /// the per-mode flag setup at sweph.c#L3018-L3027 — <c>SEFLG_NONUT</c>
    /// is forced; the True-* family additionally lets
    /// <c>SEFLG_TRUEPOS</c>/<c>NOABERR</c>/<c>NOGDEFL</c> pass through
    /// from the caller (sweph.c#L3027), and the galactic-equator class
    /// forces <c>SEFLG_TRUEPOS</c> to keep the galactic pole free of
    /// aberration and light deflection (sweph.c#L3018).
    /// </summary>
    private double ComputeStarAnchoredAyanamsa(JulianDay jdEt, EphemerisFlags flags, StarAnchoredPreset preset)
    {
        if (_fixedStarService is null)
            throw new NotSupportedException(
                $"SiderealMode {_configuration.Mode} requires a fixed-star catalogue. "
                + "Wire one up via EphemerisContextBuilder.UseFixedStarCatalog(path) (or pass an "
                + "FixedStarService to the SiderealService constructor / AttachFixedStarService).");

        // sweph.c#L3014-L3027: bare path ⇒ ephemeris source + NONUT; pass-through flags for
        // the True-* family so the user's TRUEPOS / NOABERR / NOGDEFL toggles still reach
        // the star pipeline. The Sidereal flag is intentionally not forwarded — we want the
        // tropical star longitude here.
        const EphemerisFlags EphMask =
            EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph;
        var starFlags = (flags & EphMask) | EphemerisFlags.NoNutation;
        if ((starFlags & EphMask) == 0) starFlags |= EphemerisFlags.MoshierEph;

        const EphemerisFlags TrueStarPassThrough =
            EphemerisFlags.TruePosition | EphemerisFlags.NoAberration | EphemerisFlags.NoGravDeflection;

        switch (preset.Kind)
        {
            case StarAnchoredKind.Ecliptic:
                starFlags |= flags & TrueStarPassThrough;
                break;
            case StarAnchoredKind.EclipticTruePosition:
                // sweph.c#L3018: galactic-equator modes force SEFLG_TRUEPOS.
                starFlags |= EphemerisFlags.TruePosition;
                break;
            case StarAnchoredKind.EquatorialArmcToMc:
                // sweph.c#L3115: SgrA* with SEFLG_EQUATORIAL → x[0] is RA in degrees.
                // True-* pass-through still applies (sweph.c#L3115 reuses iflag_true).
                starFlags |= EphemerisFlags.Equatorial | (flags & TrueStarPassThrough);
                break;
        }

        var star = _fixedStarService.Compute(preset.StarName, jdEt, starFlags);

        // FixedStarService returns the position triple in degrees by default
        // (no SEFLG_RADIANS). Position.X is ecliptic longitude (or RA when
        // EQUATORIAL was set); Position.Y is latitude / declination.
        double anchorDeg;
        if (preset.Kind == StarAnchoredKind.EquatorialArmcToMc)
        {
            // sweph.c#L3116-L3118: project RA onto the ecliptic at jdEt via armc_to_mc.
            // C uses iflag-aware obliquity (swi_epsiln(tjd_et, iflag) — true ε if nutation
            // is in play); but this code path forced NONUT, so true ε == mean ε.
            var epsRad = Precession.MeanObliquity(jdEt.Value, _models);
            var epsDeg = epsRad * AstronomicalConstants.RadToDeg;
            anchorDeg = HouseAscendantMath.ArmcToMc(star.Position.X, epsDeg);
        }
        else
        {
            anchorDeg = star.Position.X;
        }

        return Degnorm(anchorDeg - preset.TargetLongitudeDeg);
    }

    /// <summary>
    /// Mirrors <c>get_aya_correction</c> (sweph.c#L2960-L3000): adjust the
    /// ayanamsha for the difference between the precession model used to
    /// define it and the model used by the calc pipeline. Returns 0 when
    /// no correction applies.
    /// </summary>
    private double ComputePrecessionCorrection(EphemerisFlags flags)
    {
        if (_configuration.PrecOffset is not PrecessionModel definingModel)
            return 0;
        if ((_configuration.Flags & SiderealFlags.NoPrecessionOffset) != 0)
            return 0;
        if (_configuration.T0 == AstronomicalConstants.J2000)
            return 0;

        var currentModel = _models.PrecessionLongTerm;
        if (definingModel == currentModel)
            return 0;

        var t0 = _configuration.T0;
        if (_configuration.T0IsUt)
            t0 += _calendar.DeltaT(new JulianDay(t0));

        Span<double> x = stackalloc double[3];
        x[0] = 1; x[1] = 0; x[2] = 0;
        // Vernal point of t0 → J2000 with our current model.
        Precession.Apply(x, t0, AstronomicalConstants.J2000, _models);
        // Back from J2000 → t0 with the *defining* model. The clone is
        // cached on the service in AssignConfiguration; doing the
        // record-with allocation here would land on the per-call hot
        // path and break the M-14 zero-allocation budget.
        Precession.Apply(x, AstronomicalConstants.J2000, t0, _definingModelsForCorrection);

        var eps = Precession.MeanObliquity(t0, _models);
        FrameTransform.EquatorialToEcliptic(x, eps);
        var lonRad = System.Math.Atan2(x[1], x[0]);
        var corr = lonRad * AstronomicalConstants.RadToDeg;
        // C: signed value near 0; if > 350 subtract 360 (sweph.c#L2997).
        if (corr > 350)
            corr -= 360;
        return corr;
    }

    private static double Degnorm(double x)
    {
        x %= 360.0;
        if (x < 0) x += 360.0;
        return x;
    }

    /// <summary>
    /// Atomically swap <see cref="_configuration"/> and refresh the
    /// cached <see cref="_definingModelsForCorrection"/>. The clone
    /// is cheap (one record allocation per <c>SetMode</c> call) but
    /// would be hot-path-allocating if recomputed inside
    /// <see cref="ComputePrecessionCorrection"/> on every
    /// <see cref="GetAyanamsaWithSpeed"/> invocation.
    /// </summary>
    private void AssignConfiguration(SiderealConfiguration configuration)
    {
        _configuration = configuration;
        if (configuration.PrecOffset is PrecessionModel definingModel
            && definingModel != _models.PrecessionLongTerm)
        {
            _definingModelsForCorrection = _models with
            {
                PrecessionLongTerm = definingModel,
                PrecessionShortTerm = definingModel,
            };
        }
        else
        {
            _definingModelsForCorrection = null;
        }
    }
}
