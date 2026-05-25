// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputeWhere               — swe_lun_occult_where        (swecl.c#L606)
//   FindNextGlobal             — swe_lun_occult_when_glob    (swecl.c#L1572)
//   FindNextLocal              — swe_lun_occult_when_loc     (swecl.c#L2071)
//   OccultWhenLoc (helper)     — occult_when_loc             (swecl.c#L2412)
//   EclipseWhere (helper)      — eclipse_where               (swecl.c#L640)
//   EclipseHow   (helper)      — eclipse_how                 (swecl.c#L967)
//   FetchBodyOrStar            — calc_planet_star            (swecl.c#L888)
//   GeoAltMin / GeoAltMax      — SEI_ECL_GEOALT_MIN/MAX
//
// Reference drivers:
//  /tmp/lunoccultwhereref.c — golden values for `swe_lun_occult_where`
//    at the geocentric maxima of recent planet occultations.
//  /tmp/lunoccultwhenref.c — golden values for
//    `swe_lun_occult_when_glob` (forward, backward, total-only filter,
//    annular-throws) and `swe_lun_occult_when_loc` (Auckland Saturn,
//    Reykjavik Mercury, Sydney Mars, Auckland Saturn backward).
//  /tmp/lunoccult_star_ref.c — golden values for the M-16 star-anchored
//    overloads (Aldebaran / Antares / Regulus / Spica).
//
// Star handling: stars are point sources (drad = 0); the local-search
// star branch sets contacts 1/4 equal to contacts 2/3 because a point
// source has no penumbra. Stars whose ecliptic latitude exceeds 7° are
// rejected early — they can never be occulted by the Moon.
//
// The geometry shares structure with SolarEclipseService — kept duplicated
// while the Solar service stabilises; a future refactor can extract the
// shared geometry into an `EclipseWhereHowEngine`.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Application.Stars;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Stars;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Lunar-occultation service. Equivalent to the C library's
/// <c>swe_lun_occult_where</c> / <c>swe_lun_occult_when_glob</c> /
/// <c>swe_lun_occult_when_loc</c> entry points — covers single-time
/// geometry (<see cref="ComputeGlobalAt(JulianDay, CelestialBody, EphemerisFlags)"/>)
/// and global / local occultation search. Supports any body whose physical
/// diameter is recorded in <see cref="EclipseConstants.BodyRadiusAu"/>; the
/// <c>starName</c>-overloads handle the C library's star branch
/// (drad = 0, point source). The caller-supplied <c>ifl</c> is masked
/// down to the ephemeris-source bits before reaching the inner pipeline.
/// </summary>
public sealed class LunarOccultationService
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;
    private const double AuKm = AstronomicalConstants.AstronomicalUnitMeters / 1000.0;

    private const EphemerisFlags EphMask =
        EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph;

    /// <summary>Minimum permitted observer altitude (m).</summary>
    private const double GeoAltMin = -500.0;

    /// <summary>Maximum permitted observer altitude (m).</summary>
    private const double GeoAltMax = 25_000.0;

    /// <summary>Earth equatorial radius (km) used by the parabola sweeps.</summary>
    private const double EarthEquatorialRadiusKm = 6378.140;

    /// <summary>
    /// Defensive cap on the next-try retry loop. Each retry advances by
    /// 20 days (or 1 day after a long-latitude reject), so 10 000 steps
    /// covers ~600 years — far beyond any realistic occultation window
    /// but still bounded against a degenerate input.
    /// </summary>
    private const int MaxRetrySteps = 10_000;

    private const double TwoHoursInDays = 2.0 / 24.0;
    private const double TenMinutesInDays = 10.0 / 24.0 / 60.0;
    private const double TwoMinutesInDays = 2.0 / 24.0 / 60.0;
    private const double TenSecondsInDays = 10.0 / 24.0 / 60.0 / 60.0;

    /// <summary>Empirical fudge factor on the Moon's angular radius for
    /// 2nd / 3rd contact accuracy.</summary>
    private const double MoonRadiusFudgeContacts = 0.99916;

    /// <summary>Maximum tolerated star ecliptic latitude (degrees). Stars
    /// above this limit can never be occulted by the Moon — even with
    /// lunar parallax and stellar proper motion factored in.</summary>
    private const double StarMaxEclipticLatitudeDeg = 7.0;

    private readonly BodyService _body;
    private readonly CalendarService _calendar;
    private readonly HorizontalCoordsService _horizontal;
    private readonly RiseTransitService? _riseTransit;
    private readonly FixedStarService? _stars;

    /// <summary>
    /// Constructs the service over a body service, a calendar service
    /// (used for ΔT and sidereal time) and a horizontal-coords service
    /// (used to check whether the place of maximum sees the
    /// occultation above the horizon). This three-arg overload covers
    /// <see cref="ComputeGlobalAt(JulianDay, CelestialBody, EphemerisFlags)"/>
    /// and <see cref="FindNextGlobal(JulianDay, CelestialBody, EphemerisFlags, EclipseTypeFlags, bool)"/>.
    /// To call <see cref="FindNextLocal(JulianDay, CelestialBody, EphemerisFlags, GeographicLocation, bool)"/>
    /// use the four-arg constructor that also accepts a rise/transit
    /// service; the <c>starName</c> overloads further require the
    /// five-arg constructor with a <see cref="FixedStarService"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Any of <paramref name="body"/> / <paramref name="calendar"/> /
    /// <paramref name="horizontal"/> is <see langword="null"/>.
    /// </exception>
    public LunarOccultationService(
        BodyService body,
        CalendarService calendar,
        HorizontalCoordsService horizontal)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        _riseTransit = null;
        _stars = null;
    }

    /// <summary>
    /// Four-arg constructor that also wires the
    /// <see cref="RiseTransitService"/> required by
    /// <see cref="FindNextLocal(JulianDay, CelestialBody, EphemerisFlags, GeographicLocation, bool)"/>.
    /// Without it, the local-search method throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public LunarOccultationService(
        BodyService body,
        CalendarService calendar,
        HorizontalCoordsService horizontal,
        RiseTransitService riseTransit)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        _riseTransit = riseTransit ?? throw new ArgumentNullException(nameof(riseTransit));
        _stars = null;
    }

    /// <summary>
    /// Full-functionality constructor that also wires the
    /// <see cref="FixedStarService"/> required by the <c>starName</c>
    /// overloads. Without it, the star-anchored methods throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public LunarOccultationService(
        BodyService body,
        CalendarService calendar,
        HorizontalCoordsService horizontal,
        RiseTransitService riseTransit,
        FixedStarService stars)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        _riseTransit = riseTransit ?? throw new ArgumentNullException(nameof(riseTransit));
        _stars = stars ?? throw new ArgumentNullException(nameof(stars));
    }

    /// <summary>
    /// Determines the geographic position at which the Moon's occultation
    /// of <paramref name="body"/> reaches its maximum at <paramref name="jdUt"/>,
    /// the eclipse-type classification at that point, the per-observer
    /// attributes there, and the fundamental-plane shadow geometry.
    /// Mirrors <c>swe_lun_occult_where</c>.
    /// </summary>
    /// <param name="jdUt">Universal-time Julian Day at which the geometry is evaluated.</param>
    /// <param name="body">
    /// Body being occulted by the Moon. The Sun is also accepted —
    /// the geometry is then identical to <c>swe_sol_eclipse_where</c>.
    /// Lunar nodes / apogees and asteroids without a tabulated diameter
    /// degenerate to a point body (radius = 0); the result still carries
    /// the geometry but the body's contribution to the cone half-angle
    /// vanishes.
    /// </param>
    /// <param name="ephemerisFlags">Frame / source / option flags. Only the source bits are honoured.</param>
    public LunarOccultationGlobalReport ComputeGlobalAt(
        JulianDay jdUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags)
    {
        var ifl = ephemerisFlags & EphMask;
        var target = Occultee.FromBody(body);

        var (whereType, geo, geom) = EclipseWhere(jdUt, ifl, target);
        var attrs = ComputeAttributesAt(jdUt, ifl, geo, target, geom.CoreShadowDiameterAtMaxKm);
        return new LunarOccultationGlobalReport(whereType, geo, attrs, geom);
    }

    /// <summary>
    /// Star-anchored variant of
    /// <see cref="ComputeGlobalAt(JulianDay, CelestialBody, EphemerisFlags)"/>.
    /// Mirrors <c>swe_lun_occult_where(jd, ipl=-1, starname, …)</c> at
    /// <c>swecl.c#L606</c>: the moon occults a fixed star, treated as a
    /// point source (<c>drad = 0</c>). The geometry helper falls into the
    /// <c>rsminusrm &lt; 0</c> branch deterministically, so the result is
    /// always classified as <see cref="EclipseTypeFlags.Total"/>; the
    /// hybrid annular branches are geometrically excluded.
    /// </summary>
    /// <param name="starName">Catalogue lookup string in the format
    /// accepted by <see cref="SharpAstrology.SwissEphemerides.Application.Stars.FixedStarService.Compute"/>.</param>
    /// <exception cref="System.InvalidOperationException">The service was
    /// constructed without a <c>FixedStarService</c>.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// The named star is not in the catalogue (raised by the underlying
    /// <c>FixedStarService</c>).</exception>
    public LunarOccultationGlobalReport ComputeGlobalAt(
        JulianDay jdUt,
        string starName,
        EphemerisFlags ephemerisFlags)
    {
        if (starName is null) throw new ArgumentNullException(nameof(starName));
        EnsureStarServiceWired();
        var ifl = ephemerisFlags & EphMask;
        var target = Occultee.FromStar(starName);

        var (whereType, geo, geom) = EclipseWhere(jdUt, ifl, target);
        var attrs = ComputeAttributesAt(jdUt, ifl, geo, target, geom.CoreShadowDiameterAtMaxKm);
        return new LunarOccultationGlobalReport(whereType, geo, attrs, geom);
    }

    /// <summary>
    /// Eclipse-where geometry for any occulting body or star. Identical in
    /// structure to <c>SolarEclipseService.EclipseWhere</c>, with the body
    /// radius and body fetch parameterised through <paramref name="target"/>.
    /// </summary>
    private (EclipseTypeFlags Type, GeographicLocation Location, SolarEclipseGeometry Geometry)
        EclipseWhere(JulianDay jdUt, EphemerisFlags ifl, Occultee target)
    {
        var dt = _calendar.DeltaT(jdUt);
        var jdEt = new JulianDay(jdUt.Value + dt);

        var fetchFlags = EphemerisFlags.Equatorial | ifl;

        var moonState = _body.Compute(CelestialBody.Moon, jdEt, fetchFlags);
        Span<double> rsInit = stackalloc double[3];
        FetchOcculteeXyz(target, jdEt, fetchFlags, observer: null, rsInit, out _);

        Span<double> rmInit = stackalloc double[3];
        rmInit[0] = moonState.Position.X; rmInit[1] = moonState.Position.Y; rmInit[2] = moonState.Position.Z;

        var sidtRad = _calendar.SiderealTime(jdUt) * 15.0 * DegToRad;

        const double rmoon = EclipseConstants.MoonRadiusAu;
        const double dmoon = 2.0 * rmoon;
        const double de = EclipseConstants.EarthReferenceRadiusAu;
        var earthobl = 1.0 - EclipseConstants.EarthOblateness;
        var drad = target.Drad;

        Span<double> rm = stackalloc double[3];
        Span<double> rs = stackalloc double[3];
        Span<double> e = stackalloc double[3];
        Span<double> et = stackalloc double[3];
        Span<double> xs = stackalloc double[3];
        Span<double> xst = stackalloc double[3];

        EclipseTypeFlags retc = EclipseTypeFlags.None;
        bool noEclipse = false;
        double dsmt = 0, s0 = 0, r0 = 0, d0 = 0, D0 = 0;
        double cosf1 = 0, cosf2 = 0;

        for (int niter = 0; niter < 2; niter++)
        {
            rm[0] = rmInit[0]; rm[1] = rmInit[1]; rm[2] = rmInit[2];
            rs[0] = rsInit[0]; rs[1] = rsInit[1]; rs[2] = rsInit[2];

            rm[2] /= earthobl;
            rs[2] /= earthobl;

            var dm = System.Math.Sqrt(rm[0] * rm[0] + rm[1] * rm[1] + rm[2] * rm[2]);

            for (int i = 0; i < 3; i++)
            {
                e[i] = rm[i] - rs[i];
                et[i] = rmInit[i] - rsInit[i];
            }
            var dsm = System.Math.Sqrt(e[0] * e[0] + e[1] * e[1] + e[2] * e[2]);
            dsmt = System.Math.Sqrt(et[0] * et[0] + et[1] * et[1] + et[2] * et[2]);
            for (int i = 0; i < 3; i++)
            {
                e[i] /= dsm;
                et[i] /= dsmt;
            }

            var sinf1 = (drad - rmoon) / dsm;
            cosf1 = System.Math.Sqrt(1 - sinf1 * sinf1);
            var sinf2 = (drad + rmoon) / dsm;
            cosf2 = System.Math.Sqrt(1 - sinf2 * sinf2);

            s0 = -(rm[0] * e[0] + rm[1] * e[1] + rm[2] * e[2]);
            r0 = System.Math.Sqrt(dm * dm - s0 * s0);
            d0 = (s0 / dsm * (drad * 2 - dmoon) - dmoon) / cosf1;
            D0 = (s0 / dsm * (drad * 2 + dmoon) + dmoon) / cosf2;

            retc = EclipseTypeFlags.None;
            noEclipse = false;
            if (de * cosf1 >= r0)
                retc |= EclipseTypeFlags.Central;
            else if (r0 <= de * cosf1 + System.Math.Abs(d0) / 2)
                retc |= EclipseTypeFlags.NonCentral;
            else if (r0 <= de * cosf2 + D0 / 2)
                retc |= EclipseTypeFlags.Partial | EclipseTypeFlags.NonCentral;
            else
                noEclipse = true;

            var d = s0 * s0 + de * de - dm * dm;
            d = d > 0 ? System.Math.Sqrt(d) : 0;
            var s = s0 - d;

            for (int i = 0; i < 3; i++)
                xs[i] = rm[i] + s * e[i];
            xst[0] = xs[0]; xst[1] = xs[1]; xst[2] = xs[2] * earthobl;

            if (niter == 0)
            {
                var rxy = System.Math.Sqrt(xst[0] * xst[0] + xst[1] * xst[1]);
                var lat = rxy == 0
                    ? (xst[2] >= 0 ? System.Math.PI / 2 : -System.Math.PI / 2)
                    : System.Math.Atan(xst[2] / rxy);
                var cosfi = System.Math.Cos(lat);
                var sinfi = System.Math.Sin(lat);
                var eobl = EclipseConstants.EarthOblateness;
                var cc = 1.0 / System.Math.Sqrt(cosfi * cosfi + (1 - eobl) * (1 - eobl) * sinfi * sinfi);
                earthobl = (1 - eobl) * (1 - eobl) * cc;
                continue;
            }

            var lonRad = System.Math.Atan2(xs[1], xs[0]);
            var rxyXs = System.Math.Sqrt(xs[0] * xs[0] + xs[1] * xs[1]);
            var latRad = rxyXs == 0
                ? (xs[2] >= 0 ? System.Math.PI / 2 : -System.Math.PI / 2)
                : System.Math.Atan(xs[2] / rxyXs);

            lonRad -= sidtRad;
            var lonDeg = AngleMath.NormalizeDegrees(lonRad * RadToDeg);
            if (lonDeg > 180.0) lonDeg -= 360.0;
            var latDeg = latRad * RadToDeg;

            var dx = rmInit[0] - xst[0];
            var dy = rmInit[1] - xst[1];
            var dz = rmInit[2] - xst[2];
            var sToObs = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

            var coreDiamKm = (sToObs / dsmt * (drad * 2 - dmoon) - dmoon) * cosf1 * AuKm;
            var penumDiamKm = (sToObs / dsmt * (drad * 2 + dmoon) + dmoon) * cosf2 * AuKm;

            if ((retc & EclipseTypeFlags.Partial) == 0 && !noEclipse)
            {
                if (coreDiamKm > 0)
                    retc |= EclipseTypeFlags.Annular;
                else
                    retc |= EclipseTypeFlags.Total;
            }

            return (
                retc,
                new GeographicLocation(lonDeg, latDeg, 0.0),
                new SolarEclipseGeometry(
                    CoreShadowDiameterAtMaxKm: coreDiamKm,
                    PenumbraDiameterAtMaxKm: penumDiamKm,
                    ShadowAxisDistanceFromGeocenterKm: r0 * AuKm,
                    CoreShadowDiameterFundamentalPlaneKm: d0 * AuKm,
                    PenumbraDiameterFundamentalPlaneKm: D0 * AuKm,
                    CoreShadowConeCosine: cosf1,
                    PenumbraConeCosine: cosf2));
        }

        throw new System.Diagnostics.UnreachableException(
            $"LunarOccultationService.EclipseWhere: oblateness-iteration loop "
            + $"exited without producing a result at jdUt={jdUt.Value:R}, ifl={ifl}, target={target}.");
    }

    /// <summary>
    /// Eclipse-how observer attributes for any occulting body or star.
    /// Returns the eclipse-type bits (without central/noncentral, which the
    /// caller composes from <see cref="EclipseWhere"/>) and the per-observer
    /// attribute set. The Saros / NASA-magnitude block is intentionally
    /// skipped — it only applies to the Sun. For stars the fetch is
    /// geocentric (stellar parallax is sub-arcsec and well below the
    /// Moshier residual on the Moon).
    /// </summary>
    private (EclipseTypeFlags Type, LunarOccultationAttributes Attrs)
        EclipseHow(JulianDay jdUt, EphemerisFlags ifl, GeographicLocation observer,
                   Occultee target)
    {
        var dt = _calendar.DeltaT(jdUt);
        var jdEt = new JulianDay(jdUt.Value + dt);
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);

        var fetchFlags = EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | ifl;

        Span<double> xs = stackalloc double[3];
        FetchOcculteeXyz(target, jdEt, fetchFlags, observerLoc, xs, out var ds);
        var moonCartState = _body.Compute(CelestialBody.Moon, jdEt, fetchFlags, observerLoc);
        Span<double> xm = stackalloc double[3];
        xm[0] = moonCartState.Position.X; xm[1] = moonCartState.Position.Y; xm[2] = moonCartState.Position.Z;

        var dm = System.Math.Sqrt(xm[0] * xm[0] + xm[1] * xm[1] + xm[2] * xm[2]);

        const double rmoonRad = EclipseConstants.MoonRadiusAu;
        var drad = target.Drad;

        var rmoon = System.Math.Asin(rmoonRad / dm) * RadToDeg;
        var rsun = drad > 0 ? System.Math.Asin(drad / ds) * RadToDeg : 0.0;
        var rsplusrm = rsun + rmoon;
        var rsminusrm = rsun - rmoon;

        var cosDctr = (xs[0] * xm[0] + xs[1] * xm[1] + xs[2] * xm[2]) / (ds * dm);
        if (cosDctr > 1) cosDctr = 1; else if (cosDctr < -1) cosDctr = -1;
        var dctr = System.Math.Acos(cosDctr) * RadToDeg;

        EclipseTypeFlags retc;
        if (dctr < rsminusrm)
            retc = EclipseTypeFlags.Annular;
        else if (dctr < System.Math.Abs(rsminusrm))
            retc = EclipseTypeFlags.Total;
        else if (dctr < rsplusrm)
            retc = EclipseTypeFlags.Partial;
        else
            retc = EclipseTypeFlags.None;

        var diameterRatio = rsun > 0 ? rmoon / rsun : 0.0;

        var lsunFlat = System.Math.Asin(rsun / 2 * DegToRad) * 2;
        var lsunleft = -dctr + rsun + rmoon;
        var fractionDiameterCovered = lsunFlat > 0 ? lsunleft / rsun / 2 : 1.0;

        double obscuration;
        if (retc == EclipseTypeFlags.None || rsun == 0)
            obscuration = 1.0;
        else if (retc == EclipseTypeFlags.Total || retc == EclipseTypeFlags.Annular)
            obscuration = rmoon * rmoon / rsun / rsun;
        else
        {
            var a = 2 * dctr * rmoon;
            var b = 2 * dctr * rsun;
            if (a < 1e-9)
            {
                obscuration = rmoon * rmoon / rsun / rsun;
            }
            else
            {
                var aArg = (dctr * dctr + rmoon * rmoon - rsun * rsun) / a;
                if (aArg > 1) aArg = 1; else if (aArg < -1) aArg = -1;
                var bArg = (dctr * dctr + rsun * rsun - rmoon * rmoon) / b;
                if (bArg > 1) bArg = 1; else if (bArg < -1) bArg = -1;
                var aAng = System.Math.Acos(aArg);
                var bAng = System.Math.Acos(bArg);
                var (sinA, cosA) = System.Math.SinCos(aAng);
                var (sinB, cosB) = System.Math.SinCos(bAng);
                var sc1 = aAng * rmoon * rmoon / 2 - cosA * sinA * rmoon * rmoon / 2;
                var sc2 = bAng * rsun * rsun / 2 - cosB * sinB * rsun * rsun / 2;
                obscuration = (sc1 + sc2) * 2 / System.Math.PI / rsun / rsun;
            }
        }

        // Body azimuth/altitude via the topocentric polar position.
        Span<double> bodyPolar = stackalloc double[6];
        Span<double> bodyCart6 = stackalloc double[6];
        bodyCart6[0] = xs[0]; bodyCart6[1] = xs[1]; bodyCart6[2] = xs[2];
        Polar.CartesianToPolarWithSpeed(bodyCart6, bodyPolar);
        var horiz = _horizontal.ToHorizontal(
            jdUt, HorizontalConversionInput.FromEquatorial,
            observer, atPressMbar: 0.0, atTempC: 10.0,
            bodyPolar[0] * RadToDeg, bodyPolar[1] * RadToDeg);

        // Visibility test (swecl.c#L1109-L1116) — same form for occultations.
        var hminAppr = -(34.4556 + (1.75 + 0.37) * System.Math.Sqrt(observer.AltitudeMeters)) / 60.0;
        if (retc != EclipseTypeFlags.None
            && horiz.TrueAltitudeDeg + rsun + System.Math.Abs(hminAppr) >= 0)
        {
            retc |= EclipseTypeFlags.Visible;
        }

        var attrs = new LunarOccultationAttributes(
            DiameterFractionCovered: fractionDiameterCovered,
            DiameterRatioMoonOverBody: diameterRatio,
            DiscFractionObscured: obscuration,
            CoreShadowDiameterKm: 0.0,
            BodyAzimuthDeg: horiz.AzimuthDeg,
            BodyTrueAltitudeDeg: horiz.TrueAltitudeDeg,
            BodyApparentAltitudeDeg: horiz.ApparentAltitudeDeg,
            MoonBodyAngularDistanceDeg: dctr);

        return (retc, attrs);
    }

    private LunarOccultationAttributes ComputeAttributesAt(
        JulianDay jdUt,
        EphemerisFlags ifl,
        GeographicLocation observer,
        Occultee target,
        double coreShadowDiamKm)
    {
        var (_, attrs) = EclipseHow(jdUt, ifl, observer, target);
        return attrs with { CoreShadowDiameterKm = coreShadowDiamKm };
    }

    // -----------------------------------------------------------------
    // when_glob — global occultation search (swecl.c#L1572)
    // -----------------------------------------------------------------

    /// <summary>
    /// Finds the next geocentric occultation of <paramref name="body"/> by
    /// the Moon after <paramref name="startUt"/> (or the previous one when
    /// <paramref name="backward"/> is <c>true</c>). Mirrors
    /// <c>swe_lun_occult_when_glob</c>.
    /// </summary>
    /// <param name="eclipseTypeFilter">
    /// Filter on the wanted occultation classification. Use
    /// <see cref="EclipseTypeFlags.None"/> (the default) for "any of
    /// total / partial / annular (Sun only) / annular-total (Sun only)
    /// / central / non-central".
    /// </param>
    /// <exception cref="System.ArgumentException">
    /// Thrown when the filter requests an annular or annular-total
    /// occultation for a non-Sun body — annular geometry only applies
    /// when the apparent body radius exceeds the Moon's.
    /// </exception>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located occultation, or
    /// <c>default(LunarOccultationGlobalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when no occultation is found
    /// inside the safety cap.
    /// </returns>
    public EphemerisResult<LunarOccultationGlobalSearchReport> FindNextGlobal(
        JulianDay startUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        EclipseTypeFlags eclipseTypeFilter = EclipseTypeFlags.None,
        bool backward = false)
    {
        var ifl = ephemerisFlags & EphMask;
        var target = Occultee.FromBody(body);
        var ifltype = NormalizeOccultationFilter(eclipseTypeFilter, target);
        return FindNextGlobalCore(startUt, target, ifl, ifltype, backward);
    }

    /// <summary>
    /// Star-anchored variant of
    /// <see cref="FindNextGlobal(JulianDay, CelestialBody, EphemerisFlags, EclipseTypeFlags, bool)"/>.
    /// Mirrors <c>swe_lun_occult_when_glob</c> with the
    /// <c>starname</c>-non-empty branches at <c>swecl.c#L1641 / L1659 /
    /// L1702 / L1706 / L1929 / L1950</c>. Stars below <c>|β| &gt; 7°</c> can
    /// never be occulted; the service rejects them upstream.
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// The filter requests <see cref="EclipseTypeFlags.Annular"/> or
    /// <see cref="EclipseTypeFlags.AnnularTotal"/>: those are
    /// geometrically impossible for a point source.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">The service was
    /// constructed without a <c>FixedStarService</c>, or the star's
    /// ecliptic latitude exceeds 7°.</exception>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located occultation, or
    /// <c>default(LunarOccultationGlobalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when no occultation is found
    /// inside the safety cap.
    /// </returns>
    public EphemerisResult<LunarOccultationGlobalSearchReport> FindNextGlobal(
        JulianDay startUt,
        string starName,
        EphemerisFlags ephemerisFlags,
        EclipseTypeFlags eclipseTypeFilter = EclipseTypeFlags.None,
        bool backward = false)
    {
        if (starName is null) throw new ArgumentNullException(nameof(starName));
        EnsureStarServiceWired();
        var ifl = ephemerisFlags & EphMask;
        var target = Occultee.FromStar(starName);
        var ifltype = NormalizeOccultationFilter(eclipseTypeFilter, target);
        EnsureStarOccultable(target, startUt, ifl);
        return FindNextGlobalCore(startUt, target, ifl, ifltype, backward);
    }

    private EphemerisResult<LunarOccultationGlobalSearchReport> FindNextGlobalCore(
        JulianDay startUt, Occultee target, EphemerisFlags ifl,
        EclipseTypeFlags ifltype, bool backward)
    {
        var direction = backward ? -1 : 1;
        var t = startUt.Value;

        for (var step = 0; step < MaxRetrySteps; step++)
        {
            var (report, advance) = TryGlobalOccultationAt(
                t, target, ifl, ifltype, startUt, backward, direction);
            if (report.HasValue)
                return EphemerisResult<LunarOccultationGlobalSearchReport>.Ok(report.Value);
            t += direction * advance;
        }

        return EphemerisResult<LunarOccultationGlobalSearchReport>.WithWarning(
            default,
            "Lunar-occultation search exceeded the safety cap of " +
            $"{MaxRetrySteps} retry steps without finding a match.");
    }

    /// <summary>
    /// One iteration of the global occultation search. Returns either the
    /// found occultation, or the number of days to advance before retrying.
    /// </summary>
    private (LunarOccultationGlobalSearchReport? Report, double Advance)
        TryGlobalOccultationAt(
            double tStart, Occultee target,
            EphemerisFlags ifl, EclipseTypeFlags ifltype,
            JulianDay startUt, bool backward, int direction)
    {
        // Step 1: rough conjunction in geocentric ecliptic longitude.
        if (!RoughConjunctionGeocentric(tStart, target, ifl, direction, out var tjd))
            return (null, 20);

        // Step 2: latitude difference filter (>2° → no occultation possible).
        var (bodyLatDeg, moonLatDeg) = GeocentricEclipticLatitudes(tjd, target, ifl);
        if (System.Math.Abs(bodyLatDeg - moonLatDeg) > 2.0)
            return (null, 20);

        // Step 3: parabola refinement on dctr − (rmoon + rsun).
        var iflag = EphemerisFlags.Equatorial | ifl;
        var dtdiv = 3.0;
        Span<double> dc = stackalloc double[3];
        for (var dt = 1.0; dt > 0.0001; dt /= dtdiv)
        {
            for (var i = 0; i < 3; i++)
            {
                var tt = tjd - dt + i * dt;
                dc[i] = MoonBodyEdgeMetric(new JulianDay(tt), iflag, target);
            }
            ParabolaFit.FindMaximum(dc[0], dc[1], dc[2], dt, out var dtInt, out _);
            tjd += dtInt + dt;
        }

        // Step 4: TT → UT via single deltaT subtraction (swecl.c#L1718).
        var maxUt = tjd - _calendar.DeltaT(new JulianDay(tjd));

        // Step 5: classification via EclipseWhere.
        var (whereType, whereLoc, whereGeom) = EclipseWhere(new JulianDay(maxUt), ifl, target);

        if (whereType == EclipseTypeFlags.None)
            return (null, 20);

        // Step 6: strict-direction check (swecl.c#L1742-L1748).
        if ((backward && maxUt >= startUt.Value - 0.0001)
            || (!backward && maxUt <= startUt.Value + 0.0001))
            return (null, 20);

        // Step 7: filter checks (swecl.c#L1764-L1817).
        if ((ifltype & EclipseTypeFlags.NonCentral) == 0 && (whereType & EclipseTypeFlags.NonCentral) != 0)
            return (null, 20);
        if ((ifltype & EclipseTypeFlags.Central) == 0 && (whereType & EclipseTypeFlags.Central) != 0)
            return (null, 20);
        if ((ifltype & EclipseTypeFlags.Annular) == 0 && (whereType & EclipseTypeFlags.Annular) != 0)
            return (null, 20);
        if ((ifltype & EclipseTypeFlags.Partial) == 0 && (whereType & EclipseTypeFlags.Partial) != 0)
            return (null, 20);
        if ((ifltype & (EclipseTypeFlags.Total | EclipseTypeFlags.AnnularTotal)) == 0
            && (whereType & EclipseTypeFlags.Total) != 0)
            return (null, 20);

        // Step 8: phase contacts via EclipseWhere + dcore metrics
        // (swecl.c#L1825-L1875).
        var retc = whereType;
        double partialBegin = 0, partialEnd = 0;
        double? totalityBegin = null, totalityEnd = null;
        double? centerBegin = null, centerEnd = null;
        var phaseLast = (retc & EclipseTypeFlags.Partial) != 0
            ? 0
            : (retc & EclipseTypeFlags.NonCentral) != 0 ? 1 : 2;
        var dta = TwoHoursInDays;
        var dtb = TenMinutesInDays;
        Span<double> dcEdge = stackalloc double[3];
        for (var n = 0; n <= phaseLast; n++)
        {
            if (n == 1 && (retc & EclipseTypeFlags.Partial) != 0) continue;
            if (n == 2 && (retc & EclipseTypeFlags.NonCentral) != 0) continue;

            for (var i = 0; i < 3; i++)
            {
                var ts = maxUt - dta + i * dta;
                dcEdge[i] = OccultationPhaseEdgeMetric(n, new JulianDay(ts), ifl, target);
            }
            if (!ParabolaFit.FindZero(dcEdge[0], dcEdge[1], dcEdge[2], dta, out var dt1, out var dt2))
            {
                dt1 = -dta; dt2 = dta;
            }
            var beginTime = maxUt + dt1 + dta;
            var endTime = maxUt + dt2 + dta;

            // Three Newton-style refinement passes per endpoint.
            for (var m = 0; m < 3; m++)
            {
                var dtRef = dtb / System.Math.Pow(3.0, m);
                beginTime = RefineOccultationPhaseEdge(n, beginTime, dtRef, ifl, target);
                endTime = RefineOccultationPhaseEdge(n, endTime, dtRef, ifl, target);
            }

            switch (n)
            {
                case 0: partialBegin = beginTime; partialEnd = endTime; break;
                case 1: totalityBegin = beginTime; totalityEnd = endTime; break;
                case 2: centerBegin = beginTime; centerEnd = endTime; break;
            }
        }

        // Step 9: annular-total upgrade (swecl.c#L1879-L1897). Only the
        // Sun can produce hybrid eclipses; for non-Sun bodies and for
        // stars (drad = 0) the condition is geometrically impossible.
        if ((retc & EclipseTypeFlags.Total) != 0
            && totalityBegin.HasValue && totalityEnd.HasValue
            && !target.IsStar
            && target.Body == CelestialBody.Sun)
        {
            var coreAtMax = SignedCoreShadowDiameterKm(new JulianDay(maxUt), ifl, target);
            var coreAtBegin = SignedCoreShadowDiameterKm(new JulianDay(totalityBegin.Value), ifl, target);
            var coreAtEnd = SignedCoreShadowDiameterKm(new JulianDay(totalityEnd.Value), ifl, target);
            if (coreAtMax * coreAtBegin < 0 || coreAtMax * coreAtEnd < 0)
            {
                retc |= EclipseTypeFlags.AnnularTotal;
                retc &= ~EclipseTypeFlags.Total;
            }
        }

        if ((ifltype & EclipseTypeFlags.Total) == 0 && (retc & EclipseTypeFlags.Total) != 0)
            return (null, 20);
        if ((ifltype & EclipseTypeFlags.AnnularTotal) == 0 && (retc & EclipseTypeFlags.AnnularTotal) != 0)
            return (null, 20);

        // Step 10: local apparent noon — RA conjunction between Moon
        // and body inside the partial window (swecl.c#L1925-L1967).
        var localApparentNoon = FindLocalApparentNoonOcc(
            partialBegin, partialEnd, maxUt, ifl, target);

        return (
            new LunarOccultationGlobalSearchReport(
                EclipseType: retc,
                MaximumTime: new JulianDay(maxUt),
                LocalApparentNoonTime: localApparentNoon is null ? null : new JulianDay(localApparentNoon.Value),
                PartialBeginTime: new JulianDay(partialBegin),
                PartialEndTime: new JulianDay(partialEnd),
                TotalityBeginTime: totalityBegin is null ? null : new JulianDay(totalityBegin.Value),
                TotalityEndTime: totalityEnd is null ? null : new JulianDay(totalityEnd.Value),
                CenterlineBeginTime: centerBegin is null ? null : new JulianDay(centerBegin.Value),
                CenterlineEndTime: centerEnd is null ? null : new JulianDay(centerEnd.Value)),
            0);
    }

    // -----------------------------------------------------------------
    // when_loc — local occultation search (swecl.c#L2071 + L2412)
    // -----------------------------------------------------------------

    /// <summary>
    /// Finds the next occultation of <paramref name="body"/> by the Moon
    /// visible from <paramref name="observer"/> after
    /// <paramref name="startUt"/> (or the previous one when
    /// <paramref name="backward"/> is <c>true</c>). Mirrors
    /// <c>swe_lun_occult_when_loc</c>: searches K-style, classifies via
    /// topocentric geometry, refines the four contacts, then verifies the
    /// maximum is above the horizon — retrying the next conjunction if
    /// not.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown when the observer altitude is outside the valid range
    /// [-500 m, 25 000 m] enforced by the C source.
    /// </exception>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located occultation, or
    /// <c>default(LunarOccultationLocalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when no visible occultation
    /// is found within the safety cap.
    /// </returns>
    public EphemerisResult<LunarOccultationLocalSearchReport> FindNextLocal(
        JulianDay startUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        GeographicLocation observer,
        bool backward = false)
    {
        EnsureLocalSearchPrerequisites(observer);
        var ifl = ephemerisFlags & EphMask;
        var target = Occultee.FromBody(body);
        return FindNextLocalCore(startUt, target, ifl, observer, backward);
    }

    /// <summary>
    /// Star-anchored variant of
    /// <see cref="FindNextLocal(JulianDay, CelestialBody, EphemerisFlags, GeographicLocation, bool)"/>.
    /// Mirrors the star branches of <c>occult_when_loc</c> at
    /// <c>swecl.c#L2412</c>. Contacts 1 and 4 coincide with contacts 2 and
    /// 3 because the star is a point source
    /// (<c>swecl.c#L2696-L2698</c>).
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Observer altitude outside [-500 m, 25 000 m].</exception>
    /// <exception cref="System.InvalidOperationException">
    /// The service was constructed without a <c>FixedStarService</c>
    /// or without a <c>RiseTransitService</c>, or the star's ecliptic
    /// latitude exceeds 7°.</exception>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located occultation, or
    /// <c>default(LunarOccultationLocalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when no visible occultation
    /// is found within the safety cap.
    /// </returns>
    public EphemerisResult<LunarOccultationLocalSearchReport> FindNextLocal(
        JulianDay startUt,
        string starName,
        EphemerisFlags ephemerisFlags,
        GeographicLocation observer,
        bool backward = false)
    {
        if (starName is null) throw new ArgumentNullException(nameof(starName));
        EnsureLocalSearchPrerequisites(observer);
        EnsureStarServiceWired();
        var ifl = ephemerisFlags & EphMask;
        var target = Occultee.FromStar(starName);
        EnsureStarOccultable(target, startUt, ifl);
        return FindNextLocalCore(startUt, target, ifl, observer, backward);
    }

    private EphemerisResult<LunarOccultationLocalSearchReport> FindNextLocalCore(
        JulianDay startUt, Occultee target, EphemerisFlags ifl,
        GeographicLocation observer, bool backward)
    {
        var direction = backward ? -1 : 1;
        var t = startUt.Value;
        for (var step = 0; step < MaxRetrySteps; step++)
        {
            var (report, advance) = TryLocalOccultationAt(
                t, target, ifl, observer, startUt, backward, direction);
            if (report.HasValue)
                return EphemerisResult<LunarOccultationLocalSearchReport>.Ok(report.Value);
            t += direction * advance;
        }
        return EphemerisResult<LunarOccultationLocalSearchReport>.WithWarning(
            default,
            "Local lunar-occultation search exceeded the safety cap of " +
            $"{MaxRetrySteps} retry steps without finding a visible match.");
    }

    /// <summary>
    /// One iteration of the local occultation search. Returns either the
    /// found visible occultation or the days to advance before retrying.
    /// </summary>
    private (LunarOccultationLocalSearchReport? Report, double Advance)
        TryLocalOccultationAt(
            double tStart, Occultee target,
            EphemerisFlags ifl, GeographicLocation observer,
            JulianDay startUt, bool backward, int direction)
    {
        // Step 1: rough conjunction in geocentric ecliptic longitude
        // (the C helper uses iflaggeo at swecl.c#L2449 → ecliptic).
        if (!RoughConjunctionGeocentric(tStart, target, ifl, direction, out var tjd))
            return (null, 20);

        // Step 2: latitude difference filter.
        var (bodyLatDeg, moonLatDeg) = GeocentricEclipticLatitudes(tjd, target, ifl);
        if (System.Math.Abs(bodyLatDeg - moonLatDeg) > 2.0)
            return (null, 20);

        // Step 3: parabola refinement on dctr − (rmoon + rsun) using
        // TOPOCENTRIC body / Moon (swecl.c#L2500-L2533).
        var dtdiv = 2.0;
        Span<double> dc = stackalloc double[3];
        for (var dt = 1.0; dt > 0.00001; dt /= dtdiv)
        {
            if (dt < 0.01) dtdiv = 2.0;
            for (var i = 0; i < 3; i++)
            {
                var tt = tjd - dt + i * dt;
                dc[i] = TopocentricMoonBodyEdgeMetric(
                    new JulianDay(tt), ifl, observer, target,
                    out _, out _);
            }
            ParabolaFit.FindMaximum(dc[0], dc[1], dc[2], dt, out var dtInt, out _);
            tjd += dtInt + dt;
        }

        // Step 4: existence check.
        var dctrMax = TopocentricMoonBodyDistanceDeg(
            new JulianDay(tjd), ifl, observer, target,
            out var rmoonMax, out var rsunMax);
        var rsplusrmMax = rmoonMax + rsunMax;
        var rsminusrmMax = rsunMax - rmoonMax;
        if (dctrMax > rsplusrmMax)
            return (null, 20);

        // Step 5: TT → UT (one fixed-point ΔT pass, mirrors swecl.c#L2561-L2562).
        var maxUtRaw = tjd - _calendar.DeltaT(new JulianDay(tjd));
        var maxUt = tjd - _calendar.DeltaT(new JulianDay(maxUtRaw));

        if ((backward && maxUt >= startUt.Value - 0.0001)
            || (!backward && maxUt <= startUt.Value + 0.0001))
            return (null, 20);

        // Step 6: classification.
        EclipseTypeFlags retc;
        if (dctrMax < rsminusrmMax)
            retc = EclipseTypeFlags.Annular;
        else if (dctrMax < System.Math.Abs(rsminusrmMax))
            retc = EclipseTypeFlags.Total;
        else
            retc = EclipseTypeFlags.Partial;

        // Step 7: contacts 2 & 3 (totality begin / end).
        double? c2 = null, c3 = null;
        if (dctrMax <= System.Math.Abs(rsminusrmMax))
        {
            // Sample at tjd ± 2 minutes; middle dc is fabs(rsminusrm) - dctrmin.
            Span<double> dcCol = stackalloc double[3];
            dcCol[1] = System.Math.Abs(rsminusrmMax) - dctrMax;
            for (var i = 0; i <= 2; i += 2)
            {
                var t = i == 0 ? tjd - TwoMinutesInDays : tjd + TwoMinutesInDays;
                var dctr = TopocentricMoonBodyDistanceDeg(
                    new JulianDay(t), ifl, observer, target,
                    out var rm, out var rs,
                    moonScale: MoonRadiusFudgeContacts);
                dcCol[i] = System.Math.Abs(rs - rm) - dctr;
            }
            if (ParabolaFit.FindZero(dcCol[0], dcCol[1], dcCol[2], TwoMinutesInDays, out var dt1, out var dt2))
            {
                var t2 = tjd + dt1 + TwoMinutesInDays;
                var t3 = tjd + dt2 + TwoMinutesInDays;
                t2 = RefineLocalContactWithSpeed(t2, ifl, observer, target, isPartial: false);
                t3 = RefineLocalContactWithSpeed(t3, ifl, observer, target, isPartial: false);
                c2 = t2 - _calendar.DeltaT(new JulianDay(t2));
                c3 = t3 - _calendar.DeltaT(new JulianDay(t3));
            }
        }

        // Step 8: contacts 1 & 4 (penumbra begin / end). For stars
        // (point sources) the C library short-circuits this branch
        // and sets contacts 1/4 = contacts 2/3 (swecl.c#L2696-L2698).
        double? c1 = null, c4 = null;
        if (target.IsStar)
        {
            c1 = c2;
            c4 = c3;
        }
        else
        {
            Span<double> dcCol = stackalloc double[3];
            dcCol[1] = rsplusrmMax - dctrMax;
            for (var i = 0; i <= 2; i += 2)
            {
                var t = i == 0 ? tjd - TwoHoursInDays : tjd + TwoHoursInDays;
                var dctr = TopocentricMoonBodyDistanceDeg(
                    new JulianDay(t), ifl, observer, target,
                    out var rm, out var rs);
                dcCol[i] = (rs + rm) - dctr;
            }
            if (ParabolaFit.FindZero(dcCol[0], dcCol[1], dcCol[2], TwoHoursInDays, out var dt1, out var dt2))
            {
                var t1 = tjd + dt1 + TwoHoursInDays;
                var t4 = tjd + dt2 + TwoHoursInDays;
                t1 = RefineLocalContactWithSpeed(t1, ifl, observer, target, isPartial: true);
                t4 = RefineLocalContactWithSpeed(t4, ifl, observer, target, isPartial: true);
                c1 = t1 - _calendar.DeltaT(new JulianDay(t1));
                c4 = t4 - _calendar.DeltaT(new JulianDay(t4));
            }
        }

        // Step 9: visibility check at each contact, mirroring
        // swecl.c#L2703-L2721. Calls EclipseHow at each contact and
        // checks attr[6] (apparent altitude) > 0.
        var attrsAtMax = default(LunarOccultationAttributes);
        for (var i = 4; i >= 0; i--)
        {
            double? tret = i switch
            {
                0 => maxUt,
                1 => c1,
                2 => c2,
                3 => c3,
                4 => c4,
                _ => null,
            };
            if (tret is null) continue;
            var (_, attrs) = EclipseHow(new JulianDay(tret.Value), ifl, observer, target);
            if (i == 0) attrsAtMax = attrs;
            if (attrs.BodyApparentAltitudeDeg > 0)
            {
                retc |= EclipseTypeFlags.Visible;
                retc |= i switch
                {
                    0 => EclipseTypeFlags.MaxVisible,
                    1 => EclipseTypeFlags.PartialBeginVisible,
                    2 => EclipseTypeFlags.TotalBeginVisible,
                    3 => EclipseTypeFlags.TotalEndVisible,
                    4 => EclipseTypeFlags.PartialEndVisible,
                    _ => EclipseTypeFlags.None,
                };
            }
        }
        if ((retc & EclipseTypeFlags.Visible) == 0)
            return (null, 20);

        // Step 10: body rise / set inside the occultation window.
        // The C source searches with SE_BIT_DISC_BOTTOM, topocentric.
        // Stars are pseudo-bodies for RiseTransitService, so we only
        // run this block for actual celestial bodies.
        var topoFlags = ifl | EphemerisFlags.Topocentric;
        var riseFlags = RiseTransitFlags.Rise | RiseTransitFlags.DiscBottom;
        var setFlags = RiseTransitFlags.Set | RiseTransitFlags.DiscBottom;
        var bodyRiseSearchStart = new JulianDay((c1 ?? maxUt) - 0.1);
        double? bodyRiseInWindow = null, bodySetInWindow = null;
        if (!target.IsStar)
        {
            var bodyRiseRes = _riseTransit!.Find(
                bodyRiseSearchStart, target.Body, topoFlags, riseFlags, observer);
            var bodySetRes = _riseTransit!.Find(
                bodyRiseSearchStart, target.Body, topoFlags, setFlags, observer);
            if (c1.HasValue && c4.HasValue)
            {
                if (!bodyRiseRes.HasWarning && bodyRiseRes.Value.Value > c1.Value && bodyRiseRes.Value.Value < c4.Value)
                    bodyRiseInWindow = bodyRiseRes.Value.Value;
                if (!bodySetRes.HasWarning && bodySetRes.Value.Value > c1.Value && bodySetRes.Value.Value < c4.Value)
                    bodySetInWindow = bodySetRes.Value.Value;
            }
        }

        // Step 11: Sun rise / set check at first / last contact for the
        // OCC_BEG_DAYLIGHT / OCC_END_DAYLIGHT flags
        // (swecl.c#L2746-L2761).
        if (c1.HasValue)
        {
            var sunRiseRes = _riseTransit!.Find(
                new JulianDay(c1.Value), CelestialBody.Sun, topoFlags, RiseTransitFlags.Rise, observer);
            var sunSetRes = _riseTransit!.Find(
                new JulianDay(c1.Value), CelestialBody.Sun, topoFlags, RiseTransitFlags.Set, observer);
            if (!sunRiseRes.HasWarning && !sunSetRes.HasWarning && sunSetRes.Value.Value < sunRiseRes.Value.Value)
                retc |= EclipseTypeFlags.OccultationBeginDaylight;
        }
        if (c4.HasValue)
        {
            var sunRiseRes = _riseTransit!.Find(
                new JulianDay(c4.Value), CelestialBody.Sun, topoFlags, RiseTransitFlags.Rise, observer);
            var sunSetRes = _riseTransit!.Find(
                new JulianDay(c4.Value), CelestialBody.Sun, topoFlags, RiseTransitFlags.Set, observer);
            if (!sunRiseRes.HasWarning && !sunSetRes.HasWarning && sunSetRes.Value.Value < sunRiseRes.Value.Value)
                retc |= EclipseTypeFlags.OccultationEndDaylight;
        }

        // Step 12: final attr[3] = dcore[0] from EclipseWhere at maximum
        // (swecl.c#L2093-L2096) plus the NonCentral flag carry-over.
        var (whereType, _, whereGeom) = EclipseWhere(new JulianDay(maxUt), ifl, target);
        retc |= (whereType & EclipseTypeFlags.NonCentral);
        attrsAtMax = attrsAtMax with { CoreShadowDiameterKm = whereGeom.CoreShadowDiameterAtMaxKm };

        return (
            new LunarOccultationLocalSearchReport(
                EclipseType: retc,
                MaximumTime: new JulianDay(maxUt),
                FirstContactTime: c1 is null ? null : new JulianDay(c1.Value),
                SecondContactTime: c2 is null ? null : new JulianDay(c2.Value),
                ThirdContactTime: c3 is null ? null : new JulianDay(c3.Value),
                FourthContactTime: c4 is null ? null : new JulianDay(c4.Value),
                BodyRiseDuringOccultationTime: bodyRiseInWindow is null ? null : new JulianDay(bodyRiseInWindow.Value),
                BodySetDuringOccultationTime: bodySetInWindow is null ? null : new JulianDay(bodySetInWindow.Value),
                Attributes: attrsAtMax),
            0);
    }

    // -----------------------------------------------------------------
    // Search-helper methods
    // -----------------------------------------------------------------

    /// <summary>
    /// Advances <paramref name="tStart"/> by <c>dl / 13</c> until the
    /// geocentric ecliptic-longitude difference between body and Moon is
    /// below 0.1°. The initial <c>dl</c> is biased by <c>−360°</c> for
    /// backward searches so the first step moves backwards in time;
    /// subsequent iterations re-normalise to (−180°, 180°] and let the
    /// iteration converge symmetrically. Returns false when the loop fails
    /// to converge inside 50 iterations.
    /// </summary>
    private bool RoughConjunctionGeocentric(
        double tStart, Occultee target, EphemerisFlags ifl,
        int direction, out double tConjunction)
    {
        var t = tStart;

        // Initial step uses the direction-biased dl (backward search:
        // dl − 360 ⇒ negative ⇒ first hop goes back in time).
        var (bodyLonInit, _) = GeocentricEclipticLongitude(t, target, ifl);
        var (moonLonInit, _) = GeocentricMoonEclipticLongitude(t, ifl);
        var dlInit = AngleMath.NormalizeDegrees(bodyLonInit - moonLonInit);
        if (direction < 0)
            dlInit -= 360.0;
        if (System.Math.Abs(dlInit) <= 0.1)
        {
            tConjunction = t;
            return true;
        }
        t += dlInit / 13.0;

        for (var iter = 0; iter < 50; iter++)
        {
            var (bodyLon, _) = GeocentricEclipticLongitude(t, target, ifl);
            var (moonLon, _) = GeocentricMoonEclipticLongitude(t, ifl);
            var dl = AngleMath.NormalizeDegrees(bodyLon - moonLon);
            if (dl > 180.0) dl -= 360.0;
            if (System.Math.Abs(dl) <= 0.1)
            {
                tConjunction = t;
                return true;
            }
            t += dl / 13.0;
        }
        tConjunction = t;
        return false;
    }

    private (double LonDeg, double LatDeg) GeocentricEclipticLongitude(
        double tjdEt, Occultee target, EphemerisFlags ifl)
    {
        // Ecliptic Cartesian (no Equatorial flag → ecliptic frame).
        Span<double> pos = stackalloc double[3];
        FetchOcculteeXyz(target, new JulianDay(tjdEt), ifl, observer: null, pos, out _);
        var lon = System.Math.Atan2(pos[1], pos[0]) * RadToDeg;
        var rxy = System.Math.Sqrt(pos[0] * pos[0] + pos[1] * pos[1]);
        var lat = rxy == 0 ? 0 : System.Math.Atan2(pos[2], rxy) * RadToDeg;
        return (AngleMath.NormalizeDegrees(lon), lat);
    }

    private (double LonDeg, double LatDeg) GeocentricMoonEclipticLongitude(
        double tjdEt, EphemerisFlags ifl)
    {
        var state = _body.Compute(CelestialBody.Moon, new JulianDay(tjdEt), ifl);
        var lon = System.Math.Atan2(state.Position.Y, state.Position.X) * RadToDeg;
        var rxy = System.Math.Sqrt(state.Position.X * state.Position.X + state.Position.Y * state.Position.Y);
        var lat = rxy == 0 ? 0 : System.Math.Atan2(state.Position.Z, rxy) * RadToDeg;
        return (AngleMath.NormalizeDegrees(lon), lat);
    }

    private (double BodyLatDeg, double MoonLatDeg) GeocentricEclipticLatitudes(
        double tjdEt, Occultee target, EphemerisFlags ifl)
    {
        var (_, bodyLat) = GeocentricEclipticLongitude(tjdEt, target, ifl);
        var (_, moonLat) = GeocentricMoonEclipticLongitude(tjdEt, ifl);
        return (bodyLat, moonLat);
    }

    /// <summary>
    /// Geocentric Moon-body edge-angle metric (degrees), evaluated at TT time
    /// <paramref name="jdEt"/>.
    /// </summary>
    private double MoonBodyEdgeMetric(
        JulianDay jdEt, EphemerisFlags iflag, Occultee target)
    {
        Span<double> sxs = stackalloc double[3];
        FetchOcculteeXyz(target, jdEt, iflag, observer: null, sxs, out var ds);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, iflag);
        var dm = moon.Distance;
        var sx = sxs[0] / ds;
        var sy = sxs[1] / ds;
        var sz = sxs[2] / ds;
        var mx = moon.Position.X / dm;
        var my = moon.Position.Y / dm;
        var mz = moon.Position.Z / dm;
        var dot = sx * mx + sy * my + sz * mz;
        if (dot > 1.0) dot = 1.0;
        else if (dot < -1.0) dot = -1.0;
        var dctr = System.Math.Acos(dot) * RadToDeg;
        var rmoon = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg;
        var rsun = target.Drad > 0 ? System.Math.Asin(target.Drad / ds) * RadToDeg : 0.0;
        return dctr - (rmoon + rsun);
    }

    /// <summary>
    /// Topocentric Moon-body angular distance (degrees) and the two apparent
    /// radii at the observer.
    /// </summary>
    private double TopocentricMoonBodyDistanceDeg(
        JulianDay jdEt, EphemerisFlags ifl, GeographicLocation observer,
        Occultee target,
        out double rmoonDeg, out double rsunDeg,
        double moonScale = 1.0)
    {
        var topoFlags = EphemerisFlags.Topocentric | ifl;
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        Span<double> sxs = stackalloc double[3];
        FetchOcculteeXyz(target, jdEt, topoFlags, observerLoc, sxs, out var ds);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, topoFlags, observerLoc);
        var dm = moon.Distance;
        var sx = sxs[0] / ds;
        var sy = sxs[1] / ds;
        var sz = sxs[2] / ds;
        var mx = moon.Position.X / dm;
        var my = moon.Position.Y / dm;
        var mz = moon.Position.Z / dm;
        var dot = sx * mx + sy * my + sz * mz;
        if (dot > 1.0) dot = 1.0;
        else if (dot < -1.0) dot = -1.0;
        rmoonDeg = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg * moonScale;
        rsunDeg = target.Drad > 0 ? System.Math.Asin(target.Drad / ds) * RadToDeg : 0.0;
        return System.Math.Acos(dot) * RadToDeg;
    }

    private double TopocentricMoonBodyEdgeMetric(
        JulianDay jdEt, EphemerisFlags ifl, GeographicLocation observer,
        Occultee target,
        out double rmoonDeg, out double rsunDeg)
    {
        var dctr = TopocentricMoonBodyDistanceDeg(
            jdEt, ifl, observer, target,
            out rmoonDeg, out rsunDeg);
        return dctr - (rmoonDeg + rsunDeg);
    }

    /// <summary>
    /// Newton-style refinement of a contact time using the speed vectors at
    /// <paramref name="tret"/>. One pass at <c>dt = 10 sec</c> followed by
    /// one at <c>dt = 1 sec</c>. For stars the velocity is treated as zero —
    /// proper motion is microscopic on the contact-refinement timescale.
    /// </summary>
    private double RefineLocalContactWithSpeed(
        double tret, EphemerisFlags ifl, GeographicLocation observer,
        Occultee target, bool isPartial)
    {
        var topoFlags = EphemerisFlags.Topocentric | EphemerisFlags.Speed | ifl;
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        var moonScale = isPartial ? 1.0 : MoonRadiusFudgeContacts;

        Span<double> xs = stackalloc double[3];
        Span<double> vxs = stackalloc double[3];
        Span<double> xm = stackalloc double[3];
        Span<double> dcCol = stackalloc double[2];

        for (var m = 0; m < 2; m++)
        {
            var dt = m == 0 ? TenSecondsInDays : TenSecondsInDays / 10.0;
            FetchOcculteeXyzWithVelocity(target, new JulianDay(tret), topoFlags, observerLoc, xs, vxs, out _);
            var moon = _body.Compute(CelestialBody.Moon, new JulianDay(tret), topoFlags, observerLoc);
            xm[0] = moon.Position.X; xm[1] = moon.Position.Y; xm[2] = moon.Position.Z;
            var vmx = moon.Velocity.X; var vmy = moon.Velocity.Y; var vmz = moon.Velocity.Z;

            for (var i = 0; i < 2; i++)
            {
                if (i == 1)
                {
                    xs[0] -= vxs[0] * dt; xs[1] -= vxs[1] * dt; xs[2] -= vxs[2] * dt;
                    xm[0] -= vmx * dt; xm[1] -= vmy * dt; xm[2] -= vmz * dt;
                }
                var ds = System.Math.Sqrt(xs[0] * xs[0] + xs[1] * xs[1] + xs[2] * xs[2]);
                var dm = System.Math.Sqrt(xm[0] * xm[0] + xm[1] * xm[1] + xm[2] * xm[2]);
                var rmoon = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg * moonScale;
                var rsun = target.Drad > 0 ? System.Math.Asin(target.Drad / ds) * RadToDeg : 0.0;
                var dot = (xs[0] * xm[0] + xs[1] * xm[1] + xs[2] * xm[2]) / (ds * dm);
                if (dot > 1.0) dot = 1.0;
                else if (dot < -1.0) dot = -1.0;
                var dctr = System.Math.Acos(dot) * RadToDeg;
                dcCol[i] = isPartial
                    ? (rsun + rmoon) - dctr
                    : System.Math.Abs(rsun - rmoon) - dctr;
            }
            var slope = (dcCol[0] - dcCol[1]) / dt;
            if (slope == 0) return tret;
            var dt1 = -dcCol[0] / slope;
            tret += dt1;
        }
        return tret;
    }

    /// <summary>
    /// Phase-edge metric for occultations. Same formula as the solar-eclipse
    /// helper but evaluated against the occultation's
    /// <see cref="EclipseWhere"/> output.
    /// </summary>
    private double OccultationPhaseEdgeMetric(
        int n, JulianDay jdUt, EphemerisFlags ifl, Occultee target)
    {
        var (_, _, geom) = EclipseWhere(jdUt, ifl, target);
        return n switch
        {
            0 => geom.PenumbraDiameterFundamentalPlaneKm / 2.0
                 + EarthEquatorialRadiusKm / geom.CoreShadowConeCosine
                 - geom.ShadowAxisDistanceFromGeocenterKm,
            1 => System.Math.Abs(geom.CoreShadowDiameterFundamentalPlaneKm) / 2.0
                 + EarthEquatorialRadiusKm / geom.PenumbraConeCosine
                 - geom.ShadowAxisDistanceFromGeocenterKm,
            _ => EarthEquatorialRadiusKm / geom.PenumbraConeCosine
                 - geom.ShadowAxisDistanceFromGeocenterKm,
        };
    }

    private double RefineOccultationPhaseEdge(
        int n, double ut, double dt, EphemerisFlags ifl, Occultee target)
    {
        Span<double> dc = stackalloc double[2];
        for (var i = 0; i < 2; i++)
            dc[i] = OccultationPhaseEdgeMetric(n, new JulianDay(ut - dt + i * dt), ifl, target);
        var slope = (dc[1] - dc[0]) / dt;
        if (slope == 0) return ut;
        return ut - dc[1] / slope;
    }

    private double SignedCoreShadowDiameterKm(
        JulianDay jdUt, EphemerisFlags ifl, Occultee target)
    {
        var (_, _, geom) = EclipseWhere(jdUt, ifl, target);
        return geom.CoreShadowDiameterAtMaxKm;
    }

    /// <summary>
    /// Geocentric ΔRA (body − Moon) in degrees, normalised to (−180°, 180°].
    /// </summary>
    private double BodyMoonRightAscensionDifferenceDeg(
        JulianDay jdEt, EphemerisFlags iflag, Occultee target)
    {
        Span<double> bxs = stackalloc double[3];
        FetchOcculteeXyz(target, jdEt, iflag, observer: null, bxs, out _);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, iflag);
        var raBody = System.Math.Atan2(bxs[1], bxs[0]) * RadToDeg;
        var raMoon = System.Math.Atan2(moon.Position.Y, moon.Position.X) * RadToDeg;
        var d = AngleMath.NormalizeDegrees(raBody - raMoon);
        if (d > 180.0) d -= 360.0;
        return d;
    }

    /// <summary>
    /// Body-Moon RA-conjunction (transit) inside the partial window. Returns
    /// null when no transit happens during the occultation.
    /// </summary>
    private double? FindLocalApparentNoonOcc(
        double partialBeginUt, double partialEndUt, double maxUt,
        EphemerisFlags ifl, Occultee target)
    {
        var iflag = EphemerisFlags.Equatorial | ifl;
        Span<double> endpointRaDelta = stackalloc double[2];
        for (var i = 0; i < 2; i++)
        {
            var ut = i == 0 ? partialBeginUt : partialEndUt;
            var et = ut + _calendar.DeltaT(new JulianDay(ut));
            endpointRaDelta[i] = BodyMoonRightAscensionDifferenceDeg(new JulianDay(et), iflag, target);
        }
        if (endpointRaDelta[0] * endpointRaDelta[1] >= 0)
            return null;

        var tjd = maxUt;
        var dt = 0.1;
        var halfWidth = (partialEndUt - partialBeginUt) / 2.0;
        if (halfWidth < dt) dt = halfWidth / 2.0;

        Span<double> dc = stackalloc double[2];
        for (var iter = 0; iter < 50 && dt > 0.01; iter++, dt /= 3.0)
        {
            for (var i = 0; i < 2; i++)
            {
                var ut = tjd - i * dt;
                var et = ut + _calendar.DeltaT(new JulianDay(ut));
                dc[i] = BodyMoonRightAscensionDifferenceDeg(new JulianDay(et), iflag, target);
            }
            var slope = (dc[1] - dc[0]) / dt;
            if (slope < 1e-10) break;
            tjd += dc[0] / slope;
        }
        return tjd;
    }

    /// <summary>
    /// Validates and expands the user-supplied filter for occultations.
    /// Annular and annular-total are only meaningful for the Sun
    /// (<see cref="CelestialBody.Sun"/>); the impossible
    /// <c>Partial | Central</c> combo is rejected. Star-anchored occultations
    /// always reject annular/annular-total — a point source can never be
    /// larger than the Moon's apparent disc. <c>None</c> expands to "any of
    /// total / partial / non-central / central", plus
    /// <c>Annular | AnnularTotal</c> for Sun.
    /// </summary>
    private static EclipseTypeFlags NormalizeOccultationFilter(
        EclipseTypeFlags filter, Occultee target)
    {
        if (filter == (EclipseTypeFlags.Partial | EclipseTypeFlags.Central))
            throw new ArgumentException("Central partial occultations do not exist.", nameof(filter));

        var allowsAnnular = !target.IsStar && target.Body == CelestialBody.Sun;

        if (!allowsAnnular)
        {
            var stripped = filter & ~(EclipseTypeFlags.NonCentral | EclipseTypeFlags.Central);
            if (stripped == EclipseTypeFlags.Annular || filter == EclipseTypeFlags.AnnularTotal)
            {
                var what = target.IsStar ? "stars" : $"body {target.Body}";
                throw new ArgumentException(
                    $"Annular occultations do not exist for {what}.", nameof(filter));
            }
            filter &= ~(EclipseTypeFlags.Annular | EclipseTypeFlags.AnnularTotal);
        }

        if (filter == EclipseTypeFlags.None)
        {
            filter = EclipseTypeFlags.Total | EclipseTypeFlags.Partial
                   | EclipseTypeFlags.NonCentral | EclipseTypeFlags.Central;
            if (allowsAnnular)
                filter |= EclipseTypeFlags.Annular | EclipseTypeFlags.AnnularTotal;
        }
        if ((filter & (EclipseTypeFlags.Total | EclipseTypeFlags.Annular | EclipseTypeFlags.AnnularTotal)) != 0)
            filter |= EclipseTypeFlags.NonCentral | EclipseTypeFlags.Central;
        if ((filter & EclipseTypeFlags.Partial) != 0)
            filter |= EclipseTypeFlags.NonCentral;
        return filter;
    }

    // -----------------------------------------------------------------
    // Body / star fetcher (mirrors `calc_planet_star` at swecl.c#L888)
    // -----------------------------------------------------------------

    /// <summary>
    /// Body-neutral fetch of cartesian position (AU) for the body or
    /// star being occulted. <paramref name="position3"/> must have
    /// length ≥ 3. For stars the <paramref name="observer"/> argument
    /// is ignored and the fetch is geocentric — stellar parallax is
    /// sub-arcsecond, far below the Moshier residual on the Moon.
    /// </summary>
    private void FetchOcculteeXyz(
        Occultee target, JulianDay jdEt, EphemerisFlags fetchFlags,
        ObserverLocation? observer, Span<double> position3, out double distance)
    {
        if (!target.IsStar)
        {
            var st = observer is { } obs
                ? _body.Compute(target.Body, jdEt, fetchFlags, obs)
                : _body.Compute(target.Body, jdEt, fetchFlags);
            position3[0] = st.Position.X; position3[1] = st.Position.Y; position3[2] = st.Position.Z;
            distance = st.Distance;
            return;
        }

        // Star path: strip Topocentric, request polar (no Cartesian),
        // convert the polar (lon, lat in degrees, dist in AU) to xyz.
        var starFlags = (fetchFlags & ~EphemerisFlags.Topocentric)
                        & ~EphemerisFlags.Cartesian;
        var pos = _stars!.Compute(target.StarName!, jdEt, starFlags);
        var lonRad = pos.Position.X * DegToRad;
        var latRad = pos.Position.Y * DegToRad;
        var d = pos.Position.Z;
        var clat = System.Math.Cos(latRad);
        position3[0] = d * clat * System.Math.Cos(lonRad);
        position3[1] = d * clat * System.Math.Sin(lonRad);
        position3[2] = d * System.Math.Sin(latRad);
        distance = d;
    }

    /// <summary>
    /// Same as <see cref="FetchOcculteeXyz"/> but also returns the
    /// cartesian velocity. For stars the velocity is zeroed — proper
    /// motion is ≪ mas/day, well below any contact-refinement step.
    /// </summary>
    private void FetchOcculteeXyzWithVelocity(
        Occultee target, JulianDay jdEt, EphemerisFlags fetchFlags,
        ObserverLocation? observer,
        Span<double> position3, Span<double> velocity3, out double distance)
    {
        if (!target.IsStar)
        {
            var st = observer is { } obs
                ? _body.Compute(target.Body, jdEt, fetchFlags, obs)
                : _body.Compute(target.Body, jdEt, fetchFlags);
            position3[0] = st.Position.X; position3[1] = st.Position.Y; position3[2] = st.Position.Z;
            velocity3[0] = st.Velocity.X; velocity3[1] = st.Velocity.Y; velocity3[2] = st.Velocity.Z;
            distance = st.Distance;
            return;
        }

        FetchOcculteeXyz(target, jdEt, fetchFlags, observer, position3, out distance);
        velocity3[0] = velocity3[1] = velocity3[2] = 0.0;
    }

    // -----------------------------------------------------------------
    // Guard helpers
    // -----------------------------------------------------------------

    private void EnsureLocalSearchPrerequisites(GeographicLocation observer)
    {
        if (observer.AltitudeMeters < GeoAltMin || observer.AltitudeMeters > GeoAltMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"Observer altitude must be between {GeoAltMin:F0} and {GeoAltMax:F0} metres.");
        }
        if (_riseTransit is null)
        {
            throw new InvalidOperationException(
                "FindNextLocal requires the four-arg constructor with a RiseTransitService.");
        }
    }

    private void EnsureStarServiceWired()
    {
        if (_stars is null)
        {
            throw new InvalidOperationException(
                "Star occultation methods require the five-arg constructor with a FixedStarService.");
        }
    }

    /// <summary>
    /// Early-reject for stars whose ecliptic latitude exceeds 7°. The Moon's
    /// monthly path stays within ±5.3° of the ecliptic, and stellar proper
    /// motion / lunar parallax never close that gap. Probes the star at the
    /// search start once.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The star's ecliptic latitude exceeds
    /// <see cref="StarMaxEclipticLatitudeDeg"/>.
    /// </exception>
    private void EnsureStarOccultable(Occultee target, JulianDay startUt, EphemerisFlags ifl)
    {
        var jdEt = new JulianDay(startUt.Value + _calendar.DeltaT(startUt));
        Span<double> pos = stackalloc double[3];
        FetchOcculteeXyz(target, jdEt, ifl, observer: null, pos, out _);
        var rxy = System.Math.Sqrt(pos[0] * pos[0] + pos[1] * pos[1]);
        var latDeg = rxy == 0 ? 0 : System.Math.Atan2(pos[2], rxy) * RadToDeg;
        if (System.Math.Abs(latDeg) > StarMaxEclipticLatitudeDeg)
        {
            throw new InvalidOperationException(
                $"Star \"{target.StarName}\" cannot be occulted: ecliptic latitude " +
                $"{latDeg:F2}° exceeds the {StarMaxEclipticLatitudeDeg:F0}° limit.");
        }
    }

    // -----------------------------------------------------------------
    // Occultee — internal value type identifying the body or star
    // being occulted by the Moon.
    // -----------------------------------------------------------------

    /// <summary>
    /// Identifies the body or star being occulted. Either
    /// <see cref="StarName"/> is non-null (star path) or
    /// <see cref="Body"/> + <see cref="Drad"/> are populated (body
    /// path). Stays a value type so the geometry helpers can pass it
    /// around without heap allocation.
    /// </summary>
    private readonly struct Occultee
    {
        public CelestialBody Body { get; }
        public string? StarName { get; }
        public double Drad { get; }

        private Occultee(CelestialBody body, string? starName, double drad)
        {
            Body = body;
            StarName = starName;
            Drad = drad;
        }

        public bool IsStar => StarName is not null;

        public static Occultee FromBody(CelestialBody body) =>
            new(body, null, EclipseConstants.BodyRadiusAu((int)body));

        public static Occultee FromStar(string name) =>
            new(default, name, 0.0);
    }
}
