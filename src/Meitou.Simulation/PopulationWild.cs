using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.World;

namespace Meitou.Simulation;

/// <summary>What fills a wild site: the homeless squads of an area sector (6.2) or the roaming pool of a nest (6.3).</summary>
public enum WildKind : byte { Sector, Nest }

/// <summary>
/// A place in the wild that gets squads while its zone is active: an area sector (one zone cell and its BIOME_GROUP) or a nest of a biome group.
/// <see cref="Spots"/> are the seeded candidate centres of a nest (the first walkable one is used when it loads); a sector has none.
/// </summary>
public sealed record WildSite(int Index, WildKind Kind, ZoneCoordinate Zone, int Group, TownData? Nest, int Faction, IReadOnlyList<Vector2> Spots);

/// <summary>
/// The wild population (docs/game/factions-squads-towns.md 6.2, 6.3): homeless squads of the area sectors and the animals (and camps) of the nests. Runs
/// only when <see cref="PopulationData.Biomes"/> is set.
/// <list type="bullet">
/// <item><b>Sectors</b> (6.2). Every zone cell is a sector of its BIOME_GROUP. When its zone activates it gets ceil(P x <c>homeless spawn amount</c>)
/// squads: what the original's area tick reaches for a sector loaded alone (it spawns while the island's population is below P x the sum of the amounts and
/// the sector's own count below its share; with one sector that stops at the ceiling). The island-wide sum over several loaded sectors, the counters a and
/// b and the "town nearby" halving are not modelled (engine choice). Template weights are 6.2's homeless branch,
/// <c>max(0, v0 - k x count)</c> with k = (sum of v0) / (P x amount) and the counts of this sector's own picks; world-state gated templates are skipped.
/// The spot: a random point of the sector, pushed out of the nearest town to twice its radius and out of other towns of another faction to their radius,
/// at least <see cref="PopulationSettings.HomelessPlayerDistance"/> from every player character, walkable; 40 tries (the original's 4 rounds of 10), else
/// that squad is not made. A sector whose nearest town is the player's makes none.</item>
/// <item><b>Nests</b> (6.3). The original adds one nest per area tick until a group has <c>num nests</c> x the nests multiplier; here the layout is made at
/// once from the world seed (engine choice; the end state is the same count): each nest's TOWN is a weighted pick from the group's <c>nests</c> (v0, 0 as
/// 100, the TOWN's <c>world state</c> gate), its place a seeded cell of the group's area with candidate spots away from placed towns, or for groups with
/// <c>nests at fixed markers only</c> one of the placed NEST_MARKER towns of the area. When its zone activates the nest fills its roaming pool to
/// floor(0.7 x <c>nest resident population</c>) members from the TOWN's <c>roaming squads</c> (weight max(0, v0 - count), k = 1; an entry whose expected
/// size passes the free capacity is skipped), the squads wandering round the nest. Squads are not replaced while the nest is loaded (the respawn delay is
/// <b>Unknown</b>); a reload fills it again.</item>
/// <item><b>Loading</b> follows the towns: the same zone gate, the same unload grace; the squads are built on the population thread and taken in at a
/// tick boundary; on unload they are removed. Every roll is keyed by the world seed and the sector's zone or the nest's index and the squad's number, never
/// by the tick, so a reload makes the same squads (their spots can differ when the player stands elsewhere).</item>
/// </list>
/// </summary>
public sealed partial class PopulationSystem
{
    readonly ConcurrentQueue<BuiltWild> builtWild = new();
    List<WildSite>? wildSites;
    SiteState[] wildStates = [];
    readonly Dictionary<ZoneCoordinate, List<int>> wildByZone = [];
    readonly SortedSet<int> wildLoaded = [];
    readonly List<int> wildBuffer = [];

    sealed record BuiltWild(int Site, long LoadId, Vector2 Centre, List<BuiltSquad> Squads);

    /// <summary>The wild sites (sectors and nests), made at the first look of a world with biomes; empty before or without.</summary>
    public IReadOnlyList<WildSite> WildSites => wildSites ?? [];
    public SiteStatus WildStatusOf(int site) => wildStates[site].Status;
    /// <summary>The squad ids a wild site has now.</summary>
    public IReadOnlyList<int> WildSquadsOf(int site) => wildStates[site].Squads;

