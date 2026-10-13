using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Engine;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Saving;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Save.SaveSamples;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Save;

/// <summary>
/// The quicksave of the game host (docs/simulation.md "Saves", "The game host"): a running world is captured, written and read, and a fresh world made from the file holds
/// the same player state. What differs is what the save does not carry (the tick, paths and orders, animation, combat state, the resident population).
/// </summary>
public class QuickSaveTests
{
    const StandardParts Parts = StandardParts.Population | StandardParts.Player | StandardParts.Movement | StandardParts.Body;

    sealed record Game(SimWorld World, PopulationData Data, Squad Squad) : IDisposable
    {
        public void Dispose()
        {
            World.Dispose();
            foreach (var s in World.Systems) (s as IDisposable)?.Dispose();
        }
    }

    static SimWorld NewWorld(PopulationData data, int threads, ulong seed = 11, int minPartition = 4)
    {
        var walk = new OpenGroundWalkability(SyntheticTown.Ground);
        var systems = SyntheticTown.Standard(data, walk, Parts, new PopulationSettings { CheckEveryTicks = 1 });
        return new SimWorld(new WorldSettings { Seed = seed, Threads = threads, PublishSnapshots = false, MinPartitionSize = minPartition }, walk, systems.Systems);
    }

    static Game Start(PopulationData data, int threads = 1)
    {
        var world = NewWorld(data, threads);
        var population = world.Systems.OfType<PopulationSystem>().Single();
        var squad = population.StartPlayer(world, NewGameStart.From(data.Db.Find("40-t")!));
        world.Commands.Enqueue(new FocusCommand(SyntheticTown.Centre) { Tick = 0 });
        return new Game(world, data, squad);
    }


    /// <summary>The fields MEDICAL_STATE holds, part by part, equal.</summary>
    static void SameSavedMedicalState(MedicalState a, MedicalState b)
    {
        Assert.Equal(a.Parts.Count, b.Parts.Count);
        for (int k = 0; k < a.Parts.Count; k++)
        {
            Assert.Equal(a.Parts[k].Template.StringId, b.Parts[k].Template.StringId);
            Assert.Equal((a.Parts[k].Flesh, a.Parts[k].Bandage, a.Parts[k].Rig, a.Parts[k].Stun, a.Parts[k].Wear, a.Parts[k].HitMult, a.Parts[k].HitWeight),
                (b.Parts[k].Flesh, b.Parts[k].Bandage, b.Parts[k].Rig, b.Parts[k].Stun, b.Parts[k].Wear, b.Parts[k].HitMult, b.Parts[k].HitWeight));
        }
        Assert.Equal((a.Blood, a.Bleeding, a.Hunger, a.Fed, a.KoTimer, a.Unconscious, a.Dead), (b.Blood, b.Bleeding, b.Hunger, b.Fed, b.KoTimer, b.Unconscious, b.Dead));
    }

    static (SaveClock Clock, string Folder, SaveGame Save) Quicksave(Game g, string name)
    {
        var clock = new SaveClock(3, 7, 45);
        var save = SaveCapture.Capture(g.World, g.Data, clock, null, new SaveCaptureOptions { Placements = [new Meitou.Data.World.TownPlacement("inst", "30-t", SyntheticTown.Centre, null)] });
        string folder = Path.Combine(Temp(name), QuickSave.Name);
        save.Write(folder);
        return (clock, folder, SaveGame.Load(folder));
    }

