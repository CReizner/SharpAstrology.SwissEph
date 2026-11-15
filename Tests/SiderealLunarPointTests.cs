using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

/// <summary>
/// The derived lunar points of <see cref="BodyService"/> that the
/// <see cref="SharpAstrology.Enums.Planets"/> vocabulary does not name. They run through the
/// same three branches as the lunar nodes, so they carried the same defect: the branch returned
/// before the sidereal step and <c>EphemerisFlags.Sidereal</c> did nothing.
/// </summary>
public class SiderealLunarPointTests
{
    private const double OneArcsecond = 1.0 / 3600.0;

    public static TheoryData<CelestialBody, string> Cases()
    {
        var data = new TheoryData<CelestialBody, string>();
        foreach (var body in new[]
                 {
                     CelestialBody.TrueNode, CelestialBody.MeanNode,
                     CelestialBody.OsculatingApogee, CelestialBody.MeanApogee,
                     CelestialBody.InterpolatedApogee, CelestialBody.InterpolatedPerigee,
                 })
        foreach (var time in new[] { "1988-09-04T01:15:00Z", "2026-07-29T12:00:00Z" })
        {
            data.Add(body, time);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SiderealLongitudeIsTropicalMinusAyanamsa(CelestialBody body, string time)
    {
        var pointInTime = DateTime.Parse(time).ToUniversalTime();
        using var ctx = new EphemerisContextBuilder()
            .UseSwissEphFiles(EphePath.Resolve())
            .UseSiderealMode(SiderealMode.Lahiri)
            .Build();

        var jdUt = ctx.Calendar.UtcToJulianDay(pointInTime);
        var ayanamsa = ctx.Sidereal.GetAyanamsaUt(jdUt);

        const EphemerisFlags baseFlags = EphemerisFlags.Speed | EphemerisFlags.SwissEph;
        var tropical = LongitudeOf(ctx.Bodies.ComputeUt(body, jdUt, baseFlags));
        var sidereal = LongitudeOf(ctx.Bodies.ComputeUt(body, jdUt, baseFlags | EphemerisFlags.Sidereal));

        var expected = Normalize(tropical - ayanamsa);
        Assert.Equal(0.0, SignedDifference(sidereal, expected), OneArcsecond);
    }

    private static double LongitudeOf(BodyState state)
        => Normalize(Math.Atan2(state.Position.Y, state.Position.X) * 180.0 / Math.PI);

    private static double Normalize(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }

    private static double SignedDifference(double a, double b)
    {
        var d = Normalize(a - b);
        return d > 180.0 ? d - 360.0 : d;
    }
}
