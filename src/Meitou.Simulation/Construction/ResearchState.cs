using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Construction;

namespace Meitou.Simulation.Construction;

/// <summary>A queued research: its RESEARCH id and the game seconds put into it (the save's <c>current prog&lt;k&gt;</c>).</summary>
public sealed class ResearchItem(string id, float seconds)
{
    public string Id { get; } = id;
    public float Seconds { get; set; } = seconds;
}

/// <summary>
/// The player's research (docs/game/buildings-production.md "Research", docs/simulation.md "Production"): the finished RESEARCH ids and the buildings they unlock, the queue, the ids already
/// paid for and the progress of ones taken off the queue. Set at a new game (<see cref="ResearchGate.StartResearch"/>) and when a save loads. <see cref="Unlocked"/> is replaced, never
/// changed in place, so a snapshot may hold it. Hashed only once something changed in play (<see cref="Changed"/>), so worlds without research hash as before.
/// </summary>
public sealed class ResearchState
{
    static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    readonly List<string> completed = [];
    readonly List<ResearchItem> queue = [];
    readonly List<string> paid = [];
    readonly Dictionary<string, float> partial = new(StringComparer.Ordinal);

    /// <summary>The finished RESEARCH ids, in the order they were finished (the save's <c>finished&lt;k&gt;</c> order).</summary>
    public IReadOnlyList<string> Completed => completed;
    /// <summary>The BUILDING ids the completed research unlocks.</summary>
    public IReadOnlySet<string> Unlocked { get; private set; } = Nothing;
    /// <summary>The research queue; the first item is worked.</summary>
    public IReadOnlyList<ResearchItem> Queue => queue;
    /// <summary>Research whose cost was paid (the save's <c>paid&lt;k&gt;</c>): queuing it again costs nothing (<b>Observed</b>, <c>FUN_1408343c0</c>).</summary>
    public IReadOnlyList<string> Paid => paid;
    /// <summary>The seconds of research taken off the queue, kept for when it is queued again.</summary>
    public IReadOnlyDictionary<string, float> Partial => partial;
    /// <summary>The manager's tech level: 1, raised to L + 1 by each finished level upgrade of level L (<b>Observed</b>, <c>FUN_140830880</c>).</summary>
    public int TechLevel { get; private set; } = 1;
    /// <summary>The highest <c>tech level</c> of the player's complete research benches (set by the production system each tick; not saved, not hashed).</summary>
    public int BenchLevel { get; internal set; }
    /// <summary>The research rate of the last tick: the sum of the bench contributions after the 0.6 falloff (the window's CURRENT RATE).</summary>
    public float Rate { get; internal set; }
    /// <summary>Something changed in play: the state joins the world hash from then on.</summary>
    public bool Changed { get; private set; }
    /// <summary>Bumped on every change the host shows.</summary>
    public long Version { get; private set; }

    internal void Touch()
    {
        Version++;
        Changed = true;
    }

    /// <summary>Replaces the completed research (and clears the queue).</summary>
    public void Set(GameDatabase db, IEnumerable<string> ids) => Set(db, ids, [], [], []);

    /// <summary>Replaces the whole state (a save's): finished, queued with their seconds, and paid ids.</summary>
    public void Set(GameDatabase db, IEnumerable<string> ids, IEnumerable<(string Id, float Seconds)> queued, IEnumerable<string> paidIds, IEnumerable<(string Id, float Seconds)> kept)
    {
        completed.Clear();
        foreach (var id in ids)
            if (!completed.Contains(id, StringComparer.Ordinal)) completed.Add(id);
        queue.Clear();
        foreach (var (id, s) in queued)
            if (db.Find(id) is { Type: FcsRecordType.RESEARCH } && !completed.Contains(id, StringComparer.Ordinal) && queue.TrueForAll(q => q.Id != id)) queue.Add(new ResearchItem(id, s));
        paid.Clear();
        foreach (var id in paidIds)
            if (!paid.Contains(id, StringComparer.Ordinal)) paid.Add(id);
        partial.Clear();
        foreach (var (id, s) in kept) partial[id] = s;
        Refresh(db);
        Version++;
        Changed = false;
    }

    void Refresh(GameDatabase db)
    {
        Unlocked = ResearchGate.Unlocked(db, completed);
        int level = 1;
        foreach (var id in completed)
            if (db.Find(id) is { Type: FcsRecordType.RESEARCH } r && r.GetBool("is level upgrade") && r.GetInt("level") is var l && l >= level) level = l + 1;
        TechLevel = level;
    }

    /// <summary>Finishes one research (nothing when it is done already): it leaves the queue and its unlocks apply.</summary>
    public bool Complete(GameDatabase db, string id)
    {
        if (completed.Contains(id, StringComparer.Ordinal)) return false;
        completed.Add(id);
        queue.RemoveAll(q => q.Id == id);
        partial.Remove(id);
        Refresh(db);
        Touch();
        return true;
    }

    public bool IsUnlocked(string building) => Unlocked.Contains(building);
    public bool IsDone(string id) => completed.Contains(id, StringComparer.Ordinal);
    public bool IsQueued(string id) => queue.Exists(q => q.Id == id);

    internal void Enqueue(string id)
    {
        queue.Add(new ResearchItem(id, partial.Remove(id, out float s) ? s : 0));
        if (!paid.Contains(id, StringComparer.Ordinal)) paid.Add(id);
        Touch();
    }

    internal bool Dequeue(string id)
    {
        int i = queue.FindIndex(q => q.Id == id);
        if (i < 0) return false;
        if (queue[i].Seconds > 0) partial[id] = queue[i].Seconds;
        queue.RemoveAt(i);
        Touch();
        return true;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Add(0x52657365);
        h.Add(completed.Count);
        foreach (var id in completed) h.Add(Rng.StableHash(id));
        h.Add(queue.Count);
        foreach (var q in queue) { h.Add(Rng.StableHash(q.Id)); h.Add(q.Seconds); }
        h.Add(paid.Count);
        foreach (var id in paid) h.Add(Rng.StableHash(id));
        foreach (var (id, s) in partial.OrderBy(p => p.Key, StringComparer.Ordinal)) { h.Add(Rng.StableHash(id)); h.Add(s); }
    }
}