    [Fact]
    public void A_running_world_is_quicksaved_and_a_fresh_world_loaded_from_it_holds_the_same_player_state()
    {
        var db = SyntheticTown.Database(anatomy: true);
        var data = SyntheticTown.Data(db);
        using var a = Start(data);
        // The squad walks, two are selected, one is wounded in the left leg, and the money has changed.
        var chosen = a.Squad.Members.Take(2).ToList();
        a.World.Commands.Enqueue(new SelectCommand(chosen) { Tick = 0 });
        a.World.Commands.Enqueue(new MoveOrder([], new Vector3(1200, 300, 2300)) { Tick = 1 });
        a.World.RunTicks(150);
        var wounded = a.Squad.Members[1];
        var woundedCold = a.World.Characters.Cold(wounded.Slot)!;
        woundedCold.Medical!.ApplyHit(1, new HitDamage(60, 10, 0), new MedicalContext(GameConstants.Default, BodyOptions.Default, woundedCold.Race!)
        { Toughness = woundedCold.Stats!.Toughness, Strength = woundedCold.Stats.Strength, Seed = 11, CharacterKey = Rng.Key(wounded) });
        woundedCold.Stats.Gain(StatsEnumerated.Strength, 3.5f);
        a.World.RunTicks(30);
        a.World.Player.Money = 1234;
        a.World.Player.Selection.Remove(a.Squad.Members[0]);

        var (clock, folder, save) = Quicksave(a, "quick");
        try
        {
            using var b = new Game(NewWorld(data, 1), data, null!);
            var loaded = SaveLoader.Load(b.World, save, data);
            var squadB = b.World.Squads.Find(b.World.Player.Squad)!;

            Assert.Equal(clock, loaded.Clock);
            Assert.Equal(1234, b.World.Player.Money);
            Assert.Equal(a.Squad.Members.Count, squadB.Members.Count);
            Assert.Equal(a.Squad.TemplateId, squadB.TemplateId);
            Assert.Equal(a.World.Characters.Cold(a.Squad.Leader.Slot)!.Name, b.World.Characters.Cold(squadB.Leader.Slot)!.Name);
            for (int i = 0; i < a.Squad.Members.Count; i++)
            {
                var ida = a.Squad.Members[i];
                var idb = squadB.Members[i];
                var ca = a.World.Characters.Cold(ida.Slot)!;
                var cb = b.World.Characters.Cold(idb.Slot)!;
                var ha = a.World.Characters.Previous[ida.Slot];
                var hb = b.World.Characters.Previous[idb.Slot];
                Assert.Equal(ca.Name, cb.Name);
                Assert.Equal(ca.RecordId, cb.RecordId);
                Assert.Equal(ca.Role, cb.Role);
                Assert.Equal(ha.Position, hb.Position);
                Assert.Equal(ha.Yaw, hb.Yaw, 3);
                // The body systems run on the loaded character: race, the saved stats and medical state, inventory and fighter.
                Assert.NotNull(cb.Race);
                Assert.NotNull(cb.Inventory);
                Assert.NotNull(cb.Fighter);
                Assert.True(cb.IsPlayer);
                Assert.Equal(ca.Stats!.ToArray(), cb.Stats!.ToArray());
                Assert.Equal(ca.Medical!.Blood, cb.Medical!.Blood, 3);
                SameSavedMedicalState(ca.Medical!, cb.Medical!);
                // Not in the save's keys (docs/formats/save.md): the wound list and the pain, which the medical tick works out again.
                Assert.Empty(cb.Medical.Wounds);
                Assert.Equal(ha.MaxSpeed, hb.MaxSpeed, 2);
                Assert.Equal(ca.Inventory!.Items.Count, cb.Inventory!.Items.Count);
            }
            // The selection comes back as saved (CAMERA selected_character / selected_characters<k>).
            Assert.Equal(a.World.Player.Selection.Select(m => a.World.Characters.Cold(m.Slot)!.Name), b.World.Player.Selection.Select(m => b.World.Characters.Cold(m.Slot)!.Name));
            Assert.Single(b.World.Player.Selection);

            // What is not carried, stated exactly: the world starts at tick 0 with no paths, orders or animation, and its state hash is not the original's.
            Assert.Equal(0, b.World.Tick);
            Assert.NotEqual(a.World.StateHash(), b.World.StateHash());
            Assert.All(squadB.Members, m => Assert.Empty(b.World.Characters.Cold(m.Slot)!.OrderQueue));
            Assert.All(squadB.Members, m => Assert.Empty(b.World.Characters.Cold(m.Slot)!.Path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); }
    }

    [Fact]
    public void A_world_loaded_from_a_quicksave_runs_the_same_whatever_the_thread_count_and_captures_again()
    {
        var db = SyntheticTown.Database(anatomy: true);
        var data = SyntheticTown.Data(db);
        using var a = Start(data);
        a.World.RunTicks(40);
        var (_, folder, save) = Quicksave(a, "threads");
        try
        {
            var hashes = new List<ulong[]>();
            foreach (int threads in new[] { 1, 4 })
            {
                using var b = new Game(NewWorld(data, threads), data, null!);
                var loaded = SaveLoader.Load(b.World, save, data);
                b.World.Commands.Enqueue(new FocusCommand(SyntheticTown.Centre) { Tick = 0 });
                b.World.Commands.Enqueue(new SelectCommand(b.World.Squads.Find(b.World.Player.Squad)!.Members.ToList()) { Tick = 0 });
                b.World.Commands.Enqueue(new MoveOrder([], new Vector3(1300, 300, 2400)) { Tick = 2 });
                var seen = new List<ulong>();
                for (int i = 0; i < 6; i++)
                {
                    b.World.RunTicks(40);
                    seen.Add(b.World.StateHash());
                }
                hashes.Add([.. seen]);
                // Captured again from the running loaded world: the same player squad, carried on from the file it came from.
                var again = SaveCapture.Capture(b.World, data, loaded.Clock, loaded);
                Assert.Equal(save.Camera.Members, again.Camera.Members);
                Assert.Equal(save.Platoons.Select(p => p.Name), again.Platoons.Select(p => p.Name));
            }
            Assert.Equal(hashes[0], hashes[1]);
        }
        finally { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); }
    }

