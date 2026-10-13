using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Ogre;
using Meitou.Simulation;

namespace Meitou.Tests.Simulation;

/// <summary>How animals look and move (docs/game/animals.md "Appearance" and "Animations"): their looks, skeletons and clip sets from the install's records.</summary>
[Slow]
public class AnimalAnimationTests(ITestOutputHelper output)
{
    const string Bonedog = "3979-gamedata.base", BeakThing = "2859-gamedata.base", Leviathan = "3967-gamedata.base";

    static (GameDatabase Db, string Root) Install()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        return (InstallData.BaseGame!, InstallData.Install!.Root);
    }

    static string Path(string root, string fcs) => System.IO.Path.Combine(root, fcs.Replace('\\', System.IO.Path.DirectorySeparatorChar).TrimStart('.', System.IO.Path.DirectorySeparatorChar));

    static readonly Lazy<AnimationLengths> lengths = new(() => AnimationLengths.Load(InstallData.Install!.Root, InstallData.BaseGame));

    [Fact]
    public void Every_animal_has_a_look_from_its_race_whose_mesh_skeleton_and_fur_fit()
    {
        var (db, root) = Install();
        var animals = db.OfType(FcsRecordType.ANIMAL_CHARACTER).ToList();
        Assert.Equal(43, animals.Count);
        foreach (var record in animals)
        {
            var look = CharacterAppearance.BuildAnimal(db, root, record);
            Assert.Same(record, look.Animal);
            Assert.Equal(look.Race.GetPath("male mesh"), look.Race.GetPath("female mesh"));   // the male mesh stands for both
            var mesh = OgreMeshReader.ReadFile(Path(root, look.BodyMesh));
            var skeleton = OgreSkeletonReader.ReadFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path(root, look.BodyMesh))!, mesh.SkeletonName!));
            Assert.True(lengths.Value.HasSpecies(look.Race.StringId), $"{record.Name}: its race's skeleton was read");
            Assert.NotNull(look.BodyTexture);
            // The fur (the race's hair ATTACHMENT) is skinned with the body's bone palette: its own skeleton's handles name the same bones.
            foreach (var fur in look.Parts.Where(p => p.Mode == AttachMode.SharedSkeleton))
            {
                var furMesh = OgreMeshReader.ReadFile(Path(root, fur.Mesh));
                var furSkeleton = OgreSkeletonReader.ReadFile(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path(root, fur.Mesh))!, furMesh.SkeletonName!));
                Assert.All(furSkeleton.Bones, b => Assert.Equal(b.Name, skeleton.FindBone(b.Handle)?.Name));
            }
        }
        // One look per record: the population's source hands every animal of a record the same instance (one asset per species in the renderer).
        var source = new GeneratedAppearances(db, root);
        Assert.Same(source.Create(Bonedog, null, 1), source.Create(Bonedog, null, 2));
    }

    [Fact]
    public void Clip_lengths_are_per_skeleton()
    {
        Install();
        var db = InstallData.BaseGame!;
        string Race(string id) => db.Find(id)!.GetReferences("race").First(r => r.Values.Value0 > 0).TargetStringId;
        var all = lengths.Value;
        Assert.Equal(0.767f, all.For(Race(Bonedog)).Of("attack1"), 3);
        Assert.Equal(5f, all.For(Race(BeakThing)).Of("attack1"), 3);
        Assert.Equal(2.433f, all.For(Race(Leviathan)).Of("attack1"), 3);
        Assert.Equal(1.1f, all.For(Race(Bonedog)).Of("walk"), 3);
        // People keep the humanoid table (a race without a table of its own falls back to it).
        Assert.Same(all, all.For("204-gamedata.base"));
        Assert.Same(all, all.For(null));
    }

    [Fact]
    public void Animal_clips_come_after_every_humanoid_one_and_follow_the_records_lists()
    {
        var (db, _) = Install();
        var library = AnimationLibrary.FromDatabase(db);
        var defs = library.Definitions;
        int firstAnimal = defs.ToList().FindIndex(d => d.Category == AnimationLibrary.AnimalCategory);
        Assert.True(firstAnimal > 0);
        Assert.All(defs.Skip(firstAnimal), d => Assert.Equal(AnimationLibrary.AnimalCategory, d.Category));   // appended: no humanoid index moved
        Assert.Null(library.Animal("204-gamedata.base"));

        var dog = library.Animal(Bonedog)!;
        Assert.Equal(["run", "walk"], dog.Movement.Select(i => defs[i].Clip).Order());
        Assert.Equal(["idle noloop", "idle2", "idle3"], dog.Idles.Select(x => defs[x.Index].Clip).Order());
        Assert.Equal("gotosleep", defs[dog.Fall[0]].Clip);
        Assert.Contains(dog.Lying, i => defs[i].Clip == "sleep");   // the bonedog lists no looping sleeponfloor: the fallback
        // A creature technique is a technique the library finds by id.
        Assert.True(library.IndexOfTechnique("4016-gamedata.base") >= firstAnimal);
    }

    [Fact]
    public void A_bonedog_walks_runs_and_idles_on_its_own_clips()
    {
        var (db, _) = Install();
        var library = AnimationLibrary.FromDatabase(db);
        var system = new AnimationSystem(library, lengths.Value);
        var cold = new CharacterCold { RecordId = Bonedog };
        var a = new CharacterAnimation();
        string Playing(float speed)
        {
            for (int t = 0; t < 30; t++) system.Update(a, cold, speed, 1 / 30f, 1, 7, t);
            var layers = system.Publish(a);
            output.WriteLine($"{speed}: {string.Join(", ", layers.Select(l => $"{l.Name} {l.Weight:0.00} @{l.Time:0.00}"))}");
            return layers.MaxBy(l => l.Weight).Name;
        }
        Assert.Contains(Playing(0), new[] { "idle noloop", "idle2", "idle3" });
        Assert.Equal("walk", Playing(15));
        Assert.Equal("run", Playing(90));
        Assert.Equal(1f, a.Rate);   // no body: size 1, F = 1
    }
}
