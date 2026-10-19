using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Construction;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Items;
using Meitou.Simulation.Production;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Production (docs/simulation.md "Production", docs/game/buildings-production.md "Production", "Farming", "Research"): wells, farms, research benches.</summary>
public class ProductionTests
{
    const string Water = "50-t", Crop = "51-t", Book = "52-t", Gadget = "53-t", Well = "60-t", Farm = "61-t", Bench = "62-t", Workshop = "63-t", Study = "70-t";
    /// <summary>The stat that gives skill factor 1: lerp(0.5, 1.4, x/100) = 1.</summary>
    const float Unit = 100 / 1.8f;
    const int PlayerFaction = 2;
    static readonly GameConstants Constants = BaseGame with { ProductionSpeed = 2, ResearchRate = 0.3f };
    static readonly Vector3 At = new(1000, 300, 2000);

    static FcsRecord Rec(string id, FcsRecordType type, string name) => new() { StringId = id, Flags = 0x10, Name = name, RecordType = type };

    /// <summary>
    /// A well (0.1 water per second at skill 1), an indoor farm (4 plants, 1 h to grow, 2 crops each at farming 33.3, 1 water per game hour) and a level 1 research bench with
    /// a 4 x 4 grid, one research of 0.3 h costing 2 books, and a workshop crafting a gadget of 1 crop in 1 h (its section consumes water until a craft rebinds it; the output
    /// holds 3).
    /// </summary>
    static GameDatabase Database()
    {
        FcsRecord Item(string id, string name) { var r = Rec(id, FcsRecordType.ITEM, name); r.Floats["weight kg"] = 1; r.Ints["stackable"] = 100; return r; }
        FcsRecord Function(string id, int function, int stat)
        {
            var r = Rec(id, FcsRecordType.BUILDING_FUNCTIONALITY, id);
            r.Ints["function"] = function;
            r.Ints["stat used"] = stat;
            return r;
        }
        FcsRecord Building(string id, string name, string function)
        {
            var r = Rec(id, FcsRecordType.BUILDING, name);
            r.References["functionality"] = [new FcsReference(function, 0, 0, 0)];
            return r;
        }
        var wellFn = Function("80-t", MachineData.MineFunction, (int)StatsEnumerated.Labouring);
        wellFn.Floats["production mult"] = 0.05f;
        wellFn.References["produces"] = [new FcsReference(Water, 10, 0, 0)];
        var farmFn = Function("81-t", MachineData.MineFunction, (int)StatsEnumerated.Farming);
        farmFn.Ints["world resource mining"] = MachineData.GroundResource;
        farmFn.Ints["max operators"] = 2;
        farmFn.References["consumes"] = [new FcsReference(Water, 10, 0, 0)];
        farmFn.References["produces"] = [new FcsReference(Crop, 20, 0, 0)];
        var benchFn = Function("82-t", MachineData.ResearchFunction, (int)StatsEnumerated.Science);
        benchFn.Ints["tech level"] = 1;
        var farmData = Rec("83-t", FcsRecordType.FARM_DATA, "Test Crop");
        farmData.Ints["amount"] = 4;
        foreach (var (key, value) in new[] { ("growth time", 1f), ("harvest time", 48f), ("death time", 8f), ("consumption rate", 24f), ("harvest rate", 256f), ("clear rate", 500f), ("output per plant", 2f), ("green", 1f) })
            farmData.Floats[key] = value;
        var farm = Building(Farm, "Test Farm", "81-t");
        farm.References["farm data"] = [new FcsReference("83-t", 0, 0, 0)];
        var bench = Building(Bench, "Test Bench", "82-t");
        bench.Bools["has inventory"] = true;
        bench.Ints["storage size width"] = 4;
        bench.Ints["storage size height"] = 4;
        var study = Rec(Study, FcsRecordType.RESEARCH, "Test Study");
        study.Ints["level"] = 1;
        study.Ints["time"] = 1;
        study.References["cost"] = [new FcsReference(Book, 2, 0, 0)];
        var gadget = Item(Gadget, "Gadget");
        gadget.Ints["production time"] = 1;
        gadget.References["ingredients"] = [new FcsReference(Crop, 100, 0, 0)];
        var workshopFn = Function("84-t", CraftBench.CraftingFunction, (int)StatsEnumerated.Engineering);
        workshopFn.References["item crafts"] = [new FcsReference(Gadget, 3, 0, 0)];
        workshopFn.References["consumes"] = [new FcsReference(Water, 5, 100, 0)];
        var file = new FcsFile();
        file.Records.AddRange([Item(Water, "Water"), Item(Crop, "Crop"), Item(Book, "Book"), wellFn, farmFn, benchFn, farmData, Building(Well, "Test Well", "80-t"), farm, bench, study, gadget, workshopFn,
            Building(Workshop, "Test Workshop", "84-t")]);
        var db = new GameDatabase();
        db.Apply(file, "test.mod");
        return db;
    }

