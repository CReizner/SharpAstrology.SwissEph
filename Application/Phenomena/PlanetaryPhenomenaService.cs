// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference driver: /tmp/phenoref.c — golden values for the major planets +
// Sun/Moon at JD 2460310.5 with the geometric flag set
// (TRUEPOS+NOABERR+NOGDEFL+NONUT). Magnitudes use the Mallama-2018 / Vreijs
// formulas which the C library enables by default (#define MAG_MALLAMA_2018 1,
// #define MAG_MOON_VREIJS 1 at swecl.c#L3749-L3750).

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Apparent-phenomena finder for planets. Mirrors the C entry points
/// <c>swe_pheno</c> (TT, swecl.c#L3791) and <c>swe_pheno_ut</c> (UT,
/// swecl.c#L4114). Returns phase angle, phase fraction, elongation,
/// apparent diameter, apparent magnitude, and (for the Moon) horizontal
/// parallax. Phase-1 implementation: covers the geometric channels plus
/// the Mallama-2018 + Vreijs apparent magnitudes for Sun, Moon, Mercury,
/// Venus, Mars, Jupiter, Saturn, Uranus, and Neptune. Asteroid magnitudes
/// (Bowell H/G phase function) and fictitious bodies are deferred.
/// </summary>
public sealed class PlanetaryPhenomenaService
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;
    private const double AuMeters = AstronomicalConstants.AstronomicalUnitMeters;

    private readonly BodyService _body;
    private readonly CalendarService _calendar;

    /// <summary>
    /// Constructs the service over a body service and a calendar
    /// service. Both are required.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Either <paramref name="body"/> or <paramref name="calendar"/>
    /// is <see langword="null"/>.
    /// </exception>
    public PlanetaryPhenomenaService(BodyService body, CalendarService calendar)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    /// <summary>UT entry point. Mirrors <c>swe_pheno_ut</c> (swecl.c#L4114).</summary>
    public PlanetaryAttributes ComputeUt(JulianDay jdUt, CelestialBody body, EphemerisFlags flags, ObserverLocation? observer = null)
    {
        var dt = _calendar.DeltaT(jdUt);
        return Compute(new JulianDay(jdUt.Value + dt), body, flags, observer);
    }

    /// <summary>TT entry point. Mirrors <c>swe_pheno</c> (swecl.c#L3791).</summary>
    public PlanetaryAttributes Compute(JulianDay jdEt, CelestialBody body, EphemerisFlags flags, ObserverLocation? observer = null)
    {
        // Mask the input flags down to the set that swe_pheno honours
        // (swecl.c#L3811-L3817). Strip JplHorizons bits if present.
        flags &= EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph
            | EphemerisFlags.TruePosition | EphemerisFlags.J2000Equinox
            | EphemerisFlags.NoNutation | EphemerisFlags.NoGravDeflection
            | EphemerisFlags.NoAberration | EphemerisFlags.Topocentric;

        // Heliocentric flag set for the planet at retarded time (swecl.c#L3818-L3823).
        var iflagp = (flags & ~EphemerisFlags.Topocentric) | EphemerisFlags.Heliocentric;

        // Geocentric planet — cartesian for unit-vector dot products.
        var xx = ComputeCartesian(body, jdEt, flags, observer);
        // Geocentric planet — polar (lon, lat, r). The C call passes plain flags and
        // gets ecliptic-of-date polar; we mirror that.
        var lbr = ComputePolar(body, jdEt, flags, observer);

        // Phase angle / phase fraction — non-Sun/Earth/node bodies.
        double phaseAngleDeg = 0.0;
        double phaseFraction = 0.0;
        double dt = lbr.r * AstronomicalConstants.LightTimeAuPerDay;
        var lbr2 = (lon: 0.0, lat: 0.0, r: 0.0);
        if (HasPhase(body))
        {
            if ((flags & EphemerisFlags.TruePosition) != 0) dt = 0.0;
            // Heliocentric planet at t-dt.
            var xx2 = ComputeCartesian(body, new JulianDay(jdEt.Value - dt), iflagp, observer);
            lbr2 = ComputePolar(body, new JulianDay(jdEt.Value - dt), iflagp, observer);
            phaseAngleDeg = System.Math.Acos(DotProductUnit(xx, xx2)) * RadToDeg;
            phaseFraction = (1.0 + System.Math.Cos(phaseAngleDeg * DegToRad)) / 2.0;
        }

        // Apparent diameter (degrees) — swecl.c#L3877-L3887.
        var ipl = (int)body;
        var dd = ipl < PlanetDiameters.Meters.Length ? PlanetDiameters.Meters[ipl] : 0.0;
        double appDiamDeg;
        if (lbr.r < dd / 2.0 / AuMeters)
            appDiamDeg = 180.0;
        else
            appDiamDeg = System.Math.Asin(dd / 2.0 / AuMeters / lbr.r) * 2.0 * RadToDeg;

        // Apparent magnitude — Mallama 2018 / Vreijs (swecl.c#L3891-L4056).
        double mag = ComputeMagnitude(body, lbr.r, lbr2.r, lbr.lon, lbr.lat, lbr2.lon, lbr2.lat, phaseAngleDeg, appDiamDeg, jdEt, dt);

        // Elongation (Sun-Earth-planet angle) — swecl.c#L4058-L4067.
        double elongationDeg = 0.0;
        if (body != CelestialBody.Sun && body != CelestialBody.Earth)
        {
            var xxSun = ComputeCartesian(CelestialBody.Sun, jdEt, flags, observer);
            elongationDeg = System.Math.Acos(DotProductUnit(xx, xxSun)) * RadToDeg;
        }

        // Horizontal parallax (Moon only).
        double horParallaxDeg = 0.0;
        if (body == CelestialBody.Moon)
        {
            // Geocentric horizontal parallax: asin(R_earth / d).
            // Distance comes from the equatorial-radians query (swecl.c#L4073-L4077).
            // For our purposes lbr.r is the same value (planet's distance in AU).
            var sinhp = AstronomicalConstants.EarthRadiusMeters / lbr.r / AuMeters;
            horParallaxDeg = System.Math.Asin(sinhp) * RadToDeg;
            // Topocentric variant (swecl.c#L4078-L4084) is deferred — caller can request via observer.
        }

        return new PlanetaryAttributes(
            phaseAngleDeg, phaseFraction, elongationDeg, appDiamDeg, mag, horParallaxDeg);
    }

    private static bool HasPhase(CelestialBody body)
        => body != CelestialBody.Sun
            && body != CelestialBody.Earth
            && body != CelestialBody.MeanNode
            && body != CelestialBody.TrueNode
            && body != CelestialBody.MeanApogee
            && body != CelestialBody.OsculatingApogee;

    /// <summary>
    /// Apparent magnitude per body using the formulas the C library defaults to
    /// (Mallama 2018 for Mercury..Neptune; Vreijs for the Moon; the Sun's
    /// magnitude scales with apparent vs mean disc area).
    /// </summary>
    private static double ComputeMagnitude(
        CelestialBody body,
        double rGeoAu,
        double rHelioAu,
        double geoLonDeg,
        double geoLatDeg,
        double helioLonDeg,
        double helioLatDeg,
        double phaseAngleDeg,
        double appDiamDeg,
        JulianDay jdEt,
        double dt)
    {
        switch (body)
        {
            case CelestialBody.Sun:
            {
                var dSun = PlanetDiameters.Meters[(int)CelestialBody.Sun];
                var meanDiamDeg = System.Math.Asin(dSun / 2.0 / AuMeters) * 2.0 * RadToDeg;
                var fac = appDiamDeg / meanDiamDeg;
                fac *= fac;
                return -26.86 - 2.5 * System.Math.Log10(fac);
            }
            case CelestialBody.Moon:
            {
                // Vreijs: dual-branch formula stitching Allen below 147.14° and
                // Samaha cube above (swecl.c#L3898-L3914).
                var a = phaseAngleDeg;
                double mag;
                if (a <= 147.1385465)
                {
                    mag = -21.62 + 0.026 * System.Math.Abs(a) + 0.000000004 * System.Math.Pow(a, 4);
                }
                else
                {
                    mag = -4.5444 - 2.5 * System.Math.Log10(System.Math.Pow(180.0 - a, 3));
                }
                mag += 5.0 * System.Math.Log10(rGeoAu * rHelioAu * AuMeters / AstronomicalConstants.EarthRadiusMeters);
                return mag;
            }
            case CelestialBody.Mercury:
            {
                // swecl.c#L3923-L3927.
                double a = phaseAngleDeg;
                double a2 = a * a, a3 = a2 * a, a4 = a3 * a, a5 = a4 * a, a6 = a5 * a;
                double mag = -0.613 + a * 6.3280E-02 - a2 * 1.6336E-03 + a3 * 3.3644E-05
                    - a4 * 3.4265E-07 + a5 * 1.6893E-09 - a6 * 3.0334E-12;
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                return mag;
            }
            case CelestialBody.Venus:
            {
                double a = phaseAngleDeg;
                double a2 = a * a, a3 = a2 * a, a4 = a3 * a;
                double mag;
                if (a <= 163.7)
                    mag = -4.384 - a * 1.044E-03 + a2 * 3.687E-04 - a3 * 2.814E-06 + a4 * 8.938E-09;
                else
                    mag = 236.05828 - a * 2.81914E+00 + a2 * 8.39034E-03;
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                return mag;
            }
            case CelestialBody.Mars:
            {
                double a = phaseAngleDeg;
                double a2 = a * a;
                double mag;
                if (a <= 50.0)
                    mag = -1.601 + a * 0.02267 - a2 * 0.0001302;
                else
                    mag = -0.367 - a * 0.02573 + a2 * 0.0003445;
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                return mag;
            }
            case CelestialBody.Jupiter:
            {
                double a = phaseAngleDeg;
                double a2 = a * a;
                double mag = -9.395 - a * 3.7E-04 + a2 * 6.16E-04;
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                return mag;
            }
            case CelestialBody.Saturn:
            {
                // Mallama 2018 (swecl.c#L3963-L3983).
                double a = phaseAngleDeg;
                var T = (jdEt.Value - dt - AstronomicalConstants.J2000) / 36525.0;
                var inRad = (28.075216 - 0.012998 * T + 0.000004 * T * T) * DegToRad;
                var omRad = (169.508470 + 1.394681 * T + 0.000412 * T * T) * DegToRad;
                var lonRad = geoLonDeg * DegToRad;
                var latRad = geoLatDeg * DegToRad;
                var sinB1 = System.Math.Sin(inRad) * System.Math.Cos(latRad)
                    * System.Math.Sin(lonRad - omRad)
                    - System.Math.Cos(inRad) * System.Math.Sin(latRad);
                var helioLonRad = helioLonDeg * DegToRad;
                var helioLatRad = helioLatDeg * DegToRad;
                var sinB2 = System.Math.Sin(inRad) * System.Math.Cos(helioLatRad)
                    * System.Math.Sin(helioLonRad - omRad)
                    - System.Math.Cos(inRad) * System.Math.Sin(helioLatRad);
                // C averages the two sin-of-tilts in the asin domain (swecl.c#L3981).
                var sinB = System.Math.Abs(System.Math.Sin(
                    (System.Math.Asin(sinB1) + System.Math.Asin(sinB2)) / 2.0));
                double mag = -8.914 - 1.825 * sinB + 0.026 * a
                    - 0.378 * sinB * System.Math.Pow(System.Math.E, -2.25 * a);
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                return mag;
            }
            case CelestialBody.Uranus:
            {
                double a = phaseAngleDeg;
                double a2 = a * a;
                double mag = -7.110 + a * 6.587E-3 + a2 * 1.045E-4;
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                mag -= 0.05;
                return mag;
            }
            case CelestialBody.Neptune:
            {
                // swecl.c#L3996-L4005.
                double mag;
                if (jdEt.Value < 2_444_239.5)
                    mag = -6.89;
                else if (jdEt.Value <= 2_451_544.5)
                    mag = -6.89 - 0.0055 * (jdEt.Value - 2_444_239.5) / 365.25;
                else
                    mag = -7.00;
                mag += 5.0 * System.Math.Log10(rHelioAu * rGeoAu);
                return mag;
            }
            default:
                return 0.0;
        }
    }

    /// <summary>
    /// Geocentric or topocentric body position in cartesian (J2000-equator-of-source-frame
    /// or ecliptic-of-date depending on the C "SEFLG_XYZ" branch). We use
    /// the body service with <see cref="EphemerisFlags.Cartesian"/> set so the
    /// returned <see cref="BodyState.Position"/> is cartesian, then read out the X/Y/Z.
    /// The values are unit-normalised before the dot-product call so the
    /// cartesian frame label is irrelevant to the angle result.
    /// </summary>
    private (double x, double y, double z) ComputeCartesian(
        CelestialBody body, JulianDay jdEt, EphemerisFlags flags, ObserverLocation? observer)
    {
        var bs = _body.Compute(body, jdEt, flags | EphemerisFlags.Cartesian, observer);
        return (bs.Position.X, bs.Position.Y, bs.Position.Z);
    }

    private (double lon, double lat, double r) ComputePolar(
        CelestialBody body, JulianDay jdEt, EphemerisFlags flags, ObserverLocation? observer)
    {
        // BodyService returns cartesian internally; convert to polar.
        var bs = _body.Compute(body, jdEt, flags & ~EphemerisFlags.Cartesian, observer);
        Span<double> cart = stackalloc double[6];
        cart[0] = bs.Position.X; cart[1] = bs.Position.Y; cart[2] = bs.Position.Z;
        cart[3] = bs.Velocity.X; cart[4] = bs.Velocity.Y; cart[5] = bs.Velocity.Z;
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);
        return (polar[0] * RadToDeg, polar[1] * RadToDeg, polar[2]);
    }

    private static double DotProductUnit((double x, double y, double z) a, (double x, double y, double z) b)
    {
        var ra = System.Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);
        var rb = System.Math.Sqrt(b.x * b.x + b.y * b.y + b.z * b.z);
        if (ra == 0.0 || rb == 0.0) return 0.0;
        var dot = (a.x * b.x + a.y * b.y + a.z * b.z) / (ra * rb);
        // Numerical safety: clamp into [-1, 1] before acos.
        if (dot > 1.0) dot = 1.0;
        else if (dot < -1.0) dot = -1.0;
        return dot;
    }
}
