// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference drivers:
//   /tmp/gauquelinref.c — golden values for the imeth=0 / imeth=1 paths
//                         (Berlin, JD 2460310.5, Sun..Saturn).
//   /tmp/gauqref.c      — golden values for the imeth=2..5 rise/set paths.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Houses;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Method selector for <see cref="GauquelinSectorService.Compute"/>. Mirrors
/// the <c>imeth</c> parameter of <c>swe_gauquelin_sector</c>
/// (swecl.c#L6304-L6305).
/// </summary>
public enum GauquelinMethod
{
    /// <summary>Geometric, with ecliptic latitude. Default.</summary>
    Geometric = 0,
    /// <summary>Geometric, latitude forced to zero (longitude only).</summary>
    GeometricLongitudeOnly = 1,
    /// <summary>From rise/set times of the disc centre, no refraction.</summary>
    FromRiseSet = 2,
    /// <summary>From rise/set times of the disc centre, with refraction.</summary>
    FromRiseSetRefraction = 3,
    /// <summary>From rise/set times of the disc bottom (lower limb), no refraction.</summary>
    FromRiseSetDiscBottom = 4,
    /// <summary>From rise/set times of the disc bottom (lower limb), with refraction.</summary>
    FromRiseSetDiscBottomRefraction = 5,
}

