using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

public class FrameBiasTests
{
    [Theory]
    [InlineData(EphemerisSource.SwissEph, 402, false, false, false)]
    [InlineData(EphemerisSource.SwissEph, 403, false, false, true)]
    [InlineData(EphemerisSource.SwissEph, 441, false, false, true)]
    [InlineData(EphemerisSource.SwissEph, 0, false, false, true)]
    [InlineData(EphemerisSource.SwissEph, 441, true, false, false)]
    [InlineData(EphemerisSource.SwissEph, 441, false, true, false)]
    [InlineData(EphemerisSource.Jpl, 402, false, false, false)]
    [InlineData(EphemerisSource.Jpl, 441, false, false, true)]
    [InlineData(EphemerisSource.Jpl, 0, false, false, true)]
    [InlineData(EphemerisSource.Moshier, 402, false, false, true)]
    [InlineData(EphemerisSource.Moshier, 0, true, false, false)]
    public void SourceSeriesAndControlsSelectTheCFrameRotation(
        EphemerisSource sourceKind, int deNumber, bool icrs, bool disableBias, bool rotates)
    {
        var raw = new BodyState(new Vec3(1, .2, .3), new Vec3(.01, .02, .03), 0,
            sourceKind, BodyStateFrame.BarycentricJ2000Equator) { DeNumber = deNumber };
        var models = AstronomicalModelOverrides.Default with
        {
            FrameBias = disableBias ? FrameBiasModel.None : FrameBiasModel.Iau2006,
        };
        var bodies = new BodyService(new SourceRouter([new StaticSource(raw)]), new CalendarService(), models);
        // Request Swiss even for the Moshier source: this exercises fallback.
        var flags = sourceKind == EphemerisSource.Jpl ? EphemerisFlags.JplEph : EphemerisFlags.SwissEph;
        flags |= EphemerisFlags.TruePosition | EphemerisFlags.Barycentric | EphemerisFlags.Equatorial
            | EphemerisFlags.J2000Equinox | EphemerisFlags.Speed;
        if (icrs) flags |= EphemerisFlags.Icrs;
        var result = bodies.Compute(CelestialBody.Mercury, JulianDay.J2000, flags);

        // Oracle: local Swiss C 2.10.03, swi_bias([1,.2,.3,.01,.02,.03],
        // 2451545, SEFLG_SPEED, FALSE), default IAU2006 model.
        var position = rotates ? new Vec3(1.0000000100118982, .200000080702972, .29999991282500077) : raw.Position;
        var velocity = rotates ? new Vec3(.010000001001190343, .020000001699765377, .029999998533092872) : raw.Velocity;
        Assert.True((result.Position - position).Length < 1e-15);
        Assert.True((result.Velocity - velocity).Length < 1e-16);
        Assert.Equal(deNumber, result.DeNumber);
        Assert.Equal(sourceKind, result.Source);
    }

    [Theory]
    [InlineData(CelestialBody.Sun)]
    [InlineData(CelestialBody.Moon)]
    [InlineData(CelestialBody.Mercury)]
    public void SwissFileMetadataSurvivesSiderealAndObserverTransforms(CelestialBody body)
    {
        using var context = new EphemerisContextBuilder().UseSwissEphFiles(EphePath.Resolve())
            .DisableMoshier().UseSiderealMode(SiderealMode.Lahiri).Build();
        var flags = EphemerisFlags.SwissEph | EphemerisFlags.Speed
            | EphemerisFlags.Topocentric | EphemerisFlags.Sidereal;
        var result = context.Bodies.Compute(body, JulianDay.J2000, flags, new ObserverLocation(13.405, 52.52, 34));
        Assert.Equal(441, result.DeNumber); // Local test files: DE441, recorded in MoonReference.json.
    }

    private sealed class StaticSource(BodyState raw) : IBodyPositionSource
    {
        public EphemerisSource Kind => raw.Source;
        public bool CanProvide(CelestialBody body) => true;
        public BodyState Compute(CelestialBody body, JulianDay jd, EphemerisFlags flags)
            => body == CelestialBody.Mercury ? raw : raw with { Position = Vec3.Zero, Velocity = Vec3.Zero };
    }
}
