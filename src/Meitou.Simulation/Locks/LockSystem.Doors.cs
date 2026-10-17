using System.Numerics;

namespace Meitou.Simulation.Locks;

/// <summary>Opens the closed doors of a player building, or closes its open ones when any is open (the door panel's open/close button, <c>FUN_140546fb0</c>; a locked door is unlocked first).</summary>
public sealed record ToggleDoorsOrder(string Building) : SimCommand;

/// <summary>Flips <c>wants lock</c> on every door of a player building (the door panel's lock button, <c>FUN_140547060</c>): a closed door locks at once, an open one when it closes; unlocking clears the lock.</summary>
public sealed record ToggleDoorLocksOrder(string Building) : SimCommand;

/// <summary>
/// The doors (docs/simulation.md "Cages, beds and locks"; the original's rules in docs/game/locks.md "Doors"): every tick the opening and closing leaves move (<see cref="FixtureRegistry.StepDoors"/>);
/// every <see cref="WalkerEveryTicks"/> ticks a character whose path runs through a closed door opens it, or stops at a locked one ("It's locked."); the player's finished buildings
/// get their doors registered (<see cref="FixtureRegistry.RegisterPlayerBuilding"/>) and the player's door orders are applied. Serial phases only.
/// <list type="bullet">
/// <item><b>Engine choice</b>: the original's walk needs its destination and location "accessible" (states 59, 60), which Task_OpenDoor (84, 85) makes true by opening the door
/// (the walker faces the door, a locked one fails the task); here the walk itself opens it: a walker within <see cref="OpenReach"/> of a closed door whose current or next path leg
/// crosses the leaf's plane within its width (plus <see cref="DoorSlack"/>). At a locked one the walker stops, within <see cref="LockedReach"/> on its current leg. The walker does not
/// wait for the leaf (the navmesh lets paths through closed unlocked doors).</item>
/// <item>No walk closes a door behind it (<b>Observed</b>: only Task_CloseDoor, Task_LockDoor and the door panel start a close, and only the lock tasks need a closed door): doors stay
/// open until a lock-up round or the player closes them.</item>
/// </list>
/// </summary>
public sealed partial class LockSystem
{
    /// <summary>Engine choices of the walk-through opening (see the class remarks).</summary>
    public const int WalkerEveryTicks = 3;
    public const float OpenReach = 40, LockedReach = 15, DoorSlack = 6, DoorHeight = 40;

    long buildingsRevision = -1;

    void DoorInputs(World world, SimCommand command)
    {
        var fixtures = world.Fixtures;
        switch (command)
        {
            case ToggleDoorsOrder toggle:
            {
                var doors = PlayerDoors(world, toggle.Building);
                if (doors.Count == 0) break;
                bool anyOpen = doors.Any(d => d.Motion == DoorMotion.Open);
                foreach (var d in doors)
                {
                    if (anyOpen) { FixtureRegistry.CloseDoor(d); continue; }
                    if (d.Lock is { EffectivelyLocked: true } l) { l.Locked = false; fixtures.Touch(d); }
                    FixtureRegistry.OpenDoor(d);
                }
                break;
            }
            case ToggleDoorLocksOrder lockToggle:
            {
                var doors = PlayerDoors(world, lockToggle.Building);
                if (doors.Count == 0) break;
                bool wants = !doors.All(d => d.WantsLock);
                foreach (var d in doors)
                {
                    d.WantsLock = wants;
                    if (d.Lock is not { } l) continue;
                    if (wants && d.Motion == DoorMotion.Closed) l.Locked = true;
                    else if (!wants && l.EffectivelyLocked) l.Locked = false;
                    fixtures.Touch(d);
                }
                break;
            }
        }
    }

    /// <summary>The door fixtures of a building the player's faction owns (none for any other).</summary>
    static List<Fixture> PlayerDoors(World world, string building)
    {
        var list = new List<Fixture>();
        foreach (var f in world.Fixtures.OfBuilding(building))
            if (f.Kind == FixtureKind.Door && !f.DoorBroken && f.Owner >= 0 && f.Owner == world.Player.Faction) list.Add(f);
        return list;
    }

