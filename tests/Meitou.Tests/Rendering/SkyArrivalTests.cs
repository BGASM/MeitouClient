using System.Numerics;
using Meitou.Game;
using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

public class SkyArrivalTests
{
    static WorldCamera Camera() => new() { Target = new(100, 30, -200), Yaw = 0.4f, Pitch = 0.6f, Distance = 80 };

    [Fact]
    public void AscentRotatesAroundTheEyeAndHoldsAtMidnightUntilReady()
    {
        var camera = Camera();
        var eye = camera.Eye;
        var arrival = new SkyArrival(camera, 13);
        Assert.Equal(13, arrival.Apply(camera, 8));
        arrival.Advance(SkyArrival.AscentSeconds / 2);
        Assert.InRange(arrival.Apply(camera, 8), 13, 24);
        Assert.True(Vector3.Distance(eye, camera.Eye) < 0.001f);
        arrival.Advance(10);
        Assert.Equal(0, arrival.Apply(camera, 8));
        Assert.Equal(SkyArrival.SkyPitch, camera.Pitch);
        Assert.True(arrival.AtStars);
        Assert.False(arrival.Finished);
        Assert.True(camera.Forward.Y > 0.99f);
    }

    [Fact]
    public void ArrivalEndsAtTheSavedCameraAndHourWithoutChangingTheRig()
    {
        var arrival = new SkyArrival(Camera(), 23);
        arrival.Skip();
        arrival.Arrive();
        var destination = Camera();
        destination.Target += new Vector3(15000, 400, 6000);
        destination.Yaw = 5.5f;
        destination.Distance = 170;
        var target = destination.Target;
        var eye = destination.Eye;
        arrival.Advance(SkyArrival.DescentSeconds);
        Assert.Equal(19, arrival.Apply(destination, 19));
        Assert.True(Vector3.Distance(target, destination.Target) < 0.01f);
        Assert.True(Vector3.Distance(eye, destination.Eye) < 0.01f);
        Assert.True(Vector3.Distance(CameraForward(5.5f, 0.6f), destination.Forward) < 0.001f);
        Assert.True(arrival.Finished);
    }

    [Fact]
    public void ArrivalBlendsThePosterLensIntoTheGameplayLens()
    {
        var source = Camera();
        source.FieldOfView = 0.7f;
        var arrival = new SkyArrival(source, 22);
        arrival.Skip();
        arrival.Arrive();
        var destination = Camera();
        destination.FieldOfView = 1.1f;
        arrival.Apply(destination, 13);
        Assert.Equal(0.7f, destination.FieldOfView, 4);
        destination.FieldOfView = 1.1f;
        arrival.Advance(SkyArrival.DescentSeconds / 2);
        arrival.Apply(destination, 13);
        Assert.Equal(0.9f, destination.FieldOfView, 4);
    }

    [Fact]
    public void SkippingTheAscentDoesNotDeclareLoadingFinished()
    {
        var arrival = new SkyArrival(Camera(), 13);
        arrival.Skip();
        Assert.True(arrival.AtStars);
        Assert.False(arrival.Finished);
        arrival.Arrive();
        Assert.False(arrival.Finished);
        arrival.Skip();
        Assert.True(arrival.Finished);
    }

    [Theory]
    [InlineData(23, 0, 0.5f, 23.5f)]
    [InlineData(13, 0, 1, 0)]
    [InlineData(0, 13, 1, 13)]
    public void LightingAlwaysTravelsForwardThroughMidnight(float from, float to, float t, float expected) =>
        Assert.Equal(expected, SkyArrival.ForwardHour(from, to, t));

    static Vector3 CameraForward(float yaw, float pitch) =>
        -new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
}