    [Fact]
    public void A_capture_from_a_base_made_ahead_writes_the_same_files_as_one_that_makes_its_own()
    {
        var db = SyntheticTown.Database(anatomy: true);
        var data = SyntheticTown.Data(db);
        using var a = Start(data);
        a.World.RunTicks(30);
        var clock = new SaveClock(2, 9, 30);
        var options = new SaveCaptureOptions { Seed = 77, Placements = [new Meitou.Data.World.TownPlacement("inst", "30-t", SyntheticTown.Centre, null)] };
        string one = Path.Combine(Temp("ahead-1"), "q"), two = Path.Combine(Temp("ahead-2"), "q");
        try
        {
            // First from nothing, then from the quicksave that wrote: both ways.
            SaveCapture.Capture(a.World, data, clock, null, options).Write(one);
            SaveCapture.Capture(a.World, data, clock, null, options, SaveCapture.PrepareBase(data, null, options)).Write(two);
            AssertSameFolders(one, two);

            using var w2 = NewWorld(data, 1);
            var loaded = SaveLoader.Load(w2, SaveGame.Load(one), data);
            Directory.Delete(Path.GetDirectoryName(two)!, true);
            SaveCapture.Capture(a.World, data, clock, loaded, options).Write(one + "-b");
            SaveCapture.Capture(a.World, data, clock, loaded, options, SaveCapture.PrepareBase(data, loaded, options)).Write(two);
            AssertSameFolders(one + "-b", two);
        }
        finally
        {
            foreach (var d in new[] { one, one + "-b", two }) if (Directory.GetParent(d) is { Exists: true } parent) parent.Delete(true);
        }
    }