    void Doors(World world)
    {
        SyncPlayerBuildings(world);
        world.Fixtures.StepDoors(world.TickSeconds);
        if (world.Tick % WalkerEveryTicks == 0) OpenForWalkers(world);
    }

    /// <summary>The player's finished buildings have their doors registered; a building no longer finished (dismantled, ruined) loses them.</summary>
    void SyncPlayerBuildings(World world)
    {
        var reg = world.Buildings;
        if (reg.Revision == buildingsRevision) return;
        buildingsRevision = reg.Revision;
        var fixtures = world.Fixtures;
        foreach (var b in fixtures.Buildings.ToArray())
            if (fixtures.OfBuilding(b) is [{ Site: -1 }, ..] && reg.Find(b) is not { Complete: true, Destroyed: false }) fixtures.Unregister(b);
        if (reg.Count == 0 || world.Systems.OfType<PopulationSystem>().FirstOrDefault()?.Data is not { } data) return;
        foreach (var site in reg.All)
            if (site.Complete && !site.Destroyed) fixtures.RegisterPlayerBuilding(world, data, site);
    }

    struct WalkerScan
    {
        public World World;
        public Fixture Door;
        public DoorSpot Spot;
        public bool Opened;
    }

    /// <summary>Opens the closed, unlocked doors that walkers' paths run through; a walker at a locked one stops.</summary>
    void OpenForWalkers(World world)
    {
        var fixtures = world.Fixtures;
        foreach (var f in fixtures.All)
        {
            if (f.Kind != FixtureKind.Door || f.DoorBroken || f.Motion is DoorMotion.Open or DoorMotion.Opening) continue;
            if (fixtures.DoorSpotOf(f) is not { } spot) continue;
            var scan = new WalkerScan { World = world, Door = f, Spot = spot };
            world.Grid.Query(spot.Centre.X, spot.Centre.Z, OpenReach, ref scan, static (int slot, ref WalkerScan s) =>
            {
                if (s.Opened) return;
                var table = s.World.Characters;
                ref var n = ref table.Next[slot];
                if (!n.Alive || ((MoveFlags)n.Flags & MoveFlags.HasPath) == 0 || table.Cold(slot) is not { } c || c.Medical is { Incapacitated: true } || c.IsImprisoned) return;
                if (MathF.Abs(n.Position.Y - s.Spot.Centre.Y) > DoorHeight) return;
                float away = Vector2.Distance(new Vector2(n.Position.X, n.Position.Z), new Vector2(s.Spot.Centre.X, s.Spot.Centre.Z));
                if (away > OpenReach) return;
                if (s.Door.Lock is { EffectivelyLocked: true })
                {
                    // Task_OpenDoor fails at a locked door for anyone, the owner too (docs/game/locks.md "Doors").
                    if (away > LockedReach || !Crosses(n.Position, c.Path, n.PathCursor, s.Spot, legs: 1)) return;
                    MovementSystem.Stop(table, slot);
                    if (c.IsPlayer) s.World.Post($"{c.Name}: It's locked.");
                    return;
                }
                if (!Crosses(n.Position, c.Path, n.PathCursor, s.Spot, legs: 2)) return;
                s.Opened = FixtureRegistry.OpenDoor(s.Door) || s.Door.Motion == DoorMotion.Opening;
            });
        }
    }

    /// <summary>Whether one of the next <paramref name="legs"/> legs of a path (from the walker to its next point, then on) crosses the leaf's plane within its width plus <see cref="DoorSlack"/>, in X/Z.</summary>
    static bool Crosses(Vector3 at, Vector3[] path, int cursor, DoorSpot door, int legs)
    {
        var c = new Vector2(door.Centre.X, door.Centre.Z);
        var normal = new Vector2(door.Normal.X, door.Normal.Z);
        var a = new Vector2(at.X, at.Z);
        for (int k = cursor; k < path.Length && k < cursor + legs; k++)
        {
            var b = new Vector2(path[k].X, path[k].Z);
            float da = Vector2.Dot(a - c, normal), db = Vector2.Dot(b - c, normal);
            if ((da <= 0) != (db <= 0) && Vector2.Distance(a + (b - a) * (da / (da - db)), c) <= door.HalfWidth + DoorSlack) return true;
            a = b;
        }
        return false;
    }
}
