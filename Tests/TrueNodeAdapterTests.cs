using SharpAstrology.Enums;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;
using Xunit;
using Adapter = SharpAstrology.Ephemerides.SwissEphemerides;

namespace SharpAstrology.SwissEph.Tests;

public class TrueNodeAdapterTests
{
    [Theory]
    [InlineData(EphCalculationMode.Tropic)]
    [InlineData(EphCalculationMode.Sidereal)]
    public void AdapterUsesApparentNorthNodeAndAntipodalSouthNode(EphCalculationMode mode)
    {
        using var context = new EphemerisContextBuilder()
            .UseSwissEphFiles(EphePath.Resolve()).DisableMoshier()
            .UseSiderealMode(SiderealMode.Lahiri).Build();
        using var adapter = new Adapter(context);
        var time = new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var flags = EphemerisFlags.SwissEph | EphemerisFlags.Speed;
        if (mode == EphCalculationMode.Sidereal) flags |= EphemerisFlags.Sidereal;
        // Keep the adapter's current time conversion separate from this
        // regression: the service's TT calculations have independent C fixtures.
        var jd = context.Calendar.UtcToJulianDay(time);
        var state = context.Bodies.ComputeUt(CelestialBody.TrueNode, jd, flags);
        Span<double> cart = [state.Position.X, state.Position.Y, state.Position.Z,
            state.Velocity.X, state.Velocity.Y, state.Velocity.Z];
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);
        var north = adapter.PlanetsPosition(Planets.NorthNode, time, mode);
        var south = adapter.PlanetsPosition(Planets.SouthNode, time, mode);
        var longitude = (polar[0] * 180 / Math.PI + 360) % 360;
        Assert.Equal(longitude, north.Longitude, 1e-10);
        Assert.Equal(polar[1] * 180 / Math.PI, north.Latitude, 1e-10);
        Assert.Equal(polar[2], north.Distance, 1e-14);
        Assert.Equal(polar[3] * 180 / Math.PI, north.SpeedLongitude, 1e-10);
        Assert.Equal(polar[4] * 180 / Math.PI, north.SpeedLatitude, 1e-10);
        Assert.Equal(polar[5], north.SpeedDistance, 1e-14);
        Assert.Equal((north.Longitude + 180) % 360, south.Longitude, 1e-10);
        Assert.Equal(-north.Latitude, south.Latitude);
        Assert.Equal(north.Distance, south.Distance);
        Assert.Equal(north.SpeedLongitude, south.SpeedLongitude);
        Assert.Equal(-north.SpeedLatitude, south.SpeedLatitude);
        Assert.Equal(north.SpeedDistance, south.SpeedDistance);
    }

    [Theory]
    [InlineData(CelestialBody.TrueNode)]
    [InlineData(CelestialBody.OsculatingApogee)]
    public void MoshierOsculatingPointsDoNotApplyLightTime(CelestialBody body)
    {
        using var context = new EphemerisContextBuilder().Build();
        var jd = new JulianDay(2460680.5);
        const EphemerisFlags flags = EphemerisFlags.MoshierEph | EphemerisFlags.Speed;
        var apparent = context.Bodies.Compute(body, jd, flags);
        var geometric = context.Bodies.Compute(body, jd, flags | EphemerisFlags.TruePosition);
        Assert.Equal(EphemerisSource.Moshier, apparent.Source);
        Assert.Equal(geometric.Position, apparent.Position);
        Assert.Equal(geometric.Velocity, apparent.Velocity);
    }
}
