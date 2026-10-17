using System.Numerics;

namespace Meitou.Simulation.Locks;

/// <summary>The original's <c>Character+0x2f8</c> (save key <c>in something</c>, docs/game/carry-loot.md): 0 normal, 1 in a bed, 2 imprisoned.</summary>
public enum HeldState : byte { None = 0, Bed = 1, Imprisoned = 2 }

/// <summary>What a <see cref="Fixture"/> is.</summary>
public enum FixtureKind : byte
{
    /// <summary>One door of a placed building (an entry of its <c>doors</c> list).</summary>
    Door,
    /// <summary>A BUILDING with <c>has lock</c> (safes, chests, lockers).</summary>
    Container,
    /// <summary>BUILDING_FUNCTIONALITY function 8: cages, the prisoner pole and the peeler (<see cref="Fixture.Restrains"/>).</summary>
    Cage,
    /// <summary>A bed of an interior layout; no lock.</summary>
    Bed,
}

/// <summary>The door's motion state as saved in <c>state&lt;i&gt;</c> (docs/game/locks.md "Doors"; not the DoorState enum): 0 closed, 1 open, 2 opening (engine reading), 3 closing.</summary>
public enum DoorMotion : byte { Closed = 0, Open = 1, Opening = 2, Closing = 3 }

/// <summary>
/// The lock object of docs/game/locks.md "Lock object": one structure for doors, lockable buildings, cages (and, later, shackles). The pick timer lives on the lock, as in
/// the original; <see cref="Rolls"/> counts the rolls made on it, the counter of its stateless random stream.
/// </summary>
public sealed class LockState
{
    /// <summary>The first pick roll comes after this many seconds; a failed one draws the next wait uniform in [<see cref="RetryMin"/>, <see cref="RetryMax"/>].</summary>
    public const float FirstRoll = 5, RetryMin = 5, RetryMax = 20;

    /// <summary>0..100.</summary>
    public int Level { get; set; }
    /// <summary>Resistance to cutting and breaking.</summary>
    public float Hardness { get; set; }
    public bool Locked { get; set; }
    public bool Broken { get; set; }
    /// <summary>Seconds of picking since the last roll, and the time of the next roll.</summary>
    public float PickTime { get; set; }
    public float NextRoll { get; set; } = FirstRoll;
    public int Rolls { get; set; }

    /// <summary>Locked flag set, not broken and level at least 1 (the owner's destruction is not modelled): a broken or level-0 lock is open whatever its flag.</summary>
    public bool EffectivelyLocked => Locked && !Broken && Level >= 1;

    internal void Hash(ref StateHasher h)
    {
        h.Add(Level); h.Add(Hardness); h.Add(Locked); h.Add(Broken); h.Add(PickTime); h.Add(NextRoll); h.Add(Rolls);
    }
}

/// <summary>
/// A door, lockable container, cage or bed of a placed building, in the world's <see cref="FixtureRegistry"/>. <see cref="Id"/>: the building's instance id for its own lock
/// (a placed cage or safe), <c>&lt;instance&gt;#&lt;i&gt;</c> for its door i, the <see cref="Meitou.Data.World.TownFixture.Id"/> for an interior cage or container, and
/// <c>&lt;instance&gt;@bed&lt;k&gt;</c> for its k-th bed (engine names).
/// </summary>
public sealed class Fixture
{
    public required string Id { get; init; }
    /// <summary>The instance id of the placed building it belongs to (the house of a door, the building of an interior item): what <see cref="IDoorGate"/> and the save name.</summary>
    public required string Building { get; init; }
    public required FixtureKind Kind { get; init; }
    /// <summary>The BUILDING record (the door's own record for a door).</summary>
    public string RecordId { get; init; } = "";
    /// <summary>The town site it was registered with, and the owner faction (a position in the data's factions; -1 none).</summary>
    public int Site { get; init; } = -1;
    public int Owner { get; set; } = -1;
    /// <summary>Where an occupant is held, and which way it faces (the item's origin: the cage's attach node is not read, an engine choice).</summary>
    public Vector3 Position { get; init; }
    public float Yaw { get; init; }
    /// <summary>Null for beds.</summary>
    public LockState? Lock { get; init; }

    /// <summary>Doors: the index in the building's <c>doors</c> list (the save's suffix), the motion state, the open amount 0..1, the door's own broken flag, <c>gatecode</c> (kept as read) and <c>wants lock</c>.</summary>
    public int DoorIndex { get; init; } = -1;
    public DoorMotion Motion { get; set; }
    public float OpenAmount { get; set; }
    public bool DoorBroken { get; set; }
    public int GateCode { get; set; } = -1;
    public bool WantsLock { get; set; }

    /// <summary>Cages: the functionality's <c>restrains movement</c> (pole, peeler). Cages and beds: <c>use range</c>, and who is in it.</summary>
    public bool Restrains { get; init; }
    public float UseRange { get; init; }
    public CharacterId Occupant { get; set; } = CharacterId.None;

    /// <summary>Its state came from a loaded save (the squad's <c>initial door state</c> then does not apply).</summary>
    public bool Restored { get; set; }

    internal void Hash(ref StateHasher h)
    {
        h.Add(Rng.StableHash(Id)); h.Add((int)Kind); h.Add(Owner);
        if (Lock is { } l) l.Hash(ref h); else h.Add(-1);
        if (Kind == FixtureKind.Door) { h.Add((int)Motion); h.Add(OpenAmount); h.Add(DoorBroken); h.Add(WantsLock); }
        h.Add(Occupant);
    }
}

/// <summary>Where the simulation tells navigation that a building's doors are shut for paths (the game host forwards it to the navmesh's door table). Serial phases only.</summary>
public interface IDoorGate
{
    /// <summary><paramref name="blocked"/>: every door of the building instance is effectively locked (and none broken), so paths may not cross them.</summary>
    void SetDoorsBlocked(string buildingInstance, bool blocked);
    /// <summary>Opens every door again (the world was replaced).</summary>
    void ResetDoors();
}
