using System.Diagnostics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using static Meitou.Tests.Simulation.SyntheticTown;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>
/// What the AI costs (docs/simulation.md "AI", Cost): a crowded synthetic town of about 1800 residents with AI packages, 900 ticks with the night falling halfway, the AI
/// system's <c>Think</c> (summed over the workers) and <c>SlowWorld</c> timed against the whole tick. Asserts a loose bound per character; Release build for the numbers.
/// </summary>
public class AiBenchTests(ITestOutputHelper output)
{
    /// <summary>Times an inner system's Think and SlowWorld.</summary>
    sealed class Timed(ITickSystem inner) : ITickSystem
    {
        public long ThinkTicks, SlowTicks;
        public void Think(SimWorld world, Partition part)
        {
            long t = Stopwatch.GetTimestamp();
            inner.Think(world, part);
            Interlocked.Add(ref ThinkTicks, Stopwatch.GetTimestamp() - t);
        }
        public void SlowWorld(SimWorld world)
        {
            long t = Stopwatch.GetTimestamp();
            inner.SlowWorld(world);
            SlowTicks += Stopwatch.GetTimestamp() - t;
        }
    }

    [Theory, Bench]
    [InlineData(1)]
    [InlineData(4)]
    public void Ai_cost_in_a_crowded_town(int threads)
    {
        var db = AiTests.Database(residentSquads: 300);
        var data = SyntheticTown.Data(db);
        var walk = new OpenGroundWalkability(Ground);
        var set = SyntheticTown.Standard(data, walk, StandardParts.Population | StandardParts.Movement | StandardParts.Ai);
        var timed = new Timed(set.Ai!);
        var systems = set.Systems.Select(s => s == set.Ai ? (ITickSystem)timed : s).ToList();
        using var world = new SimWorld(new WorldSettings { Seed = 5, Threads = threads, StartHours = 24 + 13, PublishSnapshots = true }, walk, systems);
        world.Commands.Enqueue(new FocusCommand(Centre) { Tick = 0 });
        world.RunTicks(300);   // warm up: spawn, JIT, first choices
        timed.ThinkTicks = timed.SlowTicks = 0;
        int n = world.Characters.Count;
        const int Ticks = 900;
        var watch = Stopwatch.StartNew();
        world.RunTicks(Ticks / 2);
        world.GameHours = 24 + 22.5;   // night falls halfway: every member re-plans
        world.RunTicks(Ticks / 2);
        watch.Stop();
        double us(long t) => t * 1e6 / Stopwatch.Frequency / Ticks;
        output.WriteLine($"{threads} thread(s), {n} characters, {world.Squads.Count} squads: tick {watch.Elapsed.TotalMilliseconds * 1000 / Ticks:0} us, AI think {us(timed.ThinkTicks):0.0} us (summed over threads), AI slow world {us(timed.SlowTicks):0.0} us");
        // About 0.1 us per character per tick on a desktop CPU; ten times that is a regression.
        Assert.InRange((us(timed.ThinkTicks) + us(timed.SlowTicks)) / n, 0, 1.0);
    }
}
