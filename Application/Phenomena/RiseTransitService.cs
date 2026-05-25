// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference drivers:
//   /tmp/risetransref.c       — fast-path (rise_set_fast) cases
//   /tmp/riseref_slow.c       — slow-path / twilight / polar / fixstar / mtransit cases

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Application.Stars;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Rise / set / meridian-transit / twilight finder. Mirrors the C
/// library's <c>swe_rise_trans</c> dispatcher (swecl.c#L4355) and
/// implements both branches:
/// <list type="bullet">
/// <item>The <c>rise_set_fast</c> simple-arc algorithm at swecl.c#L4203
/// for Sun/Moon/planets/lunar-nodes at non-polar latitudes — ≤60° (≤65°
/// for the Sun).</item>
/// <item>The <c>swe_rise_trans_true_hor</c> full search at swecl.c#L4387
/// for high latitudes, twilight depressions, fixed stars, meridian
/// transits and explicit <see cref="RiseTransitFlags.ForceSlowMethod"/>
/// calls.</item>
/// </list>
/// </summary>
public sealed class RiseTransitService
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;
    private const double TwoHours = 1.0 / 12.0;

    private readonly BodyService _body;
    private readonly CalendarService _calendar;
    private readonly HorizontalCoordsService _horizontal;
    private readonly FixedStarService? _fixedStar;

    /// <summary>
    /// Constructs the service over its three required collaborators. The
    /// fixed-star overload of <see cref="Find(JulianDay, string, EphemerisFlags, RiseTransitFlags, GeographicLocation, double, double)"/>
    /// is unavailable through this constructor; pass a <see cref="FixedStarService"/>
    /// to the four-arg constructor to enable it.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Any of <paramref name="body"/> / <paramref name="calendar"/> /
    /// <paramref name="horizontal"/> is <see langword="null"/>.
    /// </exception>
    public RiseTransitService(BodyService body, CalendarService calendar, HorizontalCoordsService horizontal)
        : this(body, calendar, horizontal, fixedStar: null)
    {
    }

    /// <summary>
    /// Constructs the service with an attached <see cref="FixedStarService"/>,
    /// which enables the fixed-star overload of <see cref="Find(JulianDay, string, EphemerisFlags, RiseTransitFlags, GeographicLocation, double, double)"/>.
    /// </summary>
    public RiseTransitService(
        BodyService body,
        CalendarService calendar,
        HorizontalCoordsService horizontal,
        FixedStarService? fixedStar)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        _fixedStar = fixedStar;
    }

    /// <summary>
    /// Returns the next JD (UT) at which <paramref name="body"/> rises/sets
    /// / meridian-transits as requested by <paramref name="rsmi"/>. Reads
    /// <paramref name="atPressMbar"/> = 0 as "estimate atmospheric pressure
    /// from observer altitude" (matching <c>swe_rise_trans</c>'s convention).
    /// </summary>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> wrapping the located JD. When the
    /// body does not rise / set inside the search window (e.g. circumpolar
    /// object or polar night), the result carries <c>default(JulianDay)</c>
    /// together with a non-null <see cref="EphemerisResult{T}.Warning"/>
    /// describing the miss — mirroring the C library's <c>retc &gt; 0</c>
    /// "no event" soft-failure convention.
    /// </returns>
    public EphemerisResult<JulianDay> Find(
        JulianDay jdUtStart,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        RiseTransitFlags rsmi,
        GeographicLocation observer,
        double atPressMbar = 0.0,
        double atTempC = 15.0)
    {
        // Meridian-transit dispatch: own algorithm at calc_mer_trans.
        if ((rsmi & (RiseTransitFlags.UpperMeridianTransit | RiseTransitFlags.LowerMeridianTransit)) != 0)
        {
            return Wrap(MeridianTransitForBody(jdUtStart, body, ephemerisFlags, rsmi, observer));
        }

        if (UseFastPath(body, rsmi, observer))
        {
            if ((rsmi & (RiseTransitFlags.Rise | RiseTransitFlags.Set)) == 0) rsmi |= RiseTransitFlags.Rise;
            return Wrap(RiseSetFast(jdUtStart, body, ephemerisFlags, rsmi, observer, atPressMbar, atTempC));
        }
        return Wrap(RiseSetSlowForBody(jdUtStart, body, ephemerisFlags, rsmi, observer, atPressMbar, atTempC));
    }

    private static EphemerisResult<JulianDay> Wrap((JulianDay Jd, bool Found) raw) =>
        raw.Found
            ? EphemerisResult<JulianDay>.Ok(raw.Jd)
            : EphemerisResult<JulianDay>.WithWarning(default, "no rise/set/transit event inside the search window");

    /// <summary>
    /// Fixed-star overload. Routes through the slow path
    /// (<c>swe_rise_trans_true_hor</c>'s <c>do_fixstar</c> branch,
    /// swecl.c#L4455). The implementation requires a
    /// <see cref="FixedStarService"/> to have been injected at construction time;
    /// otherwise this throws <see cref="System.InvalidOperationException"/>.
    /// </summary>
    /// <param name="jdUtStart">Search anchor in UT.</param>
    /// <param name="starName">Catalogue lookup string. Same formats as
    /// <see cref="FixedStarService.Compute"/> — traditional name, <c>",bayer"</c>
    /// designation, or 1-based index.</param>
    /// <param name="ephemerisFlags">Frame / source / option flags. Only
    /// the <see cref="EphemerisFlags.MoshierEph"/> /
    /// <see cref="EphemerisFlags.SwissEph"/> / <see cref="EphemerisFlags.JplEph"/>
    /// triple plus <see cref="EphemerisFlags.NoNutation"/> /
    /// <see cref="EphemerisFlags.TruePosition"/> are consulted.</param>
    /// <param name="rsmi">Rise / set / transit / twilight flag combo.
    /// Twilight bits are ignored for fixed stars (mirrors the C: twilight
    /// depression is anchored to the Sun, not to the star itself).</param>
    /// <param name="observer">Observer geographic position.</param>
    /// <param name="atPressMbar">Atmospheric pressure in mbar; 0 = derive
    /// from observer altitude.</param>
    /// <param name="atTempC">Air temperature in degrees Celsius.</param>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> wrapping the located JD. When the
    /// star does not rise / set inside the search window, the result carries
    /// <c>default(JulianDay)</c> together with a non-null
    /// <see cref="EphemerisResult{T}.Warning"/>.
    /// </returns>
    public EphemerisResult<JulianDay> Find(
        JulianDay jdUtStart,
        string starName,
        EphemerisFlags ephemerisFlags,
        RiseTransitFlags rsmi,
        GeographicLocation observer,
        double atPressMbar = 0.0,
        double atTempC = 15.0)
    {
        if (starName is null) throw new ArgumentNullException(nameof(starName));
        if (_fixedStar is null)
            throw new InvalidOperationException(
                "Find(starName, ...) requires the four-arg constructor with a FixedStarService.");

        // Pre-fetch the star's apparent equatorial position once (mirrors
        // swecl.c#L4456). swe_rise_trans_true_hor passes iflag &= EPHMASK
        // | NONUT | TRUEPOS to swe_fixstar; we replicate.
        var starFlags = (ephemerisFlags & (EphemerisFlags.MoshierEph
                                           | EphemerisFlags.SwissEph
                                           | EphemerisFlags.JplEph
                                           | EphemerisFlags.NoNutation
                                           | EphemerisFlags.TruePosition))
                        | EphemerisFlags.Equatorial;
        if (starFlags == EphemerisFlags.Equatorial) starFlags |= EphemerisFlags.MoshierEph;
        var starPos = _fixedStar.ComputeUt(starName, jdUtStart, starFlags);

        // Twilight depression is anchored to the Sun's altitude, not the
        // star's — strip those bits for the star path (mirrors C, where
        // twilight only fires when ipl == SE_SUN at swecl.c#L4441).
        rsmi &= ~(RiseTransitFlags.CivilTwilight | RiseTransitFlags.NauticalTwilight | RiseTransitFlags.AstronomicalTwilight);

        if ((rsmi & (RiseTransitFlags.UpperMeridianTransit | RiseTransitFlags.LowerMeridianTransit)) != 0)
        {
            var fetcher = new RiseTransitSearch.StarFetcher(starPos.Position.X, starPos.Position.Y, starPos.Distance);
            var (t, ok) = RiseTransitSearch.MeridianTransit(
                ref fetcher, _calendar, jdUtStart.Value, observer.LongitudeDeg,
                isLowerTransit: (rsmi & RiseTransitFlags.LowerMeridianTransit) != 0);
            return ok
                ? EphemerisResult<JulianDay>.Ok(new JulianDay(t))
                : EphemerisResult<JulianDay>.WithWarning(default, "no rise/set/transit event inside the search window");
        }
        else
        {
            var fetcher = new RiseTransitSearch.StarFetcher(starPos.Position.X, starPos.Position.Y, starPos.Distance);
            var (t, ok) = RiseTransitSearch.RiseSetSlow(
                ref fetcher,
                jdUtStart.Value,
                isFixedStar: true,
                isSun: false,
                bodyDiameterMeters: 0.0,
                _horizontal,
                observer,
                rsmi,
                atPressMbar,
                atTempC,
                horHgtDeg: 0.0,
                inputFrame: HorizontalConversionInput.FromEquatorial);
            return ok
                ? EphemerisResult<JulianDay>.Ok(new JulianDay(t))
                : EphemerisResult<JulianDay>.WithWarning(default, "no rise/set/transit event inside the search window");
        }
    }

    private static bool UseFastPath(CelestialBody body, RiseTransitFlags rsmi, GeographicLocation observer)
    {
        if ((rsmi & RiseTransitFlags.ForceSlowMethod) != 0) return false;
        if ((rsmi & (RiseTransitFlags.CivilTwilight | RiseTransitFlags.NauticalTwilight | RiseTransitFlags.AstronomicalTwilight)) != 0) return false;
        if ((rsmi & (RiseTransitFlags.UpperMeridianTransit | RiseTransitFlags.LowerMeridianTransit)) != 0) return false;
        if (body > CelestialBody.TrueNode) return false;
        var absLat = Math.Abs(observer.LatitudeDeg);
        return absLat <= 60.0 || (body == CelestialBody.Sun && absLat <= 65.0);
    }

    private (JulianDay Jd, bool Found) RiseSetSlowForBody(
        JulianDay jdUtStart,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        RiseTransitFlags rsmi,
        GeographicLocation observer,
        double atPressMbar,
        double atTempC)
    {
        var bodyFlags = BodyFlagsForSlowPath(ephemerisFlags, rsmi, out var hasObserver, out var observerLoc, observer);
        var zeroEclLat = (rsmi & RiseTransitFlags.GeocentricNoEclipticLatitude) != 0;
        var bodyDiameter = BodyDiameterMeters(body);

        var fetcher = new RiseTransitSearch.PlanetFetcher(_body, body, bodyFlags,
            hasObserver, observerLoc, zeroEclLat);
        var inputFrame = zeroEclLat
            ? HorizontalConversionInput.FromEcliptic
            : HorizontalConversionInput.FromEquatorial;
        var (t, ok) = RiseTransitSearch.RiseSetSlow(
            ref fetcher,
            jdUtStart.Value,
            isFixedStar: false,
            isSun: body == CelestialBody.Sun,
            bodyDiameterMeters: bodyDiameter,
            _horizontal,
            observer,
            rsmi,
            atPressMbar,
            atTempC,
            horHgtDeg: 0.0,
            inputFrame: inputFrame);
        return ok ? (new JulianDay(t), true) : (default, false);
    }

    private (JulianDay Jd, bool Found) MeridianTransitForBody(
        JulianDay jdUtStart,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        RiseTransitFlags rsmi,
        GeographicLocation observer)
    {
        var bodyFlags = BodyFlagsForSlowPath(ephemerisFlags, rsmi, out var hasObserver, out var observerLoc, observer);
        var zeroEclLat = (rsmi & RiseTransitFlags.GeocentricNoEclipticLatitude) != 0;
        var fetcher = new RiseTransitSearch.PlanetFetcher(_body, body, bodyFlags,
            hasObserver, observerLoc, zeroEclLat);
        var (t, ok) = RiseTransitSearch.MeridianTransit(
            ref fetcher, _calendar, jdUtStart.Value, observer.LongitudeDeg,
            isLowerTransit: (rsmi & RiseTransitFlags.LowerMeridianTransit) != 0);
        return ok ? (new JulianDay(t), true) : (default, false);
    }

    /// <summary>
    /// Builds the body-fetch flag set used by both the meridian-transit and
    /// rise/set slow paths. Mirrors the masking at swecl.c#L4425-L4434
    /// and #L4701-L4703: keep the ephemeris source plus optional
    /// NoNutation/TruePosition; add Equatorial+Topocentric unless the caller
    /// requested ecliptic-no-latitude (Hindu) mode.
    /// </summary>
    private static EphemerisFlags BodyFlagsForSlowPath(
        EphemerisFlags ephemerisFlags,
        RiseTransitFlags rsmi,
        out bool hasObserver,
        out ObserverLocation observerLoc,
        GeographicLocation observer)
    {
        var iflag = ephemerisFlags & (EphemerisFlags.MoshierEph
                                       | EphemerisFlags.SwissEph
                                       | EphemerisFlags.JplEph
                                       | EphemerisFlags.NoNutation
                                       | EphemerisFlags.TruePosition);
        hasObserver = false;
        observerLoc = default;
        if ((rsmi & RiseTransitFlags.GeocentricNoEclipticLatitude) == 0)
        {
            iflag |= EphemerisFlags.Equatorial | EphemerisFlags.Topocentric;
            hasObserver = true;
            observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        }
        return iflag;
    }

    private static double BodyDiameterMeters(CelestialBody body)
    {
        var ipl = (int)body;
        return ipl < PlanetDiameters.Meters.Length ? PlanetDiameters.Meters[ipl] : 0.0;
    }

    private (JulianDay Jd, bool Found) RiseSetFast(
        JulianDay jdUtStart,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        RiseTransitFlags rsmi,
        GeographicLocation observer,
        double atPressMbar,
        double atTempC)
    {
        var facrise = (rsmi & RiseTransitFlags.Set) != 0 ? -1 : 1;
        var topo = (rsmi & RiseTransitFlags.GeocentricNoEclipticLatitude) == 0;
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        var bodyFlags = ephemerisFlags | EphemerisFlags.Equatorial;
        if (topo) bodyFlags |= EphemerisFlags.Topocentric;

        // Atmospheric pressure estimation when caller passes 0.
        if (atPressMbar == 0.0)
            atPressMbar = 1013.25 * System.Math.Pow(1 - 0.0065 * observer.AltitudeMeters / 288.0, 5.255);

        // Refraction term at the horizon (apparent → true at altitude ~0).
        // Mirrors swecl.c#L4284-L4285.
        var refr = ApparentRefractionAtHorizon(observer.AltitudeMeters, atPressMbar, atTempC);

        var jdUtSecond = false;
        var tjdUt = jdUtStart;
        var tjdUtOriginal = jdUtStart.Value;

        retry:
        var (raStartDeg, decStartDeg, _) = ComputeEquatorial(body, tjdUt, bodyFlags, observerLoc);

        // Semi-diurnal arc (sda) from declination + observer latitude.
        var sda = -System.Math.Tan(observer.LatitudeDeg * DegToRad) * System.Math.Tan(decStartDeg * DegToRad);
        if (sda >= 1) sda = 10;        // approximately circumpolar non-rising — give a fallback ~10° to allow refraction
        else if (sda <= -1) sda = 180; // always above horizon
        else sda = System.Math.Acos(sda) * RadToDeg;

        // Apparent sidereal time (mirrors swe_sidtime → apparent GMST).
        var armc = SiderealApparentDeg(tjdUt, observer.LongitudeDeg);
        var md = AngleMath.NormalizeDegrees(raStartDeg - armc);
        var mdrise = AngleMath.NormalizeDegrees(sda * facrise);
        var dmd = AngleMath.NormalizeDegrees(md - mdrise);
        if (dmd > 358) dmd -= 360;

        // Rough rising/setting time.
        var tr = tjdUt.Value + dmd / 360.0;

        var nloop = body == CelestialBody.Moon ? 4 : 2;
        for (var i = 0; i < nloop; i++)
        {
            var trJd = new JulianDay(tr);
            var (raDeg, decDeg, distAu) = ComputeEquatorial(body, trJd, bodyFlags, observerLoc);
            var rdi = ComputeDiscRadiusPlusRefr(body, distAu, rsmi, refr);
            var (xazAlt, _) = ToHorizontal(trJd, observer, atPressMbar, atTempC, raDeg, decDeg);
            var (xaz2Alt, _) = ToHorizontal(new JulianDay(tr + 0.001), observer, atPressMbar, atTempC, raDeg, decDeg);
            var dd = xaz2Alt - xazAlt;
            var dalt = xazAlt + rdi;
            var dt = dalt / dd / 1000.0;
            if (dt > 0.1) dt = 0.1;
            else if (dt < -0.1) dt = -0.1;
            tr -= dt;
        }

        // Restart the search forward by half a day if the found event is
        // before the user-supplied start (mirrors swecl.c#L4318-L4322).
        if (tr < tjdUtOriginal && !jdUtSecond)
        {
            tjdUt = new JulianDay(tjdUt.Value + 0.5);
            jdUtSecond = true;
            goto retry;
        }
        return (new JulianDay(tr), true);
    }

    private (double LonDeg, double LatDeg, double DistAu) ComputeEquatorial(
        CelestialBody body, JulianDay jdUt, EphemerisFlags flags, ObserverLocation observerLoc)
    {
        var bs = _body.ComputeUt(body, jdUt, flags, observerLoc);
        Span<double> cart = stackalloc double[6];
        Span<double> polar = stackalloc double[6];
        cart[0] = bs.Position.X; cart[1] = bs.Position.Y; cart[2] = bs.Position.Z;
        cart[3] = bs.Velocity.X; cart[4] = bs.Velocity.Y; cart[5] = bs.Velocity.Z;
        Polar.CartesianToPolarWithSpeed(cart, polar);
        return (polar[0] * RadToDeg, polar[1] * RadToDeg, polar[2]);
    }

    private (double TrueAlt, double ApparentAlt) ToHorizontal(
        JulianDay jdUt,
        GeographicLocation observer,
        double atPressMbar,
        double atTempC,
        double raDeg,
        double decDeg)
    {
        var r = _horizontal.ToHorizontal(jdUt,
            HorizontalConversionInput.FromEquatorial,
            observer,
            atPressMbar,
            atTempC,
            raDeg,
            decDeg);
        return (r.TrueAltitudeDeg, r.ApparentAltitudeDeg);
    }

    private double SiderealApparentDeg(JulianDay jdUt, double geoLonDeg)
    {
        // Apparent GMST: the C swe_sidtime computes true obliquity + nutation
        // internally; we mirror that via a (jdEt) trip through Precession +
        // Nutation, then plug both into CalendarService.SiderealTime.
        var jdEt = jdUt.Value + _calendar.DeltaT(jdUt);
        var meanEpsRad = Domain.Frames.Precession.MeanObliquity(jdEt);
        var nut = Domain.Frames.Nutation.Compute(jdEt);
        var epsTrueDeg = (meanEpsRad + nut.DeltaEpsilonRad) * RadToDeg;
        var nutLonDeg = nut.DeltaPsiRad * RadToDeg;
        var sidtHours = _calendar.SiderealTime(jdUt, epsTrueDeg, nutLonDeg);
        return AngleMath.NormalizeDegrees(sidtHours * 15.0 + geoLonDeg);
    }

    private static double ApparentRefractionAtHorizon(double geoAltMeters, double atPressMbar, double atTempC)
    {
        // swe_refrac_extended(0.000001, 0, atpress, attemp, lapse, APP_TO_TRUE).
        // Returns trueAlt; refraction = inputApparent − returnedTrue.
        var trueAlt = RefractionMath.RefracExtended(
            0.000001, geoAltMeters, atPressMbar, atTempC,
            RefractionMath.DefaultLapseRate,
            RefractionMath.Direction.ApparentToTrue, out var bundle);
        return bundle.ApparentAltitudeDeg - trueAlt;
    }

    private static double ComputeDiscRadiusPlusRefr(CelestialBody body, double distAu, RiseTransitFlags rsmi, double refr)
    {
        var ipl = (int)body;
        var dd = ipl < PlanetDiameters.Meters.Length ? PlanetDiameters.Meters[ipl] : 0.0;
        if ((rsmi & RiseTransitFlags.FixedDiscSize) != 0)
        {
            if (body == CelestialBody.Sun) distAu = 1.0;
            else if (body == CelestialBody.Moon) distAu = 0.00257;
        }
        var rdi = 0.0;
        if ((rsmi & RiseTransitFlags.DiscCenter) == 0)
            rdi = System.Math.Asin(dd / 2.0 / AstronomicalConstants.AstronomicalUnitMeters / distAu) * RadToDeg;
        if ((rsmi & RiseTransitFlags.DiscBottom) != 0)
            rdi = -rdi;
        if ((rsmi & RiseTransitFlags.NoRefraction) == 0)
            rdi += refr;
        return rdi;
    }
}
