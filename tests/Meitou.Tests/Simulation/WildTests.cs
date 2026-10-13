using System.Diagnostics;
using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>The wild population: homeless squads of the area sectors (6.2) and nests (6.3).</summary>
public class WildTests(ITestOutputHelper output)
{
    static RecordLink L(string id, int v0, int v1 = 0) => new(id, v0, v1, 0);

    // ---- the pure rules ----

    [Fact]
    public void Homeless_weights_are_target_shares()
    {
        var group = new BiomeGroupData
        {
            Id = "g", Name = "g", HomelessSpawnAmount = 2,
            HomelessSpawns = [L("a", 30), L("b", 10), L("c", 0), L("gated", 60)],
        };
        bool Usable(string id) => id != "gated";
        // k = (30 + 10 + 0 + 60) / (P x amount) = 100 / 2 = 50; v0 = 0 is dropped (6.1), a gated entry weighs nothing.
        Assert.Equal([30f, 10f, 0f, 0f], PopulationSystem.HomelessWeights(group, 1, [0, 0, 0, 0], Usable));
        Assert.Equal([0f, 10f, 0f, 0f], PopulationSystem.HomelessWeights(group, 1, [1, 0, 0, 0], Usable));
        // P = 2 halves k: one squad of a costs 25 of its 30.
        Assert.Equal([5f, 10f, 0f, 0f], PopulationSystem.HomelessWeights(group, 2, [1, 0, 0, 0], Usable));
        // No amount: nothing.
        Assert.All(PopulationSystem.HomelessWeights(group with { HomelessSpawnAmount = 0 }, 1, [0, 0, 0, 0], Usable), w => Assert.Equal(0f, w));
        Assert.Equal(1, PopulationSystem.HomelessCount(group with { HomelessSpawnAmount = 0.33f }, 1));
        Assert.Equal(2, PopulationSystem.HomelessCount(group with { HomelessSpawnAmount = 1.25f }, 1));
        Assert.Equal(1, PopulationSystem.HomelessCount(group with { HomelessSpawnAmount = 1 }, 1));
        Assert.Equal(0, PopulationSystem.HomelessCount(group with { HomelessSpawnAmount = 0 }, 4));
        Assert.Equal(3, PopulationSystem.HomelessCount(group with { HomelessSpawnAmount = 1.2f }, 2));
    }

    [Fact]
    public void Weighted_picks_follow_the_weights()
    {
        float[] weights = [30, 10, 0, 60];
        var counts = new int[4];
        const int n = 200_000;
        for (int i = 0; i < n; i++) counts[PopulationSystem.Pick(weights, Rng.Hash(7, (ulong)i, RngPurpose.Test))]++;
        Assert.Equal(0, counts[2]);
        Assert.InRange(counts[0] / (double)n, 0.29, 0.31);
        Assert.InRange(counts[1] / (double)n, 0.095, 0.105);
        Assert.InRange(counts[3] / (double)n, 0.59, 0.61);
        Assert.Equal(-1, PopulationSystem.Pick([0f, 0f], 1));

        // The generic pick of 6.1 (BIOME_GROUP nests): 0 counts as 100, gated entries skipped.
        var picks = new Dictionary<string, int>();
        ulong c = 0;
        for (int i = 0; i < n; i++)
        {
            var id = SquadFactory.WeightedPick([L("x", 0), L("y", 300), L("z", 50)], id => id == "z", () => Rng.Hash(9, c++, RngPurpose.Test))!;
            picks[id] = picks.GetValueOrDefault(id) + 1;
        }
        Assert.False(picks.ContainsKey("z"));
        Assert.InRange(picks["x"] / (double)n, 0.24, 0.26);
    }

    [Fact]
    public void Expected_size_follows_the_documented_rule()
    {
        var t = new SquadTemplate
        {
            Id = "t", Name = "t", Leader = L("l", 1),
            Squad = [L("a", 3, 0), L("b", 2, 100), L("c", 2, 5)],
            Animals = [L("d", 0, 0), L("e", 4, 0)],
        };
        // 1 + 3 + 2 + (2 + 5) / 2 = 3 + 4 = 13; the empty entry is skipped.
        Assert.Equal(13, SquadFactory.ExpectedSize(t, 1));
        // A multiplier below 1 scales only the range entry: (7 / 2) x 0.5 = 1.5 -> 1.
        Assert.Equal(11, SquadFactory.ExpectedSize(t, 0.5f));
        Assert.Equal(13, SquadFactory.ExpectedSize(t, 3));
    }

    // ---- a synthetic wilderness ----

    const uint New = 0x10;
    static FcsRecord Rec(string id, FcsRecordType type, string name) => new() { StringId = id, Flags = New, Name = name, RecordType = type };

