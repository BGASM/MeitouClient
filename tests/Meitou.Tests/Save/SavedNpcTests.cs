using System.Numerics;
using Meitou.Data;
using Meitou.Data.Save;
using Meitou.Engine;
using Meitou.Simulation;
using Meitou.Simulation.Saving;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Save.SaveSamples;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Save;

/// <summary>The NPC platoons of a real save (docs/formats/save.md "NPC platoons", docs/simulation.md "Saves"): made as their saved characters, and kept by a Meitou save.</summary>
public class SavedNpcTests(ITestOutputHelper output)
{
    sealed record Run(SimWorld World, PopulationSystem Population) : IDisposable
    {
        public void Dispose()
        {
            World.Dispose();
            foreach (var s in World.Systems) (s as IDisposable)?.Dispose();
        }
    }

    [Fact, Slow]
    public void A_real_saves_npcs_are_made_as_their_saved_characters_and_a_meitou_save_of_them_loads_back_the_same()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null, "Kenshi install not found");
        string? folder = Folders.FirstOrDefault(f => Path.GetFileName(f) == "2") ?? Folders.FirstOrDefault();
        Assert.SkipWhen(folder is null, "No saves with a quick.save found");
        var db = InstallData.FullLoadOrder!;
        var levels = InstallData.Levels!;
        var data = PopulationData.Create(db, levels.Towns(), new GeneratedAppearances(db, install!.Root));
        var walk = new OpenGroundWalkability((_, _) => 300);

        // The population and the player only: nobody walks off, so a character stands where it was made.
        Run Start(SaveGame save, out LoadedSave loaded)
        {
            var systems = StandardSystems.Build(data, walk, new StandardSystemOptions { Parts = StandardParts.Population | StandardParts.Player, Population = new PopulationSettings { CheckEveryTicks = 1 } });
            var world = new SimWorld(new WorldSettings { Seed = 5, Threads = 2, PublishSnapshots = false }, walk, systems.Systems);
            var run = new Run(world, systems.Population!);
            loaded = SaveLoader.Load(world, save, data, new SaveLoadOptions { ApplyRelations = false, Appearances = data.Appearances });
            // Until every town in the player's zones has its squads, then a little longer for the roaming platoons.
            bool Done() => run.Population.ActiveZones.Count > 0 && data.Sites.Where(s => run.Population.ActiveZones.Contains(s.Zone)).All(s => run.Population.StatusOf(s.Index) == SiteStatus.Loaded);
            Assert.True(TestWaits.TickUntil(world, Done), "the towns round the player were made");
            world.RunTicks(30);
            return run;
        }

        // The characters other than the player's, by name and place.
        static List<(string Name, int X, int Z)> Npcs(SimWorld world)
        {
            var list = new List<(string, int, int)>();
            var prev = world.Characters.Previous;
            for (int i = 0; i < prev.Length; i++)
                if (prev[i].Alive && world.Characters.Cold(i) is { IsPlayer: false } cold) list.Add((cold.Name, (int)MathF.Round(prev[i].Position.X * 10), (int)MathF.Round(prev[i].Position.Z * 10)));
            list.Sort();
            return list;
        }

        using var a = Start(Load(folder!), out var loadedA);

        // The save's NPC platoons that were made: each of their characters is where and who the save says.
        int made = 0;
        foreach (var platoon in a.World.Saved!.Platoons)
            foreach (var m in platoon.Members.Where(m => !m.Live.IsNone))
            {
                var cold = a.World.Characters.Cold(m.Live.Slot)!;
                var at = a.World.Characters.Previous[m.Live.Slot].Position;
                Assert.Equal(m.Source.Name.Length > 0 ? m.Source.Name : db.Find(m.Source.RecordId)!.Name, cold.Name);
                Assert.True(Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(m.Source.Position.X, m.Source.Position.Z)) < 5, $"{platoon.Name}/{cold.Name} is {Vector2.Distance(new Vector2(at.X, at.Z), new Vector2(m.Source.Position.X, m.Source.Position.Z))} from its saved place");
                made++;
            }
        output.WriteLine($"{Path.GetFileName(folder)}: {made} saved NPC(s) made, {a.World.Characters.Count} characters, {a.World.Squads.All.Count()} squads");
        Assert.True(made > 0);

        // A Meitou save of that world, loaded into a new one: the same characters in the same places, none made twice.
        string root = Temp("npcs");
        try
        {
            var save = SaveCapture.Capture(a.World, data, loadedA.Clock, loadedA, new SaveCaptureOptions { Placements = levels.Towns().ToList() });
            string saved = QuickSave.Folder(root);
            save.Write(saved);
            using var b = Start(SaveGame.Load(saved), out _);
            var was = Npcs(a.World);
            var now = Npcs(b.World);
            output.WriteLine($"{was.Count} NPCs before, {now.Count} after the Meitou save; squads {a.World.Squads.All.Count()} -> {b.World.Squads.All.Count()}");
            Assert.Equal(was, now);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
