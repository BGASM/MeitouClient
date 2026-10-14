using System.Numerics;
using Meitou.Data.Gameplay.Ai;

namespace Meitou.Simulation.Ai;

/// <summary>
/// A squad's AI state (docs/game/ai.md "Blackboard update"): its packages by slot, the package running and the role lists it loaded. Changed only in
/// <see cref="AiSystem"/>'s serial step; the members' think reads it.
/// </summary>
public sealed class Blackboard
{
    static readonly IReadOnlyList<AiGoal>[] NoRoles = [[], [], [], [], []];

    readonly SortedDictionary<int, List<AiPackage>> slots = [];

    public Blackboard(IEnumerable<SlottedPackage> packages)
    {
        foreach (var (slot, package) in packages)
        {
            if (!slots.TryGetValue(slot, out var list)) slots[slot] = list = [];
            list.Add(package);
        }
    }

    /// <summary>The package running (null: none could start; the members take only their default goals).</summary>
    public AiPackage? Current { get; private set; }
    public int CurrentSlot { get; private set; } = -1;
    /// <summary>The current package's place in its slot's list (-1: none).</summary>
    public int CurrentIndex { get; private set; } = -1;
    /// <summary>Raised whenever another package starts, so the members choose again at once.</summary>
    public int Version { get; private set; }
    /// <summary>The current package's goals per <see cref="AiRole"/>.</summary>
    public IReadOnlyList<AiGoal>[] Roles { get; private set; } = NoRoles;
    /// <summary>How many members act out each goal type (the crowding count of the original's <c>TaskInfo</c>), as the last serial step counted them.</summary>
    public Dictionary<TaskType, int> Crowd { get; } = [];
    /// <summary>All packages in slot order (highest first), as listed.</summary>
    public IEnumerable<SlottedPackage> Packages => slots.Reverse().SelectMany(s => s.Value.Select(p => new SlottedPackage(s.Key, p)));

    /// <summary>
    /// One pass of the original's package choice (docs/game/ai.md "Blackboard update", Verified): slots from the highest down until one reports success. In a slot
    /// the list is scanned in order: the current package stays while it is not finished, or hands over to its <c>Leads to</c> package when it is; an entry before it
    /// that can start pre-empts it; a lone slot-0 package starts even when it cannot. Plain packages can always start and never finish (the base class);
    /// the subclasses (patrols, wars, contracts and the rest) are not built, so here they never start except as that lone slot-0 package, and never finish.
    /// </summary>
    public void Update()
    {
        foreach (var (slot, list) in slots.Reverse())
            if (TrySlot(slot, list)) return;
    }

    bool TrySlot(int slot, List<AiPackage> list)
    {
        if (slot == 0 && list.Count == 1)
        {
            if (Current != list[0]) Start(list[0], 0);
            return true;
        }
        foreach (var p in list)
        {
            if (p == Current)
            {
                if (!Finished(p)) return true;
                if (p.LeadsTo is { } next && Find(next) is { } follow)
                {
                    Start(follow.Package, follow.Slot);
                    return true;
                }
                return false;
            }
            if (CanStart(p))
            {
                Start(p, slot);
                return true;
            }
        }
        return false;
    }

    static bool CanStart(AiPackage p) => p.IsPlain;
    static bool Finished(AiPackage p) => false;

    SlottedPackage? Find(string id)
    {
        foreach (var (slot, list) in slots)
            foreach (var p in list)
                if (p.Id == id) return new SlottedPackage(slot, p);
        return null;
    }

    void Start(AiPackage package, int slot)
    {
        Current = package;
        CurrentSlot = slot;
        CurrentIndex = slots[slot].IndexOf(package);
        Roles = package.Roles;
        Version++;
    }

    internal void Hash(ref StateHasher hasher)
    {
        hasher.Add(CurrentSlot);
        hasher.Add(Version);
        hasher.Add(CurrentIndex);
    }
}

/// <summary>What the AI decided for one character (<see cref="CharacterCold.Ai"/>): the goal it acts out, where its post is, where it mills about.</summary>
public sealed class AiMind
{
    /// <summary>The goal being acted out (null before the first think).</summary>
    public AiGoal? Goal { get; set; }
    public AiBehaviour Behaviour { get; set; }
    /// <summary>The <see cref="Blackboard.Version"/> the goal was chosen under.</summary>
    public int Version { get; set; } = -1;
    /// <summary>Its own spot: where it stood when the AI took it over (the stand-in for a node of its home building).</summary>
    public Vector2 Post { get; set; }
    /// <summary>The centre and radius <see cref="MovementSystem"/>'s wander uses instead of the squad's home (radius 0: the squad's).</summary>
    public Vector2 WanderCentre { get; set; }
    public float WanderRadius { get; set; }
    /// <summary>It was sent walking to its post and has not arrived.</summary>
    public bool Walking { get; set; }
    /// <summary>Walks to its post in a row that ended elsewhere (no way there, or interrupted); at <see cref="AiSystem.MaxFailedWalks"/> the spot it stands on becomes its post.</summary>
    public int FailedWalks { get; set; }

    internal void Hash(ref StateHasher hasher)
    {
        hasher.Add((int)(Goal?.Task.Type ?? TaskType.None));
        hasher.Add((int)Behaviour);
        hasher.Add(Version);
        hasher.Add(Post);
        hasher.Add(WanderCentre);
        hasher.Add(WanderRadius);
        hasher.Add(Walking);
        hasher.Add(FailedWalks);
    }
}
