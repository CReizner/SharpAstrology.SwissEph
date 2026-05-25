// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Direction flag for <see cref="HorizontalCoordsService.ToHorizontal"/>.
/// Mirrors <c>SE_ECL2HOR</c> (=0) / <c>SE_EQU2HOR</c> (=1) at swephexp.h#L364.
/// </summary>
public enum HorizontalConversionInput
{
    /// <summary>Ecliptic input (longitude, latitude).</summary>
    FromEcliptic = 0,
    /// <summary>Equatorial input (right ascension, declination).</summary>
    FromEquatorial = 1,
}

/// <summary>
/// Direction flag for <see cref="HorizontalCoordsService.FromHorizontal"/>.
/// Mirrors <c>SE_HOR2ECL</c> (=0) / <c>SE_HOR2EQU</c> (=1) at swephexp.h#L366.
/// </summary>
public enum HorizontalConversionOutput
{
    /// <summary>Convert horizontal → ecliptic.</summary>
    ToEcliptic = 0,
    /// <summary>Convert horizontal → equatorial.</summary>
    ToEquatorial = 1,
}

/// <summary>
/// Result of <see cref="HorizontalCoordsService.ToHorizontal"/>. Mirrors
/// the three-double <c>xaz</c> output of <c>swe_azalt</c>: azimuth (from
/// south, clockwise), true altitude, apparent altitude.
/// </summary>
public readonly record struct HorizontalCoordinates(
    double AzimuthDeg,
    double TrueAltitudeDeg,
    double ApparentAltitudeDeg);

/// <summary>
/// Geographic observer position. Latitude/longitude in degrees, geometric
/// altitude in metres above mean sea level. Mirrors the
/// <c>geopos[3] = {lon, lat, alt}</c> tuple used by <c>swe_azalt</c> /
/// <c>swe_azalt_rev</c>.
/// </summary>
public readonly record struct GeographicLocation(
    double LongitudeDeg,
    double LatitudeDeg,
    double AltitudeMeters);

/// <summary>
/// Horizontal-coordinates service. Mirrors <c>swe_azalt</c> and
/// <c>swe_azalt_rev</c> (swecl.c#L2788 / #L2839): replays the C routine
/// verbatim using <see cref="CalendarService.SiderealTime(JulianDay, double?)"/>
/// for the mean GMST term, <see cref="Precession.MeanObliquity"/> +
/// <see cref="Nutation.Compute"/> for the true-obliquity term, and
/// <see cref="RefractionMath.RefracExtended"/> for the apparent-altitude
/// term. The C library's "atmospheric pressure 0 ⇒ estimate from observer
/// altitude" convention is preserved: pass
/// <see cref="EstimatePressureFromAltitude"/> for the <c>atPressMbar</c>
/// parameter to trigger the U.S. Standard-Atmosphere estimation.
/// </summary>
public sealed class HorizontalCoordsService
{
    /// <summary>Sentinel value: caller asks for pressure to be estimated from observer altitude.</summary>
    public const double EstimatePressureFromAltitude = 0.0;

    private const double RadToDeg = Domain.Constants.AstronomicalConstants.RadToDeg;
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides? _models;

    /// <summary>Constructs the service. <paramref name="models"/> is forwarded to the precession / nutation calls.</summary>
    public HorizontalCoordsService(CalendarService calendar, AstronomicalModelOverrides? models = null)
    {
        _calendar = calendar;
        _models = models;
    }

    /// <summary>
    /// Ecliptic or equatorial → horizontal. Mirrors <c>swe_azalt</c>.
    /// <paramref name="atPressMbar"/> = 0 triggers an altitude-based estimate
    /// (as in the C source).
    /// </summary>
    public HorizontalCoordinates ToHorizontal(
        JulianDay jdUt,
        HorizontalConversionInput direction,
        GeographicLocation observer,
        double atPressMbar,
        double atTempC,
        double inputLonDeg,
        double inputLatDeg)
    {
        var (epsTrue, nutLon) = TrueObliquityAndNutationDeg(jdUt);
        var sidt = _calendar.SiderealTime(jdUt, epsTrue, nutLon);
        var armc = AngleMath.NormalizeDegrees(sidt * 15.0 + observer.LongitudeDeg);
        var ra = inputLonDeg;
        var dec = inputLatDeg;
        if (direction == HorizontalConversionInput.FromEcliptic)
            CoordinateRotation.Rotate(ref ra, ref dec, -epsTrue);

        var mdd = AngleMath.NormalizeDegrees(ra - armc);
        var lonOnAxis = AngleMath.NormalizeDegrees(mdd - 90);
        var latOnAxis = dec;
        CoordinateRotation.Rotate(ref lonOnAxis, ref latOnAxis, 90 - observer.LatitudeDeg);
        lonOnAxis = AngleMath.NormalizeDegrees(lonOnAxis + 90);
        var azimuth = 360 - lonOnAxis;
        var trueAlt = latOnAxis;

        var pressure = atPressMbar == EstimatePressureFromAltitude
            ? 1013.25 * System.Math.Pow(1 - 0.0065 * observer.AltitudeMeters / 288, 5.255)
            : atPressMbar;
        var apparentAlt = RefractionMath.RefracExtended(
            trueAlt, observer.AltitudeMeters, pressure, atTempC,
            RefractionMath.DefaultLapseRate, RefractionMath.Direction.TrueToApparent, out _);
        return new HorizontalCoordinates(azimuth, trueAlt, apparentAlt);
    }

    /// <summary>
    /// Horizontal → ecliptic or equatorial. Mirrors <c>swe_azalt_rev</c>.
    /// Output is (longitude, latitude) in degrees.
    /// </summary>
    public (double LonDeg, double LatDeg) FromHorizontal(
        JulianDay jdUt,
        HorizontalConversionOutput direction,
        GeographicLocation observer,
        double azimuthDeg,
        double trueAltitudeDeg)
    {
        var (epsTrue, nutLon) = TrueObliquityAndNutationDeg(jdUt);
        var sidt = _calendar.SiderealTime(jdUt, epsTrue, nutLon);
        var armc = AngleMath.NormalizeDegrees(sidt * 15.0 + observer.LongitudeDeg);
        var az = AngleMath.NormalizeDegrees(360 - azimuthDeg - 90);
        var alt = trueAltitudeDeg;
        CoordinateRotation.Rotate(ref az, ref alt, observer.LatitudeDeg - 90);
        var ra = AngleMath.NormalizeDegrees(az + armc + 90);
        var dec = alt;

        if (direction == HorizontalConversionOutput.ToEcliptic)
            CoordinateRotation.Rotate(ref ra, ref dec, epsTrue);
        return (ra, dec);
    }

    private (double EpsTrueDeg, double NutLonDeg) TrueObliquityAndNutationDeg(JulianDay jdUt)
    {
        var jdEt = jdUt.Value + _calendar.DeltaT(jdUt);
        var meanEpsRad = Precession.MeanObliquity(jdEt, _models);
        var nut = Nutation.Compute(jdEt, _models);
        return ((meanEpsRad + nut.DeltaEpsilonRad) * RadToDeg, nut.DeltaPsiRad * RadToDeg);
    }
}
