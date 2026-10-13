using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation.Combat;

namespace Meitou.Simulation;

/// <summary>Settings of the <see cref="AggroSystem"/>; every number is an engine choice (docs/simulation.md "Combat").</summary>
public sealed record AggroSettings
{
    /// <summary>A character looks round once every this many ticks (staggered by slot: slot s on the ticks where tick % n == s % n).</summary>
    public int ScanEveryTicks { get; init; } = 15;
    /// <summary>How far an NPC sees an enemy, in world units (dm), before the race's <c>vision range mult</c>. The original's range is <b>Unknown</b>.</summary>
    public float SightRange { get; init; } = 300;
    /// <summary>An NPC fighting farther than this from where its fight began gives up.</summary>
    public float LeashRange { get; init; } = 750;
    /// <summary>Game seconds an NPC that gave up does not pick a new fight by itself (it still answers an attack).</summary>
    public float CalmSeconds { get; init; } = 20;
    /// <summary>Keep a <see cref="AggroSystem.Log"/> of every fight started (the host prints it; tests read it).</summary>
    public bool RecordLog { get; init; }
}

/// <summary>A fight the <see cref="AggroSystem"/> started: who saw whom, the factions, the distance and how many squad mates joined.</summary>
public readonly record struct AggroEntry(long Tick, CharacterId Attacker, CharacterId Target, int AttackerFaction, int TargetFaction, float Distance, int Joined);

/// <summary>
/// Hostility by faction relations (an <b>engine stand-in</b> for the stage 9 AI's senses and target choice, docs/simulation.md "Combat"):
/// <list type="bullet">
/// <item><b>Look</b> (<c>Act</c>, parallel, own slot, reading the last tick's positions through the <see cref="SpatialGrid"/>): an awake NPC with a fighter, not fighting,
/// not treating or resting, whose turn it is (one in <see cref="AggroSettings.ScanEveryTicks"/>, by slot) finds the nearest living, awake character with a body
/// within its sight (<see cref="AggroSettings.SightRange"/> x its race's <c>vision range mult</c>) whose faction its own is hostile to (relation -30 or lower) and
/// that neither faction coexists with; ties go to the lower slot. It emits one effect at that target.</item>
/// <item><b>Decide</b> (<c>Apply</c>, serial commit order): the looker, if still free, and its free squad mates within <see cref="RetaliationSystem.JoinRange"/> (the
/// retaliation join rule) get an <see cref="AttackOrder"/> for the next tick through <see cref="World.Defer"/>. The player's characters never look and never join.</item>
/// <item><b>Leash and stand down</b> (<c>SlowWorld</c>, serial, every tick): an NPC that starts fighting (by this system, retaliation or anything else) has its position
/// kept; beyond <see cref="AggroSettings.LeashRange"/> from it the NPC stops and stays calm (no looking) for <see cref="AggroSettings.CalmSeconds"/>. When its fight is
/// over (target down, or stopped) it goes back to what it did before (wander or follow; a go-to becomes idle, which the roaming squads order again).</item>
/// </list>
/// The live relation table is hashed here once a deed has changed it (<see cref="FactionRelations.Changes"/>).
/// </summary>
public sealed class AggroSystem(FactionRelations relations, AggroSettings? settings = null) : ITickSystem, IStateHashed
{
    /// <summary>The <see cref="Effect.Kind"/> of a sighting: <c>Target</c> is the enemy, <c>Amount</c> the distance.</summary>
    public const int SightEffect = 1;
    const byte NoTask = 0xFF;

    struct Slot
    {
        public int Generation;
        public bool Tracked;
        public Vector2 Origin;
        public byte Task;
        public long CalmUntil;
    }

    Slot[] slots = [];
    int size;
    readonly List<AggroEntry> log = [];
    readonly HashSet<int> taken = [];
    long takenTick = -1;

    public AggroSettings Settings { get; } = settings ?? new AggroSettings();
    public FactionRelations Relations { get; } = relations;

    /// <summary>The fights started since the last <see cref="TakeLog"/> (with <see cref="AggroSettings.RecordLog"/>).</summary>
    public IReadOnlyList<AggroEntry> Log => log;

    /// <summary>Takes the log (the host, between ticks).</summary>
    public AggroEntry[] TakeLog()
    {
        AggroEntry[] all = [.. log];
        log.Clear();
        return all;
    }

    /// <summary>The fights this system started so far.</summary>
    public long Started { get; private set; }

