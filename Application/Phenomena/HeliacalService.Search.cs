// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Phase 11e: outer-loop heliacal-event search → swe_heliacal_ut at swehel.c#L3385.
// Reference driver: /tmp/heliacal_e_ref.c (uses /tmp/swehel_exposed.c).
//
// The 11e port covers the visibility-limit method (helflag.AvKindMask = 0).
// M-26 added the four arc-of-vision search variants (AvKind_VR, _Pto, _Min7,
// _Min9) — see HeliacalService.AvKind.cs and reference driver
// /tmp/heliacal_avkind_ref.c.
//
// M-25 added the outer-planet acronychal branch (Mars / Jupiter / Saturn
// EveningFirst & MorningLast). The C library's swe_heliacal_ut front gate
// rejects these inputs unless an AvKind flag is set; the branch *inside*
// heliacal_ut_vis_lim that handles them was dead code under SE2. To match
// it we drive heliacal_ut_vis_lim directly, mapping our public
// EveningFirst → SE_ACRONYCHAL_RISING (5) and MorningLast → SE_ACRONYCHAL_SETTING
// (6) so get_asc_obl_with_sun and get_acronychal_day take the is_acronychal
// branch. Reference driver: /tmp/heliacal_outer_ref.c.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

public sealed partial class HeliacalService
{
    // MAX_COUNT_SYNPER / _MAX at swehel.c#L82-L83.
    private const int MaxCountSynper = 5;
    private const int MaxCountSynperMax = 200;

    // tcon[] at swehel.c#L2566-L2577 — anchor JDs for each planet's
    // (superior, inferior) conjunction or (conjunction, opposition).
    private static readonly double[] TConSup = {
        0.0,        // sun (unused)
        2451550.0,  // moon
        2451604.0,  // mercury (superior)
        2451980.0,  // venus    (superior)
        2451727.0,  // mars     (conjunction)
        2451673.0,  // jupiter  (conjunction)
        2451675.0,  // saturn   (conjunction)
        2451581.0,  // uranus   (conjunction)
        2451568.0,  // neptune  (conjunction)
    };
    private static readonly double[] TConInf = {
        0.0,
        2451550.0,
        2451670.0,  // mercury (inferior)
        2452280.0,  // venus    (inferior)
        2452074.0,  // mars     (opposition)
        2451877.0,  // jupiter  (opposition)
        2451868.0,  // saturn   (opposition)
        2451768.0,  // uranus   (opposition)
        2451753.0,  // neptune  (opposition)
    };

    /// <summary>
    /// Search the next heliacal event (rising / setting / first / last) of
    /// <paramref name="body"/> after <paramref name="jdUtStart"/>. Mirrors
    /// <c>swe_heliacal_ut</c> at swehel.c#L3385. Throws
    /// <see cref="ArgumentException"/> for the Sun (no heliacal phenomena) and
    /// <see cref="ArgumentOutOfRangeException"/> when the observer altitude
    /// is outside <see cref="HeliacalConstants.GeoAltitudeMinMeters"/> /
    /// <see cref="HeliacalConstants.GeoAltitudeMaxMeters"/>. When no event
    /// is found inside the configured synodic-period budget
    /// (<c>MAX_COUNT_SYNPER</c>), the returned
    /// <see cref="EphemerisResult{T}"/> carries <c>default(HeliacalEvent)</c>
    /// together with a non-null <see cref="EphemerisResult{T}.Warning"/>.
    /// </summary>
    /// <remarks>
    /// Two search families are supported:
    /// <list type="bullet">
    ///   <item><description>The default "visibility-limit method" (no <c>AvKind*</c> bit
    ///   in <paramref name="helFlags"/>). Mirrors <c>heliacal_ut_vis_lim</c>.</description></item>
    ///   <item><description>The four arc-of-vision variants — <see cref="HeliacalFlags.AvKindVR"/>,
    ///   <see cref="HeliacalFlags.AvKindPto"/>, <see cref="HeliacalFlags.AvKindMin7"/>,
    ///   <see cref="HeliacalFlags.AvKindMin9"/>. Exactly one bit may be set;
    ///   combining two bits throws <see cref="ArgumentException"/>.</description></item>
    /// </list>
    /// The Moon path supports the visibility-limit method and, of the
    /// arc-of-vision variants, only <see cref="HeliacalFlags.AvKindVR"/>.
    /// </remarks>
    /// <param name="jdUtStart">Julian Day (UT) at which to start the search.</param>
    /// <param name="body">Body whose heliacal event is sought. The Sun is rejected.</param>
    /// <param name="eventType">
    /// Selector for which event class to find — see
    /// <see cref="HeliacalEventType"/>. Type 1/2 are valid for any body
    /// except the Moon; type 3/4 are only valid for Mercury, Venus and the
    /// Moon (other planets reject 3/4 with <see cref="ArgumentException"/>).
    /// </param>
    /// <param name="helFlags">
    /// Search-method and search-budget bits — see <see cref="HeliacalFlags"/>.
    /// At most one <c>AvKind*</c> bit may be set; the Moon additionally
    /// rejects every <c>AvKind*</c> bit except <see cref="HeliacalFlags.AvKindVR"/>.
    /// </param>
    public EphemerisResult<HeliacalEvent> FindHeliacalEvent(
        JulianDay jdUtStart,
        ObserverLocation observer,
        AtmosphericConditions atmosphere,
        ObserverParameters observerParameters,
        CelestialBody body,
        HeliacalEventType eventType,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags = EphemerisFlags.MoshierEph)
    {
        if (body == CelestialBody.Sun)
            throw new ArgumentException("the sun has no heliacal rising or setting", nameof(body));
        if (observer.HeightMeters < HeliacalConstants.GeoAltitudeMinMeters
            || observer.HeightMeters > HeliacalConstants.GeoAltitudeMaxMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"location for heliacal events must be between {HeliacalConstants.GeoAltitudeMinMeters:0} and {HeliacalConstants.GeoAltitudeMaxMeters:0} m above sea");
        }
        if (_riseTransit is null)
            throw new InvalidOperationException("FindHeliacalEvent requires a RiseTransitService — pass one to the HeliacalService ctor.");

