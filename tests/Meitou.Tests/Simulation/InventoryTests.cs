using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Inventories, weight and eating (docs/simulation.md "Items and eating").</summary>
public class InventoryTests
{
    static SimWorld World(ulong seed = 3, int threads = 1, FeedSettings? feed = null, BodyOptions? options = null)
    {
        var db = SyntheticTown.Database(anatomy: true, items: true);
        return SyntheticTown.World(seed, threads, db: db, bodies: true, feed: feed, bodyOptions: options);
    }

    static IEnumerable<int> Humans(SimWorld world) =>
        Enumerable.Range(0, world.Characters.HighWater).Where(s => world.Characters.Previous[s].Alive && world.Characters.Cold(s)!.Inventory is not null);

    [Fact]
    public void Items_take_free_cells_and_stack_and_a_full_grid_refuses()
    {
        var inv = new Inventory(4, 2);
        ItemInstance Item(string id, int w, int h, bool stack = false) => new() { Record = id, Name = id, Width = w, Height = h, Stackable = stack };
        Assert.True(inv.Add(Item("a", 2, 2)));
        Assert.True(inv.Add(Item("b", 2, 2)));
        Assert.Equal([(0, 0), (2, 0)], inv.Items.Select(i => (i.X, i.Y)));
        Assert.False(inv.Add(Item("c", 1, 1)));
        var bolts = new Inventory(4, 2);
        Assert.True(bolts.Add(Item("bolt", 1, 1, stack: true)));
        Assert.True(bolts.Add(Item("bolt", 1, 1, stack: true)));
        Assert.Equal(2, Assert.Single(bolts.Items).Quantity);
    }

    [Fact]
    public void Nobody_starves_when_npcs_are_provisioned_but_the_player_gets_nothing_free()
    {
        // A very short hunger time: three levels last under two game hours.
        var options = new BodyOptions { HungerTime = 0.02f };
        using var fed = World(feed: new FeedSettings { ProvisionHours = 0.5f }, options: options);
        fed.RunTicks(40000);   // 12 game hours
        var table = fed.Characters;
        Assert.NotEmpty(Humans(fed));
        Assert.All(Humans(fed), s => Assert.False(table.Cold(s)!.Medical!.Dead, "provisioned NPCs do not starve"));
        Assert.Contains(Humans(fed), s => table.Cold(s)!.NextProvisionTick > 0);

        using var unfed = World(feed: new FeedSettings { ProvisionNpcs = false }, options: options);
        unfed.RunTicks(40000);
        Assert.Contains(Humans(unfed), s => unfed.Characters.Cold(s)!.Medical!.Dead && unfed.Characters.Cold(s)!.Medical!.Cause == DeathCause.Starvation);
    }
}