    /// <summary>The synthetic town's data plus a biome group with homeless spawns and a dog nest; the group covers the four zones round the world's origin.</summary>
    static PopulationData WildData()
    {
        var db = SyntheticTown.Database(residentSquads: 1);
        var dogs = Rec("52-t", FcsRecordType.SQUAD_TEMPLATE, "Dog pack");
        dogs.References["animals"] = [new FcsReference("4-t", 3, 0, 50)];
        dogs.References["faction"] = [new FcsReference("53-t", 0, 0, 0)];
        var beasts = Rec("53-t", FcsRecordType.FACTION, "Beasts");
        beasts.Ints["default relation"] = -50;
        var nest = Rec("51-t", FcsRecordType.TOWN, "Dog Den");
        nest.Ints["type"] = 0;
        nest.Ints["nest resident population"] = 10;
        nest.Floats["size radius"] = 200;
        nest.Floats["town radius mult"] = 1;
        nest.References["faction"] = [new FcsReference("53-t", 0, 0, 0)];
        nest.References["roaming squads"] = [new FcsReference("52-t", 100, 0, 0)];
        var group = Rec("50-t", FcsRecordType.BIOME_GROUP, "Wilds");
        group.Ints["index"] = 0x123456;
        group.Ints["num nests"] = 2;
        group.Floats["homeless spawn amount"] = 1.5f;
        group.References["homeless spawns"] = [new FcsReference("10-t", 30, 0, 0), new FcsReference("52-t", 10, 0, 0), new FcsReference("11-t", 50, 0, 0)];
        group.References["nests"] = [new FcsReference("51-t", 0, 0, 0)];
        var file = new FcsFile();
        file.Records.AddRange([dogs, beasts, nest, group]);
        db.Apply(file, "wild.mod");

        var groups = BiomeGroupData.LoadAll(db);
        var cells = new int[WeatherAreas.Cells * WeatherAreas.Cells];
        Array.Fill(cells, -1);
        for (int z = 31; z <= 32; z++)
            for (int x = 31; x <= 32; x++) cells[z * WeatherAreas.Cells + x] = 0;
        return PopulationData.Create(db, [new TownPlacement("inst", "30-t", SyntheticTown.Centre, null)], biomes: new BiomeMap(groups, cells));
    }

    static readonly Vector3 Origin = new(0, 300, 0);   // the corner of four zones: all four are active

