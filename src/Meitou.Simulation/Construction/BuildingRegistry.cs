using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Meitou.Data;
using Meitou.Data.Gameplay.Construction;
using Meitou.Data.World;

namespace Meitou.Simulation.Construction;

/// <summary>A material entry of a site: the ITEM record, the amount needed and the amount handed over so far (whole units).</summary>
public sealed class MaterialSlot(string item, float required)
{
    public string Item { get; } = item;
    public float Required { get; set; } = required;
    public float Delivered { get; set; }
}

/// <summary>
/// A player-built building or construction site (docs/simulation.md "Buildings"; the state is the original's Building+0x160 block, docs/game/buildings-production.md
/// "Construction state"). Serial phases only. <see cref="Progress"/> and <see cref="Target"/> are in material units.
/// </summary>
public sealed class BuildSite
{
    public required string Id { get; init; }
    public required string Record { get; init; }
    /// <summary>The BUILDING's name (messages only; not hashed).</summary>
    public string Name { get; init; } = "";
    public Vector3 Position { get; init; }
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
    public int Floor { get; init; }
    /// <summary>The instance id of the building this one stands in (null outdoors).</summary>
    public string? Parent { get; set; }
    /// <summary>The owning faction, a position in the world's faction list.</summary>
    public int Owner { get; init; }
    public required MaterialSlot[] Materials { get; init; }
    public float Progress { get; set; }
    public float Target { get; set; }
    /// <summary>Progress per game second of one unit of build work: <c>build speed mult</c> / (1200/11).</summary>
    public float Rate { get; init; }
    public bool Complete { get; set; }
    public bool Paused { get; set; }
    /// <summary>The save's <c>destroyed</c> flag; the first tick on it turns the building back into a half-built site (<see cref="ConstructionSystem"/>).</summary>
    public bool Destroyed { get; set; }
    /// <summary>The destroyed flag has been acted on (the building is a ruin being rebuilt).</summary>
    public bool Ruined { get; set; }
    /// <summary>Builders that have applied work to this site this tick (the 0.75 falloff counter, reset each tick); not saved.</summary>
    public int BuilderCounter { get; set; }
    /// <summary>A builder has taken it apart this tick (derived; the snapshot's flag).</summary>
    public bool Dismantling { get; set; }
    /// <summary>The zone the site stands in (its id and save file).</summary>
    public ZoneCoordinate Zone => WorldLayout.ZoneOf(Position.X, Position.Z);

    /// <summary>The materials are all delivered and built: <c>sum(min(delivered, required))</c>, what progress may reach (<b>Verified</b>, <c>FUN_14054a3f0</c>).</summary>
    public float Deliverable
    {
        get
        {
            float sum = 0;
            foreach (var m in Materials) sum += MathF.Min(m.Delivered, m.Required);
            return sum;
        }
    }

    /// <summary>A site that is not complete and has built all it was given: the builder waits for more materials (<c>sum(min(delivered, required)) &lt;= progress</c>).</summary>
    public bool Stalled => !Complete && Deliverable <= Progress;

    internal void Hash(ref StateHasher h)
    {
        h.Add(Rng.StableHash(Id));
        h.Add(Rng.StableHash(Record));
        h.Add(Position);
        h.Add(Rotation.X); h.Add(Rotation.Y); h.Add(Rotation.Z); h.Add(Rotation.W);
        h.Add(Floor);
        h.Add(Parent is null ? 0UL : Rng.StableHash(Parent));
        h.Add(Owner);
        h.Add(Progress);
        h.Add(Target);
        h.Add(Rate);
        h.Add(Complete);
        h.Add(Paused);
        if (Destroyed || Ruined) { h.Add(0x44657374); h.Add(Destroyed); h.Add(Ruined); }
        h.Add(Materials.Length);
        foreach (var m in Materials)
        {
            h.Add(Rng.StableHash(m.Item));
            h.Add(m.Required);
            h.Add(m.Delivered);
        }
    }
}

/// <summary>
/// The player-built sites of the world in creation order (docs/simulation.md "Buildings"). Ids are the original's <c>&lt;n&gt;-zone&lt;X&gt;.&lt;Y&gt;-INGAME</c>, n from a counter per zone that
/// starts above every such id in the loaded save (<see cref="Reserve"/>). Every change that the host or a save must see bumps <see cref="Version"/>; adding, completing, destroying and
/// removing a site also bump <see cref="Revision"/>. Hashed by <see cref="World.StateHash"/> only while it holds a site.
/// </summary>
public sealed partial class BuildingRegistry
{
    readonly List<BuildSite> sites = [];
    readonly Dictionary<string, BuildSite> byId = new(StringComparer.Ordinal);
    readonly Dictionary<ZoneCoordinate, int> counters = [];
    IReadOnlyList<BuildingSnapshot> published = [];
    long publishedVersion = -1;

    public int Count => sites.Count;
    public IReadOnlyList<BuildSite> All => sites;
    public BuildSite? Find(string id) => byId.GetValueOrDefault(id);

    /// <summary>Bumped when a site is added, completed (or turned back into a site), destroyed or removed: the host diffs the snapshot's buildings by id when it changes.</summary>
    public long Revision { get; private set; }
    /// <summary>Bumped by every change to a site (progress included); the published snapshots are rebuilt only then.</summary>
    public long Version { get; private set; }

