using System.Numerics;
using Meitou.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

public class MovementTests
{
    static SimWorld Flat(int threads = 1, Func<float, float, float>? ground = null, bool synchronous = true, params ITickSystem[] extra)
    {
        var walk = new OpenGroundWalkability(ground ?? SyntheticTown.Ground);
        var movement = new MovementSystem(new PathService(walk, synchronous));
        return new SimWorld(new WorldSettings { Threads = threads, PublishSnapshots = false }, walk, [movement, .. extra]);
    }

    static CharacterId Add(SimWorld w, Vector3 at, CharacterTask task = CharacterTask.Idle, float max = 80, float walk = 15, byte mode = SpeedMode.Free)
    {
        var hot = new CharacterHot { Position = at, Health = 100, Task = (byte)task, MaxSpeed = max, WalkSpeed = walk, Mode = mode };
        return w.Characters.Spawn(hot, new CharacterCold(), 0);
    }

    static Vector3 Where(SimWorld w, CharacterId id) => w.Characters.Previous[id.Slot].Position;

    [Fact]
    public void A_move_order_walks_the_character_to_the_target_and_it_idles_there()
    {
        using var w = Flat();
        var id = Add(w, new(0, 300, 0));
        w.Commands.Enqueue(new MoveOrder([id], new Vector3(400, 0, 0)) { Tick = 0 });
        w.RunTicks(30 * 3);
        var mid = Where(w, id);
        Assert.InRange(mid.X, 50, 399);
        w.RunTicks(30 * 12);
        var end = Where(w, id);
        Assert.InRange(Vector2.Distance(new(end.X, end.Z), new(400, 0)), 0, MovementSystem.ArriveRadius + 1);
        Assert.Equal((byte)CharacterTask.Idle, w.Characters.Previous[id.Slot].Task);
        Assert.Equal(0, w.Characters.Previous[id.Slot].Velocity.Length());
        Assert.Equal(300, end.Y);
    }

    [Fact]
    public void Characters_do_not_walk_into_the_water_and_unreachable_goals_give_up()
    {
        // A lake between x = 200 and 300.
        float Lake(float x, float z) => x is > 200 and < 300 ? 50 : 300;
        using var w = Flat(ground: Lake);
        var id = Add(w, new(0, 300, 0));
        w.Commands.Enqueue(new MoveOrder([id], new Vector3(500, 0, 0)) { Tick = 0 });
        w.RunTicks(300);
        Assert.True(Where(w, id).X < 200);
        Assert.Equal((byte)CharacterTask.Idle, w.Characters.Previous[id.Slot].Task);
    }

    [Fact]
    public void Spawning_or_removing_in_a_parallel_phase_fails_loudly()
    {
        using var w = Flat(extra: new Misbehaving());
        Add(w, new(0, 300, 0));
        Assert.Throws<InvalidOperationException>(() => w.RunTick());
        using var w2 = Flat(extra: new RemovesInInputs());
        var id = Add(w2, new(0, 300, 0));
        Assert.Throws<InvalidOperationException>(() => w2.RunTick());
        Assert.True(w2.Characters.IsAlive(id));
    }

    sealed class Misbehaving : ITickSystem
    {
        public void Think(SimWorld world, Partition partition) =>
            world.Characters.Spawn(new CharacterHot(), new CharacterCold(), world.Tick);
    }

    sealed class RemovesInInputs : ITickSystem
    {
        public void Inputs(SimWorld world, IReadOnlyList<SimCommand> commands) => world.Characters.Remove(new CharacterId(0, 0));
    }
}