    sealed class Rig : IDisposable
    {
        public SimWorld World { get; }
        public ItemFactory Items { get; }
        public ProductionSystem Production { get; }
        public List<string> Sites { get; } = [];

        public Rig(GameDatabase db, GameConstants constants, string[] records, int threads = 1, bool snapshots = false)
        {
            Items = new ItemFactory(db, constants);
            var walk = new OpenGroundWalkability(SyntheticTown.Ground);
            Production = new ProductionSystem(db, constants, BodyOptions.Default, Items, null);
            World = new SimWorld(new WorldSettings { Seed = 5, Threads = threads, PublishSnapshots = snapshots, MinPartitionSize = 1 }, walk,
                [new PlayerSystem(), new MovementSystem(new PathService(walk, true)), Production]);
            World.Player.Faction = PlayerFaction;
            World.Research.Set(db, []);
            for (int i = 0; i < records.Length; i++)
            {
                var p = At + new Vector3(i * 60, 0, 0);
                string id = World.Buildings.NewId(WorldLayout.ZoneOf(p.X, p.Z));
                World.Buildings.Add(new BuildSite { Id = id, Record = records[i], Position = p, Owner = PlayerFaction, Materials = [], Complete = true });
                Sites.Add(id);
            }
        }

        /// <summary>A player character beside site <paramref name="site"/> with one stat and what it carries (one stack each).</summary>
        public CharacterId Add(int site, StatsEnumerated stat, float level, params (string Item, int Count)[] carried)
        {
            var inventory = new Inventory();
            foreach (var (item, count) in carried) inventory.Add(Items.Create(item, count)!);
            var stats = new CharacterStats();
            stats[stat] = level;
            var cold = new CharacterCold { Name = "worker" + site, Race = Human, Stats = stats, Medical = MedicalState.Create(Human, 50), Inventory = inventory, IsPlayer = true, Faction = PlayerFaction };
            var p = World.Buildings.Find(Sites[site])!.Position + new Vector3(0, 0, 10);
            return World.Characters.Spawn(new CharacterHot { Position = p, MaxSpeed = 40, WalkSpeed = 15, Task = (byte)CharacterTask.Idle }, cold, 0);
        }

        public Inventory Carried(CharacterId id) => World.Characters.Cold(id.Slot)!.Inventory!;
        public int Count(CharacterId id, string item) => Carried(id).All().Where(i => i.Record == item).Sum(i => i.Quantity);

        /// <summary>Puts the character's first stack of <paramref name="item"/> into slot <paramref name="slot"/> of a site.</summary>
        public void Put(CharacterId who, string item, int site, int slot, int x = 0, int y = 0)
        {
            var all = Carried(who).All().ToList();
            int index = all.FindIndex(i => i.Record == item);
            var it = all[index];
            World.Commands.Enqueue(new PutInMachine(who, new InventoryItemToken(index, it.Record, it.Section, it.X, it.Y, it.Quantity, it.Quality), Sites[site], slot, x, y) { Tick = World.Tick });
        }

