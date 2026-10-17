using Meitou.Simulation.Items;
using Meitou.Simulation.Bodies;
using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.World;

namespace Meitou.Simulation;

/// <summary>Makes the drawable half of a character (rolled by <see cref="CharacterGenerator"/>); null when it cannot be made.</summary>

/// <summary>
/// Zone activation and town residents (docs/game/game-loop.md "Zones", factions-squads-towns.md 6.4). The camera's focus (a
/// <see cref="FocusCommand"/>) activates every zone overlapping the box of +-340 units round it, plus a ring of one more zone when
/// <c>Fast zone hopping</c> is on (<see cref="ZoneActivation"/>). A placed town in an active zone gets its resident squads (squad
/// factory, then characters rolled by the generator); when its zone is no longer active for <see cref="PopulationSettings.UnloadGraceSeconds"/>
/// its characters are removed (the original turns the squads into abstract "unloaded" ones, which the AI stage will do). The
/// characters are built on a background thread when <see cref="PopulationSettings.Background"/> is set and taken in at the first tick
/// after they are ready.
/// <para>Residents (docs/game/factions-squads-towns.md "Town residents"): the resident table (<see cref="ResidentEntries"/>) is housed in the town's buildings
/// and each squad made at a node of its home (<c>PopulationHomes.cs</c>); a town without building data puts each entry's v0 squads at random spots within
/// <see cref="PopulationSettings.PlacementRadiusShare"/> of the town radius round its placed position, on dry ground (engine choice). Bar squads, the roaming squads (<c>PopulationRoaming.cs</c>) and the wild, homeless squads and nests (<c>PopulationWild.cs</c>), are made here too.</para>
/// </summary>
public sealed partial class PopulationSystem : ITickSystem, IDisposable
{
    readonly PopulationData data;
    readonly PopulationSettings settings;
    readonly CharacterAssembly assembly;
    readonly SiteState[] states;
    // The zones the towns are centred in (a town is made when its zone is active).
    readonly HashSet<ZoneCoordinate> centres;
    readonly ConcurrentQueue<BuiltTown> built = new();
    readonly BlockingCollection<Action>? work;
    readonly Thread? worker;
    Vector3? focus;
    // The zones active as of the last look, and the set the next look fills (swapped, so a look allocates nothing; the host reads ActiveZones within a frame only).
    HashSet<ZoneCoordinate> active = [], spareActive = [];
    readonly List<Vector3> foci = [];

    sealed class SiteState
    {
        public SiteStatus Status;
        public long UnloadAtTick = -1;
        public long LoadId;
        public readonly List<int> Squads = [];
        /// <summary>A nest's top-up is being built.</summary>
        public bool Pending;
        /// <summary>The nest rolls used since it loaded (a top-up's keys follow them).</summary>
        public int Made;
    }

    public PopulationSystem(PopulationData data, PopulationSettings? settings = null)
    {
        this.data = data;
        this.settings = settings ?? new PopulationSettings();
        assembly = new CharacterAssembly(data);
        states = new SiteState[data.Sites.Count];
        for (int i = 0; i < states.Length; i++) states[i] = new SiteState();
        centres = [.. data.Sites.Select(s => s.Zone)];
        KeepRoamers = this.settings.KeepRoamers;
        OffscreenFights = this.settings.OffscreenFights;
        OffscreenHealing = this.settings.OffscreenHealing;
        if (!this.settings.Background) return;
        work = [];
        worker = new Thread(() => { foreach (var job in work.GetConsumingEnumerable()) job(); }) { IsBackground = true, Name = "Meitou.Sim population" };
        worker.Start();
    }

    /// <summary>The off-screen switches (<see cref="PopulationSettings.KeepRoamers"/> and the next two), changed live by the host.</summary>
    public bool KeepRoamers { get; set; }
    public bool OffscreenFights { get; set; }
    public bool OffscreenHealing { get; set; }

