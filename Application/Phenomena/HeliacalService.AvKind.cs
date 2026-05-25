// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// M-26: arc-of-vision search methods. Mirrors heliacal_ut_arc_vis at
// swehel.c#L2211 and moon_event_arc_vis at swehel.c#L2114. Reference
// driver: /tmp/heliacal_avkind_ref.c.
//
// Four AvKind modes (mutually exclusive bits):
//   AvKind_VR    : Reijs walk-through to local-min of arc visionis
//   AvKind_Pto   : photometric — walk back until body alt drops below 0
//   AvKind_Min7  : fixed sun altitude -7°
//   AvKind_Min9  : fixed sun altitude -9°
//
// The moon path supports AvKind_VR only (swehel.c#L2128-L2132); the front
// gate in FindHeliacalEvent enforces this.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

public sealed partial class HeliacalService
{
    /// <summary>
    /// Mirrors <c>heliacal_ut_arc_vis</c> at swehel.c#L2211 — the
    /// arc-of-vision method (planets and fixed stars) for one synodic
    /// window. Returns <see langword="null"/> when no event is found
    /// within <c>maxlength</c> days of <paramref name="tjdStart"/>.
    /// </summary>
    internal HeliacalEvent? HeliacalUtArcVis(
        double tjdStart,
        ObserverLocation observer,
        AtmosphericConditions atm,
        ObserverParameters obs,
        CelestialBody body,
        HeliacalEventType eventType,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags)
    {
        // Per-planet daystep / window — swehel.c#L2244-L2257.
        double dayStep;
        double maxLength;
        switch (body)
        {
            case CelestialBody.Mercury: dayStep = 1; maxLength = 100; break;
            case CelestialBody.Venus: dayStep = 64; maxLength = 384; break;
            case CelestialBody.Mars: dayStep = 128; maxLength = 640; break;
            case CelestialBody.Jupiter: dayStep = 64; maxLength = 384; break;
            case CelestialBody.Saturn: dayStep = 64; maxLength = 256; break;
            default: dayStep = 64; maxLength = 256; break;
        }

        // TypeEvent → RiseSet eventtype + DayStep direction (swehel.c#L2259-L2267).
        // 1 morning first  → eventtype=1 (rise), step forward
        // 2 evening last   → eventtype=2 (set),  step backward
        // 3 evening first  → eventtype=2 (set),  step forward (acronychal rising)
        // 4 morning last   → eventtype=1 (rise), step backward (acronychal setting)
        var rsEvent = eventType switch
        {
            HeliacalEventType.MorningFirst => RiseTransitFlags.Rise,
            HeliacalEventType.EveningLast => RiseTransitFlags.Set,
            HeliacalEventType.EveningFirst => RiseTransitFlags.Set,
            HeliacalEventType.MorningLast => RiseTransitFlags.Rise,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };
        if (eventType == HeliacalEventType.EveningLast || eventType == HeliacalEventType.MorningLast)
            dayStep = -dayStep;
        // Sun rise/set takes the disc-centre flag; matches RiseSet() at
        // swehel.c#L539.
        var rsmi = rsEvent | RiseTransitFlags.DiscCenter;

        // Bounds (swehel.c#L2272-L2280).
        var jdnDaysUT = tjdStart;
        var jdnDaysUTfinal = jdnDaysUT + maxLength;
        jdnDaysUT -= 1.0;
        if (dayStep < 0)
        {
            (jdnDaysUT, jdnDaysUTfinal) = (jdnDaysUTfinal, jdnDaysUT);
        }
        var jdnDaysUTstep = jdnDaysUT - dayStep;
        var doneOneDay = false;
        var arcusVisDelta = 199.0;
        var arcusVisPto = -5.55;
        var jdnArcVisUT = double.NaN;
        var jdnDaysUTstepOud = jdnDaysUTstep;
        var arcusVisDeltaOud = arcusVisDelta;
        var sgn = dayStep < 0 ? -1.0 : 1.0;
        var geo = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);
        var negateTdelta = eventType == HeliacalEventType.EveningLast
                           || eventType == HeliacalEventType.EveningFirst;