        public Machine Machine(int site) => World.Machines.Find(Sites[site])!;
        public void Dispose() => World.Dispose();
    }

    // ------------------------------------------------------------------ determinism

    /// <summary>A well, a farm and a workshop are worked, water goes into the farm, books into the bench and crops into the workshop, the study and three gadgets are
    /// queued; hashes at six checkpoints.</summary>
    static List<ulong> Scripted(int threads, out (int Water, int Crops, bool Studied, int Gadgets) seen)
    {
        using var rig = new Rig(Database(), Constants, [Well, Farm, Bench, Workshop], threads);
        var labourer = rig.Add(0, StatsEnumerated.Labouring, Unit);
        var farmer = rig.Add(1, StatsEnumerated.Farming, 100 / 3f, (Water, 5));
        var scientist = rig.Add(2, StatsEnumerated.Science, Unit, (Book, 3));
        var crafter = rig.Add(3, StatsEnumerated.Engineering, Unit, (Crop, 5));
        rig.World.RunTick();
        rig.World.Commands.Enqueue(new OperateOrder([labourer], rig.Sites[0]) { Tick = 2 });
        rig.World.Commands.Enqueue(new OperateOrder([farmer], rig.Sites[1]) { Tick = 2 });
        rig.World.Commands.Enqueue(new OperateOrder([scientist], rig.Sites[2]) { Tick = 2 });
        rig.World.Commands.Enqueue(new QueueCraft(rig.Sites[3], Gadget, 3) { Tick = rig.World.Tick });
        rig.World.Commands.Enqueue(new OperateOrder([crafter], rig.Sites[3]) { Tick = 2 });
        rig.Put(farmer, Water, 1, 0);
        rig.Put(scientist, Book, 2, -1);
        rig.Put(crafter, Crop, 3, 0);
        rig.World.RunTick();
        rig.World.Commands.Enqueue(new QueueResearch(Study) { Tick = rig.World.Tick });
        var hashes = new List<ulong>();
        foreach (int target in new[] { 5, 60, 600, 1500, 3000, 5000 })
        {
            rig.World.RunTicks(target - (int)rig.World.Tick);
            hashes.Add(rig.World.StateHash());
        }
        seen = (rig.Machine(0).Output!.Count, rig.Machine(1).Output!.Count, rig.World.Research.IsDone(Study), rig.Machine(3).Craft!.Done.Sum(i => i.Quantity));
        return hashes;
    }

    [Fact]
    public void Wells_farms_research_and_crafting_hash_the_same_at_1_4_and_16_threads()
    {
        var one = Scripted(1, out var seen);
        Assert.True(seen.Water > 0 && seen.Crops > 0 && seen.Studied && seen.Gadgets == 3, $"{seen}");
        Assert.Equal(one, Scripted(4, out _));
        Assert.Equal(one, Scripted(16, out _));
    }

    // ------------------------------------------------------------------ items and research costs

