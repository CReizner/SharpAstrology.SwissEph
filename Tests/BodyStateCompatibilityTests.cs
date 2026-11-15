using System.Reflection;
using System.Text.Json;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

public class BodyStateCompatibilityTests
{
    [Fact]
    public void ExistingConstructorAndFiveValueDeconstructionRemainAvailable()
    {
        var state = new BodyState(new Vec3(1, 2, 3), new Vec3(4, 5, 6), 7,
            EphemerisSource.SwissEph, BodyStateFrame.GeocentricJ2000Equator)
        {
            DeNumber = 441,
        };
        var (position, velocity, distance, source, frame) = state;
        Assert.Equal(state.Position, position);
        Assert.Equal(state.Velocity, velocity);
        Assert.Equal(state.Distance, distance);
        Assert.Equal(state.Source, source);
        Assert.Equal(state.Frame, frame);
        Assert.Equal(441, (state with { Distance = 8 }).DeNumber);
    }

    [Fact]
    public void SourceMetadataIsNotPartOfThePublicPropertyOrJsonSurface()
    {
        Assert.Null(typeof(BodyState).GetProperty("DeNumber", BindingFlags.Public | BindingFlags.Instance));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new BodyState { DeNumber = 441 }));
        Assert.False(json.RootElement.TryGetProperty("DeNumber", out _));
    }
}