        do
        {
            if (Math.Abs(dayStep) == 1) doneOneDay = true;
            do
            {
                jdnDaysUTstepOud = jdnDaysUTstep;
                arcusVisDeltaOud = arcusVisDelta;
                jdnDaysUTstep += dayStep;

                // Sun rise/set anchor.
                var rsRes = _riseTransit!.Find(
                    new JulianDay(jdnDaysUTstep), CelestialBody.Sun, ephemerisFlags,
                    rsmi, geo, atm.PressureMbar, atm.TemperatureCelsius);
                if (rsRes.HasWarning) return null;
                var tret = rsRes.Value.Value;

                // Sun position at sun rise/set — first call gives us the dec.
                // and topocentric altitude that feed HourAngle.
                var (altSAtRise, _) = ObjectAltAzi(new JulianDay(tret), observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);
                var (_, decSAtRise) = ComputeEquatorial(new JulianDay(tret), observer, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);
                var trise = AtmosphericModel.HourAngleHours(altSAtRise, decSAtRise, observer.LatitudeDegrees);

                // Choose sun altitude target for arcus-vis interpolation.
                var sunsangle = arcusVisPto;
                if ((helFlags & HeliacalFlags.AvKindMin7) != 0) sunsangle = -7.0;
                if ((helFlags & HeliacalFlags.AvKindMin9) != 0) sunsangle = -9.0;
                var theliacal = AtmosphericModel.HourAngleHours(sunsangle, decSAtRise, observer.LatitudeDegrees);
                var tdelta = theliacal - trise;
                if (negateTdelta) tdelta = -tdelta;
                jdnArcVisUT = tret - tdelta / 24.0;

                // Sun azimuth/altitude at the heliacal time.
                var (altS, aziS) = ObjectAltAzi(new JulianDay(jdnArcVisUT), observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);
                // Object azimuth/altitude + apparent magnitude at the same time.
                var (altO, aziO) = ObjectAltAzi(new JulianDay(jdnArcVisUT), observer, atm, body, helFlags, ephemerisFlags, topocentric: true);
                var objectMagn = ApparentMagnitude(new JulianDay(jdnArcVisUT), body, observer, helFlags, ephemerisFlags);
                var deltaAlt = altO - altS;

                var (xm, ym, sun) = ArcusVisionisMath.HeliacalAngle(
                    objectMagn, obs,
                    aziO,
                    altMDeg: -1, aziMDeg: 0,
                    new JulianDay(jdnArcVisUT), aziS,
                    observer.LatitudeDegrees, observer.HeightMeters,
                    atm, helFlags);
                _ = xm;
                var arcusVis = ym;
                arcusVisPto = sun;
                arcusVisDelta = deltaAlt - arcusVis;
            }
            while ((arcusVisDeltaOud > 0 || arcusVisDelta < 0)
                   && (jdnDaysUTfinal - jdnDaysUTstep) * sgn > 0);

            if (!doneOneDay && (jdnDaysUTfinal - jdnDaysUTstep) * sgn > 0)
            {
                // Halve the daystep and rewind one step (swehel.c#L2362-L2366).
                arcusVisDelta = arcusVisDeltaOud;
                dayStep = ((int)(Math.Abs(dayStep) / 2.0)) * sgn;
                jdnDaysUTstep = jdnDaysUTstepOud;
            }
        }
        while (!doneOneDay && (jdnDaysUTfinal - jdnDaysUTstep) * sgn > 0);

        var d = (jdnDaysUTfinal - jdnDaysUTstep) * sgn;
        if (d <= 0 || d >= maxLength) return null;

        // Per-mode refinement — swehel.c#L2385-L2438.
        var direct = HeliacalConstants.TimeStepDefaultMinutes / 24.0 / 60.0;
        if (dayStep < 0) direct = -direct;

        if ((helFlags & HeliacalFlags.AvKindVR) != 0)
        {
            jdnArcVisUT = ArcVisRefineVR(jdnArcVisUT, direct, body, observer, atm, obs, helFlags, ephemerisFlags);
        }
        if ((helFlags & HeliacalFlags.AvKindPto) != 0)
        {
            jdnArcVisUT = ArcVisRefinePto(jdnArcVisUT, direct, body, observer, atm, helFlags, ephemerisFlags);
        }