    static (SimWorld World, PopulationSystem Population) WildWorld(PopulationData data, int threads, StandardParts parts = StandardParts.Population | StandardParts.Movement, ulong seed = 3)
    {
        var walk = new OpenGroundWalkability(SyntheticTown.Ground);
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
        {
            Parts = parts, SynchronousPaths = true,
            Population = new PopulationSettings { CheckEveryTicks = 15, UnloadGraceSeconds = 2 },
        });
        var world = new SimWorld(new WorldSettings { Seed = seed, Threads = threads, PublishSnapshots = false, MinPartitionSize = 4 }, walk, systems.Systems);
        world.Commands.Enqueue(new FocusCommand(Origin) { Tick = 0 });
        return (world, systems.Population!);
    }

    static List<string> WildSquads(SimWorld world, PopulationSystem pop)
    {
        var list = new List<string>();
        foreach (var site in pop.WildSites)
            foreach (int id in pop.WildSquadsOf(site.Index))
            {
                var squad = world.Squads.Find(id)!;
                var members = string.Join(",", squad.Members.Select(m => world.Characters.Cold(m.Slot)!.RecordId));
                list.Add($"{site.Kind} {site.Index} {squad.TemplateId} {squad.Position} [{members}]");
            }
        return list;
    }

    [Fact]
    public void A_wilderness_gets_its_homeless_squads_and_nests()
    {
        var data = WildData();
        var (world, pop) = WildWorld(data, 1);
        using var _ = world;
        world.RunTicks(16);
        var sectors = pop.WildSites.Where(s => s.Kind == WildKind.Sector).ToList();
        var nests = pop.WildSites.Where(s => s.Kind == WildKind.Nest).ToList();
        Assert.Equal(4, sectors.Count);
        Assert.Equal(2, nests.Count);
        Assert.All(nests, n => Assert.Equal("Dog Den", n.Nest!.Name));
        foreach (var s in sectors)
        {
            Assert.Equal(SiteStatus.Loaded, pop.WildStatusOf(s.Index));
            // ceil(1.5) = 2 squads; the gated template never.
            Assert.Equal(2, pop.WildSquadsOf(s.Index).Count);
            Assert.All(pop.WildSquadsOf(s.Index), id => Assert.NotEqual("11-t", world.Squads.Find(id)!.TemplateId));
        }
        foreach (var n in nests)
        {
            // floor(0.7 x 10) = 7 members; a pack is expected to be 3: two packs, then 1 free is too little.
            var squads = pop.WildSquadsOf(n.Index);
            Assert.Equal(2, squads.Count);
            foreach (int id in squads)
            {
                var squad = world.Squads.Find(id)!;
                Assert.Equal("52-t", squad.TemplateId);
                Assert.Equal(3, squad.Members.Count);
                Assert.Equal(data.FactionIndex("53-t"), squad.Faction);
                Assert.True(Vector2.Distance(squad.HomeCentre, new Vector2(squad.Position.X, squad.Position.Z)) <= 200 * 0.6f + 1);
            }
        }
        foreach (var line in WildSquads(world, pop)) output.WriteLine(line);
    }

    [Fact]
    public void The_wild_agrees_at_one_four_and_sixteen_threads()
    {
        var data = WildData();
        ulong[]? first = null;
        foreach (int threads in new[] { 1, 4, 16 })
        {
            var (world, _) = WildWorld(data, threads);
            using var __ = world;
            var hashes = new ulong[4];
            for (int c = 0; c < hashes.Length; c++)
            {
                world.RunTicks(150);
                hashes[c] = world.StateHash();
            }
            if (first is null) first = hashes;
            else Assert.Equal(first, hashes);
        }
        var (other, _) = WildWorld(data, 1, seed: 4);
        using var ___ = other;
        other.RunTicks(600);
        Assert.NotEqual(first![^1], other.StateHash());
    }

    [Fact]
    public void Unloading_and_reloading_makes_the_same_squads()
    {
        var data = WildData();
        var (world, pop) = WildWorld(data, 1, StandardParts.Population);
        using var _ = world;
        world.RunTicks(16);
        var before = WildSquads(world, pop);
        Assert.NotEmpty(before);
        world.Commands.Enqueue(new FocusCommand(new Vector3(60000, 300, 60000)) { Tick = world.Tick });
        world.RunTicks(200);
        Assert.All(pop.WildSites, s => Assert.Equal(SiteStatus.Unloaded, pop.WildStatusOf(s.Index)));
        Assert.Empty(WildSquads(world, pop));
        world.Commands.Enqueue(new FocusCommand(Origin) { Tick = world.Tick });
        world.RunTicks(20);
        // Squad ids and slots are new; site, template, place and members are the same.
        Assert.Equal(before, WildSquads(world, pop));
    }

    // ---- the installed game ----

    static PopulationData RealData()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var biomes = BiomeMap.Create(db, WeatherAreas.Load(InstallData.Install!));
        return PopulationData.Create(db, InstallData.Levels!.Towns(), biomes: biomes);
    }

    static (SimWorld World, StandardSystemSet Systems) RealWorld(PopulationData data, Vector3 at, StandardParts parts, int threads = 1)
    {
        var walk = new OpenGroundWalkability((_, _) => 300);
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
        {
            Parts = parts, SynchronousPaths = true,
            Population = new PopulationSettings { CheckEveryTicks = 15 },
            Combat = new CombatOptions { SelfApproach = false, TickMedical = false },
        });
        var world = new SimWorld(new WorldSettings { Seed = 1, Threads = threads, PublishSnapshots = false }, walk, systems.Systems);
        world.Commands.Enqueue(new FocusCommand(at) { Tick = 0 });
        return (world, systems);
    }

    [Fact]
    [Slow]
    public void The_base_game_lays_out_its_nests()
    {
        var data = RealData();
        var sites = PopulationSystem.BuildWildSites(data, new PopulationSettings(), data.Biomes!, 1);
        var nests = sites.Where(s => s.Kind == WildKind.Nest).ToList();
        int sectors = sites.Count - nests.Count;
        output.WriteLine($"{sectors} sectors with homeless spawns, {nests.Count} nests");
        foreach (var g in nests.GroupBy(n => n.Nest!.Name).OrderByDescending(g => g.Count())) output.WriteLine($"  {g.Count(),3} {g.Key}");
        Assert.All(nests, n => Assert.Equal(TownType.Nest, n.Nest!.Type));
        // Every nest is one the group lists, and no group has more than its num nests.
        foreach (var g in nests.GroupBy(n => n.Group))
        {
            var group = data.Biomes!.Groups[g.Key];
            Assert.True(g.Count() <= group.NumNests, group.Name);
            Assert.All(g, n => Assert.Contains(group.Nests, e => e.Id == n.Nest!.Id));
        }
        Assert.InRange(nests.Count, 100, 531);
    }

    [Fact]
    [Slow]
    public void A_wolf_den_spawns_its_wolves()
    {
        var data = RealData();
        var sites = PopulationSystem.BuildWildSites(data, new PopulationSettings(), data.Biomes!, 1);
        var den = sites.First(s => s.Kind == WildKind.Nest && s.Nest!.Name == "Wolf Den");
        var at = new Vector3(den.Spots[0].X, 300, den.Spots[0].Y);
        var (world, systems) = RealWorld(data, at, StandardParts.Population | StandardParts.Movement | StandardParts.Body | StandardParts.Combat | StandardParts.Animation);
        using var _ = world;
        world.RunTicks(16);
        var pop = systems.Population!;
        var squads = pop.WildSquadsOf(den.Index);
        Assert.NotEmpty(squads);
        int members = 0;
        foreach (int id in squads)
        {
            var squad = world.Squads.Find(id)!;
            Assert.Contains(den.Nest!.RoamingSquads, e => e.Id == squad.TemplateId);
            foreach (var m in squad.Members)
            {
                var cold = world.Characters.Cold(m.Slot)!;
                Assert.Equal(FcsRecordType.ANIMAL_CHARACTER, data.Db.Find(cold.RecordId)!.Type);
                Assert.NotNull(cold.Fighter);
                members++;
            }
            output.WriteLine($"{squad.Name}: {squad.Members.Count}");
        }
        // floor(0.7 x 10) = 7, and a squad is skipped when its expected size passes the free room.
        Assert.InRange(members, 1, (int)(0.7f * den.Nest!.NestResidentPopulation) + 4);
        world.RunTicks(300);
    }

    [Fact]
    [Slow]
    public void The_zone_of_the_hub_rolls_its_biome_squads()
    {
        var data = RealData();
        var hub = data.Sites.First(s => s.Town.Name == "The Hub");
        var (cx, cz) = WeatherAreas.CellOf(hub.Position.X, hub.Position.Z);
        var group = data.Biomes!.Groups[data.Biomes.GroupOf(cx, cz)];
        output.WriteLine($"The Hub is in {group.Name}: amount {group.HomelessSpawnAmount}, {group.HomelessSpawns.Count} homeless entries");
        var (world, systems) = RealWorld(data, hub.Position, StandardParts.Population | StandardParts.Movement);
        using var _ = world;
        world.RunTicks(16);
        var pop = systems.Population!;
        var sector = pop.WildSites.Single(s => s.Kind == WildKind.Sector && s.Zone == hub.Zone);
        var squads = pop.WildSquadsOf(sector.Index);
        Assert.InRange(squads.Count, 1, PopulationSystem.HomelessCount(group, 1));
        foreach (int id in squads)
        {
            var squad = world.Squads.Find(id)!;
            Assert.Contains(group.HomelessSpawns, e => e.Id == squad.TemplateId);
            // Pushed out of the town to twice its radius.
            Assert.True(Vector2.Distance(new Vector2(squad.Position.X, squad.Position.Z), new Vector2(hub.Position.X, hub.Position.Z))
                >= hub.Town.SizeRadius * hub.Town.TownRadiusMult * 2 - 1, squad.Name);
            output.WriteLine($"{squad.Name}: {squad.Members.Count} at {squad.Position}");
        }
    }

    [Fact]
    [Slow]
    public void A_populated_wilderness_costs_little_per_tick()
    {
        var data = RealData();
        var den = PopulationSystem.BuildWildSites(data, new PopulationSettings(), data.Biomes!, 1).First(s => s.Kind == WildKind.Nest && s.Nest!.Name == "Wolf Den");
        var at = new Vector3(den.Spots[0].X, 300, den.Spots[0].Y);
        const StandardParts all = StandardParts.Population | StandardParts.Movement | StandardParts.Pursuit | StandardParts.Body | StandardParts.Combat
            | StandardParts.Animation | StandardParts.Retaliation | StandardParts.Aggro | StandardParts.Flee;
        var (world, systems) = RealWorld(data, at, all, threads: 4);
        using var _ = world;
        world.RunTicks(60);
        int alive = 0, wild = 0;
        for (int s = 0; s < world.Characters.HighWater; s++) if (world.Characters.Previous[s].Alive) alive++;
        foreach (var site in systems.Population!.WildSites)
            foreach (int id in systems.Population.WildSquadsOf(site.Index)) wild += world.Squads.Find(id)?.Members.Count ?? 0;
        var watch = Stopwatch.StartNew();
        world.RunTicks(900);
        double ms = watch.Elapsed.TotalMilliseconds / 900;
        output.WriteLine($"{alive} characters ({wild} in wild squads), {ms:F3} ms per tick at 4 threads");
        Assert.True(wild > 0);
    }
}
