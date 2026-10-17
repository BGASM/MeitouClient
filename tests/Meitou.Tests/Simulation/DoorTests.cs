using System.Numerics;
using Meitou.Data;
using Meitou.Data.World;
using Meitou.Navigation;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Doors: leaf motion, closing to the lock, Meitou's close-behind and where the doors are (docs/game/locks.md "Doors", "Door motion"; docs/simulation.md "Doors").</summary>
public class DoorTests
{
    [Fact]
    public void The_leaf_slides_and_steps_out_by_the_original_formula()
    {
        var box = (Min: new Vector3(-20, 0, -2), Max: new Vector3(0, 40, 2));
        // No step out: slide x dist x doa.
        var plain = new DoorLeaf(0, 30, 1, 0);
        Assert.Equal(new Vector3(15, 0, 0), plain.LocalOffset(0.5f, box.Min, box.Max));
        Assert.Equal(new Vector3(30, 0, 0), plain.LocalOffset(1, box.Min, box.Max));
        // Step out 10 along the thinnest other axis (X, away from the origin: -X) until t = 10 / (0.25 x 28 + 10) = 10/17, then slide 28 along Z.
        var stepping = new DoorLeaf(2, 28, 0.5f, 10);
        var half = stepping.LocalOffset(0.5f, box.Min, box.Max);
        Assert.Equal(-8.5f, half.X, 4);
        Assert.Equal(0, half.Z);
        Assert.Equal(new Vector3(-10, 0, 28), stepping.LocalOffset(1, box.Min, box.Max));
    }

    sealed class Gate : IDoorGate
    {
        public void SetDoorsBlocked(string buildingInstance, bool blocked) { }
        public void ResetDoors() { }
        public DoorSpot? DoorSpotOf(string buildingRecordId, int doorIndex) => new(Vector3.Zero, Vector3.UnitZ, 15);
    }

    [Fact]
    public void A_door_that_wants_its_lock_locks_when_closed_and_a_walker_opened_door_closes_after_5_s()
    {
        var walk = new OpenGroundWalkability((_, _) => 150);
        using var world = new SimWorld(new WorldSettings { Seed = 31, PublishSnapshots = false, MinPartitionSize = 1 }, walk, [new LockSystem(BaseGame, BodyOptions.Default)]);
        world.Fixtures.RegisterSite(world, ArrestTests.Town(), 0);
        world.Fixtures.Doors = new Gate();

        var door = world.Fixtures.Find("house#0")!;
        door.Motion = DoorMotion.Open;
        door.OpenAmount = 1;
        door.WantsLock = true;
        Assert.True(FixtureRegistry.CloseDoor(door));
        for (int i = 0; i < 11; i++) world.Fixtures.StepDoors(0.1f);
        Assert.Equal(DoorMotion.Closed, door.Motion);
        Assert.Equal(0, door.OpenAmount);
        Assert.True(door.Lock!.Locked);

        // Meitou: a walker opened it and nobody goes through, so it closes once 5 s have passed since the last pass (and not before).
        door.WantsLock = false;
        door.Lock.Locked = false;
        door.Motion = DoorMotion.Open;
        door.OpenAmount = 1;
        door.AutoOpened = true;
        door.LastPass = world.Tick;
        int five = (int)MathF.Ceiling(LockSystem.AutoCloseSeconds / world.TickSeconds);
        world.RunTicks(five - 10);
        Assert.Equal(DoorMotion.Open, door.Motion);
        world.RunTicks(10 + LockSystem.WalkerEveryTicks);
        Assert.Equal(DoorMotion.Closing, door.Motion);
        Assert.False(door.Lock!.Locked);
    }

    [Fact, Slow]
    public void Every_door_in_the_hub_has_a_known_position()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var levels = InstallData.Levels!;
        var data = PopulationData.Create(db, levels.Towns(), buildings: TownBuildings.Build(db, levels, null, (_, _) => 0));
        var hub = data.Sites.Single(s => s.Town.Name == "The Hub");
        var geometry = new NavDoorGeometry(InstallData.Install!, db);
        int doors = 0;
        var missing = new List<string>();
        foreach (var b in hub.Buildings)
        {
            if (db.Find(b.BuildingId) is not { } record) continue;
            for (int i = 0; i < record.GetReferences("doors").Count; i++, doors++)
                if (geometry.Spot(record.StringId, i) is null) missing.Add($"{b.Name} ({record.StringId}) door {i}");
        }
        Assert.True(doors > 10, $"{doors} doors");
        Assert.Empty(missing);
    }
}