    static void AssertSameFolders(string a, string b)
    {
        var filesA = Directory.GetFiles(a, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(a, f)).Order().ToList();
        var filesB = Directory.GetFiles(b, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(b, f)).Order().ToList();
        Assert.Equal(filesA, filesB);
        foreach (var f in filesA) Assert.True(File.ReadAllBytes(Path.Combine(a, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, f))), f + " differs");
    }

    [Fact]
    public void The_quicksave_slot_is_Meitous_own_and_never_a_folder_of_the_original_game()
    {
        string folder = QuickSave.Folder();
        Assert.Equal("quicksave", Path.GetFileName(folder));
        Assert.Contains(Path.Combine("Meitou", "save"), folder);
        Assert.DoesNotContain(Path.Combine("kenshi", "save"), folder, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine("root", "quicksave"), QuickSave.Folder("root"));
    }

    [Fact]
    public void The_session_swaps_in_a_new_world_and_keeps_the_old_one_when_the_new_one_fails_to_fill()
    {
        var db = SyntheticTown.Database();
        var data = SyntheticTown.Data(db);
        var walk = new OpenGroundWalkability(SyntheticTown.Ground);
        IEnumerable<ITickSystem> Systems() => SyntheticTown.Standard(data, walk, Parts, new PopulationSettings { CheckEveryTicks = 1 }).Systems;
        using var session = new WorldSession(SyntheticTown.Centre, (0, 0, 10000, 10000), SyntheticTown.Ground, simulation: new WorldSettings(), systems: Systems(), walkability: walk);
        var first = session.World;
        first.RunTicks(3);

        Assert.Throws<InvalidOperationException>(() => session.ReplaceWorld(Systems(), _ => throw new InvalidOperationException("broken save")));
        Assert.Same(first, session.World);
        Assert.Equal(3, session.World.Tick);

        var population = (PopulationSystem?)null;
        var second = session.ReplaceWorld(Systems(), w =>
        {
            Assert.Equal(0, w.Tick);
            population = w.Systems.OfType<PopulationSystem>().Single();
            population.StartPlayer(w, NewGameStart.From(db.Find("40-t")!));
        });
        Assert.NotSame(first, second);
        Assert.Same(second, session.World);
        Assert.True(session.World.Player.Exists);
        Assert.Equal(0, session.World.Tick);
        session.World.RunTick();
        Assert.NotEmpty(session.CurrentSnapshot.Characters);
    }

    // ------------------------------------------------------------------ with the install's data

    [Fact, Slow]
    public void A_new_games_squad_keeps_its_faces_stats_and_inventory_through_a_quicksave_and_load()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var levels = InstallData.Levels!;
        var data = PopulationData.Create(db, levels.Towns(), new GeneratedAppearances(db, install!.Root));
        var start = NewGameStart.Find(NewGameStart.LoadAll(db), NewGameStart.DefaultName)!;
        var walk = new OpenGroundWalkability((_, _) => 300);
        SimWorld Make(ulong seed)
        {
            var systems = StandardSystems.Build(data, walk, new StandardSystemOptions { SynchronousPaths = true, Population = new PopulationSettings { CheckEveryTicks = 1 } });
            return new SimWorld(new WorldSettings { Seed = seed, Threads = 2, PublishSnapshots = false }, walk, systems.Systems);
        }
        using var a = Make(5);
        var squadA = a.Systems.OfType<PopulationSystem>().Single().StartPlayer(a, start);
        a.RunTicks(60);
        string root = Temp("real");
        try
        {
            var save = SaveCapture.Capture(a, data, new SaveClock(1, 13, 39), null, new SaveCaptureOptions { Placements = levels.Towns().ToList() });
            string folder = QuickSave.Folder(root);
            save.Write(folder);

            using var b = Make(5);
            var loaded = SaveLoader.Load(b, SaveGame.Load(folder), data, new SaveLoadOptions { Appearances = data.Appearances });
            var squadB = b.Squads.Find(b.Player.Squad)!;
            Assert.Equal(squadA.Members.Count, squadB.Members.Count);
            for (int i = 0; i < squadA.Members.Count; i++)
            {
                var ca = a.Characters.Cold(squadA.Members[i].Slot)!;
                var cb = b.Characters.Cold(squadB.Members[i].Slot)!;
                Assert.Equal(ca.Name, cb.Name);
                Assert.NotNull(ca.Appearance);
                Assert.Equal(ca.Appearance!.Loadout!.Seed, save.Platoons.Single().Characters[i].Handle.S);   // the look's seed is the saved handle serial
                Assert.NotNull(cb.Appearance);
                // The same look: the roll's seed, hence the same body, head, hair and clothes; and so the same inventory.
                Assert.Equal(ca.Appearance!.Loadout!.Seed, cb.Appearance!.Loadout!.Seed);
                Assert.Equal(ca.Appearance.BodyMesh, cb.Appearance.BodyMesh);
                Assert.Equal(ca.Appearance.Female, cb.Appearance.Female);
                Assert.Equal(ca.Appearance.Parts.Select(p => p.Mesh), cb.Appearance.Parts.Select(p => p.Mesh));
                Assert.Equal(ca.Inventory!.Items.Select(x => x.Record), cb.Inventory!.Items.Select(x => x.Record));
                Assert.Equal(ca.Stats!.ToArray(), cb.Stats!.ToArray());
                SameSavedMedicalState(ca.Medical!, cb.Medical!);
            }
            b.RunTicks(120);   // the loaded world runs (body, movement, population) without trouble
            foreach (var s in b.Systems) (s as IDisposable)?.Dispose();
            foreach (var s in a.Systems) (s as IDisposable)?.Dispose();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
