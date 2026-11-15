using SharpAstrology.Enums;
using SharpAstrology.Ephemerides;
using SharpAstrology.Interfaces;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.Utility;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

/// <summary>
/// The defining property of <see cref="EphCalculationMode.Sidereal"/>: for every supported
/// object the sidereal longitude is the tropical longitude minus the ayanamsa. The tolerance
/// is one arcsecond, far above the numerical noise and far below any real defect.
///
/// The lunar nodes used to fail this test. Their branches in <c>BodyService.Compute</c>
/// returned before the sidereal step, so <see cref="EphCalculationMode.Sidereal"/> had no
/// effect on Rahu and Ketu at all.
/// </summary>
public class SiderealModeTests
{
    private const double OneArcsecond = 1.0 / 3600.0;

    private static readonly Planets[] Objects =
    [
        Planets.Sun, Planets.Moon, Planets.Mercury, Planets.Venus, Planets.Mars,
        Planets.Jupiter, Planets.Saturn, Planets.Uranus, Planets.Neptune, Planets.Pluto,
        Planets.NorthNode, Planets.SouthNode, Planets.Earth,
        Planets.Chiron, Planets.Pholus, Planets.Ceres, Planets.Pallas, Planets.Juno, Planets.Vesta,
    ];

    public static TheoryData<Planets, LunarNodeStrategy, string> Cases()
    {
        var data = new TheoryData<Planets, LunarNodeStrategy, string>();
        foreach (var planet in Objects)
        foreach (var strategy in new[] { LunarNodeStrategy.TrueNode, LunarNodeStrategy.MeanNode })
        foreach (var time in new[] { "1988-09-04T01:15:00Z", "2026-07-29T12:00:00Z" })
        {
            data.Add(planet, strategy, time);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SiderealLongitudeIsTropicalMinusAyanamsa(Planets planet, LunarNodeStrategy strategy, string time)
    {
        var pointInTime = DateTime.Parse(time).ToUniversalTime();
        using var eph = CreateContext(strategy);

        var ayanamsa = eph.Ayanamsa(pointInTime);
        var tropical = eph.PlanetsPosition(planet, pointInTime).Longitude;
        var sidereal = eph.PlanetsPosition(planet, pointInTime, EphCalculationMode.Sidereal).Longitude;

        var expected = AstrologyUtility.SubtractDegree(tropical, ayanamsa);
        Assert.Equal(0.0, AstrologyUtility.AngleDifference(sidereal, expected), OneArcsecond);
    }

    /// <summary>
    /// The mode must not touch anything but the longitude. The latitude carries no ayanamsa,
    /// and the longitude speed differs by the proper motion of the ayanamsa alone, which stays
    /// well below a thousandth of a degree per day for every supported mode.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void SiderealModeLeavesLatitudeAndDistanceAlone(Planets planet, LunarNodeStrategy strategy, string time)
    {
        var pointInTime = DateTime.Parse(time).ToUniversalTime();
        using var eph = CreateContext(strategy);

        var tropical = eph.PlanetsPosition(planet, pointInTime);
        var sidereal = eph.PlanetsPosition(planet, pointInTime, EphCalculationMode.Sidereal);

        Assert.Equal(tropical.Latitude, sidereal.Latitude, 1e-9);
        Assert.Equal(tropical.Distance, sidereal.Distance, 1e-9);
        Assert.Equal(tropical.SpeedLongitude, sidereal.SpeedLongitude, 1e-3);
    }

    private static IEphemerides CreateContext(LunarNodeStrategy strategy)
        => new SharpAstrology.Ephemerides.SwissEphemerides(
            new EphemerisContextBuilder()
                .UseSwissEphFiles(EphePath.Resolve())
                .UseSiderealMode(SiderealMode.Lahiri)
                .WithLunarNodeStrategy(strategy)
                .Build(),
            ownsContext: true);
}
