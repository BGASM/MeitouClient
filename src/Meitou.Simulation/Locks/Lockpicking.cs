using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Crime;

namespace Meitou.Simulation.Locks;

/// <summary>What one <see cref="Lockpicking.Step"/> did.</summary>
public enum PickOutcome : byte
{
    /// <summary>The lock is not effectively locked: nothing to pick.</summary>
    Open,
    /// <summary>The picker may not pick it (skill too low, down, carried, on a pole...).</summary>
    NotAllowed,
    /// <summary>Time added, no roll yet.</summary>
    Picking,
    /// <summary>A roll failed; the next comes after a new 5-20 s wait.</summary>
    Failed,
    /// <summary>A roll succeeded: the lock is unlocked.</summary>
    Success,
}

/// <summary>
/// Lockpicking, breaking (docs/game/locks.md "Lockpicking", "Cutting and breaking"): the service the pick-lock order and the AI's tasks call each update of the task, in serial
/// phases only (it changes the lock, the picker's stats and its pending crime). The roll comes from the world seed, the fixture's id and the lock's roll counter.
/// Picking shackles (an item lock) is not built yet; it would call the same rules with the item's <see cref="LockState"/>.
/// </summary>
public sealed class Lockpicking(GameConstants constants, BodyOptions options)
{
    /// <summary>The chance at or above the lock's level, and the skill a caged picker loses for the roll (<b>Verified</b>).</summary>
    public const float MaxChance = 0.9f, CagedPenalty = 10;

    readonly XpService xp = new(constants, options);

    /// <summary><c>p = 0.9</c> when <paramref name="skill"/> &gt;= <paramref name="level"/>, else <c>0.9 / 2^((level - skill) / 10)</c> (<c>FUN_140297dc0</c>, <b>Verified</b>).</summary>
    public static float Chance(int level, float skill) => skill >= level ? MaxChance : MaxChance / MathF.Pow(2, (level - skill) / 10f);

    /// <summary>The effective lockpicking skill: stat 37 times its body multiplier.</summary>
    public static float Skill(CharacterCold c) =>
        (c.Stats?[StatsEnumerated.Lockpicking] ?? 0) * (c.Medical?.StatMultiplier(StatsEnumerated.Lockpicking) ?? 1);

    /// <summary>The chance of the roll and the bar: a picker imprisoned in a cage uses skill - 10.</summary>
    public static float RollChance(CharacterCold picker, int level) => Chance(level, Skill(picker) - (picker.IsImprisoned ? CagedPenalty : 0));

    /// <summary>
    /// The skill condition (166/167): the chance without the cage penalty is above <c>minimum lockpick chance</c> (0.05, strict); and the engine's conditions for the Unknown
    /// cases of locks.md: conscious, not carried or carrying, one arm working, not tied to a pole.
    /// </summary>
    public bool Allowed(World world, CharacterCold picker, int level)
    {
        if (picker.Medical is { Incapacitated: true } || !picker.CarriedBy.IsNone || !picker.Carrying.IsNone) return false;
        if (picker.Medical?.Aggregates().BothArmsDisabled == true) return false;
        if (picker.IsImprisoned && picker.InWhat is { } id && world.Fixtures.Find(id) is { Restrains: true }) return false;
        return Chance(level, Skill(picker)) > constants.MinimumLockpickChance;
    }

    /// <summary>
    /// One update of picking <paramref name="target"/> by the character in <paramref name="picker"/> for <paramref name="dt"/> game seconds (the task's update time): sets the pending crime,
    /// adds the time, and once it passes the lock's next-roll time rolls <c>rand() &lt; p</c>: success unlocks; failure waits uniform 5-20 s. Every roll trains lockpicking (1.2 x race[37]
    /// on success, 0.24 x on failure).
    /// </summary>
    public PickOutcome Step(World world, int picker, Fixture target, float dt, CrimeSystem? crimes = null)
    {
        var c = world.Characters.Cold(picker);
        if (target.Lock is not { EffectivelyLocked: true } l) return PickOutcome.Open;
        if (c is null || !Allowed(world, c, l.Level)) return PickOutcome.NotAllowed;
        Crime(world, picker, c, target, crimes);
        l.PickTime += dt;
        if (l.PickTime < l.NextRoll) return PickOutcome.Picking;
        l.PickTime = 0;
        ulong key = Rng.StableHash(target.Id);
        ulong counter = (ulong)++l.Rolls * 2;
        bool success = Rng.Float(Rng.Hash(world.Seed, key, RngPurpose.Lock, counter)) < RollChance(c, l.Level);
        if (success)
        {
            l.Locked = false;
            l.NextRoll = LockState.FirstRoll;
            world.Fixtures.Touch(target);
        }
        else
        {
            l.NextRoll = LockState.RetryMin + (LockState.RetryMax - LockState.RetryMin) * Rng.Float(Rng.Hash(world.Seed, key, RngPurpose.Lock, counter + 1));
        }
        if (c.Stats is { } stats && c.Race is { } race) xp.Lockpicking(stats, race, success);
        return success ? PickOutcome.Success : PickOutcome.Failed;
    }

    /// <summary>
    /// Breaks a lock by force (Task_BruteForceLock, <b>Observed</b>): succeeds at once when the effective strength is above 3 x hardness (the stat-scale term is taken as 1, an engine
    /// choice), setting the broken flag; books the same crime as picking. False: "I'm not strong enough to do that".
    /// </summary>
    public bool Break(World world, int picker, Fixture target, CrimeSystem? crimes = null)
    {
        var c = world.Characters.Cold(picker);
        if (c is null || target.Lock is not { } l || l.Broken) return false;
        Crime(world, picker, c, target, crimes);
        float strength = (c.Stats?[StatsEnumerated.Strength] ?? 0) * (c.Medical?.StatMultiplier(StatsEnumerated.Strength) ?? 1);
        if (strength <= 3 * l.Hardness) return false;
        l.Broken = true;
        if (target.Kind == FixtureKind.Door) target.DoorBroken = true;
        world.Fixtures.Touch(target);
        return true;
    }

    /// <summary>
    /// The pending crime of every update (<b>Observed</b>): a cage holding another character is code 7 against the cage's owner (the captive's owner faction, read here as the jail's);
    /// any other fixture owned by another faction is Burglary (2) against its owner. <see cref="CrimeSystem.SetPending"/> refuses own-faction and lawless victims.
    /// </summary>
    static void Crime(World world, int picker, CharacterCold c, Fixture target, CrimeSystem? crimes)
    {
        if (crimes is null || target.Owner < 0) return;
        var table = world.Characters;
        bool captive = target.Kind == FixtureKind.Cage && !FixtureRegistry.IsFree(table, target) && target.Occupant != table.IdOf(picker);
        if (captive) crimes.SetPending(world, picker, CrimeCode.Terrorism, target.Owner, target.Occupant, CrimeSystem.MinTimer);
        else if (target.Owner != c.Faction) crimes.SetPending(world, picker, CrimeCode.Burglary, target.Owner, CharacterId.None, CrimeSystem.MinTimer);
    }
}
