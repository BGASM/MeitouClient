using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Saving;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Save.SaveSamples;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Save;

/// <summary>Saves and worlds: loading a save into a <see cref="SimWorld"/> and capturing a world into a save (docs/simulation.md "Saves").</summary>
public class SaveWorldTests
{
    sealed record Game(SimWorld World, PopulationData Data, Squad Squad) : IDisposable
    {
        public void Dispose() => World.Dispose();
    }

    /// <summary>A started game on the synthetic town: the player's squad of four, 500 cats.</summary>
    static Game NewGame(ulong seed = 11)
    {
        var db = SyntheticTown.Database();
        var data = SyntheticTown.Data(db);
        var walk = new OpenGroundWalkability(SyntheticTown.Ground);
        var systems = SyntheticTown.Standard(data, walk, StandardParts.Population | StandardParts.Player | StandardParts.Movement, new PopulationSettings { CheckEveryTicks = 1 });
        var population = systems.Population!;
        var world = new SimWorld(new WorldSettings { Seed = seed, PublishSnapshots = false }, walk, systems.Systems);
        var squad = population.StartPlayer(world, NewGameStart.From(db.Find("40-t")!));
        world.Commands.Enqueue(new FocusCommand(new Vector3(-90000, 0, -90000)) { Tick = 0 });
        return new Game(world, data, squad);
    }

    static SimWorld EmptyWorld(ulong seed = 11) =>
        new(new WorldSettings { Seed = seed, PublishSnapshots = false }, new OpenGroundWalkability(SyntheticTown.Ground), [new PlayerSystem()]);

    static string Dir(string name) => Temp(name);

