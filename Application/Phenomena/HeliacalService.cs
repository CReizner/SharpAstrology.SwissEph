// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference driver: /tmp/heliacal_b_ref.c (the swe_vis_limit_mag block at
// the bottom). swehel.c#L1464-L1541.

using System;
using System.Diagnostics.CodeAnalysis;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Heliacal-phenomena service: visual limiting magnitude, topocentric
/// arcus visionis, heliacal angle, the underlying photometric phenomena
/// vector, and the heliacal-event search. Equivalent to the C library's
/// <c>swe_vis_limit_mag</c> / <c>swe_topo_arcus_visionis</c> /
/// <c>swe_heliacal_angle</c> / <c>swe_heliacal_pheno_ut</c> /
/// <c>swe_heliacal_ut</c> entry points (swehel.c#L1464 and below). Wires
/// <see cref="BodyService"/>, <see cref="HorizontalCoordsService"/> and
/// <see cref="PlanetaryPhenomenaService"/> for the various swe_calc /
/// swe_azalt / swe_pheno_ut calls inside the heliacal entry points.
/// </summary>
/// <remarks>
/// The upstream library tags its heliacal entry points as
/// <c>UNSUPPORTED_HELIACAL</c>: the photometric model has known
/// limitations and rough edges. We mirror that signal as
/// <see cref="ExperimentalAttribute"/> (diagnostic id <c>SE0001</c>) so
/// callers must opt in explicitly. Numerical results match the C
/// implementation bit-for-bit; the experimental flag refers to the
/// model's empirical underpinning, not to our port.
/// </remarks>
[Experimental("SE0001")]
public sealed partial class HeliacalService
{
    private const double RadToDeg = AstronomicalConstants.RadToDeg;

    private readonly BodyService _body;
    private readonly HorizontalCoordsService _horiz;
    private readonly PlanetaryPhenomenaService _pheno;
    private readonly RiseTransitService? _riseTransit;

