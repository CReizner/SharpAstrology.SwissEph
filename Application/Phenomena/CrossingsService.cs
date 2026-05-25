// Ported from swisseph-master/sweph.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris, sweph.c#L8321 ff.):
//   SolCross          — swe_solcross           (sweph.c#L8321)
//   SolCrossUt        — swe_solcross_ut        (sweph.c#L8355)
//   MoonCross         — swe_mooncross          (sweph.c#L8389)
//   MoonCrossUt       — swe_mooncross_ut       (sweph.c#L8425)
//   MoonCrossNode     — swe_mooncross_node     (sweph.c#L8456)
//   MoonCrossNodeUt   — swe_mooncross_node_ut  (sweph.c#L8493)
//   HelioCross        — swe_helio_cross        (sweph.c#L8533)
//   HelioCrossUt      — swe_helio_cross_ut     (sweph.c#L8579)
//   CrossPrecisionDeg — CROSS_PRECISION = 1/3600000 (sweph.c#L8308)

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Direction selector for <see cref="CrossingsService.HelioCross"/>:
/// <see cref="Forward"/> seeks the next crossing forward in time,
/// <see cref="Backward"/> the previous one.
/// </summary>
public enum CrossingDirection
{
    /// <summary>Forward in time (next crossing).</summary>
    Forward = 1,
    /// <summary>Backward in time (previous crossing).</summary>
    Backward = -1,
}

/// <summary>
/// Heliocentric and geocentric crossing-time finders. Returns the Julian Day of
/// the next crossing, refined to better than 1 milliarc-second. Each method
/// estimates the crossing time from the body's mean motion and then refines it
/// with the current speed reading until the longitude residual is below
/// <see cref="CrossPrecisionDeg"/>.
/// </summary>
public sealed class CrossingsService
{
    /// <summary>Convergence tolerance for the crossing solver: 1 mas (1/3 600 000 deg).</summary>
    public const double CrossPrecisionDeg = 1.0 / 3_600_000.0;

    private const double MeanSolarSpeedDegPerDay = 360.0 / 365.24;
    private const double MeanLunarSpeedDegPerDay = 360.0 / 27.32;
    private const double ChironMeanSpeedDegPerDay = 0.01971;

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
    public CrossingsService(BodyService body, CalendarService calendar)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    /// <summary>Sun-crossing in TT. Mirrors <c>swe_solcross</c> (sweph.c#L8321).</summary>
    public JulianDay SolCross(double targetEclipticLonDeg, JulianDay jdEt, EphemerisFlags flags)
        => CrossLongitude(CelestialBody.Sun, targetEclipticLonDeg, jdEt, flags, isUt: false, MeanSolarSpeedDegPerDay);

    /// <summary>Sun-crossing in UT. Mirrors <c>swe_solcross_ut</c> (sweph.c#L8355).</summary>
    public JulianDay SolCrossUt(double targetEclipticLonDeg, JulianDay jdUt, EphemerisFlags flags)
        => CrossLongitude(CelestialBody.Sun, targetEclipticLonDeg, jdUt, flags, isUt: true, MeanSolarSpeedDegPerDay);

    /// <summary>Moon-crossing in TT. Mirrors <c>swe_mooncross</c> (sweph.c#L8389).</summary>
    public JulianDay MoonCross(double targetEclipticLonDeg, JulianDay jdEt, EphemerisFlags flags)
        => CrossLongitude(CelestialBody.Moon, targetEclipticLonDeg, jdEt, flags, isUt: false, MeanLunarSpeedDegPerDay);

    /// <summary>Moon-crossing in UT. Mirrors <c>swe_mooncross_ut</c> (sweph.c#L8425).</summary>
    public JulianDay MoonCrossUt(double targetEclipticLonDeg, JulianDay jdUt, EphemerisFlags flags)
        => CrossLongitude(CelestialBody.Moon, targetEclipticLonDeg, jdUt, flags, isUt: true, MeanLunarSpeedDegPerDay);

    /// <summary>
    /// Next Moon-node crossing (zero ecliptic latitude) in TT. Returns crossing JD
    /// plus the Moon's ecliptic longitude/latitude at that instant. Mirrors
    /// <c>swe_mooncross_node</c> (sweph.c#L8456).
    /// </summary>
    public (JulianDay jd, double LonDeg, double LatDeg) MoonCrossNode(JulianDay jdEt, EphemerisFlags flags)
        => CrossNode(jdEt, flags, isUt: false);

    /// <summary>
    /// UT variant of <see cref="MoonCrossNode"/>. Mirrors
    /// <c>swe_mooncross_node_ut</c> (sweph.c#L8493).
    /// </summary>
    public (JulianDay jd, double LonDeg, double LatDeg) MoonCrossNodeUt(JulianDay jdUt, EphemerisFlags flags)
        => CrossNode(jdUt, flags, isUt: true);

    /// <summary>
    /// Heliocentric planet-crossing in TT. Mirrors <c>swe_helio_cross</c>
    /// (sweph.c#L8533).
    /// </summary>
    /// <exception cref="System.NotSupportedException">
    /// Thrown for Sun, Moon, lunar nodes / apogees and asteroid IDs ≥ <c>SE_NPLANETS</c>.
    /// </exception>
    public JulianDay HelioCross(CelestialBody body, double targetEclipticLonDeg, JulianDay jdEt, EphemerisFlags flags, CrossingDirection direction)
        => CrossHelio(body, targetEclipticLonDeg, jdEt, flags, isUt: false, direction);