        // M-26: validate AvKind selector. The four bits are mutually exclusive
        // and the moon supports only AvKind_VR (swehel.c#L2128-L2132).
        var avKind = helFlags & HeliacalFlags.AvKindMask;
        if (avKind != 0 && (avKind & (avKind - 1)) != 0)
            throw new ArgumentException("Only one HeliacalFlags.AvKind* bit may be set at a time.", nameof(helFlags));

        var (atm, obs) = AtmosphericModel.DefaultParameters(atmosphere, observer.HeightMeters, observerParameters, helFlags);
        var maxCountSynper = (helFlags & HeliacalFlags.LongSearch) != 0 ? MaxCountSynperMax : MaxCountSynper;

        // Moon path — MoonEventJDut at swehel.c#L3327.
        if (body == CelestialBody.Moon)
        {
            if (eventType == HeliacalEventType.MorningFirst || eventType == HeliacalEventType.EveningLast)
                throw new ArgumentException("the moon has no morning first / evening last event", nameof(eventType));
            if (avKind != 0 && avKind != HeliacalFlags.AvKindVR)
                throw new ArgumentException("the moon supports only HeliacalFlags.AvKindVR (swehel.c#L2128).", nameof(helFlags));

            var tjd = jdUtStart.Value;
            var ev = avKind == 0
                ? MoonEventVisLim(tjd, observer, atm, obs, eventType, helFlags, ephemerisFlags)
                : MoonEventArcVis(tjd, observer, atm, obs, eventType, helFlags, ephemerisFlags);
            // swehel.c#L3429-L3433: keep advancing 15 d while *dret < tjd0.
            while (ev.HasValue && ev.Value.VisibilityStartJdUt < jdUtStart.Value)
            {
                tjd += 15.0;
                ev = avKind == 0
                    ? MoonEventVisLim(tjd, observer, atm, obs, eventType, helFlags, ephemerisFlags)
                    : MoonEventArcVis(tjd, observer, atm, obs, eventType, helFlags, ephemerisFlags);
            }
            if (ev is null)
                return EphemerisResult<HeliacalEvent>.WithWarning(default, "no heliacal date found for moon");
            return EphemerisResult<HeliacalEvent>.Ok(ev.Value);
        }

        // Planets / fixed stars — swe_heliacal_ut outer loop, swehel.c#L3438-L3506.

        var dsynperiod = GetSynodicPeriod(body);
        var tjd0 = jdUtStart.Value;
        var tjdmax = tjd0 + dsynperiod * maxCountSynper;
        var tadd = body == CelestialBody.Mercury ? 30.0 : dsynperiod * 0.6;

        HeliacalEvent? result = null;
        for (var t = tjd0; t < tjdmax; t += tadd)
        {
            result = avKind == 0
                ? HeliacalUtVisLim(t, observer, atm, obs, body, eventType, helFlags, ephemerisFlags)
                : HeliacalUtArcVis(t, observer, atm, obs, body, eventType, helFlags, ephemerisFlags);
            // swehel.c#L3492-L3496: if the event is before the search start,
            // step forward by another half-period and retry.
            while (result.HasValue && result.Value.VisibilityStartJdUt < tjd0)
            {
                t += tadd;
                if (t >= tjdmax)
                {
                    result = null;
                    break;
                }
                result = avKind == 0
                    ? HeliacalUtVisLim(t, observer, atm, obs, body, eventType, helFlags, ephemerisFlags)
                    : HeliacalUtArcVis(t, observer, atm, obs, body, eventType, helFlags, ephemerisFlags);
            }
            if (result.HasValue) break;
        }