    /// <summary>
    /// An input takes only up to its capacity, the output comes back into the character's inventory, the study's books are paid from the bench once (cancelling and queueing again
    /// costs nothing) and it completes after hours x G/24 seconds at rate 1; the save keys hold it.
    /// </summary>
    [Fact]
    public void Inputs_clamp_to_capacity_outputs_are_taken_and_a_research_is_paid_once()
    {
        var db = Database();
        using var rig = new Rig(db, Constants, [Well, Farm, Bench]);
        var worker = rig.Add(1, StatsEnumerated.Science, Unit, (Water, 15), (Book, 4));
        rig.World.RunTick();
        rig.Put(worker, Water, 1, 0);
        rig.World.RunTick();
        Assert.Equal(10, rig.Machine(1).Inputs[0].Count + (int)MathF.Ceiling(rig.Machine(1).Inputs[0].Level));
        Assert.Equal(5, rig.Count(worker, Water));

        rig.Machine(0).Output!.Count = 3;
        rig.World.Commands.Enqueue(new TakeFromMachine(worker, rig.Sites[0], -2) { Tick = rig.World.Tick });
        rig.World.RunTick();
        Assert.Equal(0, rig.Machine(0).Output!.Count);
        Assert.Equal(8, rig.Count(worker, Water));

        rig.Put(worker, Book, 2, -1);
        rig.World.RunTick();
        rig.World.Commands.Enqueue(new QueueResearch(Study) { Tick = rig.World.Tick });
        rig.World.RunTick();
        Assert.Equal(2, rig.Machine(2).Storage!.Items.Sum(i => i.Quantity));
        rig.World.Commands.Enqueue(new CancelResearch(Study) { Tick = rig.World.Tick });
        rig.World.RunTick();
        rig.World.Commands.Enqueue(new QueueResearch(Study) { Tick = rig.World.Tick });
        rig.World.RunTick();
        Assert.Equal(2, rig.Machine(2).Storage!.Items.Sum(i => i.Quantity));
        Assert.Contains(Study, rig.World.Research.Paid);

        // The worker, science at skill factor 1, operates the level 1 bench (output rate 1): 0.3 h = 0.3 x G/24 s.
        rig.World.Commands.Enqueue(new OperateOrder([worker], rig.Sites[2]) { Tick = rig.World.Tick });
        long started = rig.World.Tick;
        for (int t = 0; t < 4000 && !rig.World.Research.IsDone(Study); t++) rig.World.RunTick();
        float seconds = (rig.World.Tick - started) * rig.World.TickSeconds;
        Assert.True(rig.World.Research.IsDone(Study));
        Assert.InRange(seconds, 0.3f * MachineData.DaySeconds / 24, 0.3f * MachineData.DaySeconds / 24 * 1.15f);
        Assert.Contains(rig.World.TakeMessages(), m => m == "Research complete: Test Study");
    }

    // ------------------------------------------------------------------ crafting