    public PopulationData Data => data;
    public PopulationSettings Settings => settings;
    public SiteStatus StatusOf(int site) => states[site].Status;
    /// <summary>The zones active as of the last look.</summary>
    public IReadOnlyCollection<ZoneCoordinate> ActiveZones => active;
    public Vector3? Focus => focus;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var c in commands)
            if (c is FocusCommand f) focus = f.Position;

        var ready = new List<BuiltTown>();
        while (built.TryDequeue(out var b)) ready.Add(b);
        ready.Sort(static (a, b) => a.Site.CompareTo(b.Site));
        foreach (var b in ready)
        {
            var state = states[b.Site];
            if (state.Status != SiteStatus.Loading || state.LoadId != b.LoadId) continue;   // no longer wanted
            Spawn(world, b);
            state.Status = SiteStatus.Loaded;
            state.UnloadAtTick = -1;
        }
        DrainRoamers(world);
        DrainWild(world);
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % settings.CheckEveryTicks != 0) return;
        RefreshZones(world);
        Discover(world);
        StepRoaming(world);
    }

    /// <summary>The XZ distance at which a player character explores a town (and discovers it).</summary>
    public const float ExploreRange = 400;
    /// <summary>The XZ distance at which a player character discovers a town: doubled for a TOWN of type Town, halved for Nest and Ruins (docs/game/ui-screens.md "Map").</summary>
    public const float DiscoverRange = 8500;
    static float DiscoverRangeOf(TownType type) => DiscoverRange * (type == TownType.Town ? 2 : type is TownType.Nest or TownType.Ruins ? 0.5f : 1);

    /// <summary>Where each wild nest stood when it last loaded (null before), for the map.</summary>
    Vector2?[] wildCentres = [];

    /// <summary>
    /// Towns and camps a player character comes within <see cref="ExploreRange"/> of become discovered and explored; ones not yet discovered within
    /// <see cref="DiscoverRangeOf"/> become discovered, with a "Discovered" message for a town (none for nests and camps).
    /// </summary>
    void Discover(World world)
    {
        if (!world.Player.Exists) return;
        var prev = world.Characters.Previous;
        var player = world.Player;
        for (int i = 0; i < prev.Length; i++)
        {
            if (!prev[i].Alive || world.Characters.Cold(i) is not { IsPlayer: true }) continue;
            var at = new Vector2(prev[i].Position.X, prev[i].Position.Z);
            foreach (var site in data.Sites)
            {
                if (site.Town.Type is TownType.NestMarker or TownType.Null) continue;   // never on the map
                float d2 = Vector2.DistanceSquared(at, new(site.Position.X, site.Position.Z)), far = DiscoverRangeOf(site.Town.Type);
                if (d2 <= ExploreRange * ExploreRange) player.Know(site.Index, TownKnowledge.Discovered | TownKnowledge.Explored);
                else if (d2 <= far * far && player.Know(site.Index, TownKnowledge.Discovered) && site.Town.Type != TownType.Nest) world.Post($"Discovered {site.Town.Name}");
            }
            var wild = WildSites;
            for (int w = 0; w < wild.Count && w < wildCentres.Length; w++)
            {
                if (wild[w] is not { Kind: WildKind.Nest, Nest: { } nest } site || wildCentres[w] is not { } centre) continue;
                float d2 = Vector2.DistanceSquared(at, centre), far = DiscoverRangeOf(TownType.Nest);
                var flags = d2 <= ExploreRange * ExploreRange ? TownKnowledge.Discovered | TownKnowledge.Explored : d2 <= far * far ? TownKnowledge.Discovered : TownKnowledge.None;
                if (flags == TownKnowledge.None) continue;
                var faction = site.Faction >= 0 && site.Faction < data.Factions.Count ? data.Factions[site.Faction] : null;
                player.KnowCamp(new CampView(w, nest.Id, nest.Name, faction?.Id ?? nest.Faction ?? "", faction?.NotReal ?? false, new Vector3(centre.X, 0, centre.Y), flags), flags);
            }
        }
        // A camp known from a save gets its place once its nest has loaded (never when the wild layout gives that site another nest).
        foreach (var camp in player.Camps)
            if (camp.Position is null && camp.Site >= 0 && camp.Site < wildCentres.Length && WildSites[camp.Site].Nest?.Id == camp.TownId && wildCentres[camp.Site] is { } c)
                player.KnowCamp(camp with { Position = new Vector3(c.X, 0, c.Y) }, TownKnowledge.None);
    }

    void RefreshZones(World world)
    {
        // The player's characters activate the zones round them (game-loop.md "Zones": the player's faction); the camera focus does only without a player.
        foci.Clear();
        if (world.Player.Exists)
        {
            var prev = world.Characters.Previous;
            for (int i = 0; i < prev.Length; i++)
                if (prev[i].Alive && world.Characters.Cold(i) is { IsPlayer: true }) foci.Add(prev[i].Position);
        }
        if (foci.Count == 0 && focus is { } camera) foci.Add(camera);
        if (foci.Count == 0) return;
        (active, spareActive) = (spareActive, active);
        active.Clear();
        foreach (var f in foci) ZoneActivation.AddZonesAround(f, settings.FastZoneHopping ? 1 : 0, active);
        world.VisitedZones.UnionWith(active);
        long grace = (long)Math.Ceiling(settings.UnloadGraceSeconds / world.TickSeconds);
        foreach (var site in data.Sites)
        {
            var state = states[site.Index];
            bool wanted = active.Contains(site.Zone);
            switch (state.Status)
            {
                case SiteStatus.Unloaded when wanted:
                    Load(world, site, state);
                    break;
                case SiteStatus.Loaded or SiteStatus.Loading when wanted:
                    state.UnloadAtTick = -1;
                    break;
                case SiteStatus.Loaded or SiteStatus.Loading:
                    if (state.UnloadAtTick < 0) state.UnloadAtTick = world.Tick + grace;
                    else if (world.Tick >= state.UnloadAtTick) Unload(world, site.Index, state);
                    break;
            }
        }
        StepWild(world, grace);
    }

    /// <summary>Whether a zone may be loaded now: the host holds a town back until its navmesh is ready, so residents are not placed inside buildings of an open-ground stand-in (null: always).</summary>
    public Func<ZoneCoordinate, bool>? ZoneGate { get; set; }

    void Load(World world, TownSite site, SiteState state)
    {
        if (ZoneGate is { } gate && !gate(site.Zone)) return;   // retried at the next look
        state.Status = SiteStatus.Loading;
        state.LoadId++;
        long loadId = state.LoadId;
        ulong seed = world.Seed;
        var walk = world.Walkability;
        EnsurePools(world);
        // A town made once (its pools say so) never makes residents again; its own squads that lost too many are made afresh (docs/simulation.md "Off-screen tier").
        bool held = HoldsSquads(world, site.Index) || PoolsOf(site.Index) is { Started: true };
        if (held) CheckRegeneration(world, site.Index);
        if (work is null)
        {
            Spawn(world, BuildTown(site, loadId, seed, walk, held));
            state.Status = SiteStatus.Loaded;
        }
        else
        {
            work.Add(() => built.Enqueue(BuildTown(site, loadId, seed, walk, held)));
        }
    }

    /// <summary>
    /// The town leaves the world: each squad it made becomes a pinned platoon that keeps its characters (<see cref="StoredSquad"/>), so the town is made of them next
    /// time and a save writes them (docs/simulation.md "Population"). A squad with no one alive left is dropped.
    /// </summary>
    void Unload(World world, int site, SiteState state)
    {
        foreach (int id in state.Squads)
        {
            if (world.Squads.Find(id) is not { } squad) continue;
            var stored = new StoredSquad { Name = squad.Name, HomeBuilding = squad.HomeBuilding, Shop = squad.Shop, Package = squad.Ai?.Current?.Id };
            var at = Store(world, squad, stored);
            world.Squads.Remove(id);
            if (stored.Members.Count == 0) continue;
            world.Platoons.Add(new Platoon
            {
                Id = world.Platoons.NextId(), Origin = site, TemplateId = squad.TemplateId, Faction = squad.Faction, Key = 0, Size = Math.Max(squad.SpawnSize, stored.Members.Count),
                Position = at, Money = squad.Money, Package = stored.Package, Stored = stored,
            });
        }
        Reset(state);
    }

    /// <summary>A wild site leaves the world: its squads are removed (the population makes them again).</summary>
    void Drop(World world, SiteState state)
    {
        var table = world.Characters;
        foreach (int id in state.Squads)
        {
            if (world.Squads.Find(id) is not { } squad) continue;
            foreach (var m in squad.Members) table.Remove(m);
            world.Squads.Remove(id);
        }
        Reset(state);
    }

    static void Reset(SiteState state)
    {
        state.Squads.Clear();
        state.Pending = false;
        state.Made = 0;
        state.Status = SiteStatus.Unloaded;
        state.UnloadAtTick = -1;
        state.LoadId++;   // a build still in flight is dropped when it arrives
    }

    /// <summary>
    /// Whether a loaded save holds squads of the town: a pinned saved platoon of the site, made now or not. They are then the town's whole population and it makes no
    /// residents or bar squads of its own (<see cref="BuildTown"/>, docs/simulation.md "Saves").
    /// </summary>
    bool HoldsSquads(World world, int site)
    {
        foreach (var p in world.Platoons.All)
            if (p.Pinned && p.Origin == site) return true;
        return false;
    }


    void Spawn(World world, BuiltTown town)
    {
        var site = data.Sites[town.Site];
        var state = states[town.Site];
        float radius = site.Town.SizeRadius * site.Town.TownRadiusMult;
        // The town's doors, cages, containers and beds, once (they stay registered when the town unloads).
        world.Fixtures.RegisterSite(world, data, town.Site);
        // A town made counts as visited in every zone of its buildings (engine choice: its residents and its buildings' locks are saved whole, docs/formats/save.md "Zone files"),
        // but for the centre zone of another town: a file there would tell the original that town was made, with no residents saved for it.
        foreach (var b in site.Buildings)
            if (b.Zone == site.Zone || !centres.Contains(b.Zone)) world.VisitedZones.Add(b.Zone);
        foreach (var built in town.Squads)
        {
            var squad = SpawnSquad(world, built, site, radius);
            state.Squads.Add(squad.Id);   // slave squads are planned (built.Plan.Slaves) but not spawned yet
        }
        if (town.Squads.Count > 0) MadeTown(world, town.Site, town.Squads.Where(s => s.Home is not null).Select(s => s.Members.Count));
        int housed = town.Squads.Count(s => s.Home is not null);
        if (townLog.Count >= 256) townLog.RemoveAt(0);   // nobody reads it in a headless world
        townLog.Add($"{site.Town.Name}: {town.Squads.Count} squad(s), {town.Squads.Sum(s => s.Members.Count)} characters; {housed} at home in " +
            $"{town.Squads.Where(s => s.Home is not null).Select(s => s.Home).Distinct().Count()} of {site.Buildings.Count(b => b.CanHouse)} houses, {town.Squads.Count - housed} elsewhere" +
            (housed < town.Squads.Count ? $" ({string.Join(", ", town.Squads.Where(s => s.Home is null).Select(s => s.Plan.Template.Name))})" : ""));
    }

    readonly List<string> townLog = [];
    /// <summary>A line for each town whose squads were made since the last call (for the host's log; not part of the state).</summary>
    public List<string> TakeTownLog()
    {
        var list = new List<string>(townLog);
        townLog.Clear();
        return list;
    }

    Squad SpawnSquad(World world, BuiltSquad built, TownSite? site, float radius, bool player = false, int platoonId = -1, bool idle = false, float yaw = 0, bool pinned = false)
    {
        var table = world.Characters;
        var walk = world.Walkability;
        var squad = new Squad
        {
            Id = world.Squads.NextId(),
            TemplateId = built.Plan.Template.Id,
            Name = built.Plan.Template.Name,
            Faction = built.Faction,
            Town = site?.Index ?? -1,
            PlatoonId = platoonId,
            Pinned = pinned,
            Position = new Vector3(built.Position.X, Ground(walk, built.Position.X, built.Position.Y, built.NearY), built.Position.Y),
            HomeCentre = site is null ? built.Position : new Vector2(site.Position.X, site.Position.Z),
            HomeRadius = Math.Max(radius * 0.8f, 40),
            HomeBuilding = built.Home,
        };
        var leaderPlan = built.Members.FirstOrDefault(m => m.Plan.Role == SquadRole.Leader)?.Plan;
        bool hasLeader = leaderPlan is not null;
        foreach (var member in built.Members)
        {
            var plan = member.Plan;
            float x = built.Position.X + plan.Offset.X, z = built.Position.Y + plan.Offset.Y;
            if (!(built.NearY is { } nearY ? walk.IsWalkable(x, z, nearY) : walk.IsWalkable(x, z))) (x, z) = (built.Position.X, built.Position.Y);
            var race = assembly.RaceOf(plan.RecordId, member.Appearance);
            var record = data.Db.Find(plan.RecordId);
            var hot = new CharacterHot
            {
                Position = new Vector3(x, Ground(walk, x, z, built.NearY), z),
                Yaw = yaw,
                Health = 100,
                Task = (byte)(player || idle ? CharacterTask.Idle : plan.Role == SquadRole.Leader || !hasLeader ? CharacterTask.Wander : CharacterTask.Follow),
                Mode = player ? SpeedMode.Free : SpeedMode.Walk,
                MaxSpeed = CharacterAssembly.StandInMaxSpeed,
                WalkSpeed = CharacterAssembly.WalkSpeed(race),
                TaskTime = 0.5f + Rng.Float(Rng.Hash(world.Seed, (ulong)squad.Id << 8 | (uint)plan.Index, RngPurpose.Think)) * 3,
            };
            var cold = new CharacterCold
            {
                Name = record?.Name ?? plan.RecordId,
                Faction = squad.Faction,
                Appearance = member.Appearance,
                RecordId = plan.RecordId,
                SquadId = squad.Id,
                Role = (int)plan.Role,
                FormationOffset = plan.Offset - (leaderPlan?.Offset ?? Vector2.Zero),
                IsPlayer = player,
                Orders = player ? Orders.PlayerDefaults : CharacterOrders.None,
                FootprintRadius = race?.GetFloat("pathfind footprint radius") ?? 0,
            };
            assembly.Attach(table.PeekNextId(), cold, ref hot, record, race, world.Seed, plan.IsAnimal ? plan.Age : 1);
            // A new NPC's bounties (economy.md 11.2; engine: not the player's characters, nor the sandbox's idle spawns).
            if (!player && !idle) cold.Bounties = Crime.SpawnBounties.Roll(data, record, world.Seed, table.PeekNextId(), world.Tick);
            var id = table.Spawn(hot, cold, world.Tick);
            squad.Members.Add(id);
            if (plan.Role == SquadRole.Leader && squad.Leader.IsNone) squad.Leader = id;
        }
        squad.SpawnSize = squad.Members.Count;
        if (squad.Leader.IsNone && squad.Members.Count > 0) squad.Leader = squad.Members[0];
        world.Squads.Add(squad);
        return squad;
    }


    /// <summary>The floor at X/Z: the one under <paramref name="nearY"/> inside a building, else the ground.</summary>
    static float Ground(IWalkability walk, float x, float z, float? nearY) => nearY is { } y ? walk.GroundHeight(x, z, y) : walk.GroundHeight(x, z);

    bool disposed;

    /// <summary>Stops the build thread; safe to call twice.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        work?.CompleteAdding();
        worker?.Join();
        work?.Dispose();
    }
}
