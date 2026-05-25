// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputeWhere                  — swe_sol_eclipse_where        (swecl.c#L574 ff)
//   ComputeHow                    — swe_sol_eclipse_how          (swecl.c#L939 ff)
//   EclipseWhere (helper)         — eclipse_where                (swecl.c#L640)
//   EclipseHow   (helper)         — eclipse_how                  (swecl.c#L967)
//   FindNextGlobal                — swe_sol_eclipse_when_glob    (swecl.c#L1185)
//   FindNextLocal                 — swe_sol_eclipse_when_loc     (swecl.c#L2019)
//   EclipseWhenLoc (helper)       — eclipse_when_loc             (swecl.c#L2123)
//   GeoAltMin / GeoAltMax         — SEI_ECL_GEOALT_MIN/MAX
//
// Reference driver: /tmp/solecliperef.c — golden values for solar-eclipse
// where/how at known eclipse dates, linked against the unmodified C library.
// Driver source reproduced as test comments.
//
// Stellar occultations (`swe_lun_occult_*`) live in
// `LunarOccultationService`; stellar variants of those paths are not yet
// supported because they need the fixed-star catalog.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Solar-eclipse service. Mirrors <c>swe_sol_eclipse_*</c>
/// (swecl.c#L565 and following) — single-time geometric queries
/// (<see cref="ComputeGlobalAt"/> ≡ <c>swe_sol_eclipse_where</c>,
/// <see cref="ComputeLocalAt"/> ≡ <c>swe_sol_eclipse_how</c>), global-search
/// (<see cref="FindNextGlobal"/> ≡ <c>swe_sol_eclipse_when_glob</c>) and
/// local-search (<see cref="FindNextLocal"/> ≡
/// <c>swe_sol_eclipse_when_loc</c>) are all implemented. The caller-supplied
/// <c>ifl</c> is masked down to the ephemeris-source bits before reaching
/// the inner pipeline, so caller-supplied <c>NoNutation</c>,
/// <c>TruePosition</c>, and frame bits do not propagate.
/// </summary>
public sealed class SolarEclipseService
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

    private readonly BodyService _body;
    private readonly CalendarService _calendar;
    private readonly HorizontalCoordsService _horizontal;
    private readonly RiseTransitService _riseTransit;
    private readonly AstronomicalModelOverrides _models;

    /// <summary>
    /// Constructs the service over a body service, a calendar service
    /// (used for ΔT and sidereal time), a horizontal-coords service
    /// (used for the azimuth / altitude visibility check), and a
    /// rise/transit service (used by <see cref="FindNextLocal"/> to
    /// detect Sun rise / set during the eclipse window).
    /// </summary>
    /// <param name="body">Body-position service.</param>
    /// <param name="calendar">Calendar / time service.</param>
    /// <param name="horizontal">Horizontal-coordinates service.</param>
    /// <param name="riseTransit">Rise / set / transit service.</param>
    /// <param name="models">
    /// Optional astronomical-model overrides. Defaults to
    /// <see cref="AstronomicalModelOverrides.Default"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Any of the four required collaborators is <see langword="null"/>.
    /// </exception>
    public SolarEclipseService(
        BodyService body,
        CalendarService calendar,
        HorizontalCoordsService horizontal,
        RiseTransitService riseTransit,
        AstronomicalModelOverrides? models = null)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        _riseTransit = riseTransit ?? throw new ArgumentNullException(nameof(riseTransit));
        _models = models ?? AstronomicalModelOverrides.Default;
    }

    /// <summary>
    /// Determines the type and geographic position of maximum eclipse for
    /// the moment <paramref name="jdUt"/>. Mirrors <c>swe_sol_eclipse_where</c>.
    /// Returns <see cref="EclipseTypeFlags.None"/> in the report when no
    /// eclipse is taking place.
    /// </summary>
    public SolarEclipseGlobalReport ComputeGlobalAt(JulianDay jdUt, EphemerisFlags ephemerisFlags)
    {
        var ifl = ephemerisFlags & EphMask;
        var (whereType, geo, geom) = EclipseWhere(jdUt, ifl);
        var attrs = ComputeAttributesAt(jdUt, ifl, geo, geom.CoreShadowDiameterAtMaxKm);
        return new SolarEclipseGlobalReport(whereType, geo, attrs, geom);
    }

    /// <summary>
    /// Determines the observer-specific eclipse view at <paramref name="jdUt"/>
    /// from <paramref name="observer"/>. Mirrors <c>swe_sol_eclipse_how</c>.
    /// Returns <see cref="EclipseTypeFlags.None"/> when the observer is
    /// outside the shadow cone at this instant.
    /// </summary>
    public SolarEclipseLocalReport ComputeLocalAt(
        JulianDay jdUt,
        EphemerisFlags ephemerisFlags,
        GeographicLocation observer)
    {
        if (observer.AltitudeMeters < GeoAltMin || observer.AltitudeMeters > GeoAltMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"Observer altitude must be between {GeoAltMin:F0} and {GeoAltMax:F0} metres.");
        }

        var ifl = ephemerisFlags & EphMask;

        // 1) eclipse_how at the observer location (also fills attr[4..6]).
        var (typeFlags, attrs) = EclipseHow(jdUt, ifl, observer);

        // 2) eclipse_where for central/noncentral classification + dcore[0].
        var (whereType, _, whereGeom) = EclipseWhere(jdUt, ifl);
        if (typeFlags != EclipseTypeFlags.None)
            typeFlags |= whereType & (EclipseTypeFlags.Central | EclipseTypeFlags.NonCentral);
        attrs = attrs with { CoreShadowDiameterKm = whereGeom.CoreShadowDiameterAtMaxKm };

        // 3) Visibility: apparent altitude ≤ 0 ⇒ no visible eclipse here.
        if (attrs.SunApparentAltitudeDeg <= 0.0)
            typeFlags = EclipseTypeFlags.None;

        if (typeFlags == EclipseTypeFlags.None)
        {
            attrs = attrs with
            {
                DiameterFractionCovered = 0,
                DiameterRatioMoonOverBody = 0,
                DiscFractionObscured = 0,
                CoreShadowDiameterKm = 0,
                MagnitudeNasa = 0,
                SarosSeriesNumber = 0,
                SarosSeriesMemberNumber = 0,
            };
        }

        return new SolarEclipseLocalReport(typeFlags, attrs);
    }

    /// <summary>
    /// Computes the eclipse type, the geographic location of the maximum
    /// (or shadow-axis touchdown), and the fundamental-plane geometry.
    /// </summary>
    private (EclipseTypeFlags Type, GeographicLocation Location, SolarEclipseGeometry Geometry)
        EclipseWhere(JulianDay jdUt, EphemerisFlags ifl)
    {
        var dt = _calendar.DeltaT(jdUt);
        var jdEt = new JulianDay(jdUt.Value + dt);

        // iflag = SEFLG_EQUATORIAL | ifl (Speed dropped — eclipse_where ignores velocities).
        var fetchFlags = EphemerisFlags.Equatorial | ifl;

        var moonState = _body.Compute(CelestialBody.Moon, jdEt, fetchFlags);
        var sunState = _body.Compute(CelestialBody.Sun, jdEt, fetchFlags);

        // Saved originals (rmt, rst).
        Span<double> rmInit = stackalloc double[3];
        rmInit[0] = moonState.Position.X; rmInit[1] = moonState.Position.Y; rmInit[2] = moonState.Position.Z;
        Span<double> rsInit = stackalloc double[3];
        rsInit[0] = sunState.Position.X; rsInit[1] = sunState.Position.Y; rsInit[2] = sunState.Position.Z;

        // Apparent sidereal time at Greenwich, in radians.
        // (NONUT branch is unreachable: ifl was masked to source bits only.)
        var sidtRad = _calendar.SiderealTime(jdUt) * 15.0 * DegToRad;

        // Body radii in AU.
        const double drad = EclipseConstants.SunRadiusAu;
        const double rmoon = EclipseConstants.MoonRadiusAu;
        const double dmoon = 2.0 * rmoon;
        const double de = EclipseConstants.EarthReferenceRadiusAu;
        var earthobl = 1.0 - EclipseConstants.EarthOblateness;

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

        // Two iterations: niter==0 refines earthobl from the place's latitude,
        // niter==1 finalises (mirrors the goto in swecl.c#L705-L851).
        for (int niter = 0; niter < 2; niter++)
        {
            rm[0] = rmInit[0]; rm[1] = rmInit[1]; rm[2] = rmInit[2];
            rs[0] = rsInit[0]; rs[1] = rsInit[1]; rs[2] = rsInit[2];

            // Apply oblateness to z.
            rm[2] /= earthobl;
            rs[2] /= earthobl;

            var dm = System.Math.Sqrt(rm[0] * rm[0] + rm[1] * rm[1] + rm[2] * rm[2]);

            // Sun-Moon vector e (oblateness-corrected) and et (original).
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

            // Distance shadow point ↔ fundamental plane.
            var d = s0 * s0 + de * de - dm * dm;
            d = d > 0 ? System.Math.Sqrt(d) : 0;
            var s = s0 - d;

            // Geographic position of eclipse maximum (squished frame).
            for (int i = 0; i < 3; i++)
                xs[i] = rm[i] + s * e[i];
            // Un-squished z.
            xst[0] = xs[0]; xst[1] = xs[1]; xst[2] = xs[2] * earthobl;

            if (niter == 0)
            {
                // Compute lat from un-squished xst.
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

            // Finalise: longitude of xs (still squished for lon, that's fine — atan2
            // unaffected since z is irrelevant to lon).
            var lonRad = System.Math.Atan2(xs[1], xs[0]);
            var rxyXs = System.Math.Sqrt(xs[0] * xs[0] + xs[1] * xs[1]);
            var latRad = rxyXs == 0
                ? (xs[2] >= 0 ? System.Math.PI / 2 : -System.Math.PI / 2)
                : System.Math.Atan(xs[2] / rxyXs);

            // C's "no_eclipse" branch sets geopos to 0 transiently but then
            // overwrites geopos[] with xs's iterated polar (swecl.c#L774-L863).
            // We mirror the *final* state — geographic position is always the
            // iterated maximum, regardless of no_eclipse.
            lonRad -= sidtRad;
            var lonDeg = AngleMath.NormalizeDegrees(lonRad * RadToDeg);
            if (lonDeg > 180.0) lonDeg -= 360.0;
            var latDeg = latRad * RadToDeg;

            // Distance moon ↔ place of eclipse on Earth (un-squished).
            var dx = rmInit[0] - xst[0];
            var dy = rmInit[1] - xst[1];
            var dz = rmInit[2] - xst[2];
            var sToObs = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // Diameters at the place of maximum (km).
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
            $"SolarEclipseService.EclipseWhere: oblateness-iteration loop "
            + $"exited without producing a result at jdUt={jdUt.Value:R}, ifl={ifl}.");
    }

    /// <summary>
    /// Returns the eclipse-type bits (without central/noncentral, which the
    /// caller composes from <see cref="EclipseWhere"/>) and the per-observer
    /// attribute set. Fields that depend on horizon coords / dcore[0] are
    /// filled in by the caller.
    /// </summary>
    private (EclipseTypeFlags Type, SolarEclipseAttributes Attrs)
        EclipseHow(JulianDay jdUt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var dt = _calendar.DeltaT(jdUt);
        var jdEt = new JulianDay(jdUt.Value + dt);
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);

        // iflag = SEFLG_EQUATORIAL | SEFLG_TOPOCTR | ifl
        var fetchFlags = EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | ifl;

        var sunCartState = _body.Compute(CelestialBody.Sun, jdEt, fetchFlags, observerLoc);
        var moonCartState = _body.Compute(CelestialBody.Moon, jdEt, fetchFlags, observerLoc);

        Span<double> xs = stackalloc double[3];
        xs[0] = sunCartState.Position.X; xs[1] = sunCartState.Position.Y; xs[2] = sunCartState.Position.Z;
        Span<double> xm = stackalloc double[3];
        xm[0] = moonCartState.Position.X; xm[1] = moonCartState.Position.Y; xm[2] = moonCartState.Position.Z;

        var ds = System.Math.Sqrt(xs[0] * xs[0] + xs[1] * xs[1] + xs[2] * xs[2]);
        var dm = System.Math.Sqrt(xm[0] * xm[0] + xm[1] * xm[1] + xm[2] * xm[2]);

        const double drad = EclipseConstants.SunRadiusAu;
        const double rmoonRad = EclipseConstants.MoonRadiusAu;

        var rmoon = System.Math.Asin(rmoonRad / dm) * RadToDeg;
        var rsun = System.Math.Asin(drad / ds) * RadToDeg;
        var rsplusrm = rsun + rmoon;
        var rsminusrm = rsun - rmoon;

        // Unit-vector dot product → angular distance.
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

        // Eclipse magnitude (fraction of solar diameter covered).
        var lsunFlat = System.Math.Asin(rsun / 2 * DegToRad) * 2;
        var lsunleft = -dctr + rsun + rmoon;
        var fractionDiameterCovered = lsunFlat > 0 ? lsunleft / rsun / 2 : 1.0;

        // Obscuration (fraction of solar disc area covered).
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

        double magnitudeNasa = fractionDiameterCovered;
        if ((retc & (EclipseTypeFlags.Total | EclipseTypeFlags.Annular)) != 0)
            magnitudeNasa = diameterRatio;

        // Az/alt of the body, mirroring swecl.c#L1027-L1029
        // (swe_azalt EQU2HOR with topocentric polar Sun in deg).
        // Convert topocentric Sun cart → polar (deg).
        Span<double> sunPolar = stackalloc double[6];
        Span<double> sunCart6 = stackalloc double[6];
        sunCart6[0] = xs[0]; sunCart6[1] = xs[1]; sunCart6[2] = xs[2];
        Polar.CartesianToPolarWithSpeed(sunCart6, sunPolar);
        var horiz = _horizontal.ToHorizontal(
            jdUt, HorizontalConversionInput.FromEquatorial,
            observer, atPressMbar: 0.0, atTempC: 10.0,
            sunPolar[0] * RadToDeg, sunPolar[1] * RadToDeg);

        // Visibility flag (swecl.c#L1109-L1116):
        // hmin_appr accounts for refraction (34.4556' at horizon) and dip
        // (1.75 + 0.37) / sqrt(geohgt) — but swe_azalt already returns the
        // refracted apparent altitude, so we mirror C's check using xh[1]
        // (true alt) + rsun + |hmin_appr|.
        var hminAppr = -(34.4556 + (1.75 + 0.37) * System.Math.Sqrt(observer.AltitudeMeters)) / 60.0;
        if (retc != EclipseTypeFlags.None
            && horiz.TrueAltitudeDeg + rsun + System.Math.Abs(hminAppr) >= 0)
        {
            retc |= EclipseTypeFlags.Visible;
        }

        var (sarosSeries, sarosMember) = SarosTables.LookupSolar(jdUt.Value);

        var attrs = new SolarEclipseAttributes(
            DiameterFractionCovered: fractionDiameterCovered,
            DiameterRatioMoonOverBody: diameterRatio,
            DiscFractionObscured: obscuration,
            CoreShadowDiameterKm: 0.0,
            SunAzimuthDeg: horiz.AzimuthDeg,
            SunTrueAltitudeDeg: horiz.TrueAltitudeDeg,
            SunApparentAltitudeDeg: horiz.ApparentAltitudeDeg,
            MoonBodyAngularDistanceDeg: dctr,
            MagnitudeNasa: magnitudeNasa,
            SarosSeriesNumber: sarosSeries,
            SarosSeriesMemberNumber: sarosMember);

        return (retc, attrs);
    }

    /// <summary>
    /// Computes the attribute set at a known geographic location, used by
    /// <see cref="ComputeGlobalAt"/> after <c>eclipse_where</c> reports the
    /// place of maximum.
    /// </summary>
    private SolarEclipseAttributes ComputeAttributesAt(
        JulianDay jdUt,
        EphemerisFlags ifl,
        GeographicLocation observer,
        double coreShadowDiamKm)
    {
        var (_, attrs) = EclipseHow(jdUt, ifl, observer);
        return attrs with { CoreShadowDiameterKm = coreShadowDiamKm };
    }

    // -----------------------------------------------------------------
    // when_glob — global eclipse search (swecl.c#L1185)
    // -----------------------------------------------------------------

    /// <summary>Earth equatorial radius (km) used by the parabola sweeps.</summary>
    private const double EarthEquatorialRadiusKm = 6378.140;

    /// <summary>Defensive cap on the lunation-counter sweep (~80 years).
    /// Solar eclipses recur every ~6 months, so any realistic search converges
    /// within a handful of K-steps; this guard only prevents pathological
    /// hangs on bad input.</summary>
    private const int MaxLunationSearchSteps = 1000;

    private const double TwoHoursInDays = 2.0 / 24.0;
    private const double TenMinutesInDays = 10.0 / 24.0 / 60.0;

    /// <summary>
    /// Finds the next solar eclipse globally visible somewhere on Earth
    /// after (or, when <paramref name="backward"/> is true, before)
    /// <paramref name="startUt"/>. Mirrors <c>swe_sol_eclipse_when_glob</c>
    /// (swecl.c#L1185).
    /// </summary>
    /// <param name="startUt">Search start time (UT).</param>
    /// <param name="ephemerisFlags">Ephemeris source flags. Only the source
    /// bits (<c>JplEph | SwissEph | MoshierEph</c>) are honoured — the C
    /// implementation strips everything else (swecl.c#L1206).</param>
    /// <param name="eclipseTypeFilter">Restrict the search to the given
    /// <see cref="EclipseTypeFlags"/> subset (<c>Total | Annular | Partial |
    /// AnnularTotal | Central | NonCentral</c>). <see cref="EclipseTypeFlags.None"/>
    /// matches any eclipse — equivalent to passing <c>0</c> in the C API.
    /// Forbidden combinations (<c>Partial | Central</c> or
    /// <c>AnnularTotal | NonCentral</c>) throw <see cref="ArgumentException"/>.</param>
    /// <param name="backward">When true, search backwards in time.</param>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> wrapping the found eclipse. When the
    /// safety cap is exceeded without locating an eclipse, the result carries
    /// <c>default(SolarEclipseGlobalSearchReport)</c> together with a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> string — mirroring the C
    /// library's <c>serr</c> / <c>retc &lt; 0</c> soft-failure convention.
    /// </returns>
    public EphemerisResult<SolarEclipseGlobalSearchReport> FindNextGlobal(
        JulianDay startUt,
        EphemerisFlags ephemerisFlags,
        EclipseTypeFlags eclipseTypeFilter = EclipseTypeFlags.None,
        bool backward = false)
    {
        var ifl = ephemerisFlags & EphMask;
        var ifltype = NormalizeEclipseFilter(eclipseTypeFilter);
        var direction = backward ? -1 : 1;

        // K = approximate lunation count since J2000 (Meeus, German p. 379).
        var k = (double)(int)((startUt.Value - AstronomicalConstants.J2000) / 365.2425 * 12.3685);
        k -= direction;

        for (var step = 0; step < MaxLunationSearchSteps; step++)
        {
            var (foundReport, advance) = TrySolarEclipseAtLunation(k, ifl, ifltype, startUt, backward);
            if (foundReport.HasValue)
                return EphemerisResult<SolarEclipseGlobalSearchReport>.Ok(foundReport.Value);
            k += direction * advance;
        }

        return EphemerisResult<SolarEclipseGlobalSearchReport>.WithWarning(
            default,
            "Solar-eclipse search exceeded the safety cap of " +
            $"{MaxLunationSearchSteps} lunation steps without finding a match.");
    }

    /// <summary>
    /// One iteration of the lunation-by-lunation search. Returns either the
    /// found eclipse, or the number of K-steps to advance before retrying.
    /// </summary>
    private (SolarEclipseGlobalSearchReport? Report, int Advance)
        TrySolarEclipseAtLunation(double k, EphemerisFlags ifl, EclipseTypeFlags ifltype, JulianDay startUt, bool backward)
    {
        var t = k / 1236.85;
        var t2 = t * t;
        var t3 = t2 * t;
        var t4 = t3 * t;

        // Argument of latitude at lunation; Meeus formula. If far from a node,
        // no eclipse is possible — skip ahead.
        var ff = AngleMath.NormalizeDegrees(160.7108 + 390.67050274 * k
                                            - 0.0016341 * t2
                                            - 0.00000227 * t3
                                            + 0.000000011 * t4);
        if (ff > 180.0) ff -= 180.0;
        if (ff > 21.0 && ff < 159.0)
            return (null, 1);

        // Approximate TT of geocentric maximum (Meeus, German p. 381).
        var tjd = 2451550.09765 + 29.530588853 * k
                                + 0.0001337 * t2
                                - 0.000000150 * t3
                                + 0.00000000073 * t4;
        var m = AngleMath.NormalizeDegrees(2.5534 + 29.10535669 * k
                                                  - 0.0000218 * t2
                                                  - 0.00000011 * t3);
        var mm = AngleMath.NormalizeDegrees(201.5643 + 385.81693528 * k
                                                     + 0.1017438 * t2
                                                     + 0.00001239 * t3
                                                     + 0.000000058 * t4);
        var e = 1.0 - 0.002516 * t - 0.0000074 * t2;
        m *= DegToRad;
        mm *= DegToRad;
        tjd = tjd - 0.4075 * System.Math.Sin(mm) + 0.1721 * e * System.Math.Sin(m);

        // Refine tjd by parabola fit on the geocentric Sun-Moon edge angle.
        var dtStart = (tjd < 2_000_000.0 || tjd > 2_500_000.0) ? 5.0 : 1.0;
        const double dtDiv = 4.0;
        var iflag = EphemerisFlags.Equatorial | ifl;
        Span<double> dc = stackalloc double[3];
        for (var dt = dtStart; dt > 0.0001; dt /= dtDiv)
        {
            var tt = tjd - dt;
            for (var i = 0; i < 3; i++)
            {
                dc[i] = GeocentricSunMoonEdgeAngle(new JulianDay(tt), iflag);
                tt += dt;
            }
            ParabolaFit.FindMaximum(dc[0], dc[1], dc[2], dt, out var dtInt, out _);
            tjd += dtInt + dt;
        }

        // Convert TT max → UT max via 3-fold ΔT iteration (swecl.c#L1301-L1303).
        var tjds = tjd - _calendar.DeltaT(new JulianDay(tjd));
        tjds = tjd - _calendar.DeltaT(new JulianDay(tjds));
        tjds = tjd - _calendar.DeltaT(new JulianDay(tjds));
        var maxUt = tjds;

        // Classification at maximum (eclipse_where).
        var (whereType, whereLoc, _) = EclipseWhere(new JulianDay(maxUt), ifl);

        // Sanity check via eclipse_how at the touchdown (swecl.c#L1307-L1312).
        // In extreme cases _where() reports no eclipse where _how() at the
        // returned coordinates does — so we trust _how() to make the final
        // existence call.
        var (howType, _) = EclipseHow(new JulianDay(maxUt), ifl, whereLoc);
        if (howType == EclipseTypeFlags.None)
            return (null, 1);

        // Strict-direction check: tret[0] must be past startUt in the chosen
        // direction (swecl.c#L1317-L1321).
        if ((backward && maxUt >= startUt.Value - 0.0001)
            || (!backward && maxUt <= startUt.Value + 0.0001))
            return (null, 1);

        // No-eclipse path: where() returned 0. C falls through to dont_times,
        // setting tret[4] = tret[5] = tjd and skipping all phase searches.
        var dontTimes = false;
        var retc = whereType;
        if (retc == EclipseTypeFlags.None)
        {
            retc = EclipseTypeFlags.Partial | EclipseTypeFlags.NonCentral;
            dontTimes = true;
        }

        // Filter checks (swecl.c#L1337-L1360).
        if ((ifltype & EclipseTypeFlags.NonCentral) == 0 && (retc & EclipseTypeFlags.NonCentral) != 0)
            return (null, 1);
        if ((ifltype & EclipseTypeFlags.Central) == 0 && (retc & EclipseTypeFlags.Central) != 0)
            return (null, 1);
        if ((ifltype & EclipseTypeFlags.Annular) == 0 && (retc & EclipseTypeFlags.Annular) != 0)
            return (null, 1);
        if ((ifltype & EclipseTypeFlags.Partial) == 0 && (retc & EclipseTypeFlags.Partial) != 0)
            return (null, 1);
        // annular-total is discovered later — at this point retc carries Total only.
        if ((ifltype & (EclipseTypeFlags.Total | EclipseTypeFlags.AnnularTotal)) == 0
            && (retc & EclipseTypeFlags.Total) != 0)
            return (null, 1);

        if (dontTimes)
        {
            return (
                BuildSearchReport(retc, maxUt, null, 0, 0, maxUt, maxUt, null, null),
                0);
        }

        // Phase sweeps (swecl.c#L1376-L1418).
        // n=0: partial begin/end (always)
        // n=1: totality begin/end (skip if pure partial)
        // n=2: centerline begin/end (skip if non-central)
        var partialBegin = 0.0;
        var partialEnd = 0.0;
        double? totalityBegin = null;
        double? totalityEnd = null;
        double? centerBegin = null;
        double? centerEnd = null;
        var phaseLast = (retc & EclipseTypeFlags.Partial) != 0
            ? 0
            : (retc & EclipseTypeFlags.NonCentral) != 0 ? 1 : 2;
        var dta = TwoHoursInDays;
        var dtb = TenMinutesInDays / 3.0;
        Span<double> dcEdge = stackalloc double[3];
        for (var n = 0; n <= phaseLast; n++)
        {
            if (n == 1 && (retc & EclipseTypeFlags.Partial) != 0) continue;
            if (n == 2 && (retc & EclipseTypeFlags.NonCentral) != 0) continue;

            // Sample edge condition at maxUt − dta, maxUt, maxUt + dta.
            var ts = maxUt - dta;
            for (var i = 0; i < 3; i++)
            {
                dcEdge[i] = PhaseEdgeMetric(n, new JulianDay(ts), ifl);
                ts += dta;
            }
            if (ParabolaFit.FindZero(dcEdge[0], dcEdge[1], dcEdge[2], dta, out var dt1Init, out var dt2Init) is false)
            {
                // Fall back: if the parabola has no real roots, treat the
                // window as the eclipse limits unchanged (matches C's
                // mid-iteration behaviour where dc retains its sign).
                dt1Init = -dta;
                dt2Init = dta;
            }
            var beginTime = maxUt + dt1Init + dta;
            var endTime = maxUt + dt2Init + dta;

            // Refine each endpoint via three Newton-style steps.
            for (var mIter = 0; mIter < 3; mIter++)
            {
                var dtRef = dtb / System.Math.Pow(3.0, mIter);
                beginTime = RefinePhaseEdge(n, beginTime, dtRef, ifl);
                endTime = RefinePhaseEdge(n, endTime, dtRef, ifl);
            }

            switch (n)
            {
                case 0: partialBegin = beginTime; partialEnd = endTime; break;
                case 1: totalityBegin = beginTime; totalityEnd = endTime; break;
                case 2: centerBegin = beginTime; centerEnd = endTime; break;
            }
        }

        // Annular-total upgrade (swecl.c#L1422-L1440): if the dcore[0] sign
        // changes between max and totality begin/end, the eclipse is hybrid.
        if ((retc & EclipseTypeFlags.Total) != 0
            && totalityBegin.HasValue && totalityEnd.HasValue)
        {
            var coreAtMax = SignedCoreShadowDiameterKm(new JulianDay(maxUt), ifl);
            var coreAtBegin = SignedCoreShadowDiameterKm(new JulianDay(totalityBegin.Value), ifl);
            var coreAtEnd = SignedCoreShadowDiameterKm(new JulianDay(totalityEnd.Value), ifl);
            if (coreAtMax * coreAtBegin < 0 || coreAtMax * coreAtEnd < 0)
            {
                retc |= EclipseTypeFlags.AnnularTotal;
                retc &= ~EclipseTypeFlags.Total;
            }
        }

        if ((ifltype & EclipseTypeFlags.Total) == 0 && (retc & EclipseTypeFlags.Total) != 0)
            return (null, 1);
        if ((ifltype & EclipseTypeFlags.AnnularTotal) == 0 && (retc & EclipseTypeFlags.AnnularTotal) != 0)
            return (null, 1);

        // Local-apparent-noon time (swecl.c#L1456-L1497).
        var localApparentNoon = FindLocalApparentNoon(
            partialBegin, partialEnd, maxUt, ifl);

        return (
            BuildSearchReport(retc, maxUt, localApparentNoon,
                partialBegin, partialEnd,
                totalityBegin, totalityEnd,
                centerBegin, centerEnd),
            0);
    }

    /// <summary>Builds the public report from the raw double-precision phase times.</summary>
    private static SolarEclipseGlobalSearchReport BuildSearchReport(
        EclipseTypeFlags type,
        double maxUt,
        double? localApparentNoonUt,
        double partialBeginUt,
        double partialEndUt,
        double? totalityBeginUt,
        double? totalityEndUt,
        double? centerBeginUt,
        double? centerEndUt)
        => new(
            EclipseType: type,
            MaximumTime: new JulianDay(maxUt),
            LocalApparentNoonTime: localApparentNoonUt is null ? null : new JulianDay(localApparentNoonUt.Value),
            PartialBeginTime: new JulianDay(partialBeginUt),
            PartialEndTime: new JulianDay(partialEndUt),
            TotalityBeginTime: totalityBeginUt is null ? null : new JulianDay(totalityBeginUt.Value),
            TotalityEndTime: totalityEndUt is null ? null : new JulianDay(totalityEndUt.Value),
            CenterLineBeginTime: centerBeginUt is null ? null : new JulianDay(centerBeginUt.Value),
            CenterLineEndTime: centerEndUt is null ? null : new JulianDay(centerEndUt.Value));

    /// <summary>
    /// Geocentric Sun-Moon-edge angular distance in degrees, evaluated at TT
    /// time <paramref name="jdEt"/>.
    /// </summary>
    private double GeocentricSunMoonEdgeAngle(JulianDay jdEt, EphemerisFlags iflag)
    {
        var sun = _body.Compute(CelestialBody.Sun, jdEt, iflag);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, iflag);
        var ds = sun.Distance;
        var dm = moon.Distance;
        var sx = sun.Position.X / ds;
        var sy = sun.Position.Y / ds;
        var sz = sun.Position.Z / ds;
        var mx = moon.Position.X / dm;
        var my = moon.Position.Y / dm;
        var mz = moon.Position.Z / dm;
        var dot = sx * mx + sy * my + sz * mz;
        if (dot > 1.0) dot = 1.0;
        else if (dot < -1.0) dot = -1.0;
        var dctr = System.Math.Acos(dot) * RadToDeg;
        var rmoon = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg;
        var rsun = System.Math.Asin(EclipseConstants.SunRadiusAu / ds) * RadToDeg;
        return dctr - (rmoon + rsun);
    }

    /// <summary>
    /// Phase-edge metric. Returns the signed quantity whose zero crossings are
    /// the begin/end of the requested phase (n=0 partial, n=1 totality, n=2
    /// centerline) — all in km.
    /// </summary>
    private double PhaseEdgeMetric(int n, JulianDay jdUt, EphemerisFlags ifl)
    {
        var (_, _, geom) = EclipseWhere(jdUt, ifl);
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

    /// <summary>
    /// One Newton-style refinement step around <paramref name="ut"/> for the
    /// requested phase metric.
    /// </summary>
    private double RefinePhaseEdge(int n, double ut, double dt, EphemerisFlags ifl)
    {
        Span<double> dc = stackalloc double[2];
        for (var i = 0; i < 2; i++)
            dc[i] = PhaseEdgeMetric(n, new JulianDay(ut - dt + i * dt), ifl);
        var slope = (dc[1] - dc[0]) / dt;
        if (slope == 0) return ut;
        return ut - dc[1] / slope;
    }

    /// <summary>
    /// Signed core-shadow diameter (km) at <paramref name="jdUt"/>, used for
    /// the annular-total transition test.
    /// </summary>
    private double SignedCoreShadowDiameterKm(JulianDay jdUt, EphemerisFlags ifl)
    {
        var (_, _, geom) = EclipseWhere(jdUt, ifl);
        return geom.CoreShadowDiameterAtMaxKm;
    }

    /// <summary>
    /// Searches for the moment of geocentric Sun-Moon RA conjunction
    /// ("local apparent noon") between the partial endpoints. Returns null
    /// when no transit happens during the eclipse.
    /// </summary>
    private double? FindLocalApparentNoon(
        double partialBeginUt, double partialEndUt, double maxUt, EphemerisFlags ifl)
    {
        var iflag = EphemerisFlags.Equatorial | ifl;
        Span<double> endpointRaDelta = stackalloc double[2];
        for (var i = 0; i < 2; i++)
        {
            var ut = i == 0 ? partialBeginUt : partialEndUt;
            var et = ut + _calendar.DeltaT(new JulianDay(ut));
            endpointRaDelta[i] = SunMoonRightAscensionDifferenceDeg(new JulianDay(et), iflag);
        }
        if (endpointRaDelta[0] * endpointRaDelta[1] >= 0)
            return null;

        var tjd = maxUt;
        var dt = 0.1;
        var halfWidth = (partialEndUt - partialBeginUt) / 2.0;
        if (halfWidth < dt)
            dt = halfWidth / 2.0;

        Span<double> dc = stackalloc double[2];
        for (var iter = 0; iter < 50 && dt > 0.01; iter++, dt /= 3.0)
        {
            for (var i = 0; i < 2; i++)
            {
                var ut = tjd - i * dt;
                var et = ut + _calendar.DeltaT(new JulianDay(ut));
                dc[i] = SunMoonRightAscensionDifferenceDeg(new JulianDay(et), iflag);
            }
            var slope = (dc[1] - dc[0]) / dt;
            if (slope < 1e-10) break;
            tjd += dc[0] / slope;
        }
        return tjd;
    }

    /// <summary>
    /// Geocentric ΔRA (Sun − Moon) in degrees, normalised to (−180°, 180°].
    /// </summary>
    private double SunMoonRightAscensionDifferenceDeg(JulianDay jdEt, EphemerisFlags iflag)
    {
        var sun = _body.Compute(CelestialBody.Sun, jdEt, iflag);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, iflag);
        var raSun = System.Math.Atan2(sun.Position.Y, sun.Position.X) * RadToDeg;
        var raMoon = System.Math.Atan2(moon.Position.Y, moon.Position.X) * RadToDeg;
        var d = AngleMath.NormalizeDegrees(raSun - raMoon);
        if (d > 180.0) d -= 360.0;
        return d;
    }

    /// <summary>
    /// Expands a user-supplied filter to the full canonical set of bits.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The filter is one of the impossible combinations
    /// (<c>Partial | Central</c>, <c>AnnularTotal | NonCentral</c>).
    /// </exception>
    private static EclipseTypeFlags NormalizeEclipseFilter(EclipseTypeFlags filter)
    {
        if (filter == (EclipseTypeFlags.Partial | EclipseTypeFlags.Central))
            throw new ArgumentException("Central partial eclipses do not exist.", nameof(filter));
        if (filter == (EclipseTypeFlags.AnnularTotal | EclipseTypeFlags.NonCentral))
            throw new ArgumentException("Non-central hybrid (annular-total) eclipses do not exist.", nameof(filter));

        if (filter == EclipseTypeFlags.None)
            return EclipseTypeFlags.Total | EclipseTypeFlags.Annular | EclipseTypeFlags.Partial
                 | EclipseTypeFlags.AnnularTotal | EclipseTypeFlags.NonCentral | EclipseTypeFlags.Central;

        if (filter == EclipseTypeFlags.Total
            || filter == EclipseTypeFlags.Annular
            || filter == EclipseTypeFlags.AnnularTotal)
            filter |= EclipseTypeFlags.NonCentral | EclipseTypeFlags.Central;

        if (filter == EclipseTypeFlags.Partial)
            filter |= EclipseTypeFlags.NonCentral;

        return filter;
    }

    // -----------------------------------------------------------------
    // when_loc — local eclipse search (swecl.c#L2019 + helper L2100)
    // -----------------------------------------------------------------

    private const double TwoMinutesInDays = 2.0 / 24.0 / 60.0;
    private const double TenSecondsInDays = 10.0 / 24.0 / 60.0 / 60.0;
    /// <summary>Empirical fudge factor on the Moon's angular radius for
    /// 2nd/3rd contact accuracy.</summary>
    private const double MoonRadiusFudgeContacts = 0.99916;

    /// <summary>
    /// Finds the next solar eclipse visible from <paramref name="observer"/>
    /// after (or, when <paramref name="backward"/> is true, before)
    /// <paramref name="startUt"/>. Mirrors <c>swe_sol_eclipse_when_loc</c>
    /// (swecl.c#L2019) — combines the global-search lunation iteration with
    /// a topocentric occultation refinement and a per-observer visibility
    /// check that rejects eclipses entirely below the horizon.
    /// </summary>
    /// <param name="startUt">Search start time (UT).</param>
    /// <param name="ephemerisFlags">Ephemeris source flags. Only the source
    /// bits are honoured; everything else is masked out (mirrors
    /// swecl.c#L2029).</param>
    /// <param name="observer">Geographic location. Altitude must lie within
    /// the C-implementation's <c>SEI_ECL_GEOALT_MIN..MAX</c> band
    /// (-500..25000 m).</param>
    /// <param name="backward">When true, search backwards in time.</param>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located eclipse, or
    /// <c>default(SolarEclipseLocalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when the search exhausted its
    /// safety cap without finding a visible match.
    /// </returns>
    public EphemerisResult<SolarEclipseLocalSearchReport> FindNextLocal(
        JulianDay startUt,
        EphemerisFlags ephemerisFlags,
        GeographicLocation observer,
        bool backward = false)
    {
        if (observer.AltitudeMeters < GeoAltMin || observer.AltitudeMeters > GeoAltMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"Observer altitude must be between {GeoAltMin:F0} and {GeoAltMax:F0} metres.");
        }

        var ifl = ephemerisFlags & EphMask;
        var direction = backward ? -1 : 1;

        var k = (double)(int)((startUt.Value - AstronomicalConstants.J2000) / 365.2425 * 12.3685);
        k -= direction;

        for (var step = 0; step < MaxLunationSearchSteps; step++)
        {
            var report = TryLocalEclipseAtLunation(k, ifl, observer, startUt, backward);
            if (report.HasValue) return EphemerisResult<SolarEclipseLocalSearchReport>.Ok(report.Value);
            k += direction;
        }

        return EphemerisResult<SolarEclipseLocalSearchReport>.WithWarning(
            default,
            "Local solar-eclipse search exceeded the safety cap of " +
            $"{MaxLunationSearchSteps} lunation steps without finding a match.");
    }

    /// <summary>
    /// One iteration of the local lunation-by-lunation search. Returns the
    /// located eclipse or null to advance K.
    /// </summary>
    private SolarEclipseLocalSearchReport? TryLocalEclipseAtLunation(
        double k, EphemerisFlags ifl, GeographicLocation observer, JulianDay startUt, bool backward)
    {
        var t = k / 1236.85;
        var t2 = t * t;
        var t3 = t2 * t;
        var t4 = t3 * t;

        var ff = AngleMath.NormalizeDegrees(160.7108 + 390.67050274 * k
                                            - 0.0016341 * t2
                                            - 0.00000227 * t3
                                            + 0.000000011 * t4);
        if (ff > 180.0) ff -= 180.0;
        if (ff > 21.0 && ff < 159.0) return null;

        // Approximate TT of geocentric maximum (Meeus, German p. 381).
        var tjd = 2451550.09765 + 29.530588853 * k
                                + 0.0001337 * t2
                                - 0.000000150 * t3
                                + 0.00000000073 * t4;
        var m = AngleMath.NormalizeDegrees(2.5534 + 29.10535669 * k
                                                  - 0.0000218 * t2
                                                  - 0.00000011 * t3);
        var mm = AngleMath.NormalizeDegrees(201.5643 + 385.81693528 * k
                                                     + 0.1017438 * t2
                                                     + 0.00001239 * t3
                                                     + 0.000000058 * t4);
        var em = 1.0 - 0.002516 * t - 0.0000074 * t2;
        m *= DegToRad;
        mm *= DegToRad;
        tjd = tjd - 0.4075 * System.Math.Sin(mm) + 0.1721 * em * System.Math.Sin(m);

        // Refine TT max via parabola fit on topocentric Sun-Moon angle
        // (swecl.c#L2167-L2193). Smaller dt floor (1e-5) than when_glob.
        var dtStart = (tjd < 1_900_000.0 || tjd > 2_500_000.0) ? 2.0 : 0.5;
        var dtDiv = 2.0;
        Span<double> dc = stackalloc double[3];
        for (var dt = dtStart; dt > 0.00001; dt /= dtDiv)
        {
            if (dt < 0.1) dtDiv = 3.0;
            var tt = tjd - dt;
            for (var i = 0; i < 3; i++)
            {
                dc[i] = TopocentricSunMoonCenterDistanceDeg(new JulianDay(tt), ifl, observer);
                tt += dt;
            }
            ParabolaFit.FindMaximum(dc[0], dc[1], dc[2], dt, out var dtInt, out _);
            tjd += dtInt + dt;
        }

        // Final dctr/rmoon/rsun at refined TT.
        var (dctr, rmoon, rsun, _, _) = TopocentricSunMoonGeometry(new JulianDay(tjd), ifl, observer);
        var rsplusrm = rsun + rmoon;
        var rsminusrm = rsun - rmoon;
        if (dctr > rsplusrm) return null;

        // TT → UT (2-fold ΔT iteration, swecl.c#L2214-L2215).
        var maxUt = tjd - _calendar.DeltaT(new JulianDay(tjd));
        maxUt = tjd - _calendar.DeltaT(new JulianDay(maxUt));

        // Direction check.
        if ((backward && maxUt >= startUt.Value - 0.0001)
            || (!backward && maxUt <= startUt.Value + 0.0001))
            return null;

        // Classify (swecl.c#L2224-L2229).
        EclipseTypeFlags retc;
        if (dctr < rsminusrm) retc = EclipseTypeFlags.Annular;
        else if (dctr < System.Math.Abs(rsminusrm)) retc = EclipseTypeFlags.Total;
        else retc = EclipseTypeFlags.Partial;
        var dctrMin = dctr;

        // Contacts 2 & 3 — only for total/annular (swecl.c#L2231-L2289).
        double? c2Ut = null;
        double? c3Ut = null;
        if (dctr <= System.Math.Abs(rsminusrm))
        {
            (c2Ut, c3Ut) = SolveInnerContacts(tjd, dctrMin, ifl, observer);
            // tret[2,3] are still TT — convert to UT.
            c2Ut = c2Ut!.Value - _calendar.DeltaT(new JulianDay(c2Ut.Value));
            c3Ut = c3Ut!.Value - _calendar.DeltaT(new JulianDay(c3Ut.Value));
        }

        // Contacts 1 & 4 (swecl.c#L2290-L2342).
        var (c1Tt, c4Tt) = SolveOuterContacts(tjd, dctrMin, ifl, observer);
        var c1Ut = c1Tt - _calendar.DeltaT(new JulianDay(c1Tt));
        var c4Ut = c4Tt - _calendar.DeltaT(new JulianDay(c4Tt));

        // Visibility (swecl.c#L2346-L2364).
        // We evaluate eclipse_how at each contact (max + 4 contacts), preserving
        // the attribute set at the maximum (i=0).
        SolarEclipseAttributes maxAttrs = default;
        var visibilityFlags = EclipseTypeFlags.None;

        // Iterate i = 4..0 so the maximum is the *last* attribute snapshot we
        // keep — matches the C comment "attr for i = 0 must be kept".
        ReadOnlySpan<double> contactTimes = [c4Ut, c3Ut ?? 0, c2Ut ?? 0, c1Ut, maxUt];
        ReadOnlySpan<EclipseTypeFlags> contactBits =
        [
            EclipseTypeFlags.PartialEndVisible,
            EclipseTypeFlags.TotalEndVisible,
            EclipseTypeFlags.TotalBeginVisible,
            EclipseTypeFlags.PartialBeginVisible,
            EclipseTypeFlags.MaxVisible,
        ];
        for (var i = 0; i < 5; i++)
        {
            if (contactTimes[i] == 0) continue;
            var (_, attrs) = EclipseHow(new JulianDay(contactTimes[i]), ifl, observer);
            if (attrs.SunApparentAltitudeDeg > 0)
            {
                visibilityFlags |= EclipseTypeFlags.Visible | contactBits[i];
            }
            if (i == 4) maxAttrs = attrs; // index 4 = maxUt
        }
        retc |= visibilityFlags;

        if ((retc & EclipseTypeFlags.Visible) == 0) return null;

        // Sunrise / sunset boundary handling (swecl.c#L2374-L2408).
        var (sunriseUt, sunriseFound) = TryFindSunriseSet(c1Ut - 0.001, observer, ifl, RiseTransitFlags.Rise);
        var (sunsetUt, sunsetFound) = TryFindSunriseSet(c1Ut - 0.001, observer, ifl, RiseTransitFlags.Set);

        if (sunriseFound && sunsetFound)
        {
            // Eclipse window entirely after sunset (sunset before C1) or
            // entirely before next sunrise (sunset > sunrise > C4).
            if (sunsetUt < c1Ut || (sunsetUt > sunriseUt && sunriseUt > c4Ut))
                return null;
        }

        double? finalSunriseDuringEclipse = null;
        double? finalSunsetDuringEclipse = null;
        var finalMaxUt = maxUt;
        if (sunriseFound && sunriseUt > c1Ut && sunriseUt < c4Ut)
        {
            finalSunriseDuringEclipse = sunriseUt;
            if ((retc & EclipseTypeFlags.MaxVisible) == 0)
            {
                finalMaxUt = sunriseUt;
                var (newType, attrsAtRise) = EclipseHow(new JulianDay(sunriseUt), ifl, observer);
                retc &= ~(EclipseTypeFlags.Total | EclipseTypeFlags.Annular | EclipseTypeFlags.Partial);
                retc |= newType & (EclipseTypeFlags.Total | EclipseTypeFlags.Annular | EclipseTypeFlags.Partial);
                maxAttrs = attrsAtRise;
            }
        }
        if (sunsetFound && sunsetUt > c1Ut && sunsetUt < c4Ut)
        {
            finalSunsetDuringEclipse = sunsetUt;
            if ((retc & EclipseTypeFlags.MaxVisible) == 0)
            {
                finalMaxUt = sunsetUt;
                var (newType, attrsAtSet) = EclipseHow(new JulianDay(sunsetUt), ifl, observer);
                retc &= ~(EclipseTypeFlags.Total | EclipseTypeFlags.Annular | EclipseTypeFlags.Partial);
                retc |= newType & (EclipseTypeFlags.Total | EclipseTypeFlags.Annular | EclipseTypeFlags.Partial);
                maxAttrs = attrsAtSet;
            }
        }

        // Wrapper-level central/non-central classification + dcore[0] for attr.
        var (whereType, _, whereGeom) = EclipseWhere(new JulianDay(finalMaxUt), ifl);
        retc |= whereType & EclipseTypeFlags.NonCentral;
        maxAttrs = maxAttrs with { CoreShadowDiameterKm = whereGeom.CoreShadowDiameterAtMaxKm };

        return new SolarEclipseLocalSearchReport(
            EclipseType: retc,
            MaximumTime: new JulianDay(finalMaxUt),
            PartialBeginTime: new JulianDay(c1Ut),
            TotalityBeginTime: c2Ut is null ? null : new JulianDay(c2Ut.Value),
            TotalityEndTime: c3Ut is null ? null : new JulianDay(c3Ut.Value),
            PartialEndTime: new JulianDay(c4Ut),
            SunriseDuringEclipseTime: finalSunriseDuringEclipse is null ? null : new JulianDay(finalSunriseDuringEclipse.Value),
            SunsetDuringEclipseTime: finalSunsetDuringEclipse is null ? null : new JulianDay(finalSunsetDuringEclipse.Value),
            Attributes: maxAttrs);
    }

    /// <summary>
    /// Topocentric Sun-Moon center-to-center angular distance (deg) at TT time.
    /// </summary>
    private double TopocentricSunMoonCenterDistanceDeg(JulianDay jdEt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var (dctr, _, _, _, _) = TopocentricSunMoonGeometry(jdEt, ifl, observer);
        return dctr;
    }

    /// <summary>
    /// Returns the topocentric (dctr, rmoon, rsun, dm, ds) geometry, all
    /// angles in degrees and distances in AU.
    /// </summary>
    private (double Dctr, double Rmoon, double Rsun, double Dm, double Ds)
        TopocentricSunMoonGeometry(JulianDay jdEt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        var fetchFlags = EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | ifl;
        var sun = _body.Compute(CelestialBody.Sun, jdEt, fetchFlags, observerLoc);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, fetchFlags, observerLoc);
        var ds = sun.Distance;
        var dm = moon.Distance;
        var dot = (sun.Position.X * moon.Position.X
                 + sun.Position.Y * moon.Position.Y
                 + sun.Position.Z * moon.Position.Z) / (ds * dm);
        if (dot > 1.0) dot = 1.0;
        else if (dot < -1.0) dot = -1.0;
        var dctr = System.Math.Acos(dot) * RadToDeg;
        var rmoon = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg;
        var rsun = System.Math.Asin(EclipseConstants.SunRadiusAu / ds) * RadToDeg;
        return (dctr, rmoon, rsun, dm, ds);
    }

    /// <summary>
    /// Solves contacts 2 and 3 (totality/annularity begin and end). Returned
    /// times are TT.
    /// </summary>
    private (double Contact2Tt, double Contact3Tt) SolveInnerContacts(
        double maxTt, double dctrMin, EphemerisFlags ifl, GeographicLocation observer)
    {
        // Sample edge metric at maxTt − twomin and maxTt + twomin, plus the
        // midpoint value (which is fabs(rsminusrm) − dctrMin from the caller).
        Span<double> dc = stackalloc double[3];
        for (var i = 0; i < 3; i += 2)
        {
            var t = i == 0 ? maxTt - TwoMinutesInDays : maxTt + TwoMinutesInDays;
            var (dctr, rmoon, rsun, _, _) = TopocentricSunMoonGeometry(new JulianDay(t), ifl, observer);
            // Apply the moon-radius fudge factor (swecl.c#L2244).
            rmoon *= MoonRadiusFudgeContacts;
            dc[i] = System.Math.Abs(rsun - rmoon) - dctr;
        }
        // Middle sample carries fabs(rsminusrm) − dctrMin from the wrapping
        // caller — the same expression evaluated at the maximum without the
        // fudge factor; close enough at the parabola's vertex.
        dc[1] = System.Math.Abs(rsminusrmAtMaxFromGeom(maxTt, ifl, observer)) - dctrMin;

        ParabolaFit.FindZero(dc[0], dc[1], dc[2], TwoMinutesInDays, out var dt1, out var dt2);
        var c2 = maxTt + dt1 + TwoMinutesInDays;
        var c3 = maxTt + dt2 + TwoMinutesInDays;

        // Newton-style refinement using velocity-derived shift (swecl.c#L2257-L2286).
        var dt = TenSecondsInDays;
        for (var refineStep = 0; refineStep < 2; refineStep++, dt /= 10.0)
        {
            c2 = NewtonRefineInnerContact(c2, dt, ifl, observer);
            c3 = NewtonRefineInnerContact(c3, dt, ifl, observer);
        }
        return (c2, c3);
    }

    /// <summary>Inner-contact metric at TT epoch <paramref name="tt"/>: <c>|rsun-rmoon| − dctr</c> with the moon-radius fudge factor.</summary>
    private double InnerContactMetric(double tt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var (dctr, rmoon, rsun, _, _) = TopocentricSunMoonGeometry(new JulianDay(tt), ifl, observer);
        rmoon *= MoonRadiusFudgeContacts;
        return System.Math.Abs(rsun - rmoon) - dctr;
    }

    /// <summary>One Newton-style refinement step for an inner contact (2nd/3rd).</summary>
    private double NewtonRefineInnerContact(double tt, double dt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        var fetchFlags = EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | EphemerisFlags.Speed | ifl;
        var sun = _body.Compute(CelestialBody.Sun, new JulianDay(tt), fetchFlags, observerLoc);
        var moon = _body.Compute(CelestialBody.Moon, new JulianDay(tt), fetchFlags, observerLoc);

        Span<double> dc = stackalloc double[2];
        for (var i = 0; i < 2; i++)
        {
            var sx = sun.Position.X - (i == 1 ? sun.Velocity.X * dt : 0);
            var sy = sun.Position.Y - (i == 1 ? sun.Velocity.Y * dt : 0);
            var sz = sun.Position.Z - (i == 1 ? sun.Velocity.Z * dt : 0);
            var mx = moon.Position.X - (i == 1 ? moon.Velocity.X * dt : 0);
            var my = moon.Position.Y - (i == 1 ? moon.Velocity.Y * dt : 0);
            var mz = moon.Position.Z - (i == 1 ? moon.Velocity.Z * dt : 0);
            var ds = System.Math.Sqrt(sx * sx + sy * sy + sz * sz);
            var dm = System.Math.Sqrt(mx * mx + my * my + mz * mz);
            var rmoon = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg * MoonRadiusFudgeContacts;
            var rsun = System.Math.Asin(EclipseConstants.SunRadiusAu / ds) * RadToDeg;
            var dot = (sx * mx + sy * my + sz * mz) / (ds * dm);
            if (dot > 1.0) dot = 1.0;
            else if (dot < -1.0) dot = -1.0;
            var dctr = System.Math.Acos(dot) * RadToDeg;
            dc[i] = System.Math.Abs(rsun - rmoon) - dctr;
        }
        var slope = (dc[0] - dc[1]) / dt;
        if (slope == 0) return tt;
        return tt - dc[0] / slope;
    }

    /// <summary>Helper for SolveInnerContacts middle sample.</summary>
    private double rsminusrmAtMaxFromGeom(double tt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var (_, rmoon, rsun, _, _) = TopocentricSunMoonGeometry(new JulianDay(tt), ifl, observer);
        return rsun - rmoon;
    }

    /// <summary>
    /// Solves contacts 1 and 4 (partial begin / end). Returns TT epochs.
    /// </summary>
    private (double Contact1Tt, double Contact4Tt) SolveOuterContacts(
        double maxTt, double dctrMin, EphemerisFlags ifl, GeographicLocation observer)
    {
        Span<double> dc = stackalloc double[3];
        for (var i = 0; i < 3; i += 2)
        {
            var t = i == 0 ? maxTt - TwoHoursInDays : maxTt + TwoHoursInDays;
            var (dctr, rmoon, rsun, _, _) = TopocentricSunMoonGeometry(new JulianDay(t), ifl, observer);
            dc[i] = (rsun + rmoon) - dctr;
        }
        // Middle sample evaluated at TT max.
        var (rmoonMid, rsunMid) = TopocentricRsunRmoon(new JulianDay(maxTt), ifl, observer);
        dc[1] = (rsunMid + rmoonMid) - dctrMin;

        ParabolaFit.FindZero(dc[0], dc[1], dc[2], TwoHoursInDays, out var dt1, out var dt2);
        var c1 = maxTt + dt1 + TwoHoursInDays;
        var c4 = maxTt + dt2 + TwoHoursInDays;

        var dt = TenMinutesInDays;
        for (var refineStep = 0; refineStep < 3; refineStep++, dt /= 10.0)
        {
            c1 = NewtonRefineOuterContact(c1, dt, ifl, observer);
            c4 = NewtonRefineOuterContact(c4, dt, ifl, observer);
        }
        return (c1, c4);
    }

    private (double Rmoon, double Rsun) TopocentricRsunRmoon(JulianDay jdEt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var (_, rmoon, rsun, _, _) = TopocentricSunMoonGeometry(jdEt, ifl, observer);
        return (rmoon, rsun);
    }

    private double NewtonRefineOuterContact(double tt, double dt, EphemerisFlags ifl, GeographicLocation observer)
    {
        var observerLoc = new ObserverLocation(observer.LongitudeDeg, observer.LatitudeDeg, observer.AltitudeMeters);
        var fetchFlags = EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | EphemerisFlags.Speed | ifl;
        var sun = _body.Compute(CelestialBody.Sun, new JulianDay(tt), fetchFlags, observerLoc);
        var moon = _body.Compute(CelestialBody.Moon, new JulianDay(tt), fetchFlags, observerLoc);

        Span<double> dc = stackalloc double[2];
        for (var i = 0; i < 2; i++)
        {
            var sx = sun.Position.X - (i == 1 ? sun.Velocity.X * dt : 0);
            var sy = sun.Position.Y - (i == 1 ? sun.Velocity.Y * dt : 0);
            var sz = sun.Position.Z - (i == 1 ? sun.Velocity.Z * dt : 0);
            var mx = moon.Position.X - (i == 1 ? moon.Velocity.X * dt : 0);
            var my = moon.Position.Y - (i == 1 ? moon.Velocity.Y * dt : 0);
            var mz = moon.Position.Z - (i == 1 ? moon.Velocity.Z * dt : 0);
            var ds = System.Math.Sqrt(sx * sx + sy * sy + sz * sz);
            var dm = System.Math.Sqrt(mx * mx + my * my + mz * mz);
            var rmoon = System.Math.Asin(EclipseConstants.MoonRadiusAu / dm) * RadToDeg;
            var rsun = System.Math.Asin(EclipseConstants.SunRadiusAu / ds) * RadToDeg;
            var dot = (sx * mx + sy * my + sz * mz) / (ds * dm);
            if (dot > 1.0) dot = 1.0;
            else if (dot < -1.0) dot = -1.0;
            var dctr = System.Math.Acos(dot) * RadToDeg;
            dc[i] = System.Math.Abs(rsun + rmoon) - dctr;
        }
        var slope = (dc[0] - dc[1]) / dt;
        if (slope == 0) return tt;
        return tt - dc[0] / slope;
    }

    /// <summary>
    /// Helper for the rise / set boundary check. Delegates to the
    /// rise/transit service's fast path and reports <c>(0, false)</c> for a
    /// circumpolar Sun.
    /// </summary>
    private (double JdUt, bool Found) TryFindSunriseSet(
        double startUt, GeographicLocation observer, EphemerisFlags ifl, RiseTransitFlags rise)
    {
        var res = _riseTransit.Find(
            new JulianDay(startUt),
            CelestialBody.Sun,
            EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | ifl,
            rise | RiseTransitFlags.DiscBottom,
            observer,
            atPressMbar: 0.0,
            atTempC: 0.0);
        return (res.Value.Value, !res.HasWarning);
    }
}