    /// <summary>Sites that are not complete (the auto-job looks only while there are some).</summary>
    public bool AnyIncomplete
    {
        get
        {
            foreach (var s in sites) if (!s.Complete) return true;
            return false;
        }
    }

    [GeneratedRegex(@"^(\d+)-zone(\d+)\.(\d+)-INGAME$")]
    private static partial Regex IdPattern();

    /// <summary>The zone and number of an id of the pattern <c>&lt;n&gt;-zone&lt;X&gt;.&lt;Y&gt;-INGAME</c>; false for any other id.</summary>
    public static bool TryParseId(string id, out ZoneCoordinate zone, out int n)
    {
        zone = default;
        n = 0;
        var m = IdPattern().Match(id);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return false;
        if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int x) || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int y)) return false;
        zone = new ZoneCoordinate(x, y);
        return true;
    }

    /// <summary>Keeps new ids of the id's zone above its number (an id of another pattern is ignored).</summary>
    public void Reserve(string id)
    {
        if (TryParseId(id, out var zone, out int n)) counters[zone] = Math.Max(counters.GetValueOrDefault(zone), n);
    }

    /// <summary>The id the next site in <paramref name="zone"/> gets (skipping any id in use).</summary>
    public string NewId(ZoneCoordinate zone)
    {
        while (true)
        {
            int n = counters.GetValueOrDefault(zone) + 1;
            counters[zone] = n;
            string id = IdOf(n, zone);
            if (!byId.ContainsKey(id)) return id;
        }
    }

    public static string IdOf(int n, ZoneCoordinate zone) =>
        string.Create(CultureInfo.InvariantCulture, $"{n}-zone{zone.X}.{zone.Y}-INGAME");

    public void Add(BuildSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (!byId.TryAdd(site.Id, site)) throw new InvalidOperationException($"site {site.Id} exists");
        sites.Add(site);
        Reserve(site.Id);
        Revision++;
        Version++;
    }

    public bool Remove(BuildSite site)
    {
        if (!byId.Remove(site.Id)) return false;
        sites.Remove(site);
        Revision++;
        Version++;
        return true;
    }

    /// <summary>Notes a change to a site's fields (progress, delivery, pause); the next snapshot is rebuilt.</summary>
    public void Touch() => Version++;

    /// <summary>Notes a change the host must act on (completed, turned back into a site, destroyed).</summary>
    public void Changed()
    {
        Revision++;
        Version++;
    }

    /// <summary>The snapshots of every site, rebuilt only when something changed since the last call.</summary>
    internal IReadOnlyList<BuildingSnapshot> Published()
    {
        if (publishedVersion == Version) return published;
        var list = new List<BuildingSnapshot>(sites.Count);
        foreach (var s in sites)
        {
            var materials = new MaterialView[s.Materials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = new MaterialView(s.Materials[i].Item, s.Materials[i].Required, s.Materials[i].Delivered);
            list.Add(new BuildingSnapshot(s.Id, s.Record, s.Position, s.Rotation, s.Floor, s.Parent, s.Owner, s.Progress, s.Target, materials, s.Complete, s.Paused) { Dismantling = s.Dismantling });
        }
        publishedVersion = Version;
        return published = list;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Add(0x426C6473);
        var zones = counters.Keys.ToList();
        zones.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        h.Add(zones.Count);
        foreach (var z in zones)
        {
            h.Add(z.X);
            h.Add(z.Y);
            h.Add(counters[z]);
        }
        h.Add(sites.Count);
        foreach (var s in sites) s.Hash(ref h);
    }
}

/// <summary>
/// The player's completed research and what it unlocks (docs/game/buildings-production.md "Build menu"). Set at a new game (<see cref="ResearchGate.StartResearch"/>) and when a save loads;
/// research progress is not built this milestone, so it is static and not hashed. <see cref="Unlocked"/> is replaced, never changed in place, so a snapshot may hold it.
/// </summary>
public sealed class ResearchState
{
    static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    readonly List<string> completed = [];

    /// <summary>The finished RESEARCH ids, in the order they were finished (the save's <c>finished&lt;k&gt;</c> order).</summary>
    public IReadOnlyList<string> Completed => completed;
    /// <summary>The BUILDING ids the completed research unlocks.</summary>
    public IReadOnlySet<string> Unlocked { get; private set; } = Nothing;

    /// <summary>Replaces the completed research.</summary>
    public void Set(GameDatabase db, IEnumerable<string> ids)
    {
        completed.Clear();
        foreach (var id in ids)
            if (!completed.Contains(id, StringComparer.Ordinal)) completed.Add(id);
        Unlocked = ResearchGate.Unlocked(db, completed);
    }

    /// <summary>Finishes one research (nothing when it is done already).</summary>
    public bool Complete(GameDatabase db, string id)
    {
        if (completed.Contains(id, StringComparer.Ordinal)) return false;
        completed.Add(id);
        Unlocked = ResearchGate.Unlocked(db, completed);
        return true;
    }

    public bool IsUnlocked(string building) => Unlocked.Contains(building);
}