    public void Schedule(World world) => Sync(world.Characters);

    void Sync(CharacterTable table)
    {
        int n = table.HighWater;
        if (slots.Length < n)
        {
            int old = slots.Length;
            Array.Resize(ref slots, Math.Max(n, slots.Length * 2));
            for (int i = old; i < slots.Length; i++) slots[i] = new Slot { Generation = -1, Task = NoTask };
        }
        size = Math.Max(size, n);
        var next = table.Next;
        if (present.Length != Relations.Count) { present = new bool[Relations.Count]; hasEnemy = new bool[Relations.Count]; }
        Array.Clear(present);
        for (int i = 0; i < n; i++)
        {
            int generation = next[i].Alive ? next[i].Generation : -1;
            if (slots[i].Generation != generation) slots[i] = new Slot { Generation = generation, Task = NoTask };
            if (generation >= 0 && table.Cold(i) is { Medical: not null } c && Relations.Contains(c.Faction)) present[c.Faction] = true;
        }
        // Which factions have an enemy among the factions present: a peaceful crowd skips its looks. Worked out again only when the set changes.
        if (present.AsSpan().SequenceEqual(presentBefore) && Relations.Changes == changesBefore) return;
        presentBefore = (bool[])present.Clone();
        changesBefore = Relations.Changes;
        presentList.Clear();
        for (int f = 0; f < present.Length; f++) if (present[f]) presentList.Add(f);
        foreach (int f in presentList)
        {
            hasEnemy[f] = false;
            foreach (int g in presentList)
                if (IsEnemy(Relations, f, g)) { hasEnemy[f] = true; break; }
        }
    }

    bool[] present = [], presentBefore = [], hasEnemy = [];
    long changesBefore = -1;
    readonly List<int> presentList = [];

    // ------------------------------------------------------------------ look

    struct Scan
    {
        public CharacterTable Table;
        public FactionRelations Relations;
        public int Self, Faction;
        public float X, Z, Range2;
        public int Best;
        public float BestD2;
    }

    public void Act(World world, Partition part, EffectBuffer effects)
    {
        var table = world.Characters;
        var prev = table.Previous;
        var next = table.Next;
        int every = Math.Max(1, Settings.ScanEveryTicks);
        int phase = (int)(world.Tick % every);
        int first = part.Start + ((phase - part.Start % every) % every + every) % every;
        for (int i = first; i < part.End; i += every)
        {
            if (!prev[i].Alive || i >= slots.Length || world.Tick < slots[i].CalmUntil) continue;
            byte task = next[i].Task;
            if (task is (byte)CharacterTask.Attack or (byte)CharacterTask.Treat or (byte)CharacterTask.Rest) continue;
            if (table.Cold(i) is not { IsPlayer: false, InCombat: false, Fighter: not null, Medical.Incapacitated: false } cold || !Relations.Contains(cold.Faction) || !hasEnemy[cold.Faction]) continue;
            float range = Settings.SightRange * (cold.Race?.VisionRangeMult ?? 1);
            if (!(range > 0)) continue;
            var scan = new Scan
            {
                Table = table, Relations = Relations, Self = i, Faction = cold.Faction,
                X = prev[i].Position.X, Z = prev[i].Position.Z, Range2 = range * range, Best = -1, BestD2 = float.MaxValue,
            };
            world.Grid.Query(scan.X, scan.Z, range, ref scan, Visit);
            if (scan.Best >= 0) effects.Emit(i, scan.Best, SightEffect, MathF.Sqrt(scan.BestD2));
        }
    }

    static void Visit(int slot, ref Scan s)
    {
        if (slot == s.Self) return;
        ref readonly var other = ref s.Table.Previous[slot];
        float dx = other.Position.X - s.X, dz = other.Position.Z - s.Z, d2 = dx * dx + dz * dz;
        // Strictly nearer only: the grid visits a cell's slots in ascending order, so a tie keeps the lower slot within a cell; across cells the
        // order is the grid's fixed one. Either way it is a function of the state.
        if (d2 > s.Range2 || d2 >= s.BestD2) return;
        if (s.Table.Cold(slot) is not { Medical.Incapacitated: false } c || !IsEnemy(s.Relations, s.Faction, c.Faction)) return;
        s.Best = slot;
        s.BestD2 = d2;
    }