    /// <summary>
    /// Queueing a gadget rebinds the workshop's water section to crops (the water falls out); two gadgets draw 2 crops and wait in the output until taken; a half-made third and
    /// the rebound section survive a save round-trip (docs/game/buildings-production.md "Crafting").
    /// </summary>
    [Fact]
    public void A_craft_rebinds_its_section_draws_ingredients_waits_to_be_taken_and_saves()
    {
        const string PlayerFactionId = "204-gamedata.base";
        var db = Database();
        using var rig = new Rig(db, Constants, [Workshop]);
        var worker = rig.Add(0, StatsEnumerated.Engineering, Unit, (Water, 2), (Crop, 5));
        rig.World.RunTick();
        rig.Put(worker, Water, 0, 0);
        rig.World.RunTick();
        Assert.Equal((Water, 2), (rig.Machine(0).Inputs[0].Item, rig.Machine(0).Inputs[0].Count));

        rig.World.Commands.Enqueue(new QueueCraft(rig.Sites[0], Gadget, 2) { Tick = rig.World.Tick });
        rig.World.RunTick();
        var section = rig.Machine(0).Inputs[0];
        Assert.Equal((Crop, 0, 1f), (section.Item, section.Count, section.Rate));
        Assert.Equal(2, rig.World.GroundItems.All.Where(g => g.Item.Record == Water).Sum(g => g.Item.Quantity));

        rig.Put(worker, Crop, 0, 0);
        rig.World.Commands.Enqueue(new OperateOrder([worker], rig.Sites[0]) { Tick = rig.World.Tick });
        var craft = rig.Machine(0).Craft!;
        for (int t = 0; t < 4000 && craft.Queue.Count > 0; t++) rig.World.RunTick();
        Assert.Empty(craft.Queue);
        Assert.Equal(2, craft.Done.Sum(i => i.Quantity));
        Assert.Equal(3, section.Count + section.Level, 0.01f);

        rig.World.Commands.Enqueue(new TakeFromMachine(worker, rig.Sites[0], -2) { Tick = rig.World.Tick });
        rig.World.RunTick();
        Assert.Empty(craft.Done);
        Assert.Equal(2, rig.Count(worker, Gadget));

        rig.World.Commands.Enqueue(new QueueCraft(rig.Sites[0], Gadget) { Tick = rig.World.Tick });
        for (int t = 0; t < 4000 && (craft.Queue.Count == 0 || craft.Queue[0].Progress < 0.3f); t++) rig.World.RunTick();
        var job = Assert.Single(craft.Queue);

        var builder = new Meitou.Data.Save.SaveBuilder(1);
        builder.AddFaction(PlayerFactionId, "Nameless", true, 1000, []);
        builder.AddEmptyWorldState();
        Meitou.Simulation.Saving.BuildingSave.Write(builder.Game, rig.World, PlayerFactionId, 5);
        Meitou.Simulation.Saving.MachineSave.Write(builder.Game, rig.World);
        string dir = Path.Combine(Path.GetTempPath(), "meitou-craft-" + Guid.NewGuid().ToString("N"));
        try
        {
            builder.Game.Write(dir);
            var save = Meitou.Data.Save.SaveGame.Load(dir);
            using var back = new Rig(db, Constants, []);
            Meitou.Simulation.Saving.BuildingSave.Read(save, back.World, db, PlayerFactionId, PlayerFaction);
            Meitou.Simulation.Saving.MachineSave.Read(save, back.World, back.Production, back.Items);
            var read = back.World.Machines.Find(rig.Sites[0])!;
            var again = Assert.Single(read.Craft!.Queue);
            Assert.Equal((job.Option.Key, job.Progress, job.Critical), (again.Option.Key, again.Progress, again.Critical));
            Assert.Equal((section.Item, section.Count, section.Level, section.Rate), (read.Inputs[0].Item, read.Inputs[0].Count, read.Inputs[0].Level, read.Inputs[0].Rate));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ------------------------------------------------------------------ the installed game

    /// <summary>
    /// The installed Weapon Smith (2033) and Leather Armour Crafting Bench (2262) offer the documented crafts (docs/game/buildings-production.md "Crafting"): every PLAYER_WEAPONS
    /// weapon type in each listed model, a katana at level 30 taking (0.3² x 100 x value/300 x 2/P) h and its material cost in iron plates; a Tricorn Hat taking coverage/100 x
    /// lerp(0.4, 1, 5) x 0.6 h and drawing coverage/100 leather and that x fabrics amount of fabrics. With no research the weapons are locked; with all of it some are not.
    /// </summary>
    [Fact, Slow]
    public void The_installed_weapon_and_armour_benches_offer_the_documented_crafts()
    {
        var db = InstallData.BaseGame;
        Assert.SkipWhen(db is null, "Kenshi install not found");
        var constants = GameConstants.FromDatabase(db!);
        using var rig = new Rig(db!, constants, ["2033-gamedata.base", "2262-gamedata.base"]);
        rig.World.RunTick();
        var smith = rig.Machine(0).Data;
        var maker = db!.Find(CraftBench.PlayerWeapons)!;
        var models = db.Find(db.Find("2033-gamedata.base")!.GetReferences("functionality")[0].TargetStringId)!.GetReferences("weapon crafts").Select(r => r.TargetStringId).ToHashSet();
        int types = maker.GetReferences("weapon types").Count(t => db.Find(t.TargetStringId) is { Type: FcsRecordType.WEAPON });
        int listed = maker.GetReferences("weapon models").Count(m => models.Contains(m.TargetStringId));
        Assert.Equal(types * listed, smith.Craft!.Options.Count);

        var katana = db.OfType(FcsRecordType.WEAPON).First(r => r.Name == "Katana");
        var recipe = smith.Craft.Recipe(db, constants, new CraftOption(CraftKind.Weapon, katana.StringId, null, 30, 1), smith.Inputs);
        Assert.Equal(0.09f * 100 * katana.GetInt("value") / 300f * 2 / constants.ProductionSpeed, recipe.Hours, 0.001f);
        Assert.Equal((smith.Inputs[0].Item, (float)katana.GetInt("material cost")), (recipe.Inputs[0].Item, recipe.Inputs[0].PerItem));

        var bench = rig.Machine(1).Data;
        var hat = db.OfType(FcsRecordType.ARMOUR).First(r => r.Name == "Tricorn Hat");
        float coverage = hat.GetReferences("part coverage").Sum(p => p.TargetStringId == "101-gamedata.quack" ? (int)(p.Values.Value0 * 1.5) : p.Values.Value0) / 100f;
        var hatRecipe = bench.Craft!.Recipe(db, constants, bench.Craft.Options.First(o => o.Item == hat.StringId), bench.Inputs);
        Assert.Equal(MathF.Max(1, coverage * 2.6f) * 0.6f, hatRecipe.Hours, 0.001f);
        var leather = hatRecipe.Inputs.Single(i => i.Item != CraftBench.Fabrics);
        Assert.Equal(coverage, leather.PerItem, 0.001f);
        Assert.Equal(coverage * hat.GetFloat("fabrics amount", 0.2f), hatRecipe.Inputs.Single(i => i.Item == CraftBench.Fabrics).PerItem, 0.001f);

        Assert.DoesNotContain(rig.Production.CraftOptions(rig.World, rig.Sites[0]), o => o.Unlocked);
        rig.World.Research.Set(db, [.. db.OfType(FcsRecordType.RESEARCH).Select(r => r.StringId)]);
        Assert.Contains(rig.Production.CraftOptions(rig.World, rig.Sites[0]), o => o.Unlocked);
    }

    /// <summary>
    /// The installed Well (1921) at skill factor 1 on ground with no water value (yield 0.5) makes 36 x 0.5 water per game day (docs/game/buildings-production.md "Production"); the Small
    /// Research Bench (1875) finishes a free level 1 research in its hours x G/24 seconds at its output rate 0.85.
    /// </summary>
    [Fact, Slow]
    public void The_installed_well_and_research_bench_work_at_the_documented_rates()
    {
        var db = InstallData.BaseGame;
        Assert.SkipWhen(db is null, "Kenshi install not found");
        var constants = GameConstants.FromDatabase(db!);
        using var rig = new Rig(db!, constants, ["1921-gamedata.base", "1875-gamedata.base"]);
        var labourer = rig.Add(0, StatsEnumerated.Labouring, Unit);
        var scientist = rig.Add(1, StatsEnumerated.Science, Unit);
        rig.World.RunTick();
        Assert.Equal(0.5f, rig.Machine(0).Yield);
        rig.World.Commands.Enqueue(new OperateOrder([labourer], rig.Sites[0]) { Tick = rig.World.Tick });
        rig.World.Commands.Enqueue(new OperateOrder([scientist], rig.Sites[1]) { Tick = rig.World.Tick });
        rig.World.RunTicks(30);
        var research = ResearchData.LoadAll(db!).First(r => rig.Production.Available(rig.World, r) && r.Cost.Count == 0 && r.Level <= 1);
        rig.World.Commands.Enqueue(new QueueResearch(research.Id) { Tick = rig.World.Tick });
        // Stats rise while operating, so the first quarter day at s ~ 1 is compared (the well's output holds 10).
        float quarter = MachineData.DaySeconds / 4;
        long started = rig.World.Tick;
        float expectedSeconds = research.Hours(constants) * MachineData.DaySeconds / 24 / 0.85f;
        long done = -1;
        while ((rig.World.Tick - started) * rig.World.TickSeconds < quarter)
        {
            rig.World.RunTick();
            if (done < 0 && rig.World.Research.IsDone(research.Id)) done = rig.World.Tick;
        }
        var well = rig.Machine(0).Output!;
        Assert.InRange(well.Count + well.Progress, 36 * 0.5f / 4 * 0.95f, 36 * 0.5f / 4 * 1.1f);
        Assert.True(done > 0, research.Name);
        Assert.InRange((done - started) * rig.World.TickSeconds, expectedSeconds * 0.9f, expectedSeconds * 1.02f);
    }
}