    /// <summary>
    /// Constructs the heliacal service over its required collaborators.
    /// The optional <see cref="RiseTransitService"/> is needed by
    /// <see cref="ComputeHeliacalPhenomena"/> and
    /// <see cref="FindHeliacalEvent"/>; pass <see langword="null"/> only if
    /// those entry points are not used.
    /// </summary>
    public HeliacalService(
        BodyService body,
        HorizontalCoordsService horiz,
        PlanetaryPhenomenaService pheno,
        RiseTransitService? riseTransit = null)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _horiz = horiz ?? throw new ArgumentNullException(nameof(horiz));
        _pheno = pheno ?? throw new ArgumentNullException(nameof(pheno));
        _riseTransit = riseTransit;
    }

    /// <summary>
    /// Visual limiting magnitude for the given body at the given moment.
    /// Mirrors <c>swe_vis_limit_mag</c> at swehel.c#L1464.
    /// </summary>
    /// <param name="jdUt">Universal Time as Julian Day.</param>
    /// <param name="observer">Geographic observer location.</param>
    /// <param name="atmosphere">
    /// Atmospheric state. Pass <see cref="AtmosphericConditions.Auto"/> (or
    /// any record with <see cref="AtmosphericConditions.PressureMbar"/>
    /// non-positive) to let the library fill in pressure/temperature/RH from
    /// observer altitude (<c>default_heliacal_parameters</c>).
    /// </param>
    /// <param name="observerParameters">
    /// Eye / optic configuration. Pass <see cref="ObserverParameters.Auto"/>
    /// to use the standard 36-year naked-eye defaults.
    /// </param>
    /// <param name="body">The body being evaluated. The Sun is rejected.</param>
    /// <param name="helFlags">Heliacal-mode flag bitmask.</param>
    /// <param name="ephemerisFlags">
    /// Source-selection flags forwarded to <see cref="BodyService"/> /
    /// <see cref="PlanetaryPhenomenaService"/>. Defaults to Moshier so the
    /// service runs file-less.
    /// </param>
    public VisibilityLimit ComputeVisibilityLimit(
        JulianDay jdUt,
        ObserverLocation observer,
        AtmosphericConditions atmosphere,
        ObserverParameters observerParameters,
        CelestialBody body,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags = EphemerisFlags.MoshierEph)
    {
        if (body == CelestialBody.Sun)
            throw new ArgumentException("swe_vis_limit_mag is not meaningful for the Sun.", nameof(body));

        // Mirror swehel.c#L1480: default-fill atmospheric/observer values from
        // the surface-altitude estimate when the caller passes auto sentinels.
        var (atm, obs) = AtmosphericModel.DefaultParameters(atmosphere, observer.HeightMeters, observerParameters, helFlags);

        var sunRa = SkyBrightnessModel.SunRaSeasonalDeg(jdUt);

        // Object position (true alt + azimuth).
        var (altO, aziO) = ObjectAltAzi(jdUt, observer, atm, body, helFlags, ephemerisFlags);
        if (altO < 0)
        {
            return new VisibilityLimit(
                LimitingMagnitude: -100.0,
                TopocentricAltitudeDeg: 0.0,
                AzimuthDeg: 0.0,
                SunAltitudeDeg: 0.0,
                SunAzimuthDeg: 0.0,
                MoonAltitudeDeg: 0.0,
                MoonAzimuthDeg: 0.0,
                ObjectMagnitude: 0.0,
                ScotopicFlag: 0,
                IsObjectAboveHorizon: false);
        }

        double altS, aziS;
        if ((helFlags & HeliacalFlags.VisLimDark) != 0)
        {
            altS = -90.0;
            aziS = 0.0;
        }
        else
        {
            (altS, aziS) = ObjectAltAzi(jdUt, observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags);
        }

        double altM, aziM;
        if (body == CelestialBody.Moon
            || (helFlags & HeliacalFlags.VisLimDark) != 0
            || (helFlags & HeliacalFlags.VisLimNoMoon) != 0)
        {
            altM = -90.0;
            aziM = 0.0;
        }
        else
        {
            (altM, aziM) = ObjectAltAzi(jdUt, observer, atm, CelestialBody.Moon, helFlags, ephemerisFlags);
        }

        var (mag, scotopicFlag) = VisibilityLimitMath.Compute(
            obs,
            altO, aziO, altM, aziM,
            jdUt,
            altS, aziS, sunRa,
            observer.LatitudeDegrees, observer.HeightMeters,
            atm, helFlags);

        var phenoFlags = ephemerisFlags | EphemerisFlags.Topocentric | EphemerisFlags.Equatorial;
        if ((helFlags & HeliacalFlags.HighPrecision) == 0)
            phenoFlags |= EphemerisFlags.NoNutation | EphemerisFlags.TruePosition;
        var attrs = _pheno.ComputeUt(jdUt, body, phenoFlags, observer);

        return new VisibilityLimit(
            LimitingMagnitude: mag,
            TopocentricAltitudeDeg: altO,
            AzimuthDeg: aziO,
            SunAltitudeDeg: altS,
            SunAzimuthDeg: aziS,
            MoonAltitudeDeg: altM,
            MoonAzimuthDeg: aziM,
            ObjectMagnitude: attrs.ApparentMagnitude,
            ScotopicFlag: scotopicFlag,
            IsObjectAboveHorizon: true);
    }

    /// <summary>
    /// Topocentric arc visionis for a body of given magnitude given the
    /// horizontal positions of object/sun/moon. Mirrors
    /// <c>swe_topo_arcus_visionis</c> at swehel.c#L1601. Returns the bisection
    /// outcome from <c>TopoArcVisionis</c> in degrees, or
    /// <see cref="HeliacalConstants.TopoArcVisionisNoCrossingDeg"/> (=99°) when
    /// the magnitude crossing is not bracketed in [0°, 45°].
    /// </summary>
    /// <param name="jdUt">Universal Time as Julian Day.</param>
    /// <param name="observer">Geographic observer location.</param>
    /// <param name="atmosphere">Atmospheric state (auto-fills via <see cref="AtmosphericConditions.Auto"/>).</param>
    /// <param name="observerParameters">Eye / optic configuration (auto-fills via <see cref="ObserverParameters.Auto"/>).</param>
    /// <param name="objectMagnitude">Apparent magnitude of the body.</param>
    /// <param name="objectAzimuthDeg">Object azimuth, degrees (north = 0, east = 90).</param>
    /// <param name="objectAltitudeDeg">Object topocentric altitude, degrees.</param>
    /// <param name="sunAzimuthDeg">Sun azimuth, degrees.</param>
    /// <param name="moonAzimuthDeg">Moon azimuth, degrees.</param>
    /// <param name="moonAltitudeDeg">Moon topocentric altitude, degrees (pass −90 to suppress).</param>
    /// <param name="helFlags">Heliacal-mode flag bitmask.</param>
    public double ComputeTopoArcusVisionis(
        JulianDay jdUt,
        ObserverLocation observer,
        AtmosphericConditions atmosphere,
        ObserverParameters observerParameters,
        double objectMagnitude,
        double objectAzimuthDeg,
        double objectAltitudeDeg,
        double sunAzimuthDeg,
        double moonAzimuthDeg,
        double moonAltitudeDeg,
        HeliacalFlags helFlags)
    {
        // swehel.c#L1604-L1608: default-fill atmospheric/observer params,
        // then evaluate SunRA via the seasonal formula.
        var (atm, obs) = AtmosphericModel.DefaultParameters(atmosphere, observer.HeightMeters, observerParameters, helFlags);
        var sunRa = SkyBrightnessModel.SunRaSeasonalDeg(jdUt);

        return ArcusVisionisMath.TopoArcVisionis(
            objectMagnitude, obs,
            objectAltitudeDeg, objectAzimuthDeg,
            moonAltitudeDeg, moonAzimuthDeg,
            jdUt, sunAzimuthDeg, sunRa,
            observer.LatitudeDegrees, observer.HeightMeters,
            atm, helFlags);
    }

    /// <summary>
    /// Heliacal angle search: best visibility altitude, the arc visionis at
    /// that altitude, and the implied sun altitude. Mirrors
    /// <c>swe_heliacal_angle</c> at swehel.c#L1695. Throws
    /// <see cref="ArgumentOutOfRangeException"/> when the observer altitude is
    /// outside <see cref="HeliacalConstants.GeoAltitudeMinMeters"/> /
    /// <see cref="HeliacalConstants.GeoAltitudeMaxMeters"/>.
    /// </summary>
    public HeliacalAngleResult ComputeHeliacalAngle(
        JulianDay jdUt,
        ObserverLocation observer,
        AtmosphericConditions atmosphere,
        ObserverParameters observerParameters,
        double objectMagnitude,
        double objectAzimuthDeg,
        double sunAzimuthDeg,
        double moonAzimuthDeg,
        double moonAltitudeDeg,
        HeliacalFlags helFlags)
    {
        if (observer.HeightMeters < HeliacalConstants.GeoAltitudeMinMeters
            || observer.HeightMeters > HeliacalConstants.GeoAltitudeMaxMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"location for heliacal events must be between {HeliacalConstants.GeoAltitudeMinMeters:0} and {HeliacalConstants.GeoAltitudeMaxMeters:0} m above sea");
        }

        var (atm, obs) = AtmosphericModel.DefaultParameters(atmosphere, observer.HeightMeters, observerParameters, helFlags);

        var (xm, ym, sun) = ArcusVisionisMath.HeliacalAngle(
            objectMagnitude, obs,
            objectAzimuthDeg,
            moonAltitudeDeg, moonAzimuthDeg,
            jdUt, sunAzimuthDeg,
            observer.LatitudeDegrees, observer.HeightMeters,
            atm, helFlags);

        return new HeliacalAngleResult(xm, ym, sun);
    }

    /// <summary>
    /// 28-value heliacal-phenomena snapshot at <paramref name="jdUt"/>.
    /// Mirrors <c>swe_heliacal_pheno_ut</c> at swehel.c#L1862. Throws
    /// <see cref="ArgumentOutOfRangeException"/> when the observer altitude is
    /// outside <see cref="HeliacalConstants.GeoAltitudeMinMeters"/> /
    /// <see cref="HeliacalConstants.GeoAltitudeMaxMeters"/>.
    /// </summary>
    /// <param name="body">Object being evaluated. The Sun is rejected.</param>
    /// <param name="eventType">Heliacal event class — selects rise vs. set + (for type 3/4) the planets-only short-circuit.</param>
    public HeliacalPhenomena ComputeHeliacalPhenomena(
        JulianDay jdUt,
        ObserverLocation observer,
        AtmosphericConditions atmosphere,
        ObserverParameters observerParameters,
        CelestialBody body,
        HeliacalEventType eventType,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags = EphemerisFlags.MoshierEph)
    {
        if (body == CelestialBody.Sun)
            throw new ArgumentException("swe_heliacal_pheno_ut is not meaningful for the Sun.", nameof(body));
        if (observer.HeightMeters < HeliacalConstants.GeoAltitudeMinMeters
            || observer.HeightMeters > HeliacalConstants.GeoAltitudeMaxMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"location for heliacal events must be between {HeliacalConstants.GeoAltitudeMinMeters:0} and {HeliacalConstants.GeoAltitudeMaxMeters:0} m above sea");
        }
        if (_riseTransit is null)
            throw new InvalidOperationException("ComputeHeliacalPhenomena requires a RiseTransitService — pass one to the HeliacalService ctor.");

        var (atm, obs) = AtmosphericModel.DefaultParameters(atmosphere, observer.HeightMeters, observerParameters, helFlags);
        var sunRa = SkyBrightnessModel.SunRaSeasonalDeg(jdUt);

        // Object positions: topo alt+azi (Angle 0/1), geo alt (Angle 7).
        var (altO, aziO) = ObjectAltAzi(jdUt, observer, atm, body, helFlags, ephemerisFlags, topocentric: true);
        var (geoAltO, _) = ObjectAltAzi(jdUt, observer, atm, body, helFlags, ephemerisFlags, topocentric: false);
        var (altS, aziS) = ObjectAltAzi(jdUt, observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);

        var appAltO = AtmosphericModel.AppAltFromTopoAlt(
            altO, atm.TemperatureCelsius, atm.PressureMbar,
            (helFlags & HeliacalFlags.HighPrecision) != 0);

        var dazAct = aziS - aziO;
        var tavAct = altO - altS;
        var parO = geoAltO - altO;

        var phenoFlags = ephemerisFlags | EphemerisFlags.Topocentric | EphemerisFlags.Equatorial;
        if ((helFlags & HeliacalFlags.HighPrecision) == 0)
            phenoFlags |= EphemerisFlags.NoNutation | EphemerisFlags.TruePosition;
        var phenoAttrs = _pheno.ComputeUt(jdUt, body, phenoFlags, observer);
        var magnO = phenoAttrs.ApparentMagnitude;

        var arcVAct = tavAct + parO;
        var arcLAct = Math.Acos(Math.Cos(arcVAct * AstronomicalConstants.DegToRad)
                                * Math.Cos(dazAct * AstronomicalConstants.DegToRad))
                      / AstronomicalConstants.DegToRad;

        // Elongation/illumination: planets use swe_pheno_ut; "stars" (which we
        // don't yet support here) would fall through to ARCLact / 100. Mirrors
        // swehel.c#L1909-L1918.
        var elong = phenoAttrs.ElongationDeg;
        var illum = phenoAttrs.PhaseFraction * 100.0;

        var kAct = AtmosphericModel.Kt(
            altS, sunRa, observer.LatitudeDegrees, observer.HeightMeters,
            atm.TemperatureCelsius, atm.RelativeHumidityPercent, atm.MeteorologicalRangeKm,
            extType: 4);

        // Lunar Yallop helpers (only for the Moon).
        var wMoon = 0.0;
        var qYal = 0.0;
        var qCrit = 0.0;
        var lMoon = 0.0;
        if (body == CelestialBody.Moon)
        {
            wMoon = ArcusVisionisMath.WidthMoon(altO, aziO, altS, aziS, parO);
            lMoon = ArcusVisionisMath.LengthMoon(wMoon, 0.0);
            qYal = ArcusVisionisMath.QYallop(wMoon, arcVAct);
            if (qYal > 0.216) qCrit = 1;
            else if (qYal > -0.014) qCrit = 2;
            else if (qYal > -0.16) qCrit = 3;
            else if (qYal > -0.232) qCrit = 4;
            else if (qYal > -0.293) qCrit = 5;
            else qCrit = 6;
        }

        // Rise/set events for both bodies.
        var rs = (eventType == HeliacalEventType.MorningFirst || eventType == HeliacalEventType.MorningLast)
            ? RiseTransitFlags.Rise : RiseTransitFlags.Set;
        var rsmi = rs | RiseTransitFlags.DiscCenter;
        var geoLoc = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);
        var jdSearchStart = new JulianDay(jdUt.Value - 4.0 / 24.0);
        var riseSetSRes = _riseTransit.Find(jdSearchStart, CelestialBody.Sun, ephemerisFlags, rsmi, geoLoc, atm.PressureMbar, atm.TemperatureCelsius);
        var riseSetORes = _riseTransit.Find(jdSearchStart, body, ephemerisFlags, rsmi, geoLoc, atm.PressureMbar, atm.TemperatureCelsius);
        var riseSetOFound = !riseSetORes.HasWarning;
        var riseSetS = riseSetSRes.Value.Value;
        var riseSetO = riseSetORes.Value.Value;
        var lag = riseSetOFound ? (riseSetO - riseSetS) : 0.0;
        var noRiseO = !riseSetOFound;

        var tbYallop = HeliacalConstants.TjdInvalid;
        if (riseSetOFound && body == CelestialBody.Moon)
            tbYallop = (riseSetO * 4 + riseSetS * 5) / 9.0;

        // Walkthrough loop — short-circuits for planets ≥ Mars when the event
        // is "earth side of conjunction" (TypeEvent 3 / 4). Mirrors
        // swehel.c#L1960-L2042.
        double tFirstVR, tBVR, tLastVR, tVisVR, minTAV;
        var isEarthSideEvent = eventType == HeliacalEventType.EveningFirst
                               || eventType == HeliacalEventType.MorningLast;
        var planetIndex = (int)body;
        var planetIsMarsOrFurther = planetIndex >= (int)CelestialBody.Mars
                                    && planetIndex <= (int)CelestialBody.Pluto;
        if (isEarthSideEvent && planetIsMarsOrFurther)
        {
            tFirstVR = HeliacalConstants.TjdInvalid;
            tBVR = HeliacalConstants.TjdInvalid;
            tLastVR = HeliacalConstants.TjdInvalid;
            tVisVR = 0.0;
            minTAV = 0.0;
        }
        else
        {
            (tFirstVR, tBVR, tLastVR, tVisVR, minTAV) = WalkthroughVisibility(
                jdUt, body, observer, atm, obs, ephemerisFlags, helFlags,
                riseSetS, riseSetO, riseSetOFound, eventType, noRiseO, rs);
        }

        return new HeliacalPhenomena(
            ObjectTopocentricAltitudeDeg: altO,
            ObjectApparentAltitudeDeg: appAltO,
            ObjectGeocentricAltitudeDeg: geoAltO,
            ObjectAzimuthDeg: aziO,
            SunTopocentricAltitudeDeg: altS,
            SunAzimuthDeg: aziS,
            ActualArcVisionisAltitudeDeg: tavAct,
            ActualArcVisionisDeg: arcVAct,
            DeltaAzimuthDeg: dazAct,
            ArcLightDeg: arcLAct,
            ExtinctionCoefficient: kAct,
            MinimumArcVisionisDeg: minTAV,
            FirstVisibilityJdUt: tFirstVR,
            BestVisibilityJdUt: tBVR,
            LastVisibilityJdUt: tLastVR,
            YallopBestJdUt: tbYallop,
            MoonCrescentWidthDeg: wMoon,
            MoonYallopQ: qYal,
            MoonYallopCriterion: qCrit,
            ParallaxObjectDeg: parO,
            ObjectMagnitude: magnO,
            ObjectRiseSetJdUt: riseSetO,
            SunRiseSetJdUt: riseSetS,
            LagDays: lag,
            VisibilityWindowDays: tVisVR,
            MoonCrescentLengthDeg: lMoon,
            ElongationDeg: elong,
            IlluminatedFractionPercent: illum);
    }

    /// <summary>
    /// Walkthrough loop at swehel.c#L1968-L2042 — local minimum of the
    /// arc-visionis difference, plus the rise/set crossings of the
    /// instantaneous DeltaAlt vs. the time-varying minimum AV (MinTAVact).
    /// Returns the five JD-or-zero outputs used by darr[12..14, 24] and the
    /// MinTAV value used by darr[11].
    /// </summary>
    private (double TFirstVR, double TBVR, double TLastVR, double TVisVR, double MinTAV) WalkthroughVisibility(
        JulianDay jdUt,
        CelestialBody body,
        ObserverLocation observer,
        AtmosphericConditions atm,
        ObserverParameters obs,
        EphemerisFlags ephemerisFlags,
        HeliacalFlags helFlags,
        double riseSetS,
        double riseSetO,
        bool riseSetOFound,
        HeliacalEventType eventType,
        bool noRiseO,
        RiseTransitFlags rs)
    {
        // -1 minute → walking backwards from sunrise (RS=Rise, morning events).
        // +1 minute → walking forwards  from sunset  (RS=Set,  evening events).
        var timeStep = -HeliacalConstants.TimeStepDefaultMinutes / 24.0 / 60.0;
        if ((rs & RiseTransitFlags.Set) != 0) timeStep = -timeStep;

        var minTAVact = 199.0;
        var minTAVoud = 0.0;
        var oldestMinTAV = 0.0;
        var deltaAltOud = 0.0;
        var deltaAlt = 0.0;
        var ta = 0.0;
        var tc = 0.0;
        var tBVR = 0.0;
        var minTAV = 0.0;
        var timePointer = riseSetS - timeStep;

        var maxTrySpan = HeliacalConstants.MaxTryHours / 24.0;
        var localStepSec = HeliacalConstants.LocalMinStepMinutes / 24.0 / 60.0;
        var sgnStep = timeStep < 0 ? -1.0 : 1.0;

        do
        {
            timePointer += timeStep;
            oldestMinTAV = minTAVoud;
            minTAVoud = minTAVact;
            deltaAltOud = deltaAlt;

            var (sunAltAt, _) = ObjectAltAzi(new JulianDay(timePointer), observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);
            var (objAltAt, _) = ObjectAltAzi(new JulianDay(timePointer), observer, atm, body, helFlags, ephemerisFlags, topocentric: true);
            deltaAlt = objAltAt - sunAltAt;

            minTAVact = DeterTAV(new JulianDay(timePointer), body, observer, atm, obs, ephemerisFlags, helFlags);

            if (minTAVoud < minTAVact && tBVR == 0)
            {
                var timeCheck = timePointer + sgnStep * localStepSec;
                if (riseSetOFound && riseSetO != 0)
                {
                    if (timeStep > 0) timeCheck = Math.Min(timeCheck, riseSetO);
                    else timeCheck = Math.Max(timeCheck, riseSetO);
                }
                var localminCheck = DeterTAV(new JulianDay(timeCheck), body, observer, atm, obs, ephemerisFlags, helFlags);
                if (localminCheck > minTAVact)
                {
                    var extrax = ArcusVisionisMath.X2Min(minTAVact, minTAVoud, oldestMinTAV);
                    tBVR = timePointer - (1 - extrax) * timeStep;
                    minTAV = ArcusVisionisMath.Funct2(minTAVact, minTAVoud, oldestMinTAV, extrax);
                }
            }

            if (deltaAlt > minTAVact && tc == 0 && tBVR == 0)
            {
                var crossPoint = ArcusVisionisMath.Crossing(deltaAltOud, deltaAlt, minTAVoud, minTAVact);
                tc = timePointer - timeStep * (1 - crossPoint);
            }

            if (deltaAlt < minTAVact && ta == 0 && tc != 0)
            {
                var crossPoint = ArcusVisionisMath.Crossing(deltaAltOud, deltaAlt, minTAVoud, minTAVact);
                ta = timePointer - timeStep * (1 - crossPoint);
            }

            // Termination:
            // - hit MaxTryHours from RiseSetS
            // - or Ta is set (full bracket found)
            // - or TbVR set + earth-side event + planet not Moon/Venus/Mercury (= naked-eye low planets)
            //   This last branch lets Mars/Jupiter/Saturn quit early once the
            //   visibility minimum is found; lunar/Venus/Mercury keep going so
            //   the rising/setting cross detection has a chance to complete.
            var span = Math.Abs(timePointer - riseSetS);
            if (span > maxTrySpan) break;
            if (ta != 0) break;
            var canEarlyExit = tBVR != 0
                               && (eventType == HeliacalEventType.EveningFirst || eventType == HeliacalEventType.MorningLast)
                               && body != CelestialBody.Moon
                               && body != CelestialBody.Venus
                               && body != CelestialBody.Mercury;
            if (canEarlyExit) break;
        } while (true);

        double tFirstVR, tLastVR;
        if ((rs & RiseTransitFlags.Set) != 0)
        {
            // RS = 2 (evening): TfirstVR = Tc, TlastVR = Ta
            tFirstVR = tc;
            tLastVR = ta;
        }
        else
        {
            // RS = 1 (morning): TfirstVR = Ta, TlastVR = Tc
            tFirstVR = ta;
            tLastVR = tc;
        }

        if (tFirstVR == 0 && tLastVR == 0)
        {
            if ((rs & RiseTransitFlags.Set) != 0)
                tLastVR = tBVR + 0.000001;
            else
                tFirstVR = tBVR - 0.000001;
        }

        if (!noRiseO)
        {
            if ((rs & RiseTransitFlags.Set) != 0)
                tLastVR = Math.Min(tLastVR, riseSetO);
            else
                tFirstVR = Math.Max(tFirstVR, riseSetO);
        }

        var tVisVR = HeliacalConstants.TjdInvalid;
        if (tLastVR != 0 && tFirstVR != 0)
            tVisVR = tLastVR - tFirstVR;
        if (tLastVR == 0) tLastVR = HeliacalConstants.TjdInvalid;
        if (tBVR == 0) tBVR = HeliacalConstants.TjdInvalid;
        if (tFirstVR == 0) tFirstVR = HeliacalConstants.TjdInvalid;

        return (tFirstVR, tBVR, tLastVR, tVisVR, minTAV);
    }

    /// <summary>
    /// Mirrors <c>DeterTAV</c> at swehel.c#L1759 — at <paramref name="jdUt"/>,
    /// fetch object/sun/moon positions and the apparent magnitude, then call
    /// <see cref="ArcusVisionisMath.TopoArcVisionis"/>.
    /// </summary>
    private double DeterTAV(
        JulianDay jdUt,
        CelestialBody body,
        ObserverLocation observer,
        AtmosphericConditions atm,
        ObserverParameters obs,
        EphemerisFlags ephemerisFlags,
        HeliacalFlags helFlags)
    {
        var sunRa = SkyBrightnessModel.SunRaSeasonalDeg(jdUt);
        var phenoFlags = ephemerisFlags | EphemerisFlags.Topocentric | EphemerisFlags.Equatorial;
        if ((helFlags & HeliacalFlags.HighPrecision) == 0)
            phenoFlags |= EphemerisFlags.NoNutation | EphemerisFlags.TruePosition;
        var attrs = _pheno.ComputeUt(jdUt, body, phenoFlags, observer);
        var magn = attrs.ApparentMagnitude;

        var (altO, aziO) = ObjectAltAzi(jdUt, observer, atm, body, helFlags, ephemerisFlags, topocentric: true);
        double altM, aziM;
        if (body == CelestialBody.Moon)
        {
            altM = -90.0;
            aziM = 0.0;
        }
        else
        {
            (altM, aziM) = ObjectAltAzi(jdUt, observer, atm, CelestialBody.Moon, helFlags, ephemerisFlags, topocentric: true);
        }
        var (_, aziS) = ObjectAltAzi(jdUt, observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);

        return ArcusVisionisMath.TopoArcVisionis(
            magn, obs,
            altO, aziO, altM, aziM,
            jdUt, aziS, sunRa,
            observer.LatitudeDegrees, observer.HeightMeters,
            atm, helFlags);
    }

    /// <summary>
    /// Mirrors <c>ObjectLoc</c> at swehel.c#L683 for <c>Angle = 0</c> (true
    /// topocentric altitude) and <c>Angle = 1</c> (azimuth from north). Pass
    /// <paramref name="topocentric"/> = false to mirror Angle 7 (geocentric
    /// equatorial → horizontal alt; ignores parallax).
    /// </summary>
    private (double AltDeg, double AziDeg) ObjectAltAzi(
        JulianDay jdUt,
        ObserverLocation observer,
        AtmosphericConditions atm,
        CelestialBody body,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags,
        bool topocentric = true)
    {
        var (raDeg, decDeg) = ComputeEquatorial(jdUt, observer, body, helFlags, ephemerisFlags, topocentric);

        var geo = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);
        var hor = _horiz.ToHorizontal(jdUt,
            HorizontalConversionInput.FromEquatorial,
            geo,
            atm.PressureMbar, atm.TemperatureCelsius,
            raDeg, decDeg);

        // swehel.c#L717-L721: Angle == 1 returns swe_azalt's azimuth (south = 0)
        // shifted by +180 to north = 0.
        var azNorth = hor.AzimuthDeg + 180.0;
        if (azNorth >= 360.0) azNorth -= 360.0;
        return (hor.TrueAltitudeDeg, azNorth);
    }

    /// <summary>
    /// Equatorial spherical coordinates of <paramref name="body"/> in degrees.
    /// Mirrors the <c>swe_calc</c> branch of <c>ObjectLoc</c>; pass
    /// <paramref name="topocentric"/> = false for Angle 7 (geocentric
    /// equator-of-date).
    /// </summary>
    private (double RaDeg, double DecDeg) ComputeEquatorial(
        JulianDay jdUt,
        ObserverLocation observer,
        CelestialBody body,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags,
        bool topocentric = true)
    {
        var flags = ephemerisFlags | EphemerisFlags.Equatorial;
        if (topocentric) flags |= EphemerisFlags.Topocentric;
        if ((helFlags & HeliacalFlags.HighPrecision) == 0)
            flags |= EphemerisFlags.NoNutation | EphemerisFlags.TruePosition;

        var state = _body.ComputeUt(body, jdUt, flags, observer);
        var ra = Math.Atan2(state.Position.Y, state.Position.X) * RadToDeg;
        if (ra < 0) ra += 360.0;
        var r = Math.Sqrt(state.Position.X * state.Position.X
                          + state.Position.Y * state.Position.Y
                          + state.Position.Z * state.Position.Z);
        var dec = Math.Asin(state.Position.Z / r) * RadToDeg;
        return (ra, dec);
    }
}
