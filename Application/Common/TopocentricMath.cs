// Ported from swisseph-master/sweph.c swi_get_observer (lines 7282-7376).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Common;

/// <summary>
/// Pure topocentric-observer math, reusable by any service that needs the
/// geographic offset of the observer from the geocenter. Mirrors
/// <c>swi_get_observer</c> at <c>sweph.c#L7282-L7376</c> stripped of the
/// cached-state branches; we always recompute. Frame bias is neglected, as
/// in the C library (sweph.c#L7368: "neglect frame bias").
/// </summary>
/// <remarks>
/// All in-tree callers — body-pipeline topocentric folding (sweph.c#L2528,
/// #L2700, #L3399, etc.) and the fixed-star pipeline (sweph.c#L6514) —
/// invoke <c>swi_get_observer</c> with <c>iflag | SEFLG_NONUT</c>. We
/// therefore mirror that NONUT path: sidereal time is computed with the
/// <em>mean</em> obliquity and zero <c>Δψ</c>, and the un-nutation block at
/// sweph.c#L7357-L7363 is skipped. The resulting Earth-fixed-to-J2000
/// rotation chain is precession only.
/// </remarks>
internal static class TopocentricMath
{
    private const double EarthRadiusMeters = 6_378_136.6;
    private const double EarthOblateness = 1.0 / 298.25642;
    private const double EarthRotSpeedRadPerDay = 7.2921151467e-5 * 86_400.0;

    /// <summary>
    /// Geographic observer offset in J2000-equatorial cartesian (AU, AU/day).
    /// Position is the WGS84 surface vector relative to the geocenter; velocity
    /// is the Earth-rotation contribution. Both components are precessed back
    /// to J2000 (no un-nutation, mirroring the C library's NONUT topocentric
    /// path) so they can be added directly to a J2000 barycentric / geocentric
    /// Earth state.
    /// </summary>
    public static BodyState ObserverOffsetJ2000Equator(
        JulianDay jdEt,
        ObserverLocation observer,
        CalendarService calendar,
        AstronomicalModelOverrides models)
    {
        const double auMeters = AstronomicalConstants.AstronomicalUnitMeters;
        const double degToRad = AstronomicalConstants.DegToRad;

        // ΔT in days → tjd_ut for sidereal-time evaluation.
        var deltaT = calendar.DeltaT(jdEt);
        var jdUt = new JulianDay(jdEt.Value - deltaT);

        // Mean obliquity for sidereal-time-with-equinoxes form. Under
        // SEFLG_NONUT the C library uses mean ε and Δψ = 0 (sweph.c#L7311-
        // L7316), reducing the call to mean Greenwich sidereal time.
        var meanEpsRad = Precession.MeanObliquity(jdEt.Value, models);
        var meanEpsDeg = meanEpsRad * AstronomicalConstants.RadToDeg;
        var sidtHours = calendar.SiderealTime(jdUt, meanEpsDeg, nutationLongitudeDegrees: 0.0);
        var sidtDeg = sidtHours * 15.0; // hours → degrees of arc

        var cosfi = System.Math.Cos(observer.LatitudeDegrees * degToRad);
        var sinfi = System.Math.Sin(observer.LatitudeDegrees * degToRad);
        var f = EarthOblateness;
        var cc = 1.0 / System.Math.Sqrt(cosfi * cosfi + (1 - f) * (1 - f) * sinfi * sinfi);
        var ss = (1 - f) * (1 - f) * cc;
        var cosl = System.Math.Cos((observer.LongitudeDegrees + sidtDeg) * degToRad);
        var sinl = System.Math.Sin((observer.LongitudeDegrees + sidtDeg) * degToRad);
        var h = observer.HeightMeters;

        // Earth-fixed → equator-of-date cartesian (m).
        System.Span<double> xobs = stackalloc double[6];
        xobs[0] = (EarthRadiusMeters * cc + h) * cosfi * cosl;
        xobs[1] = (EarthRadiusMeters * cc + h) * cosfi * sinl;
        xobs[2] = (EarthRadiusMeters * ss + h) * sinfi;
        // Velocity from Earth rotation (sweph.c#L7350).
        var rPlanar = (EarthRadiusMeters * cc + h) * cosfi;
        xobs[3] = -EarthRotSpeedRadPerDay * rPlanar * sinl;
        xobs[4] = EarthRotSpeedRadPerDay * rPlanar * cosl;
        xobs[5] = 0.0;

        // Convert metres → AU.
        for (var i = 0; i < 6; i++) xobs[i] /= auMeters;

        // sweph.c#L7357: under NONUT the un-nutation block is skipped.

        // swi_precess + swi_precess_speed (J_TO_J2000). sweph.c#L7365-L7367.
        System.Span<double> posSpan = stackalloc double[3];
        posSpan[0] = xobs[0]; posSpan[1] = xobs[1]; posSpan[2] = xobs[2];
        Precession.Apply(posSpan, jdEt.Value, AstronomicalConstants.J2000, models);
        xobs[0] = posSpan[0]; xobs[1] = posSpan[1]; xobs[2] = posSpan[2];
        Precession.ApplySpeed(xobs, jdEt.Value, AstronomicalConstants.J2000, models);

        return new BodyState(
            new Vec3(xobs[0], xobs[1], xobs[2]),
            new Vec3(xobs[3], xobs[4], xobs[5]),
            0.0,
            EphemerisSource.SwissEph,
            BodyStateFrame.GeocentricJ2000Equator);
    }
}
