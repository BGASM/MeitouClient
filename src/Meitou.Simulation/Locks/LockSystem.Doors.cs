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
/// <item><b>Engine choice</b>: the original opens a door with Task_OpenDoor, which its planner puts before a walk into a building (the walker faces the door, a locked one fails the task);
/// here the walk itself opens it, within <see cref="OpenReach"/> of the door when its path passes within <see cref="DoorPass"/> of the leaf's centre. The walker does not wait for the
/// leaf (the navmesh lets paths through closed unlocked doors).</item>
/// <item>Nothing closes a door by itself (<b>Observed</b>: only Task_CloseDoor, Task_LockDoor and the door panel start a close): doors stay open until a lock-up round or the player closes them.</item>
/// </list>
/// </summary>
public sealed partial class LockSystem
{
    /// <summary>Engine choices of the walk-through opening (see the class remarks).</summary>
    public const int WalkerEveryTicks = 3;
    public const float OpenReach = 40, DoorPass = 10, DoorHeight = 40;

    long buildingsVersion = -1;

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
        if (reg.Version == buildingsVersion) return;
        buildingsVersion = reg.Version;
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
        public Vector3 Centre;
        public bool Opened;
    }

    /// <summary>Opens the closed, unlocked doors that walkers' paths run through; a walker at a locked one stops.</summary>
    void OpenForWalkers(World world)
    {
        var fixtures = world.Fixtures;
        foreach (var f in fixtures.All)
        {
            if (f.Kind != FixtureKind.Door || f.DoorBroken || f.Motion is DoorMotion.Open or DoorMotion.Opening) continue;
            if (fixtures.DoorCentre(f) is not { } centre) continue;
            var scan = new WalkerScan { World = world, Door = f, Centre = centre };
            world.Grid.Query(centre.X, centre.Z, OpenReach, ref scan, static (int slot, ref WalkerScan s) =>
            {
                if (s.Opened) return;
                var table = s.World.Characters;
                ref var n = ref table.Next[slot];
                if (!n.Alive || ((MoveFlags)n.Flags & MoveFlags.HasPath) == 0 || table.Cold(slot) is not { } c || c.Medical is { Incapacitated: true } || c.IsImprisoned) return;
                if (MathF.Abs(n.Position.Y - s.Centre.Y) > DoorHeight || !PassesDoor(n.Position, c.Path, n.PathCursor, s.Centre)) return;
                if (Vector2.Distance(new Vector2(n.Position.X, n.Position.Z), new Vector2(s.Centre.X, s.Centre.Z)) > OpenReach) return;
                if (s.Door.Lock is { EffectivelyLocked: true })
                {
                    // Task_OpenDoor fails at a locked door for anyone, the owner too (docs/game/locks.md "Doors").
                    MovementSystem.Stop(table, slot);
                    if (c.IsPlayer) s.World.Post($"{c.Name}: It's locked.");
                    return;
                }
                s.Opened = FixtureRegistry.OpenDoor(s.Door) || s.Door.Motion == DoorMotion.Opening;
            });
        }
    }

    /// <summary>Whether the next two legs of a path (from the walker to its next point, and on to the one after) pass within <see cref="DoorPass"/> of the door, in X/Z.</summary>
    static bool PassesDoor(Vector3 at, Vector3[] path, int cursor, Vector3 door)
    {
        var d = new Vector2(door.X, door.Z);
        var a = new Vector2(at.X, at.Z);
        for (int k = cursor; k < path.Length && k < cursor + 2; k++)
        {
            var b = new Vector2(path[k].X, path[k].Z);
            if (DistanceToSegment(d, a, b) <= DoorPass) return true;
            a = b;
        }
        return false;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }
}