    /// <summary>
    /// Whether a character of faction <paramref name="mine"/> picks a fight with one of <paramref name="theirs"/>: hostile one way (mine to theirs, -30 or lower),
    /// and neither lists the other in <c>coexistence</c> (engine choice: the flag's effect in the original is <b>Unknown</b>, so either side's is enough).
    /// Same faction and allies are never enemies (an ally is at 50 or above, so not hostile).
    /// </summary>
    public static bool IsEnemy(FactionRelations relations, int mine, int theirs) =>
        relations.Contains(theirs) && relations.IsHostile(mine, theirs) && !relations.Coexists(mine, theirs) && !relations.Coexists(theirs, mine);

    // ------------------------------------------------------------------ decide

    public void Apply(World world, in Effect e)
    {
        if (e.Kind != SightEffect) return;
        if (takenTick != world.Tick) { taken.Clear(); takenTick = world.Tick; }
        var table = world.Characters;
        var next = table.Next;
        int i = e.Source, t = e.Target;
        if (!Free(table, i, taken) || !next[t].Alive || table.Cold(t) is not { Medical.Incapacitated: false } target) return;
        var fighters = new List<CharacterId> { table.IdOf(i) };
        taken.Add(i);
        RetaliationSystem.JoinMates(world, table, i, fighters, taken, Free);
        var targetId = table.IdOf(t);
        world.Defer(new AttackOrder(fighters, targetId) { Tick = world.Tick + 1 });
        Started++;
        if (Settings.RecordLog) log.Add(new AggroEntry(world.Tick, fighters[0], targetId, table.Cold(i)!.Faction, target.Faction, e.Amount, fighters.Count - 1));
    }

    static bool Free(CharacterTable table, int slot, HashSet<int> taken)
    {
        if (taken.Contains(slot) || !table.Next[slot].Alive) return false;
        byte task = table.Next[slot].Task;
        if (task is (byte)CharacterTask.Attack or (byte)CharacterTask.Treat or (byte)CharacterTask.Rest) return false;
        return table.Cold(slot) is { IsPlayer: false, InCombat: false, Fighter: not null, Medical.Incapacitated: false };
    }

    // ------------------------------------------------------------------ leash and stand down

    public void SlowWorld(World world)
    {
        var table = world.Characters;
        Sync(table);   // characters spawned this tick
        var next = table.Next;
        float leash2 = Settings.LeashRange * Settings.LeashRange;
        long calm = (long)MathF.Ceiling(Settings.CalmSeconds / world.TickSeconds);
        for (int i = 0; i < table.HighWater; i++)
        {
            ref var n = ref next[i];
            if (!n.Alive) continue;
            var cold = table.Cold(i)!;
            if (cold.IsPlayer) continue;
            ref var s = ref slots[i];
            var here = new Vector2(n.Position.X, n.Position.Z);
            if (n.Task == (byte)CharacterTask.Attack)
            {
                if (!s.Tracked)
                {
                    s.Tracked = true;
                    s.Origin = here;
                }
                else if (Vector2.DistanceSquared(here, s.Origin) > leash2 && world.Tick >= s.CalmUntil)
                {
                    world.Defer(new StopCommand([table.IdOf(i)]) { Tick = world.Tick + 1 });
                    s.CalmUntil = world.Tick + calm;
                }
                continue;
            }
            if (!s.Tracked)
            {
                s.Task = n.Task;   // the routine to go back to after a fight
                continue;
            }
            s.Tracked = false;
            if (cold.Medical is { Incapacitated: true } || cold.InCombat || n.Task != (byte)CharacterTask.Idle) continue;   // down (it wakes into wandering) or busy with something else
            MovementSystem.Stop(table, i);
            n.Task = s.Task is (byte)CharacterTask.Wander or (byte)CharacterTask.Follow or (byte)CharacterTask.Idle ? s.Task
                : s.Task == (byte)CharacterTask.GoTo ? (byte)CharacterTask.Idle : (byte)CharacterTask.Wander;
            n.TaskTime = 0;
        }
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(Relations.Changes);
        if (Relations.Changes > 0)
        {
            int n = Relations.Count;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++) hasher.Add(Relations.Get(a, b));
        }
        hasher.Add(size);
        for (int i = 0; i < size; i++)
        {
            ref readonly var s = ref slots[i];
            hasher.Add(s.Generation);
            hasher.Add(s.Tracked);
            hasher.Add(s.Origin.X);
            hasher.Add(s.Origin.Y);
            hasher.Add(s.Task);
            hasher.Add(s.CalmUntil);
        }
        hasher.Add(Started);
    }
}
