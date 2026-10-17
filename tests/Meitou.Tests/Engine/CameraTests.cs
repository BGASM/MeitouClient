using System.Numerics;
using Meitou.Engine.Cameras;
using Meitou.Engine.Input;

namespace Meitou.Tests.Engine;

public class CameraTests
{
    static readonly Func<float, float, float> Flat = (_, _) => 0;
    readonly InputState input = new();
    readonly InputBindings bindings = new();

    ActionState Tick() => bindings.Resolve(input.Consume());

    [Fact]
    public void State_reproduces_the_eye_through_the_viewers_orbit_formula()
    {
        var cam = new StrategyCamera((x, z) => 0.05f * x);
        cam.Place(new Vector2(100, 40), 1.1f, 0.7f, 300);
        var s = cam.State;
        Assert.Equal(s.Eye.X, CameraState.EyeFor(s.Target, s.Yaw, s.Pitch, s.Distance).X, 2);
        Assert.Equal(s.Eye.Y, CameraState.EyeFor(s.Target, s.Yaw, s.Pitch, s.Distance).Y, 2);
        Assert.Equal(s.Eye.Z, CameraState.EyeFor(s.Target, s.Yaw, s.Pitch, s.Distance).Z, 2);
        Assert.Equal(Vector3.Normalize(s.Target - s.Eye).X, s.Forward.X, 4);
    }

    [Fact]
    public void The_eye_stays_above_the_ground_over_a_long_random_drive()
    {
        Func<float, float, float> hills = (x, z) => 200 * MathF.Sin(x * 0.003f) * MathF.Cos(z * 0.002f);
        var rig = new CameraRig(hills);
        var rng = new Random(3);
        var keys = new[] { Key.W, Key.A, Key.S, Key.D, Key.Q, Key.E, Key.Home, Key.End, Key.Up, Key.Down };
        for (int i = 0; i < 3000; i++)
        {
            if (rng.Next(8) == 0) input.SetKey(keys[rng.Next(keys.Length)], rng.Next(2) == 0);
            input.AddWheel(rng.Next(5) == 0 ? rng.Next(-3, 4) : 0);
            rig.Update(1 / 30f, Tick());
            var s = rig.Current;
            Assert.True(s.Eye.Y >= hills(s.Eye.X, s.Eye.Z) + 12 - 0.05f, $"tick {i}");
            Assert.InRange(rig.Strategy.Distance, 10f, 2000f);
        }
    }

    [Fact]
    public void Leaving_free_flight_puts_the_pivot_where_the_view_meets_the_ground()
    {
        var rig = new CameraRig(Flat);
        input.SetKey(Key.Semicolon, true);
        rig.Update(1 / 30f, Tick());
        input.SetKey(Key.Semicolon, false);
        input.SetKey(Key.R, true);
        for (int i = 0; i < 30; i++) rig.Update(1 / 30f, Tick());
        input.SetKey(Key.R, false);
        for (int i = 0; i < 60; i++) rig.Update(1 / 30f, Tick()); // coast to a stop
        var eye = rig.Free.Position;
        var look = rig.Free.LookPointOnGround(CameraRig.LookRange);
        Assert.NotNull(look);
        Assert.Equal(0f, look!.Value.Y, 2);

        input.SetKey(Key.Semicolon, true);
        rig.Update(1 / 30f, Tick());
        Assert.False(rig.IsFree);
        Assert.Equal(look.Value.X, rig.Strategy.Pivot.X, 1);
        Assert.Equal(look.Value.Z, rig.Strategy.Pivot.Z, 1);
        Assert.Equal(Vector3.Distance(eye, look.Value), rig.Strategy.Distance, 1);
        Assert.Equal(eye.Y, rig.Current.Eye.Y, 0); // same view
    }
}
