using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Simulation;

namespace Meitou.Tests.Simulation;

public class PopulationTests
{
    [Fact]
    public void Residents_appear_when_the_focus_is_near_and_leave_after_the_grace_period()
    {
        using var world = SyntheticTown.World(5, 1, 3, new PopulationSettings { UnloadGraceSeconds = 10, CheckEveryTicks = 1 });
        var population = (PopulationSystem)world.Systems[0];
        world.RunTick();
        Assert.Equal(SiteStatus.Loaded, population.StatusOf(0));
        // 3 squads of 10 (patrol), 1 faction squad of 3; the gated one is not made.
        Assert.Equal(4, world.Squads.Count);
        var table = world.Characters;
        Assert.Equal(world.Squads.All.Sum(s => s.Members.Count), table.Count);
        var centre = new Vector2(SyntheticTown.Centre.X, SyntheticTown.Centre.Z);
        foreach (var s in world.Squads.All)
        {
            Assert.InRange(Vector2.Distance(new Vector2(s.Position.X, s.Position.Z), centre), 0, 300 * 0.6f + 0.01f);
            Assert.False(s.Leader.IsNone);
        }
        var patrols = world.Squads.All.Where(s => s.TemplateId == "10-t").ToList();
        Assert.Equal(3, patrols.Count);
        Assert.All(patrols, s => Assert.InRange(s.Members.Count, 1 + 2 + 1 + 1, 1 + 4 + 1 + 1));
        int before = table.Count;

        // The focus moves far away: nothing is removed during the grace period, then everything is.
        world.Commands.Enqueue(new FocusCommand(new Vector3(-50000, 0, -50000)) { Tick = world.Tick });
        world.RunTicks(150);   // 5 s
        Assert.Equal(before, table.Count);
        world.RunTicks(200);   // past 10 s
        Assert.Equal(SiteStatus.Unloaded, population.StatusOf(0));
        Assert.Equal(0, table.Count);
        Assert.Equal(0, world.Squads.Count);

        // Coming back loads them again (slots are reused with new generations).
        world.Commands.Enqueue(new FocusCommand(SyntheticTown.Centre) { Tick = world.Tick });
        world.RunTicks(2);
        Assert.Equal(before, table.Count);
        Assert.Contains(table.Previous.ToArray(), c => c.Alive && c.Generation > 0);
    }

    [Fact]
    public void Background_building_gives_the_same_town_as_building_in_the_tick()
    {
        using var sync = SyntheticTown.World(11, 1, 3);
        sync.RunTick();
        var settings = new PopulationSettings { Background = true };
        using var async = SyntheticTown.World(11, 1, 3, settings);
        Assert.True(TestWaits.TickUntil(async, () => async.Characters.Count > 0), "the background build never finished");
        Assert.Equal(sync.Characters.Count, async.Characters.Count);
        var a = sync.Squads.All.Select(s => (s.TemplateId, s.Position.X, s.Position.Z, s.Members.Count)).ToList();
        var b = async.Squads.All.Select(s => (s.TemplateId, s.Position.X, s.Position.Z, s.Members.Count)).ToList();
        Assert.Equal(a, b);
    }
}