        if (jdnArcVisUT < -9_999_999.0 || jdnArcVisUT > 9_999_999.0) return null;
        return new HeliacalEvent(jdnArcVisUT, 0.0, 0.0);
    }

    /// <summary>
    /// Mirrors <c>moon_event_arc_vis</c> at swehel.c#L2114 — the moon
    /// branch of the arc-of-vision dispatcher. Only AvKind_VR is supported
    /// (swehel.c#L2128-L2132); the front gate in
    /// <see cref="FindHeliacalEvent"/> enforces this.
    /// </summary>
    internal HeliacalEvent? MoonEventArcVis(
        double tjdStart,
        ObserverLocation observer,
        AtmosphericConditions atm,
        ObserverParameters obs,
        HeliacalEventType eventType,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags)
    {
        if (eventType != HeliacalEventType.EveningFirst && eventType != HeliacalEventType.MorningLast)
            throw new ArgumentException("the moon has no morning first / evening last event", nameof(eventType));

        // TypeEvent → RiseSet eventtype + Daystep (swehel.c#L2143-L2151).
        //   3 evening first → set  + step +1 (forward toward evening)
        //   4 morning last  → rise + step -1 (backward through new moon)
        var rsEvent = eventType == HeliacalEventType.EveningFirst
            ? RiseTransitFlags.Set
            : RiseTransitFlags.Rise;
        var rsmi = rsEvent | RiseTransitFlags.DiscCenter;
        var dayStep = eventType == HeliacalEventType.EveningFirst ? 1.0 : -1.0;

        var jdnDaysUT = tjdStart;
        // Evening-first: shift +30 d so the new-moon search has a stride
        // of 30 d to lock onto. (Original C: "if (TypeEvent == 1) JDNDaysUT
        // = JDNDaysUT + 30" with the *remapped* TypeEvent==1, which is the
        // morning-last case after eventtype/Daystep flip.)
        if (eventType == HeliacalEventType.MorningLast) jdnDaysUT += 30.0;

        var phenoFlags = ephemerisFlags | EphemerisFlags.Topocentric | EphemerisFlags.Equatorial;
        if ((helFlags & HeliacalFlags.HighPrecision) == 0)
            phenoFlags |= EphemerisFlags.NoNutation | EphemerisFlags.TruePosition;

        // Walk to the new-moon date by tracking the phase angle minimum.
        var phase2 = _pheno.ComputeUt(new JulianDay(jdnDaysUT), CelestialBody.Moon, phenoFlags, observer).PhaseAngleDeg;
        var goingUp = false;
        double phase1;
        var safety = 0;
        do
        {
            if (++safety > 5000) return null;
            jdnDaysUT += dayStep;
            phase1 = phase2;
            phase2 = _pheno.ComputeUt(new JulianDay(jdnDaysUT), CelestialBody.Moon, phenoFlags, observer).PhaseAngleDeg;
            if (phase2 > phase1) goingUp = true;
        }
        while (!goingUp || (goingUp && phase2 > phase1));
        // Step back to land on the smallest-phase day (swehel.c#L2169).
        jdnDaysUT -= dayStep;

        var geo = new GeographicLocation(observer.LongitudeDegrees, observer.LatitudeDegrees, observer.HeightMeters);
        var jdnDaysUTi = jdnDaysUT;
        jdnDaysUT -= dayStep;
        var minTAVoud = 199.0;
        var minTAV = 199.0;
        var oldestMinTAV = minTAV;
        var deltaAltOud = 90.0;
        var deltaAlt = 90.0;
        double tjdMoonEvent = jdnDaysUTi;
        var sgnDayStep = dayStep < 0 ? -1.0 : 1.0;
        var localStepDays = HeliacalConstants.LocalMinStepMinutes / 60.0 / 24.0;

        do
        {
            jdnDaysUT += dayStep;
            var rsRes = _riseTransit!.Find(
                new JulianDay(jdnDaysUT), CelestialBody.Moon, ephemerisFlags,
                rsmi, geo, atm.PressureMbar, atm.TemperatureCelsius);
            if (rsRes.HasWarning) return null;
            tjdMoonEvent = rsRes.Value.Value;
            var tjdMoonEventStart = tjdMoonEvent;
            minTAV = 199.0;
            oldestMinTAV = minTAV;
            do
            {
                oldestMinTAV = minTAVoud;
                minTAVoud = minTAV;
                deltaAltOud = deltaAlt;
                tjdMoonEvent -= 1.0 / 60.0 / 24.0 * sgnDayStep;
                var (altS, _) = ObjectAltAzi(new JulianDay(tjdMoonEvent), observer, atm, CelestialBody.Sun, helFlags, ephemerisFlags, topocentric: true);
                var (altO, _) = ObjectAltAzi(new JulianDay(tjdMoonEvent), observer, atm, CelestialBody.Moon, helFlags, ephemerisFlags, topocentric: true);
                deltaAlt = altO - altS;
                minTAV = DeterTAV(new JulianDay(tjdMoonEvent), CelestialBody.Moon, observer, atm, obs, ephemerisFlags, helFlags);
                var timeCheck = tjdMoonEvent - localStepDays * sgnDayStep;
                var localMinCheck = DeterTAV(new JulianDay(timeCheck), CelestialBody.Moon, observer, atm, obs, ephemerisFlags, helFlags);
                if ((minTAV <= minTAVoud || localMinCheck < minTAV)
                    && Math.Abs(tjdMoonEvent - tjdMoonEventStart) < 120.0 / 60.0 / 24.0)
                    continue;
                break;
            }
            while (true);
        }
        while (deltaAltOud < minTAVoud && Math.Abs(jdnDaysUT - jdnDaysUTi) < 15.0);

        if (Math.Abs(jdnDaysUT - jdnDaysUTi) >= 15.0) return null;

        var extrax = ArcusVisionisMath.X2Min(minTAV, minTAVoud, oldestMinTAV);
        tjdMoonEvent += (1 - extrax) * sgnDayStep / 60.0 / 24.0;
        return new HeliacalEvent(tjdMoonEvent, 0.0, 0.0);
    }

    /// <summary>
    /// Mirrors the AvKind_VR refinement block at swehel.c#L2387-L2418.
    /// Walks the time-pointer in 1-minute steps and locates the local
    /// minimum of <see cref="DeterTAV"/> via a parabolic fit to the
    /// triple (oldest, oud, act).
    /// </summary>
    private double ArcVisRefineVR(
        double jdnArcVisUT, double direct,
        CelestialBody body,
        ObserverLocation observer,
        AtmosphericConditions atm,
        ObserverParameters obs,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags)
    {
        var timeStep = direct;
        var timePointer = jdnArcVisUT;
        var oldestMinTAV = DeterTAV(new JulianDay(timePointer), body, observer, atm, obs, ephemerisFlags, helFlags);
        timePointer += timeStep;
        var minTAVoud = DeterTAV(new JulianDay(timePointer), body, observer, atm, obs, ephemerisFlags, helFlags);
        double minTAVact;
        if (minTAVoud > oldestMinTAV)
        {
            timePointer = jdnArcVisUT;
            timeStep = -timeStep;
            minTAVact = oldestMinTAV;
        }
        else
        {
            minTAVact = minTAVoud;
            minTAVoud = oldestMinTAV;
        }
        var safety = 0;
        var tbVR = 0.0;
        do
        {
            if (++safety > 50_000) return jdnArcVisUT;
            timePointer += timeStep;
            oldestMinTAV = minTAVoud;
            minTAVoud = minTAVact;
            minTAVact = DeterTAV(new JulianDay(timePointer), body, observer, atm, obs, ephemerisFlags, helFlags);
            if (minTAVoud < minTAVact)
            {
                var extrax = ArcusVisionisMath.X2Min(minTAVact, minTAVoud, oldestMinTAV);
                tbVR = timePointer - (1 - extrax) * timeStep;
            }
        }
        while (tbVR == 0);
        return tbVR;
    }

    /// <summary>
    /// Mirrors the AvKind_PTO refinement block at swehel.c#L2420-L2438.
    /// Walks back in 1-minute steps until the body's topocentric altitude
    /// drops below the horizon, then takes the midpoint of the last
    /// two samples.
    /// </summary>
    private double ArcVisRefinePto(
        double jdnArcVisUT, double direct,
        CelestialBody body,
        ObserverLocation observer,
        AtmosphericConditions atm,
        HeliacalFlags helFlags,
        EphemerisFlags ephemerisFlags)
    {
        var safety = 0;
        double oudeDatum;
        var t = jdnArcVisUT;
        do
        {
            if (++safety > 50_000) return jdnArcVisUT;
            oudeDatum = t;
            t -= direct;
            var (alt, _) = ObjectAltAzi(new JulianDay(t), observer, atm, body, helFlags, ephemerisFlags, topocentric: true);
            if (alt <= 0) break;
        }
        while (true);
        return (t + oudeDatum) / 2.0;
    }

    /// <summary>
    /// Convenience wrapper for the apparent magnitude of <paramref name="body"/>
    /// at <paramref name="jdUt"/>. Mirrors the topo-equatorial
    /// <c>swe_pheno_ut</c> idiom used inside <c>Magnitude</c>
    /// (swehel.c#L1106).
    /// </summary>
    private double ApparentMagnitude(JulianDay jdUt, CelestialBody body, ObserverLocation observer, HeliacalFlags helFlags, EphemerisFlags ephemerisFlags)
    {
        var phenoFlags = ephemerisFlags | EphemerisFlags.Topocentric | EphemerisFlags.Equatorial;
        if ((helFlags & HeliacalFlags.HighPrecision) == 0)
            phenoFlags |= EphemerisFlags.NoNutation | EphemerisFlags.TruePosition;
        return _pheno.ComputeUt(jdUt, body, phenoFlags, observer).ApparentMagnitude;
    }
}
