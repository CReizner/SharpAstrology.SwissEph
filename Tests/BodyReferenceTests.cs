using System.Text.Json;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

public class BodyReferenceTests
{
    public static IEnumerable<object[]> OsculatingCases() => Cases("osculating");
    public static IEnumerable<object[]> PositionOnlyCases() => Cases("positionOnly");
    public static IEnumerable<object[]> MotionCases() => Cases("motion");

    private static IEnumerable<object[]> Cases(string group)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "BodyReference.json")));
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("group").GetString() != group) continue;
            yield return new object[]
            {
                (CelestialBody)row.GetProperty("body").GetInt32(),
                row.GetProperty("tt").GetDouble(),
                (EphemerisFlags)row.GetProperty("flags").GetInt32(),
                row.GetProperty("vector").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
            };
        }
    }

    [Theory]
    [MemberData(nameof(OsculatingCases))]
    public void OsculatingPositionAndVelocityMatchC(CelestialBody body, double tt, EphemerisFlags flags, double[] expected)
    {
        using var context = CreateContext(flags);
        var state = context.Bodies.Compute(body, new JulianDay(tt), flags);
        Assert.Equal((flags & EphemerisFlags.SwissEph) != 0 ? EphemerisSource.SwissEph : EphemerisSource.Moshier, state.Source);
        // AU and AU/day. Apogee retains a few metres/day of existing speed
        // residual, also present for the unchanged TruePosition path.
        var velocityTolerance = body == CelestialBody.OsculatingApogee ? 2e-11 : 1e-12;
        AssertVector(state, expected, 1e-13, velocityTolerance, $"{body}, JD {tt:R}, {flags}");
        if ((flags & (EphemerisFlags.Speed | EphemerisFlags.Speed3)) == 0) Assert.Equal(Vec3.Zero, state.Velocity);
        var j2000 = (flags & EphemerisFlags.J2000Equinox) != 0 && (flags & EphemerisFlags.Sidereal) == 0;
        var expectedFrame = (flags & EphemerisFlags.Equatorial) != 0
            ? j2000 ? BodyStateFrame.GeocentricJ2000Equator : BodyStateFrame.GeocentricEquatorOfDate
            : j2000 ? BodyStateFrame.GeocentricJ2000Ecliptic : BodyStateFrame.GeocentricEclipticOfDate;
        Assert.Equal(expectedFrame, state.Frame);
    }

    [Theory]
    [MemberData(nameof(MotionCases))]
    public void PlanetPositionAndVelocityMatchC(CelestialBody body, double tt, EphemerisFlags flags, double[] expected)
    {
        using var context = CreateContext(flags);
        var state = context.Bodies.Compute(body, new JulianDay(tt), flags, new ObserverLocation(13.405, 52.52, 34));
        Assert.Equal((flags & EphemerisFlags.SwissEph) != 0 ? EphemerisSource.SwissEph : EphemerisSource.Moshier, state.Source);
        var speed = (flags & EphemerisFlags.Speed) != 0;
        var sampled = !speed && (flags & EphemerisFlags.Speed3) != 0
            || speed && (flags & EphemerisFlags.Topocentric) != 0 && (flags & EphemerisFlags.NoAberration) == 0;
        // Cartesian SPEED3 divides differences between distant planet
        // positions by 0.0001 day. Last-bit rounding in AU is amplified;
        // the normal analytic velocity path uses a much tighter bound.
        AssertVector(state, expected, 1e-13, sampled ? 1e-9 : 1e-12, $"{body}, JD {tt:R}, {flags}");
    }

    [Theory]
    [MemberData(nameof(PositionOnlyCases))]
    public void PlanetPositionWithoutSpeedMatchesC(CelestialBody body, double tt, EphemerisFlags flags, double[] expected)
    {
        using var context = CreateContext(flags);
        var observer = new ObserverLocation(13.405, 52.52, 34);
        var state = context.Bodies.Compute(body, new JulianDay(tt), flags, observer);
        var moshier = (flags & EphemerisFlags.MoshierEph) != 0;
        Assert.Equal(moshier ? EphemerisSource.Moshier : EphemerisSource.SwissEph, state.Source);
        // These bounds retain the metre-scale Cartesian residuals already
        // present with Speed, including for the distant outer planets.
        AssertVector(state, expected, moshier ? 2e-11 : 1e-11, 0, $"{body}, JD {tt:R}, {flags}");
        Assert.Equal(Vec3.Zero, state.Velocity);

        // Requesting velocity must not change the apparent position.
        var withSpeed = context.Bodies.Compute(body, new JulianDay(tt), flags | EphemerisFlags.Speed, observer);
        Assert.True((state.Position - withSpeed.Position).Length < 1e-13,
            $"{body}, JD {tt:R}, {flags}: position depends on Speed");
    }

    private static EphemerisContext CreateContext(EphemerisFlags flags)
    {
        var builder = new EphemerisContextBuilder().UseSiderealMode(SiderealMode.Lahiri);
        if ((flags & EphemerisFlags.SwissEph) != 0)
            builder.UseSwissEphFiles(EphePath.Resolve()).DisableMoshier();
        return builder.Build();
    }

    private static void AssertVector(BodyState state, double[] expected, double positionTolerance, double velocityTolerance, string label)
    {
        double[] actual = [state.Position.X, state.Position.Y, state.Position.Z,
            state.Velocity.X, state.Velocity.Y, state.Velocity.Z];
        for (var i = 0; i < 6; i++)
        {
            var tolerance = i < 3 ? positionTolerance : velocityTolerance;
            Assert.True(Math.Abs(actual[i] - expected[i]) <= tolerance,
                $"{label}, component {i}: expected {expected[i]:R}, actual {actual[i]:R}, tolerance {tolerance:R}");
        }
    }
}