    [Fact]
    public void A_new_games_world_is_captured_into_a_save_and_loaded_into_another_world()
    {
        using var g = NewGame();
        var clock = new SaveClock(2, 14, 30);
        g.World.Player.Selection.Add(g.Squad.Leader);
        var save = SaveCapture.Capture(g.World, g.Data, clock);

        Assert.Equal((2, 14, 30), (save.Camera.Day, save.Camera.Hour, save.Camera.Minute));
        Assert.Equal(500, save.Camera.PlayerMoney);
        Assert.Equal((1, 4), (save.Camera.Squads, save.Camera.Members));
        Assert.Equal(g.Data.Factions.Count, save.Factions.Count);
        var platoon = Assert.Single(save.Platoons);
        Assert.Equal("Nameless_0", platoon.Name);
        Assert.Equal(4, platoon.Characters.Count);
        Assert.Equal(1, save.PlayerFaction!.PlatoonCounter);
        // The leader is first in the selection; its handle is the CAMERA's.
        var leader = platoon.Characters.Single(c => c.IsLeader);
        Assert.Equal(leader.Handle, save.Camera.SelectedCharacter);

        string folder = Path.Combine(Dir("newgame"), "slot");
        try
        {
            save.Write(folder);
            using var world2 = EmptyWorld();
            var loaded = SaveLoader.Load(world2, SaveGame.Load(folder), g.Data);
            Assert.Equal(clock, loaded.Clock);
            Assert.Equal(500, world2.Player.Money);
            Assert.Equal(g.Data.PlayerFaction, world2.Player.Faction);
            var squad = world2.Squads.Find(world2.Player.Squad)!;
            Assert.Equal(4, squad.Members.Count);
            Assert.Equal(g.Squad.Members.Select(m => g.World.Characters.Cold(m.Slot)!.Name), squad.Members.Select(m => world2.Characters.Cold(m.Slot)!.Name));
            for (int i = 0; i < 4; i++)
            {
                var a = g.World.Characters.Previous[g.Squad.Members[i].Slot];
                var b = world2.Characters.Previous[squad.Members[i].Slot];
                Assert.Equal(a.Position, b.Position);
                Assert.Equal(a.Yaw, b.Yaw, 4);
            }
            Assert.Equal(g.Squad.Leader.Slot, g.Squad.Members.First(m => m == g.Squad.Leader).Slot);
            Assert.Equal(world2.Characters.Cold(squad.Leader.Slot)!.Name, g.World.Characters.Cold(g.Squad.Leader.Slot)!.Name);
            Assert.Equal([squad.Leader], world2.Player.Selection);
            Assert.All(squad.Members, m => Assert.True(world2.Characters.Cold(m.Slot)!.IsPlayer));
            Assert.All(squad.Members, m => Assert.NotNull(world2.Characters.Cold(m.Slot)!.Save));
            // Relations: the table the new save holds is the data's own.
            foreach (var f in loaded.Factions.Where(f => !f.IsPlayer))
                foreach (var r in f.Relations.Values)
                    Assert.Equal(g.Data.Relations.Get(g.Data.Factions.ToList().FindIndex(x => x.Id == f.Id), g.Data.Relations.IndexOf(r.FactionId)), r.Relation);
        }
        finally { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); }
    }

    // ------------------------------------------------------------------ the user's saves

    static (GameDatabase Db, PopulationData Data)? cached;

    static PopulationData Data()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        lock (typeof(SaveWorldTests))
        {
            cached ??= Make(install!);
            return cached.Value.Data;
        }
    }

    static (GameDatabase, PopulationData) Make(GameInstall install)
    {
        var db = InstallData.BaseGame!;
        var level = InstallData.Levels!;
        return (db, PopulationData.Create(db, level.Towns()));
    }

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void A_real_save_captured_again_keeps_every_record(string folder)
    {
        SkipWithoutSaves(folder);
        var data = Data();
        var original = Load(folder);
        using var world = EmptyWorld();
        var loaded = SaveLoader.Load(world, original, data, new SaveLoadOptions { ApplyRelations = false });
        var again = SaveCapture.Capture(world, data, loaded.Clock, loaded);

        // Nothing the world does not change changes: compare record by record.
        Assert.Equal(original.Quick.Records.Count, again.Quick.Records.Count);
        var changed = new List<string>();
        for (int i = 0; i < original.Quick.Records.Count; i++)
            if (!Same(original.Quick.Records[i], again.Quick.Records[i])) { var x = Bytes(original.Quick.Records[i]); var y = Bytes(again.Quick.Records[i]); int d = 0; while (d < Math.Min(x.Length, y.Length) && x[d] == y[d]) d++; changed.Add(original.Quick.Records[i].RecordType + $"(len {x.Length}/{y.Length} diff at {d} bs {original.Quick.Records[i].ByteSize}/{again.Quick.Records[i].ByteSize})"); }
        // Only the PLATOON records of the player's platoons (their char count follows the file) and the CAMERA (squads and members, the zone list) may differ.
        var unexpected = changed.Where(t => !t.StartsWith("PLATOON") && !t.StartsWith("CAMERA")).GroupBy(t => t).Select(g => g.Key + " x" + g.Count()).ToList();
        Assert.True(unexpected.Count == 0, "changed: " + string.Join(", ", unexpected));

        // The player's platoon files: the characters' positions, stats and medical state are the saved ones, the rest is untouched.
        int compared = 0;
        foreach (var lp in loaded.PlayerPlatoons)
        {
            var was = lp.Platoon;
            var now = again.Platoons.Single(p => p.Name == was.Name);
            Assert.Equal(was.Characters.Count, now.Characters.Count);
            for (int k = 0; k < was.Characters.Count; k++)
            {
                var a = was.Characters[k];
                var b = now.Characters[k];
                if (!Same(a.GameState!, b.GameState!)) Assert.Fail($"{was.Name}/{a.Name}: GAMESTATE_CHARACTER differs");
                Assert.Equal(a.Position, b.Position);
                Assert.Equal(a.Rotation, b.Rotation);
                foreach (var (x, y) in new[] { (a.Ai, b.Ai), (a.Inventory, b.Inventory), (a.Appearance, b.Appearance), (a.Stats, b.Stats) })
                    Assert.True(Same(x!, y!), $"{was.Name}/{a.Name}: {x!.RecordType} differs");
                SameFloats(a.Medical!, b.Medical!, $"{was.Name}/{a.Name} MEDICAL_STATE");
                compared++;
            }
        }
        Assert.True(compared > 0);
        // Every other file is byte for byte what it was.
        var wasFiles = original.AllFiles().ToDictionary(f => f.Name, f => f.File);
        foreach (var (name, file) in again.AllFiles().Where(f => f.Name != "quick.save"))
            if (loaded.PlayerPlatoons.All(p => p.Platoon.FileName != name))
            {
                // Zone files: the derived slot list and nextId are what the original had.
                if (name.StartsWith("zone/")) Assert.Equal(wasFiles[name].SlotLists[0], file.SlotLists[0]);
                Assert.Equal(wasFiles[name].Records.Count, file.Records.Count);
            }
    }

    /// <summary>Two records are the same when they serialise to the same bytes (which also holds for the NaN-patterned floats of decal records).</summary>
    static bool Same(FcsRecord a, FcsRecord b) => Bytes(a).AsSpan().SequenceEqual(Bytes(b));

    static byte[] Bytes(FcsRecord r)
    {
        var ms = new MemoryStream();
        FcsWriter.Write(new FcsFile { FileType = FcsFileType.V15, Records = { r } }, ms, keepByteSizes: true, lossless: true);
        return ms.ToArray();
    }

    static void SameFloats(FcsRecord a, FcsRecord b, string what)
    {
        Assert.True(a.Floats.OrderBy(k => k.Key).SequenceEqual(b.Floats.OrderBy(k => k.Key)), $"{what}: floats differ");
        Assert.True(a.Bools.OrderBy(k => k.Key).SequenceEqual(b.Bools.OrderBy(k => k.Key)), $"{what}: bools differ");
        Assert.True(a.Strings.OrderBy(k => k.Key).SequenceEqual(b.Strings.OrderBy(k => k.Key)), $"{what}: strings differ");
    }
}
