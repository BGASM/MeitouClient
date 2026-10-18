using System.Numerics;

namespace Meitou.Simulation.Locks;

/// <summary>How a character gets shackles off: the original's context-menu verbs Unlock Shackles (task 201), Use Tools (185) and Use Strength (186).</summary>
public enum ShackleWork : byte { Pick, Cut, Break }

/// <summary>A character working on <see cref="Wearer"/>'s shackles (its own or another's).</summary>
public sealed record ShackleJob(CharacterId Wearer, ShackleWork Work);

/// <summary>Work on <see cref="Wearer"/>'s locked shackles: the first of the characters (the selection when empty) who may, walking to the wearer unless it is its own.</summary>
public sealed record ShackleOrder(IReadOnlyList<CharacterId> Characters, CharacterId Wearer, ShackleWork Work) : SimCommand;

/// <summary>
/// Shackles (docs/game/locks.md "Lockpicking", "Cutting and breaking"; docs/simulation.md "Shackles"): worn ARMOUR with <c>is locked</c> holds a lock that keeps it on. Picking it rolls
/// like a cage's lock, cutting wears its hardness down with a tool, breaking is one strength test. Once it is unlocked or broken the wearer can take it off.
/// </summary>
public sealed partial class LockSystem
{
    /// <summary>A worker beside the wearer works within this distance (engine: a cage's use range).</summary>
    public const float ShackleReach = 25;

    /// <summary>The worn locked shackles of a character (null: none).</summary>
    public static Items.ItemInstance? WornShackles(CharacterCold? c) => c?.Inventory?.Items.FirstOrDefault(i => i.LockedOn);

    void ShackleInput(World world, ShackleOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Wearer, out int w) || WornShackles(table.Cold(w)) is null) return;
        foreach (var id in Who(world, order.Characters))
        {
            if (!table.TryResolveNext(id, out int s) || table.Cold(s) is not { IsPlayer: true, Medical.Incapacitated: false } c || !c.CarriedBy.IsNone) continue;
            if (c.IsImprisoned && id != order.Wearer) continue;   // from a cage only its own
            if (order.Work == ShackleWork.Cut && Lockpicking.BestTool(c) is null)
            {
                world.Post($"{c.Name}: I don't have any tools");
                continue;
            }
            c.PickingLock = null;
            c.Shackles = new ShackleJob(order.Wearer, order.Work);
            if (id != order.Wearer) MovementSystem.GoTo(table, s, Flat(table.Next[w].Position), c.RunSpeed);
            break;
        }
    }

    static Vector2 Flat(Vector3 p) => new(p.X, p.Z);

    /// <summary>Once a slow tick: each worker in reach of its wearer works the shackles; one out of reach walks on (a wearer that moved is followed).</summary>
    void ShackleWorkers(World world)
    {
        var table = world.Characters;
        var next = table.Next;
        for (int s = 0; s < table.HighWater; s++)
        {
            if (!next[s].Alive || table.Cold(s) is not { Shackles: { } job } c) continue;
            if (c.Medical is { Incapacitated: true } || !c.CarriedBy.IsNone || !table.TryResolveNext(job.Wearer, out int w) || WornShackles(table.Cold(w)) is not { } item)
            {
                c.Shackles = null;
                continue;
            }
            if (w != s)
            {
                float d = Vector2.Distance(Flat(next[s].Position), Flat(next[w].Position));
                if (d > ShackleReach)
                {
                    // Walking: a new order (another goal far from the wearer) ends it; a wearer that moved is followed.
                    if (next[s].Task != (byte)CharacterTask.GoTo) { c.Shackles = null; continue; }
                    if (Vector2.Distance(next[s].Goal, Flat(next[w].Position)) > ShackleReach) MovementSystem.GoTo(table, s, Flat(next[w].Position), c.RunSpeed);
                    continue;
                }
                if (next[s].Task == (byte)CharacterTask.GoTo) MovementSystem.Stop(table, s);
            }
            switch (job.Work)
            {
                case ShackleWork.Pick:
                    switch (Picking.StepShackles(world, s, job.Wearer, item, world.TickSeconds, Crimes))
                    {
                        case PickOutcome.Open: c.Shackles = null; break;
                        case PickOutcome.Success: c.Shackles = null; Freed(world, s, c, "Pick success!"); break;
                        case PickOutcome.Failed: if (c.IsPlayer) world.Post($"{c.Name}: Pick failed"); break;
                        case PickOutcome.NotAllowed: c.Shackles = null; if (c.IsPlayer) world.Post($"{c.Name}: I'm not skilled enough..."); break;
                    }
                    break;
                case ShackleWork.Cut:
                    if (Lockpicking.BestTool(c) is null) { c.Shackles = null; if (c.IsPlayer) world.Post($"{c.Name}: I don't have any tools"); break; }
                    if (Picking.CutShackles(world, s, job.Wearer, item, world.TickSeconds, Crimes)) { c.Shackles = null; Freed(world, s, c, "Shackles broken!"); }
                    break;
                case ShackleWork.Break:
                    c.Shackles = null;
                    if (Picking.BreakShackles(world, s, job.Wearer, item, Crimes)) Freed(world, s, c, "*Crunck*");
                    else if (c.IsPlayer) world.Post($"{c.Name}: I'm not strong enough to do that");
                    break;
            }
        }
    }

    static void Freed(World world, int s, CharacterCold c, string message)
    {
        world.EmitSound(SoundKind.Unlocked, s, world.Characters.Next[s].Position);
        if (c.IsPlayer) world.Post($"{c.Name}: {message}");
    }
}