        if ((helFlags & HeliacalFlags.SearchOnePeriod) != 0
            && (result is null || result.Value.VisibilityStartJdUt > tjd0 + dsynperiod * 1.5))
        {
            return EphemerisResult<HeliacalEvent>.WithWarning(default, "no heliacal date found within this synodic period");
        }
        if (result is null)
            return EphemerisResult<HeliacalEvent>.WithWarning(default, $"no heliacal date found within {maxCountSynper} synodic periods");
        return EphemerisResult<HeliacalEvent>.Ok(result.Value);
    }

    /// <summary>
    /// Mirrors <c>get_synodic_period</c> at swehel.c#L2095. Default 366 days
    /// for fixed stars / unknown bodies (called via <see cref="CelestialBody"/>
    /// values outside the Sun..Pluto range).
    /// </summary>
    internal static double GetSynodicPeriod(CelestialBody body) => body switch
    {
        CelestialBody.Moon => 29.530588853,
        CelestialBody.Mercury => 115.8775,
        CelestialBody.Venus => 583.9214,
        CelestialBody.Mars => 779.9361,
        CelestialBody.Jupiter => 398.8840,
        CelestialBody.Saturn => 378.0919,
        CelestialBody.Uranus => 369.6560,
        CelestialBody.Neptune => 367.4867,
        CelestialBody.Pluto => 366.7207,
        _ => 366.0,
    };

    /// <summary>
    /// Mirrors <c>find_conjunct_sun</c> at swehel.c#L2579. Locates the JD at
    /// which <paramref name="body"/>'s ecliptic longitude differs from the
    /// Sun's by <c>daspect</c> (0° for inferior / Mercury+Venus, 180° for
    /// outer planets at TypeEvent ≥ 3). Anchors are the <c>tcon[]</c>
    /// constants; the iteration is Newton on <c>(λ_body − λ_sun)</c>.
    /// </summary>
    internal double FindConjunctSun(double tjdStart, CelestialBody body, HeliacalFlags helFlags, HeliacalEventType eventType, EphemerisFlags ephemerisFlags)
    {
        var ipl = (int)body;
        var daspect = 0.0;
        if (ipl >= (int)CelestialBody.Mars
            && ((int)eventType >= 3))
            daspect = 180.0;
        // i = (TypeEvent - 1) / 2 → 0 = (1,2)=superior/conjunction, 1 = (3,4)=inferior/opposition.
        var i = ((int)eventType - 1) / 2;
        var tjd0 = i == 0 ? TConSup[ipl] : TConInf[ipl];
        var dsynperiod = GetSynodicPeriod(body);
        var tjdcon = tjd0 + (Math.Floor((tjdStart - tjd0) / dsynperiod) + 1) * dsynperiod;

        var ds = 100.0;
        while (ds > 0.5)
        {
            var (lon, lonRate) = EclipticLongitudeAndRate(new JulianDay(tjdcon), body, ephemerisFlags);
            var (lonSun, lonSunRate) = EclipticLongitudeAndRate(new JulianDay(tjdcon), CelestialBody.Sun, ephemerisFlags);
            ds = AngleMath.NormalizeDegrees(lon - lonSun - daspect);
            if (ds > 180.0) ds -= 360.0;
            tjdcon -= ds / (lonRate - lonSunRate);
            ds = Math.Abs(ds);
        }
        return tjdcon;
    }

    /// <summary>
    /// Heliacal-day search for the visibility-limit method. Mirrors
    /// <c>get_heliacal_day</c> at swehel.c#L2762. Returns the JD (UT) at
    /// which the object becomes visible (Vmag − ObjectMag &gt; 0), or
    /// <c>null</c> when no day inside the per-body window matches.
    /// </summary>
    internal double? GetHeliacalDay(double tjd, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, CelestialBody body, HeliacalFlags helFlags, HeliacalEventType eventType, EphemerisFlags ephemerisFlags)
    {
        var (isRiseOrSet, directDay, directTime) = eventType switch
        {
            HeliacalEventType.MorningFirst => (RiseTransitFlags.Rise, 1.0, -1.0),
            HeliacalEventType.EveningLast => (RiseTransitFlags.Set, -1.0, 1.0),
            HeliacalEventType.EveningFirst => (RiseTransitFlags.Set, 1.0, 1.0),
            HeliacalEventType.MorningLast => (RiseTransitFlags.Rise, -1.0, -1.0),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };

        // Per-body window + step (swehel.c#L2789-L2837).
        double tfac = 1.0;
        int ndays;
        double daystep;
        switch (body)
        {
            case CelestialBody.Moon: ndays = 16; daystep = 1; break;
            case CelestialBody.Mercury: ndays = 60; daystep = 5; tfac = 5; break;
            case CelestialBody.Venus:
                ndays = 300;
                tjd -= 30 * directDay;
                daystep = 5;
                if ((int)eventType >= 3) { daystep = 15; tfac = 3; }
                break;
            case CelestialBody.Mars: ndays = 400; daystep = 15; tfac = 5; break;
            case CelestialBody.Saturn: ndays = 300; daystep = 20; tfac = 5; break;
            default: ndays = 300; daystep = 15; tfac = 3; break;
        }

        var tend = tjd + ndays * directDay;
        var retvalOldNoSun = true; // C uses -2 sentinel; track booleans instead.
        var geo = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);

        for (var tday = tjd; (directDay > 0 && tday < tend) || (directDay < 0 && tday > tend); tday += daystep * directDay)
        {
            // Slight pull-back for revisits beyond the first iteration —
            // swehel.c#L2844-L2845.
            if (tday != tjd) tday -= 0.3 * directDay;

            var sunRiseSetRes = _riseTransit!.Find(
                new JulianDay(tday), CelestialBody.Sun, ephemerisFlags,
                isRiseOrSet, geo, atm.PressureMbar, atm.TemperatureCelsius);
            if (sunRiseSetRes.HasWarning)
            {
                retvalOldNoSun = true;
                continue;
            }

            var tret = sunRiseSetRes.Value.Value;
            var vis = ComputeVisibilityLimit(new JulianDay(tret), observer, atm, _obsForVisLim(obs), body, helFlags, ephemerisFlags);

            // "Object has appeared above horizon: reduce daystep" —
            // swehel.c#L2858-L2871.
            if (retvalOldNoSun && vis.IsObjectAboveHorizon && daystep > 1)
            {
                retvalOldNoSun = false;
                tday -= daystep * directDay;
                daystep = 1;
                if ((int)body >= (int)CelestialBody.Mars && (int)body <= (int)CelestialBody.Pluto)
                    daystep = 5;
                continue;
            }
            retvalOldNoSun = !vis.IsObjectAboveHorizon;
            if (!vis.IsObjectAboveHorizon) continue;

            // Refine the minute of visibility — swehel.c#L2877-L2895.
            const double div = 1440.0;
            var visibleAtSunsetRise = true;
            var vd = vis.LimitingMagnitude - vis.ObjectMagnitude;
            var visCur = vis;
            while (visCur.IsObjectAboveHorizon && (vd = visCur.LimitingMagnitude - visCur.ObjectMagnitude) < 0)
            {
                visibleAtSunsetRise = false;
                if (vd < -1.0) tret += 5.0 / div * directTime * tfac;
                else if (vd < -0.5) tret += 2.0 / div * directTime * tfac;
                else if (vd < -0.1) tret += 1.0 / div * directTime * tfac;
                else tret += 1.0 / div * directTime;
                visCur = ComputeVisibilityLimit(new JulianDay(tret), observer, atm, _obsForVisLim(obs), body, helFlags, ephemerisFlags);
            }

            // If visible from the sunrise/sunset moment, push a bit away
            // because vis_limit_mag has erratic behaviour right at the
            // horizon (swehel.c#L2897-L2905).
            if (visibleAtSunsetRise)
            {
                for (var k = 0; k < 10; k++)
                {
                    var probe = ComputeVisibilityLimit(new JulianDay(tret + 1.0 / div * directTime), observer, atm, _obsForVisLim(obs), body, helFlags, ephemerisFlags);
                    var probeVd = probe.LimitingMagnitude - probe.ObjectMagnitude;
                    if (probe.IsObjectAboveHorizon && probeVd > vd)
                    {
                        vd = probeVd;
                        tret += 1.0 / div * directTime;
                        visCur = probe;
                    }
                }
            }

            var vdelta = visCur.LimitingMagnitude - visCur.ObjectMagnitude;
            if (vdelta > 0)
            {
                if (((int)body >= (int)CelestialBody.Mars && (int)body <= (int)CelestialBody.Pluto) && daystep > 1)
                {
                    tday -= daystep * directDay;
                    daystep = 1;
                    continue;
                }
                return tret;
            }
        }
        return null;
    }

    /// <summary>
    /// Mirrors <c>time_optimum_visibility</c> at swehel.c#L2923. Bisection-
    /// refined search for the maximum of <c>Vmag − ObjectMag</c> in a
    /// ±100/86400 d window centred on <paramref name="tjd"/>.
    /// </summary>
    internal (double T, bool Uncertain) TimeOptimumVisibility(double tjd, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, CelestialBody body, HeliacalFlags helFlags, EphemerisFlags ephemerisFlags)
    {
        var visStart = ComputeVisibilityLimit(new JulianDay(tjd), observer, atm, obs, body, helFlags, ephemerisFlags);
        var phot0 = (visStart.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
        var phot0Sv = phot0;
        var visSv = visStart;

        double t1 = tjd, t2 = tjd;
        double vl1 = -1, vl2 = -1;

        // Forward refine.
        var d = 100.0 / 86400.0;
        for (var i = 0; i < 3; i++, d /= 10.0)
        {
            t1 += d;
            var changed = false;
            while (true)
            {
                var probe = ComputeVisibilityLimit(new JulianDay(t1 - d), observer, atm, obs, body, helFlags, ephemerisFlags);
                var diff = probe.LimitingMagnitude - probe.ObjectMagnitude;
                if (!probe.IsObjectAboveHorizon) break;
                if (probe.LimitingMagnitude <= probe.ObjectMagnitude) break;
                if (diff <= vl1) break;
                t1 -= d;
                vl1 = diff;
                changed = true;
                visSv = probe;
                phot0Sv = (probe.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
            }
            if (!changed) t1 -= d;
        }

        // Backward refine.
        d = 100.0 / 86400.0;
        for (var i = 0; i < 3; i++, d /= 10.0)
        {
            t2 -= d;
            var changed = false;
            while (true)
            {
                var probe = ComputeVisibilityLimit(new JulianDay(t2 + d), observer, atm, obs, body, helFlags, ephemerisFlags);
                var diff = probe.LimitingMagnitude - probe.ObjectMagnitude;
                if (!probe.IsObjectAboveHorizon) break;
                if (probe.LimitingMagnitude <= probe.ObjectMagnitude) break;
                if (diff <= vl2) break;
                t2 += d;
                vl2 = diff;
                changed = true;
                visSv = probe;
                phot0Sv = (probe.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
            }
            if (!changed) t2 += d;
        }

        var t = vl2 > vl1 ? t2 : t1;
        var visEnd = ComputeVisibilityLimit(new JulianDay(t), observer, atm, obs, body, helFlags, ephemerisFlags);
        var phot1 = (visEnd.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
        var uncertain = (phot1 != phot0Sv) || ((visSv.ScotopicFlag & VisibilityLimitMath.FlagBoundary) != 0);
        return (t, uncertain);
    }

    /// <summary>
    /// Mirrors <c>time_limit_invisible</c> at swehel.c#L3000. Walks
    /// <paramref name="direct"/>·d minutes until <c>Vmag &lt; ObjectMag</c>
    /// or the body drops below the horizon. <paramref name="direct"/> = +1
    /// drives forward in time (looking for sunset side); −1 drives back.
    /// </summary>
    internal (double T, bool Uncertain) TimeLimitInvisible(double tjd, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, CelestialBody body, HeliacalFlags helFlags, int direct, EphemerisFlags ephemerisFlags)
    {
        var ncnt = 3;
        var d0 = 100.0 / 86400.0;
        if (body == CelestialBody.Moon) { d0 *= 10; ncnt = 4; }

        var visStart = ComputeVisibilityLimit(new JulianDay(tjd), observer, atm, obs, body, helFlags, ephemerisFlags);
        var phot0Sv = (visStart.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
        var visSv = visStart;

        var d = d0;
        for (var i = 0; i < ncnt; i++, d /= 10.0)
        {
            while (true)
            {
                var probe = ComputeVisibilityLimit(new JulianDay(tjd + d * direct), observer, atm, obs, body, helFlags, ephemerisFlags);
                if (!probe.IsObjectAboveHorizon) break;
                if (probe.LimitingMagnitude <= probe.ObjectMagnitude) break;
                tjd += d * direct;
                visSv = probe;
                phot0Sv = (probe.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
            }
        }

        var visEnd = ComputeVisibilityLimit(new JulianDay(tjd), observer, atm, obs, body, helFlags, ephemerisFlags);
        var phot1 = (visEnd.ScotopicFlag & VisibilityLimitMath.FlagScotopic) != 0;
        var uncertain = false;
        if (visEnd.IsObjectAboveHorizon)
            uncertain = (phot1 != phot0Sv) || ((visSv.ScotopicFlag & VisibilityLimitMath.FlagBoundary) != 0);
        return (tjd, uncertain);
    }

    /// <summary>
    /// Mirrors <c>get_heliacal_details</c> at swehel.c#L3107. Refines
    /// <paramref name="tday"/> into the (start, optimum, end) triple.
    /// </summary>
    internal HeliacalEvent GetHeliacalDetails(double tday, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, CelestialBody body, HeliacalEventType eventType, HeliacalFlags helFlags, EphemerisFlags ephemerisFlags)
    {
        var (tOpt, _) = TimeOptimumVisibility(tday, observer, atm, obs, body, helFlags, ephemerisFlags);
        var direct = (eventType == HeliacalEventType.MorningFirst || eventType == HeliacalEventType.MorningLast) ? -1 : 1;
        var (tStart, _) = TimeLimitInvisible(tday, observer, atm, obs, body, helFlags, direct, ephemerisFlags);
        var (tEnd, _) = TimeLimitInvisible(tOpt, observer, atm, obs, body, helFlags, -direct, ephemerisFlags);
        // Swap [0]/[2] for evening-last (2) and evening-first (3) — swehel.c#L3141-L3148.
        if (eventType == HeliacalEventType.EveningLast || eventType == HeliacalEventType.EveningFirst)
        {
            var tmp = tEnd; tEnd = tStart; tStart = tmp;
        }
        return new HeliacalEvent(tStart, tOpt, tEnd);
    }

    /// <summary>
    /// Mirrors <c>heliacal_ut_vis_lim</c> at swehel.c#L3163 — the
    /// visibility-limit branch of the <c>heliacal_ut</c> dispatch. Assumes
    /// Mercury / Venus / TypeEvent ≤ 2 (the only paths we currently honour;
    /// outer-planet acronychal path (M-25) routes through
    /// <see cref="GetAscObliquityWithSun"/> and <see cref="GetAcronychalDay"/>).
    /// </summary>
    internal HeliacalEvent? HeliacalUtVisLim(double tjdStart, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, CelestialBody body, HeliacalEventType eventType, HeliacalFlags helFlags, EphemerisFlags ephemerisFlags)
    {
        var tjd = body == CelestialBody.Mercury ? tjdStart - 30 : tjdStart - 50;
        var isInnerPlanetPath = body == CelestialBody.Mercury || body == CelestialBody.Venus
            || eventType == HeliacalEventType.MorningFirst || eventType == HeliacalEventType.EveningLast;

        if (isInnerPlanetPath)
        {
            tjd = FindConjunctSun(tjd, body, helFlags, eventType, ephemerisFlags);

            var tday = GetHeliacalDay(tjd, observer, atm, obs, body, helFlags, eventType, ephemerisFlags);
            if (tday is null) return null;

            if ((helFlags & HeliacalFlags.NoDetails) != 0)
                return new HeliacalEvent(tday.Value, 0.0, 0.0);

            return GetHeliacalDetails(tday.Value, observer, atm, obs, body, eventType, helFlags, ephemerisFlags);
        }

        // Outer-planet acronychal path — swehel.c#L3205-L3219. The C library's
        // swe_heliacal_ut front gate refuses this combination unless an AvKind
        // flag is set, so heliacal_ut_vis_lim is unreachable from there. We
        // call it directly with the SE_ACRONYCHAL_RISING / SE_ACRONYCHAL_SETTING
        // value that get_asc_obl_with_sun and get_acronychal_day expect.
        var acroEvent = eventType == HeliacalEventType.EveningFirst
            ? AcronychalEventType.AcronychalRising
            : AcronychalEventType.AcronychalSetting;

        var tjdAlign = GetAscObliquityWithSun(tjd, body, observer, acroEvent, ephemerisFlags);
        if (tjdAlign is null) return null;

        var tdayOuter = GetAcronychalDay(tjdAlign.Value, observer, atm, obs, body, helFlags, acroEvent, ephemerisFlags);
        if (tdayOuter is null) return null;

        return new HeliacalEvent(tdayOuter.Value, 0.0, 0.0);
    }

    // SE_ACRONYCHAL_RISING / SETTING from swephexp.h#L430-L431. Used internally
    // to drive get_asc_obl_with_sun / get_acronychal_day in the outer-planet
    // branch; the public HeliacalEventType enum does not need these values
    // because the public API surfaces the EveningFirst / MorningLast aliases.
    internal enum AcronychalEventType
    {
        AcronychalRising = 5,
        AcronychalSetting = 6,
    }

    /// <summary>
    /// Mirrors <c>moon_event_vis_lim</c> at swehel.c#L3249 — the lunar
    /// branch of the heliacal-event dispatcher.
    /// </summary>
    internal HeliacalEvent? MoonEventVisLim(double tjdStart, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, HeliacalEventType eventType, HeliacalFlags helFlags, EphemerisFlags ephemerisFlags)
    {
        if (eventType == HeliacalEventType.MorningFirst || eventType == HeliacalEventType.EveningLast)
            throw new ArgumentException("the moon has no morning first / evening last event", nameof(eventType));

        var helFlags2 = helFlags & ~HeliacalFlags.HighPrecision;

        var tjd = tjdStart - 30;
        tjd = FindConjunctSun(tjd, CelestialBody.Moon, helFlags, eventType, ephemerisFlags);
        var tday = GetHeliacalDay(tjd, observer, atm, obs, CelestialBody.Moon, helFlags2, eventType, ephemerisFlags);
        if (tday is null) return null;

        // Mirror the swehel.c#L3275-L3320 dret[] sequence literally to keep
        // the swap semantics straight.
        var dret1 = tday.Value;
        var (tOpt, _) = TimeOptimumVisibility(dret1, observer, atm, obs, CelestialBody.Moon, helFlags, ephemerisFlags);
        dret1 = tOpt;

        var direct = eventType == HeliacalEventType.MorningLast ? -1 : 1;
        var (dret2, _) = TimeLimitInvisible(dret1, observer, atm, obs, CelestialBody.Moon, helFlags, direct, ephemerisFlags);
        var (dret0, _) = TimeLimitInvisible(dret1, observer, atm, obs, CelestialBody.Moon, helFlags, -direct, ephemerisFlags);

        // Sunrise/sunset clamp — swehel.c#L3294-L3313. Done before the swap.
        var geo = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);
        if (eventType == HeliacalEventType.EveningFirst)
        {
            var sunSetRes = _riseTransit!.Find(
                new JulianDay(dret0), CelestialBody.Sun, ephemerisFlags,
                RiseTransitFlags.Set, geo, atm.PressureMbar, atm.TemperatureCelsius);
            if (!sunSetRes.HasWarning && sunSetRes.Value.Value < dret1) dret0 = sunSetRes.Value.Value;
        }
        else
        {
            var sunRiseRes = _riseTransit!.Find(
                new JulianDay(dret1), CelestialBody.Sun, ephemerisFlags,
                RiseTransitFlags.Rise, geo, atm.PressureMbar, atm.TemperatureCelsius);
            if (!sunRiseRes.HasWarning && dret0 > sunRiseRes.Value.Value) dret0 = sunRiseRes.Value.Value;
        }

        if (eventType == HeliacalEventType.MorningLast)
        {
            (dret0, dret2) = (dret2, dret0);
        }
        return new HeliacalEvent(dret0, dret1, dret2);
    }

    /// <summary>
    /// Geocentric ecliptic longitude (degrees, [0, 360)) and longitude rate
    /// (degrees per UT day) of <paramref name="body"/>. Mirrors the
    /// <c>swe_calc(... epheflag|SEFLG_SPEED ...)</c> idiom used by
    /// <c>find_conjunct_sun</c>.
    /// </summary>
    private (double LonDeg, double LonRateDegPerDay) EclipticLongitudeAndRate(JulianDay jdUt, CelestialBody body, EphemerisFlags ephemerisFlags)
    {
        var flags = ephemerisFlags | EphemerisFlags.Speed;
        var bs = _body.ComputeUt(body, jdUt, flags);
        Span<double> cart = stackalloc double[6];
        cart[0] = bs.Position.X; cart[1] = bs.Position.Y; cart[2] = bs.Position.Z;
        cart[3] = bs.Velocity.X; cart[4] = bs.Velocity.Y; cart[5] = bs.Velocity.Z;
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);
        var lonDeg = polar[0] * AstronomicalConstants.RadToDeg;
        if (lonDeg < 0) lonDeg += 360.0;
        var lonRate = polar[3] * AstronomicalConstants.RadToDeg;
        return (lonDeg, lonRate);
    }

    // ObserverParameters fed to ComputeVisibilityLimit. Phase 11d's
    // ComputeHeliacalPhenomena already runs DefaultParameters → so callers
    // inside this file pass the resolved 'obs' record. ComputeVisibilityLimit
    // itself runs DefaultParameters again, which is idempotent: a non-Auto
    // record is returned unchanged. This shim makes the contract explicit.
    private static ObserverParameters _obsForVisLim(ObserverParameters obs) => obs;

    /// <summary>
    /// Apparent geocentric equatorial coordinates of <paramref name="body"/>.
    /// Mirrors <c>swe_calc(tjd, ipl, epheflag | SEFLG_EQUATORIAL, ...)</c> at
    /// swehel.c#L2465 — note this branch in the C library does <em>not</em>
    /// add SEFLG_NONUT/SEFLG_TRUEPOS regardless of HighPrecision, unlike most
    /// other heliacal call sites. Used only by <see cref="GetAscObl"/>.
    /// </summary>
    private (double RaDeg, double DecDeg) ComputeEquatorialApparent(JulianDay jdUt, CelestialBody body, EphemerisFlags ephemerisFlags)
    {
        var flags = ephemerisFlags | EphemerisFlags.Equatorial;
        var state = _body.ComputeUt(body, jdUt, flags);
        var ra = Math.Atan2(state.Position.Y, state.Position.X) * RadToDeg;
        if (ra < 0) ra += 360.0;
        var r = Math.Sqrt(state.Position.X * state.Position.X
                          + state.Position.Y * state.Position.Y
                          + state.Position.Z * state.Position.Z);
        var dec = Math.Asin(state.Position.Z / r) * RadToDeg;
        return (ra, dec);
    }

    /// <summary>
    /// Mirrors <c>get_asc_obl</c> at swehel.c#L2452. Returns the ascensio (or
    /// descensio) obliqua of <paramref name="body"/> in degrees, or
    /// <see langword="null"/> when the body is circumpolar at the observer's
    /// latitude (the C function returns -2 there).
    /// </summary>
    private double? GetAscObl(double tjd, CelestialBody body, ObserverLocation observer, bool descObl, EphemerisFlags ephemerisFlags)
    {
        var (raDeg, decDeg) = ComputeEquatorialApparent(new JulianDay(tjd), body, ephemerisFlags);
        return AcronychalGeometry.AscensioObliqua(raDeg, decDeg, observer.LatitudeDegrees, descObl);
    }

    /// <summary>
    /// Mirrors <c>get_asc_obl_diff</c> at swehel.c#L2519. Returns the wrapped
    /// difference (sun − planet, ±180°) of the two asc-obl values, or
    /// <see langword="null"/> if either body is circumpolar.
    /// </summary>
    private double? GetAscObliqDiff(double tjd, CelestialBody body, ObserverLocation observer, bool descObl, bool isAcronychal, EphemerisFlags ephemerisFlags)
    {
        var aoSun = GetAscObl(tjd, CelestialBody.Sun, observer, descObl, ephemerisFlags);
        if (aoSun is null) return null;
        // is_acronychal flips desc_obl for the planet (swehel.c#L2527-L2532).
        var planetDescObl = isAcronychal ? !descObl : descObl;
        var aoPl = GetAscObl(tjd, body, observer, planetDescObl, ephemerisFlags);
        if (aoPl is null) return null;
        var dsunpl = AngleMath.NormalizeDegrees(aoSun.Value - aoPl.Value);
        if (isAcronychal) dsunpl = AngleMath.NormalizeDegrees(dsunpl - 180.0);
        if (dsunpl > 180.0) dsunpl -= 360.0;
        return dsunpl;
    }

    /// <summary>
    /// Mirrors <c>get_asc_obl_with_sun</c> at swehel.c#L2604. Walks
    /// <paramref name="tjdStart"/> forward in 10-day steps until the
    /// sun-vs-planet asc-obl difference crosses zero with the expected sign,
    /// then bisects to ~1e-5°. Returns <see langword="null"/> on circumpolar
    /// failure.
    /// </summary>
    internal double? GetAscObliquityWithSun(double tjdStart, CelestialBody body, ObserverLocation observer, AcronychalEventType evtyp, EphemerisFlags ephemerisFlags)
    {
        // For the outer-planet branch we are always called with evtyp 5 / 6,
        // so is_acronychal is TRUE and (since body != Moon) retro is TRUE.
        // desc_obl is TRUE for ACRONYCHAL_RISING (5) and FALSE for SETTING (6).
        var descObl = evtyp == AcronychalEventType.AcronychalRising;
        const bool retro = true;
        const bool isAcronychal = true;

        var tjd = tjdStart;
        var dsunpl = GetAscObliqDiff(tjd, body, observer, descObl, isAcronychal, ephemerisFlags);
        if (dsunpl is null) return null;

        const double dsunplSentinel = -999_999_999.0;
        var dsunplSave = dsunplSentinel;
        const double initialStep = 20.0;
        var i = 0;
        while (dsunplSave == dsunplSentinel
               || Math.Abs(dsunpl.Value) + Math.Abs(dsunplSave) > 180.0
               || (retro && !(dsunplSave < 0 && dsunpl.Value >= 0))
               || (!retro && !(dsunplSave >= 0 && dsunpl.Value < 0)))
        {
            i++;
            if (i > 5000)
                throw new InvalidOperationException("loop in GetAscObliquityWithSun() (1)");
            dsunplSave = dsunpl.Value;
            tjd += 10.0;
            dsunpl = GetAscObliqDiff(tjd, body, observer, descObl, isAcronychal, ephemerisFlags);
            if (dsunpl is null) return null;
        }
        var bracketStart = tjd - initialStep;
        var daystep = initialStep / 2.0;
        tjd = bracketStart + daystep;
        var dsunplTest = GetAscObliqDiff(tjd, body, observer, descObl, isAcronychal, ephemerisFlags);
        if (dsunplTest is null) return null;

        i = 0;
        while (Math.Abs(dsunpl.Value) > 1e-5)
        {
            i++;
            if (i > 5000)
                throw new InvalidOperationException("loop in GetAscObliquityWithSun() (2)");
            if (dsunplSave * dsunplTest.Value >= 0)
            {
                dsunplSave = dsunplTest.Value;
                bracketStart = tjd;
            }
            else
            {
                dsunpl = dsunplTest;
            }
            daystep /= 2.0;
            tjd = bracketStart + daystep;
            dsunplTest = GetAscObliqDiff(tjd, body, observer, descObl, isAcronychal, ephemerisFlags);
            if (dsunplTest is null) return null;
        }
        return tjd;
    }

    /// <summary>
    /// Mirrors <c>get_acronychal_day</c> at swehel.c#L3043. Drives the
    /// outer-planet acronychal-day search by repeatedly stepping the search
    /// JD by ~0.7 d, finding the body's rise / set, walking back from there
    /// until the body becomes visible against the sky background, then
    /// running <see cref="TimeLimitInvisible"/> twice (dark sky vs no moon)
    /// and checking whether the two limits converge to within 0.5 minute.
    /// </summary>
    internal double? GetAcronychalDay(double tjd, ObserverLocation observer, AtmosphericConditions atm, ObserverParameters obs, CelestialBody body, HeliacalFlags helFlags, AcronychalEventType evtyp, EphemerisFlags ephemerisFlags)
    {
        // swehel.c#L3048: force photopic mode for the inner vis-limit calls.
        helFlags |= HeliacalFlags.VisLimPhotopic;
        RiseTransitFlags isRiseOrSet;
        int direct;
        if (evtyp == AcronychalEventType.AcronychalRising)
        {
            isRiseOrSet = RiseTransitFlags.Rise;
            direct = -1;
        }
        else
        {
            isRiseOrSet = RiseTransitFlags.Set;
            direct = 1;
        }

        var geo = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);
        var dtret = 999.0;
        double tret = tjd;
        var safety = 0;
        while (Math.Abs(dtret) > 0.5 / 1440.0)
        {
            if (++safety > 200)
                throw new InvalidOperationException("loop in GetAcronychalDay()");
            tjd += 0.7 * direct;
            if (direct < 0) tjd -= 1.0;
            var rsRes = _riseTransit!.Find(
                new JulianDay(tjd), body, ephemerisFlags,
                isRiseOrSet, geo, atm.PressureMbar, atm.TemperatureCelsius);
            if (rsRes.HasWarning) return null;
            tjd = rsRes.Value.Value;

            // Walk back from rise/set until the object is visible against
            // the sky (limit > object). swehel.c#L3076-L3080.
            var vis = ComputeVisibilityLimit(new JulianDay(tjd), observer, atm, _obsForVisLim(obs), body, helFlags, ephemerisFlags);
            var visStep = 0;
            while (vis.LimitingMagnitude < vis.ObjectMagnitude)
            {
                if (++visStep > 2000) return null;
                tjd += 10.0 / 1440.0 * -direct;
                vis = ComputeVisibilityLimit(new JulianDay(tjd), observer, atm, _obsForVisLim(obs), body, helFlags, ephemerisFlags);
                if (!vis.IsObjectAboveHorizon) return null;
            }

            var (tretDark, _) = TimeLimitInvisible(tjd, observer, atm, obs, body, helFlags | HeliacalFlags.VisLimDark, direct, ephemerisFlags);
            (tret, _) = TimeLimitInvisible(tjd, observer, atm, obs, body, helFlags | HeliacalFlags.VisLimNoMoon, direct, ephemerisFlags);
            dtret = Math.Abs(tret - tretDark);
        }
        return tret;
    }
}
