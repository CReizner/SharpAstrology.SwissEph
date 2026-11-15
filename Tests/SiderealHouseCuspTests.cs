using SharpAstrology.Enums;
using SharpAstrology.Interfaces;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.Utility;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

/// <summary>
/// House cusps and axes follow the same rule as the bodies: sidereal is tropical minus ayanamsa.
/// The one exception is <see cref="HouseSystems.WholeSign"/>. Whole sign houses are defined over
/// the boundaries of the zodiac, so the sidereal path puts its cusps on sidereal boundaries
/// instead of on the tropical ones shifted by the ayanamsa. That is the whole point of computing
/// a chart sidereally rather than shifting a tropical one afterwards.
/// </summary>
public class SiderealHouseCuspTests
{
    private const double OneArcsecond = 1.0 / 3600.0;
    private const double Latitude = 51.0;
    private const double Longitude = 11.0;

    private static readonly DateTime ReferenceTime = new(1988, 9, 4, 1, 15, 0, DateTimeKind.Utc);

    /// <summary>
    /// The house systems that are not anchored to the zodiac. Their cusps come out of the
    /// geometry of the place and the moment, and the sidereal step only shifts the result.
    /// </summary>
    public static TheoryData<HouseSystems> ZodiacFreeSystems() => new()
    {
        HouseSystems.Placidus, HouseSystems.Koch, HouseSystems.Campanus,
        HouseSystems.Regiomontanus, HouseSystems.Porphyrius, HouseSystems.Equal,
    };

    [Theory]
    [MemberData(nameof(ZodiacFreeSystems))]
    public void SiderealCuspsAreTropicalMinusAyanamsa(HouseSystems houseSystem)
    {
        using var eph = CreateContext();
        var ayanamsa = eph.Ayanamsa(ReferenceTime);
        var tropical = eph.HouseCuspPositions(ReferenceTime, Latitude, Longitude, houseSystem);
        var sidereal = eph.HouseCuspPositions(ReferenceTime, Latitude, Longitude, houseSystem,
            EphCalculationMode.Sidereal);

        foreach (var house in tropical.HouseCusps.Keys)
        {
            var expected = AstrologyUtility.SubtractDegree(tropical.HouseCusps[house], ayanamsa);
            Assert.Equal(0.0, AstrologyUtility.AngleDifference(sidereal.HouseCusps[house], expected), OneArcsecond);
        }

        foreach (var direction in new[] { Cross.Asc, Cross.Dc, Cross.Mc, Cross.Ic })
        {
            var expected = AstrologyUtility.SubtractDegree(tropical.Cross[direction], ayanamsa);
            Assert.Equal(0.0, AstrologyUtility.AngleDifference(sidereal.Cross[direction], expected), OneArcsecond);
        }
    }

    /// <summary>
    /// The documented exception. A tropical whole sign chart has its cusps on tropical sign
    /// boundaries, and taking the ayanamsa off afterwards leaves them somewhere inside a
    /// constellation. The sidereal path puts them on the boundaries where they belong.
    /// </summary>
    [Fact]
    public void SiderealWholeSignCuspsSitOnZodiacBoundaries()
    {
        using var eph = CreateContext();
        var sidereal = eph.HouseCuspPositions(ReferenceTime, Latitude, Longitude,
            HouseSystems.WholeSign, EphCalculationMode.Sidereal);

        foreach (var cusp in sidereal.HouseCusps.Values)
        {
            Assert.Equal(0.0, cusp % 30.0, OneArcsecond);
        }
    }

    private static IEphemerides CreateContext()
        => new SharpAstrology.Ephemerides.SwissEphemerides(
            new EphemerisContextBuilder()
                .UseSwissEphFiles(EphePath.Resolve())
                .UseSiderealMode(SiderealMode.Lahiri)
                .Build(),
            ownsContext: true);
}