    readonly List<WildSpawn> wildLog = [];
    /// <summary>A wild squad made: when, by a sector or a nest (its biome group or nest name), the squad, its members.</summary>
    public readonly record struct WildSpawn(long Tick, WildKind Kind, string Place, string Squad, int Members);
    /// <summary>The wild squads made since the last call (for the host's log; not part of the state).</summary>
    public List<WildSpawn> TakeWildLog()
    {
        var list = new List<WildSpawn>(wildLog);
        wildLog.Clear();
        return list;
    }

    // ---- the layout (pure given the data, the settings and the seed) ----

    void EnsureWild(ulong seed)
    {
        if (wildSites is not null || data.Biomes is not { } biomes) return;
        wildSites = BuildWildSites(data, settings, biomes, seed);
        wildStates = new SiteState[wildSites.Count];
        for (int i = 0; i < wildStates.Length; i++) wildStates[i] = new SiteState();
        foreach (var site in wildSites)
        {
            if (!wildByZone.TryGetValue(site.Zone, out var list)) wildByZone[site.Zone] = list = [];
            list.Add(site.Index);
        }
    }

    /// <summary>The sectors (every in-map zone whose group has homeless spawns), then the nests of every group in record order.</summary>
    public static List<WildSite> BuildWildSites(PopulationData data, PopulationSettings settings, BiomeMap biomes, ulong seed)
    {
        var sites = new List<WildSite>();
        var groups = biomes.Groups;
        var cellsOf = new List<(int X, int Z)>[groups.Count];
        for (int g = 0; g < groups.Count; g++) cellsOf[g] = [];
        for (int z = 0; z < WeatherAreas.Cells; z++)
            for (int x = 0; x < WeatherAreas.Cells; x++)
            {
                int g = biomes.GroupOf(x, z);
                if (g < 0) continue;
                cellsOf[g].Add((x, z));
                if (groups[g].HomelessSpawnAmount > 0 && groups[g].HomelessSpawns.Count > 0)
                    sites.Add(new WildSite(sites.Count, WildKind.Sector, new ZoneCoordinate(x, z), g, null, -1, []));
            }

        bool Gated(string townId) => data.Db.Find(townId) is not { } r || r.GetReferences("world state").Any(w => !data.States.IsTrue(w.TargetStringId));
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            int count = (int)MathF.Round(group.NumNests * settings.NestsMultiplier);
            if (count <= 0 || group.Nests.Count == 0 || cellsOf[g].Count == 0) continue;
            ulong groupKey = Rng.Mix(SeedSalts.NestLayout ^ Rng.StableHash(group.Id));
            List<TownSite>? markers = null;
            if (group.NestsAtFixedMarkersOnly)
            {
                markers = [.. data.Sites.Where(s => s.Town.Type == TownType.NestMarker
                    && WeatherAreas.CellOf(s.Position.X, s.Position.Z) is var (cx, cz) && biomes.GroupOf(cx, cz) == g)];
                count = Math.Min(count, markers.Count);
            }
            for (int i = 0; i < count; i++)
            {
                ulong key = Rng.Mix(groupKey ^ (ulong)i << 16);
                ulong counter = 0;
                ulong Next() => Rng.Hash(seed, key, RngPurpose.Spawn, counter++);
                if (SquadFactory.WeightedPick(group.Nests, Gated, Next) is not { } townId || data.Db.Find(townId) is not { } record) continue;
                var town = TownData.From(record);
                var spots = new List<Vector2>();
                if (markers is not null)
                {
                    // A seeded draw without repeats: marker i of a shuffled order.
                    int m = i + Rng.Int(Next(), markers.Count - i);
                    (markers[i], markers[m]) = (markers[m], markers[i]);
                    spots.Add(new Vector2(markers[i].Position.X, markers[i].Position.Z));
                }
                else
                {
                    var (cx, cz) = cellsOf[g][Rng.Int(Next(), cellsOf[g].Count)];
                    var (minX, minZ, maxX, maxZ) = WeatherAreas.CellRect(cx, cz);
                    for (int t = 0; t < 16 && spots.Count < 8; t++)
                    {
                        var p = new Vector2(minX + Rng.Float(Next()) * (maxX - minX), minZ + Rng.Float(Next()) * (maxZ - minZ));
                        if (!NearTown(data, p, 2)) spots.Add(p);
                    }
                    if (spots.Count == 0) continue;
                }
                var zone = WorldLayout.ZoneOf(spots[0].X, spots[0].Y);
                int faction = town.Faction is { } f ? data.FactionIndex(f) : -1;
                sites.Add(new WildSite(sites.Count, WildKind.Nest, zone, g, town, faction, spots));
            }
        }
        return sites;
    }

    /// <summary>Whether a point is within <paramref name="share"/> x the radius of a placed town (markers and points of interest do not count).</summary>
    static bool NearTown(PopulationData data, Vector2 p, float share)
    {
        foreach (var s in data.Sites)
        {
            if (s.Town.Type is TownType.NestMarker or TownType.PointOfInterest or TownType.Null) continue;
            float r = Math.Max(s.Town.SizeRadius * s.Town.TownRadiusMult, 100) * share;
            if (Vector2.DistanceSquared(p, new Vector2(s.Position.X, s.Position.Z)) < r * r) return true;
        }
        return false;
    }

    // ---- the look, in the slow phase (after the towns) ----

    void StepWild(World world, long grace)
    {
        EnsureWild(world.Seed);
        if (wildSites is null) return;
        wildBuffer.Clear();
        foreach (var zone in active)
            if (wildByZone.TryGetValue(zone, out var list)) wildBuffer.AddRange(list);
        wildBuffer.Sort();
        foreach (int i in wildBuffer)
        {
            var state = wildStates[i];
            if (state.Status == SiteStatus.Unloaded) LoadWild(world, wildSites[i], state);
            else state.UnloadAtTick = -1;
        }
        wildBuffer.Clear();
        wildBuffer.AddRange(wildLoaded);
        foreach (int i in wildBuffer)
        {
            var state = wildStates[i];
            if (active.Contains(wildSites[i].Zone)) continue;
            if (state.UnloadAtTick < 0) state.UnloadAtTick = world.Tick + grace;
            else if (world.Tick >= state.UnloadAtTick)
            {
                Unload(world, state);
                wildLoaded.Remove(i);
            }
        }
    }

    void LoadWild(World world, WildSite site, SiteState state)
    {
        if (ZoneGate is { } gate && !gate(site.Zone)) return;   // retried at the next look
        state.Status = SiteStatus.Loading;
        state.LoadId++;
        wildLoaded.Add(site.Index);
        long loadId = state.LoadId;
        ulong seed = world.Seed;
        var walk = world.Walkability;
        var players = PlayerSpots(world);
        if (work is null) SpawnWild(world, BuildWild(site, loadId, seed, walk, players));
        else work.Add(() => builtWild.Enqueue(BuildWild(site, loadId, seed, walk, players)));
    }

    static Vector2[] PlayerSpots(World world)
    {
        var list = new List<Vector2>();
        var table = world.Characters;
        for (int i = 0; i < table.Previous.Length; i++)
            if (table.Previous[i].Alive && table.Cold(i) is { IsPlayer: true }) list.Add(new Vector2(table.Previous[i].Position.X, table.Previous[i].Position.Z));
        return [.. list];
    }

    void DrainWild(World world)
    {
        if (builtWild.IsEmpty) return;
        var ready = new List<BuiltWild>();
        while (builtWild.TryDequeue(out var b)) ready.Add(b);
        ready.Sort(static (a, b) => a.Site.CompareTo(b.Site));
        foreach (var b in ready) SpawnWild(world, b);
    }

    void SpawnWild(World world, BuiltWild b)
    {
        var state = wildStates[b.Site];
        if (state.Status != SiteStatus.Loading || state.LoadId != b.LoadId) return;   // unloaded meanwhile
        var site = wildSites![b.Site];
        foreach (var built in b.Squads)
        {
            var squad = SpawnSquad(world, built, null, 0);
            if (site.Kind == WildKind.Nest)
            {
                squad.HomeCentre = b.Centre;
                squad.HomeRadius = NestRadius(site.Nest!);
            }
            else squad.HomeRadius = settings.HomelessHomeRadius;
            state.Squads.Add(squad.Id);
            if (wildLog.Count < 256) wildLog.Add(new WildSpawn(world.Tick, site.Kind, site.Kind == WildKind.Nest ? site.Nest!.Name : data.Biomes!.Groups[site.Group].Name, squad.Name, squad.Members.Count));
        }
        state.Status = SiteStatus.Loaded;
        state.UnloadAtTick = -1;
    }

    static float NestRadius(TownData nest) => Math.Max(nest.SizeRadius * nest.TownRadiusMult, 100);

    // ---- building (pure given its arguments; any thread) ----

    BuiltWild BuildWild(WildSite site, long loadId, ulong seed, IWalkability walk, Vector2[] players) =>
        site.Kind == WildKind.Nest ? BuildNest(site, loadId, seed, walk, players) : BuildSector(site, loadId, seed, walk, players);

    /// <summary>The number of homeless squads a sector loaded on its own reaches (6.2): ceil(P x amount).</summary>
    public static int HomelessCount(BiomeGroupData group, float populationMultiplier) =>
        (int)MathF.Ceiling(MathF.Max(0, populationMultiplier * group.HomelessSpawnAmount) - 1e-4f);

    /// <summary>
    /// The template weights of 6.2's homeless branch for one sector: <c>max(0, v0 - k x count)</c>, k = (sum of v0) / (P x amount), with
    /// <paramref name="counts"/> the squads of each entry already made; entries that are no template or are gated get 0.
    /// </summary>
    public static float[] HomelessWeights(BiomeGroupData group, float populationMultiplier, IReadOnlyList<int> counts, Func<string, bool> usable)
    {
        var entries = group.HomelessSpawns;
        var w = new float[entries.Count];
        float sum = 0;
        foreach (var e in entries) sum += Math.Max(0, e.V0);
        float target = populationMultiplier * group.HomelessSpawnAmount;
        if (target <= 0 || sum <= 0) return w;
        float k = sum / target;
        for (int i = 0; i < entries.Count; i++)
            w[i] = entries[i].V0 > 0 && usable(entries[i].Id) ? MathF.Max(0, entries[i].V0 - k * counts[i]) : 0;
        return w;
    }

    /// <summary>One roll over a weight table (the running sum that first exceeds roll x total), or -1 when every weight is 0.</summary>
    public static int Pick(IReadOnlyList<float> weights, ulong roll)
    {
        float total = 0;
        foreach (var w in weights) total += w;
        if (total <= 0) return -1;
        float pick = Rng.Float(roll) * total;
        int last = -1;
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0) continue;
            last = i;
            if ((pick -= weights[i]) < 0) return i;
        }
        return last;
    }

    bool TemplateUsable(string id) =>
        data.Db.Find(id) is { } r && SquadFactory.IsTemplate(r) && SquadTemplate.From(r).WorldState.All(w => data.States.IsTrue(w.Id));

    BuiltWild BuildSector(WildSite site, long loadId, ulong seed, IWalkability walk, Vector2[] players)
    {
        var squads = new List<BuiltSquad>();
        var group = data.Biomes!.Groups[site.Group];
        var (minX, minZ, maxX, maxZ) = WeatherAreas.CellRect(site.Zone.X, site.Zone.Y);
        var centre = new Vector2((minX + maxX) / 2, (minZ + maxZ) / 2);
        // A sector whose nearest town is the player's makes nothing (6.2).
        if (NearestTown(centre, out _) is { } nearest && data.PlayerFaction >= 0 && data.SiteFactions[nearest.Index] == data.PlayerFaction)
            return new BuiltWild(site.Index, loadId, centre, squads);
        int n = HomelessCount(group, settings.PopulationMultiplier);
        var counts = new int[group.HomelessSpawns.Count];
        for (int s = 0; s < n; s++)
        {
            ulong key = Rng.Mix(SeedSalts.HomelessSquad ^ (ulong)site.Zone.X << 40 ^ (ulong)site.Zone.Y << 24 ^ (uint)s);
            var weights = HomelessWeights(group, settings.PopulationMultiplier, counts, TemplateUsable);
            int e = Pick(weights, Rng.Hash(seed, key, RngPurpose.Spawn, SeedSalts.HomelessPick));
            if (e < 0) break;
            counts[e]++;
            var template = SquadTemplate.From(data.Db.Find(group.HomelessSpawns[e].Id)!);
            int faction = template.Faction is { } tf ? data.FactionIndex(tf) : -1;
            if (PlaceInSector(minX, minZ, maxX, maxZ, faction, seed, key, walk, players) is not { } spot) continue;
            if (BuildOne(template, faction, faction >= 0 ? data.Factions[faction] : null, key, seed, spot) is { } one) squads.Add(one);
        }
        return new BuiltWild(site.Index, loadId, centre, squads);
    }

    TownSite? NearestTown(Vector2 p, out float distance)
    {
        TownSite? best = null;
        distance = float.MaxValue;
        foreach (var s in data.Sites)
        {
            if (s.Town.Type is TownType.NestMarker or TownType.PointOfInterest or TownType.Null) continue;
            float d = Vector2.Distance(p, new Vector2(s.Position.X, s.Position.Z));
            if (d < distance) (best, distance) = (s, d);
        }
        return best;
    }

    /// <summary>The spot of a homeless squad (6.2, <c>FUN_1408f50d0</c>): 40 seeded tries of a point of the sector, pushed out of towns and away from the player.</summary>
    Vector2? PlaceInSector(float minX, float minZ, float maxX, float maxZ, int faction, ulong seed, ulong key, IWalkability walk, Vector2[] players)
    {
        for (int t = 0; t < 40; t++)
        {
            ulong roll = Rng.Hash(seed, key, RngPurpose.Spawn, SeedSalts.WildPlace + (ulong)t);
            var p = new Vector2(minX + Rng.Float(roll) * (maxX - minX), minZ + Rng.Float(Rng.Mix(roll)) * (maxZ - minZ));
            if (NearestTown(p, out _) is { } nearest) p = PushOut(p, nearest, 2);
            foreach (var s in data.Sites)
            {
                if (s.Town.Type is TownType.NestMarker or TownType.PointOfInterest or TownType.Null) continue;
                if (data.SiteFactions[s.Index] != faction) p = PushOut(p, s, 1);
            }
            bool tooClose = false;
            foreach (var at in players)
                if (Vector2.DistanceSquared(at, p) < settings.HomelessPlayerDistance * settings.HomelessPlayerDistance) { tooClose = true; break; }
            if (tooClose || !walk.IsWalkable(p.X, p.Y)) continue;
            return p;
        }
        return null;
    }

    static Vector2 PushOut(Vector2 p, TownSite town, float share)
    {
        var c = new Vector2(town.Position.X, town.Position.Z);
        float r = town.Town.SizeRadius * town.Town.TownRadiusMult * share;
        var d = p - c;
        float len = d.Length();
        if (len >= r || r <= 0) return p;
        return len < 1e-3f ? c + new Vector2(r, 0) : c + d / len * r;
    }

    BuiltWild BuildNest(WildSite site, long loadId, ulong seed, IWalkability walk, Vector2[] players)
    {
        var squads = new List<BuiltSquad>();
        var nest = site.Nest!;
        Vector2? centre = null;
        foreach (var s in site.Spots)
            if (walk.IsWalkable(s.X, s.Y)) { centre = s; break; }
        if (centre is not { } c) return new BuiltWild(site.Index, loadId, site.Spots[0], squads);   // no walkable spot: the nest is dormant this load
        int cap = (int)MathF.Floor(settings.NestShare * nest.NestResidentPopulation);
        var entries = nest.RoamingSquads;
        var counts = new int[entries.Count];
        var templates = new SquadTemplate?[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].V0 > 0 && TemplateUsable(entries[i].Id)) templates[i] = SquadTemplate.From(data.Db.Find(entries[i].Id)!);
        float radius = NestRadius(nest);
        int members = 0;
        var weights = new float[entries.Count];
        for (int s = 0; s < 64 && members < cap; s++)
        {
            for (int i = 0; i < entries.Count; i++)
                weights[i] = templates[i] is { } t && SquadFactory.ExpectedSize(t, settings.SquadSizeMultiplier) <= cap - members
                    ? MathF.Max(0, entries[i].V0 - counts[i]) : 0;
            ulong key = Rng.Mix(SeedSalts.NestSquad ^ (ulong)site.Index << 32 ^ (uint)s);
            int e = Pick(weights, Rng.Hash(seed, key, RngPurpose.Spawn, SeedSalts.NestPick));
            if (e < 0) break;
            counts[e]++;
            var template = templates[e]!;
            int faction = template.Faction is { } tf && data.FactionIndex(tf) is var ti and >= 0 ? ti : site.Faction;
            Vector2? spot = null;
            for (int t = 0; t < 12 && spot is null; t++)
            {
                var p = Rng.PointInDisc(Rng.Hash(seed, key, RngPurpose.Spawn, SeedSalts.WildPlace + (ulong)t), c, radius * 0.6f);
                bool tooClose = false;
                foreach (var at in players)
                    if (Vector2.DistanceSquared(at, p) < settings.NestPlayerDistance * settings.NestPlayerDistance) { tooClose = true; break; }
                if (!tooClose && walk.IsWalkable(p.X, p.Y)) spot = p;
            }
            if (spot is null) continue;
            if (BuildOne(template, faction, faction >= 0 ? data.Factions[faction] : null, key, seed, spot.Value) is not { } one) continue;
            squads.Add(one);
            members += one.Plan.TotalMembers;
        }
        return new BuiltWild(site.Index, loadId, c, squads);
    }
}