/// <summary>
/// Gauquelin-sector service. Equivalent to <c>swe_gauquelin_sector</c>.
/// Geometric methods (<see cref="GauquelinMethod.Geometric"/> and
/// <see cref="GauquelinMethod.GeometricLongitudeOnly"/>) build ARMC from
/// apparent sidereal time and observer longitude, fetch the body's
/// ecliptic-of-date longitude / latitude through <see cref="BodyService"/>,
/// and read off the sector via the <c>'G'</c> house-position machinery in
/// <see cref="HouseService"/>. Rise/set-based methods
/// (<see cref="GauquelinMethod.FromRiseSet"/>..<see cref="GauquelinMethod.FromRiseSetDiscBottomRefraction"/>)
/// dispatch through an injected <see cref="RiseTransitService"/> and
/// linearly interpolate between the bracketing rise and set events
/// (swecl.c#L6360-L6427); without one they throw
/// <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class GauquelinSectorService
{
    private const double RadToDeg = AstronomicalConstants.RadToDeg;

    private readonly BodyService _body;
    private readonly CalendarService _calendar;
    private readonly HouseService _houses;
    private readonly RiseTransitService? _rise;
    private readonly AstronomicalModelOverrides _models;

    /// <summary>
    /// Constructs the service over a body service, a calendar service
    /// (used for ΔT and apparent sidereal time), and a house service
    /// (used for the Gauquelin sector lookup). Rise/set-based methods
    /// (<see cref="GauquelinMethod.FromRiseSet"/>..<see cref="GauquelinMethod.FromRiseSetDiscBottomRefraction"/>)
    /// will throw <see cref="InvalidOperationException"/> when invoked
    /// through this overload — pass a <see cref="RiseTransitService"/>
    /// to the four-arg constructor to enable them.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Any of <paramref name="body"/> / <paramref name="calendar"/> /
    /// <paramref name="houses"/> is <see langword="null"/>.
    /// </exception>
    public GauquelinSectorService(
        BodyService body,
        CalendarService calendar,
        HouseService houses,
        AstronomicalModelOverrides? models = null)
        : this(body, calendar, houses, riseTransit: null, models)
    {
    }

    /// <summary>
    /// Constructs the service with an attached <see cref="RiseTransitService"/>,
    /// enabling the rise/set-based Gauquelin methods 2..5.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Any of <paramref name="body"/> / <paramref name="calendar"/> /
    /// <paramref name="houses"/> is <see langword="null"/>.
    /// </exception>
    public GauquelinSectorService(
        BodyService body,
        CalendarService calendar,
        HouseService houses,
        RiseTransitService? riseTransit,
        AstronomicalModelOverrides? models = null)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _rise = riseTransit;
        _models = models ?? AstronomicalModelOverrides.Default;
    }

    /// <summary>
    /// Returns the Gauquelin sector (1..36) for <paramref name="body"/> at
    /// <paramref name="jdUt"/> as seen from <paramref name="observer"/>.
    /// </summary>
    public double Compute(
        JulianDay jdUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        GauquelinMethod method,
        GeographicLocation observer,
        double atPressMbar = 0.0,
        double atTempC = 15.0)
    {
        switch (method)
        {
            case GauquelinMethod.Geometric:
            case GauquelinMethod.GeometricLongitudeOnly:
                return ComputeGeometric(jdUt, body, ephemerisFlags, method, observer);
            case GauquelinMethod.FromRiseSet:
            case GauquelinMethod.FromRiseSetRefraction:
            case GauquelinMethod.FromRiseSetDiscBottom:
            case GauquelinMethod.FromRiseSetDiscBottomRefraction:
                return ComputeFromRiseSet(jdUt, body, ephemerisFlags, method, observer, atPressMbar, atTempC);
            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown GauquelinMethod.");
        }
    }

    private double ComputeGeometric(
        JulianDay jdUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        GauquelinMethod method,
        GeographicLocation observer)
    {
        // 1) ΔT to Terrestrial Time.
        var dt = _calendar.DeltaT(jdUt);
        var jdEt = new JulianDay(jdUt.Value + dt);

        // 2) Mean obliquity + nutation (Δψ in long, Δε in obliquity).
        var meanEpsRad = Precession.MeanObliquity(jdEt.Value, _models);
        var nut = Nutation.Compute(jdEt.Value, _models);
        var trueEpsDeg = (meanEpsRad + nut.DeltaEpsilonRad) * RadToDeg;
        var nutLonDeg = nut.DeltaPsiRad * RadToDeg;

        // 3) Apparent sidereal time at Greenwich (in hours of arc) → degrees,
        //    then add observer longitude. Mirrors swecl.c#L6344.
        var sidtHours = _calendar.SiderealTime(jdUt, trueEpsDeg, nutLonDeg);
        var armcDeg = AngleMath.NormalizeDegrees(sidtHours * 15.0 + observer.LongitudeDeg);

        // 4) Body's ecliptic-of-date polar position via BodyService. The C
        //    code calls swe_calc(t_et, ipl, iflag, x0, serr); we mirror with
        //    the same flags (caller passes them).
        var bs = _body.Compute(body, jdEt, ephemerisFlags);
        Span<double> cart = stackalloc double[6];
        cart[0] = bs.Position.X; cart[1] = bs.Position.Y; cart[2] = bs.Position.Z;
        cart[3] = bs.Velocity.X; cart[4] = bs.Velocity.Y; cart[5] = bs.Velocity.Z;
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);
        var lonDeg = polar[0] * RadToDeg;
        var latDeg = polar[1] * RadToDeg;
        if (method == GauquelinMethod.GeometricLongitudeOnly) latDeg = 0.0;

        // 5) HousePosition with system 'G' (Gauquelin) — returns 1..36.
        return _houses.HousePosition(armcDeg, observer.LatitudeDeg, trueEpsDeg, HouseSystem.Gauquelin, lonDeg, latDeg);
    }

    /// <summary>
    /// Rise/set-based Gauquelin sectors. Mirrors swecl.c#L6360-L6427:
    /// <list type="number">
    /// <item>Map the four imeth values to <c>SE_BIT_DISC_CENTER</c> /
    /// <c>SE_BIT_NO_REFRACTION</c> bits.</item>
    /// <item>Locate the next rise after <paramref name="jdUt"/>, then the
    /// next set after <paramref name="jdUt"/>.</item>
    /// <item>If <c>tret[rise] &lt; tret[set]</c> the body was below the
    /// horizon at <paramref name="jdUt"/> — back up 1.2 days and locate the
    /// previous set as the lower bracket. Sectors 19..36 (below horizon)
    /// are linearly interpolated between previous-set and next-rise.</item>
    /// <item>Otherwise the body was above the horizon — back up 1.2 days
    /// and locate the previous rise. Sectors 1..18 are linearly
    /// interpolated between previous-rise and next-set.</item>
    /// <item>If either bracket is missing (circumpolar / polar night) the
    /// C library returns ERR with serr "rise or set not found"; we mirror
    /// by throwing <see cref="InvalidOperationException"/> with the same
    /// message — the caller can pre-flag polar latitudes if it wants the
    /// quieter geometric path.</item>
    /// </list>
    /// </summary>
    private double ComputeFromRiseSet(
        JulianDay jdUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        GauquelinMethod method,
        GeographicLocation observer,
        double atPressMbar,
        double atTempC)
    {
        if (_rise is null)
            throw new InvalidOperationException(
                "Rise/set-based Gauquelin methods (2..5) require a RiseTransitService — "
                + "use the four-arg GauquelinSectorService constructor that accepts one.");

        // Map imeth → rsmi bits. swecl.c#L6360-L6363:
        //   imeth ∈ {2,4} → NO_REFRACTION
        //   imeth ∈ {2,3} → DISC_CENTER (otherwise disc-bottom)
        var risemeth = (RiseTransitFlags)0;
        if (method == GauquelinMethod.FromRiseSet || method == GauquelinMethod.FromRiseSetDiscBottom)
            risemeth |= RiseTransitFlags.NoRefraction;
        if (method == GauquelinMethod.FromRiseSet || method == GauquelinMethod.FromRiseSetRefraction)
            risemeth |= RiseTransitFlags.DiscCenter;
        else
            risemeth |= RiseTransitFlags.DiscBottom;

        // Strip ephemeris-source bits to match the C: epheflag = iflag & SEFLG_EPHMASK.
        var epheFlags = ephemerisFlags & (EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph);

        // Locate next rise and next set after jd_ut.
        var nextRiseRes = _rise.Find(jdUt, body, epheFlags,
            RiseTransitFlags.Rise | risemeth, observer, atPressMbar, atTempC);
        var nextSetRes = _rise.Find(jdUt, body, epheFlags,
            RiseTransitFlags.Set | risemeth, observer, atPressMbar, atTempC);
        var riseFound = !nextRiseRes.HasWarning;
        var setFound = !nextSetRes.HasWarning;
        var nextRise = nextRiseRes.Value;
        var nextSet = nextSetRes.Value;

        bool aboveHorizon;
        JulianDay lower, upper;

        if (riseFound && setFound && nextRise.Value < nextSet.Value)
        {
            // Body is below the horizon at jdUt — bracket between previous set and next rise.
            aboveHorizon = false;
            var backStart = new JulianDay(nextSet.Value - 1.2);
            var prevSetRes = _rise.Find(backStart, body, epheFlags,
                RiseTransitFlags.Set | risemeth, observer, atPressMbar, atTempC);
            if (prevSetRes.HasWarning)
                throw new InvalidOperationException("rise or set not found");
            lower = prevSetRes.Value;
            upper = nextRise;
        }
        else if (riseFound && setFound)
        {
            // Body is above the horizon at jdUt — bracket between previous rise and next set.
            aboveHorizon = true;
            var backStart = new JulianDay(nextRise.Value - 1.2);
            var prevRiseRes = _rise.Find(backStart, body, epheFlags,
                RiseTransitFlags.Rise | risemeth, observer, atPressMbar, atTempC);
            if (prevRiseRes.HasWarning)
                throw new InvalidOperationException("rise or set not found");
            lower = prevRiseRes.Value;
            upper = nextSet;
        }
        else
        {
            // Either rise or set was not found within the search window —
            // C returns ERR with serr "rise or set not found".
            throw new InvalidOperationException("rise or set not found");
        }

        // Linear interpolation between bracketing events. Mirrors
        // swecl.c#L6416-L6420:
        //   above_horizon=true  → sec = (t-rise)/(set-rise) * 18 + 1
        //   above_horizon=false → sec = (t-set)/(rise-set) * 18 + 19
        var span = upper.Value - lower.Value;
        var frac = (jdUt.Value - lower.Value) / span;
        return aboveHorizon ? frac * 18.0 + 1.0 : frac * 18.0 + 19.0;
    }
}