    /// <summary>
    /// UT variant of <see cref="HelioCross"/>. Mirrors <c>swe_helio_cross_ut</c>
    /// (sweph.c#L8579).
    /// </summary>
    public JulianDay HelioCrossUt(CelestialBody body, double targetEclipticLonDeg, JulianDay jdUt, EphemerisFlags flags, CrossingDirection direction)
        => CrossHelio(body, targetEclipticLonDeg, jdUt, flags, isUt: true, direction);

    private JulianDay CrossLongitude(CelestialBody body, double target, JulianDay jdStart, EphemerisFlags flags, bool isUt, double meanSpeedDegPerDay)
    {
        var f = flags | EphemerisFlags.Speed;
        var (lon, _, speed) = ReadEclipticLonSpeed(body, jdStart, f, isUt);
        var dist = AngleMath.NormalizeDegrees(target - lon);
        var jd = jdStart.Value + dist / meanSpeedDegPerDay;
        for (var i = 0; i < MaxIterations; i++)
        {
            (lon, _, speed) = ReadEclipticLonSpeed(body, new JulianDay(jd), f, isUt);
            var d = AngleMath.DifferenceDegreesSigned(target, lon);
            jd += d / speed;
            if (System.Math.Abs(d) < CrossPrecisionDeg) return new JulianDay(jd);
        }
        return new JulianDay(jd);
    }

    private (JulianDay jd, double LonDeg, double LatDeg) CrossNode(JulianDay jdStart, EphemerisFlags flags, bool isUt)
    {
        var f = flags | EphemerisFlags.Speed;
        var (_, lat, _) = ReadEclipticLonLatSpeed(CelestialBody.Moon, jdStart, f, isUt, out _, out _);
        var prevLat = lat;
        var jd = jdStart.Value + 1.0;
        double lon = 0, latSpeed = 0;
        for (var i = 0; i < 60; i++) // up to 60 days to find sign change
        {
            (_, lat, _) = ReadEclipticLonLatSpeed(CelestialBody.Moon, new JulianDay(jd), f, isUt, out lon, out latSpeed);
            if ((lat >= 0 && prevLat < 0) || (lat < 0 && prevLat > 0)) break;
            jd += 1.0;
        }
        for (var i = 0; i < MaxIterations; i++)
        {
            jd -= lat / latSpeed;
            (_, lat, _) = ReadEclipticLonLatSpeed(CelestialBody.Moon, new JulianDay(jd), f, isUt, out lon, out latSpeed);
            if (System.Math.Abs(lat) < CrossPrecisionDeg) return (new JulianDay(jd), lon, lat);
        }
        return (new JulianDay(jd), lon, lat);
    }

    private JulianDay CrossHelio(CelestialBody body, double target, JulianDay jdStart, EphemerisFlags flags, bool isUt, CrossingDirection direction)
    {
        if (body == CelestialBody.Sun
            || body == CelestialBody.Moon
            || (body >= CelestialBody.MeanNode && body <= CelestialBody.OsculatingApogee)
            || body == CelestialBody.InterpolatedApogee
            || body == CelestialBody.InterpolatedPerigee)
        {
            throw new NotSupportedException(
                $"swe_helio_cross: not possible for object {(int)body} = {body}");
        }
        var f = flags | EphemerisFlags.Speed | EphemerisFlags.Heliocentric;
        var (lon, _, speed) = ReadEclipticLonSpeed(body, jdStart, f, isUt);
        var meanSpeed = body == CelestialBody.Chiron ? ChironMeanSpeedDegPerDay : speed;
        var dist = AngleMath.NormalizeDegrees(target - lon);
        var jd = direction == CrossingDirection.Forward
            ? jdStart.Value + dist / meanSpeed
            : jdStart.Value - (360.0 - dist) / meanSpeed;
        for (var i = 0; i < MaxIterations; i++)
        {
            (lon, _, speed) = ReadEclipticLonSpeed(body, new JulianDay(jd), f, isUt);
            var d = AngleMath.DifferenceDegreesSigned(target, lon);
            jd += d / speed;
            if (System.Math.Abs(d) < CrossPrecisionDeg) return new JulianDay(jd);
        }
        return new JulianDay(jd);
    }

    private const int MaxIterations = 30;

    private (double LonDeg, double LatDeg, double SpeedDegPerDay) ReadEclipticLonSpeed(
        CelestialBody body, JulianDay jd, EphemerisFlags flags, bool isUt)
    {
        return ReadEclipticLonLatSpeed(body, jd, flags, isUt, out _, out _);
    }

    private (double LonDeg, double LatDeg, double SpeedDegPerDay) ReadEclipticLonLatSpeed(
        CelestialBody body, JulianDay jd, EphemerisFlags flags, bool isUt, out double lonOut, out double latSpeedOut)
    {
        var bs = isUt ? _body.ComputeUt(body, jd, flags) : _body.Compute(body, jd, flags);
        Span<double> cart = stackalloc double[6];
        Span<double> polar = stackalloc double[6];
        cart[0] = bs.Position.X; cart[1] = bs.Position.Y; cart[2] = bs.Position.Z;
        cart[3] = bs.Velocity.X; cart[4] = bs.Velocity.Y; cart[5] = bs.Velocity.Z;
        Polar.CartesianToPolarWithSpeed(cart, polar);
        lonOut = polar[0] * AstronomicalConstants.RadToDeg;
        var latDeg = polar[1] * AstronomicalConstants.RadToDeg;
        var lonSpeedDeg = polar[3] * AstronomicalConstants.RadToDeg;
        latSpeedOut = polar[4] * AstronomicalConstants.RadToDeg;
        return (lonOut, latDeg, lonSpeedDeg);
    }
}
