using System.Numerics;
using System.Text.RegularExpressions;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using static Meitou.Tests.Save.SaveSamples;

namespace Meitou.Tests.Save;

/// <summary>Writing saves: new records, derived data, and the temp-folder commit (docs/formats/save.md).</summary>
public partial class SaveWriteTests
{
    static SaveGame Small(out SavePlatoon platoon)
    {
        var b = new SaveBuilder(7);
        b.Game.Camera.Day = 3;
        b.Game.Camera.Hour = 9;
        b.Game.Camera.Minute = 41;
        b.AddFaction("204-gamedata.base", "Nameless", true, 1000);
        var other = b.AddFaction("10-x", "Other", false, 1000, [new SaveRelation("10-x", 100, 0, 0), new SaveRelation("204-gamedata.base", -20, 0, 0)], platoonCounter: 1);
        Assert.Equal(-20, other.Relations[1].Relation);
        b.AddTown("Town state A", "9-x-INGAME", "10-x", false, new ZoneCoordinate(20, 30));
        b.AddEmptyWorldState();
        platoon = b.AddPlatoon("Nameless_0", "Nameless", "204-gamedata.base", "5-x", new Vector3(100, 20, 300), money: 12);
        b.AddCharacter(platoon, "7-x", "Beep", "204-gamedata.base", new Vector3(100, 20, 300), Quaternion.Identity, isLeader: true);
        b.AddCharacter(platoon, "8-x", "Boop", "204-gamedata.base", new Vector3(103, 20, 300), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1));
        b.Game.Camera.SetMods(["gamedata.base", "x.mod"]);
        b.Game.UpdateDerived();
        return b.Game;
    }

    [Fact]
    public void A_failed_install_puts_the_old_save_back()
    {
        var game = Small(out _);
        string root = Temp("failinstall");
        string folder = Path.Combine(root, "slot");
        try
        {
            game.Write(folder);
            var before = File.ReadAllBytes(Path.Combine(folder, "quick.save"));
            game.Camera.Day = 99;
            Assert.Throws<IOException>(() => game.Write(folder, () => throw new IOException("locked")));
            // Between the two renames the old folder was moved aside; it must be back, whole, and nothing else left.
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder, "quick.save")));
            Assert.False(Directory.Exists(folder + ".old"));
            Assert.DoesNotContain(Directory.GetDirectories(root), d => Path.GetFileName(d).StartsWith("_current"));
            game.Write(folder);
            Assert.Equal(99, SaveGame.Load(folder).Camera.Day);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ------------------------------------------------------------------ against the user's saves

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void A_loaded_save_written_to_a_folder_is_the_same_files(string folder)
    {
        SkipWithoutSaves(folder);
        var game = Load(folder);
        string target = Path.Combine(Temp("copy"), "copy");
        try
        {
            game.Write(target);
            var original = new SaveFolder([folder]);
            var copy = SaveFolder.Open(target);
            Assert.Equal(original.Names, copy.Names);
            foreach (var name in original.Names)
                Assert.True(original.ReadAllBytes(name).AsSpan().SequenceEqual(copy.ReadAllBytes(name)), $"{name} differs");
        }
        finally { Directory.Delete(Path.GetDirectoryName(target)!, recursive: true); }
    }

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void Derived_data_computed_for_a_real_save_is_what_the_game_wrote(string folder)
    {
        SkipWithoutSaves(folder);
        var original = Load(folder);
        var game = original.Clone();
        game.UpdateDerived();
        Assert.Equal(original.Quick.SlotLists[0], game.Quick.SlotLists[0]);
        Assert.Equal(original.Quick.SlotLists[1], game.Quick.SlotLists[1]);
        Assert.Equal(original.Quick.SlotLists[2], game.Quick.SlotLists[2]);
        Assert.Equal(original.Quick.SlotLists[3], game.Quick.SlotLists[3]);
        var was = original.AllFiles().ToDictionary(f => f.Name, f => f.File);
        foreach (var (name, file) in game.AllFiles())
        {
            var old = was[name];
            if (file.Data.FileType != FcsFileType.V15) continue;   // saved by the mod editor, left as it is
            Assert.True(file.Data.NextId - old.Data.NextId is 0 or -1, $"{name}: nextId {file.Data.NextId} was {old.Data.NextId}");
            Assert.Equal(old.Records.Select(r => r.ByteSize), file.Records.Select(r => r.ByteSize));
            if (name.StartsWith("zone/")) Assert.Equal(old.SlotLists.Count, file.SlotLists.Count);
        }
        // The zone list names every file, including the one the original left out.
        Assert.Equal(game.Zones.Keys, game.Camera.Zones);
    }

    /// <summary>Key names with their numbers folded: <c>flesh3</c> and <c>flesh5</c> are one key <c>flesh#</c>.</summary>
    static IEnumerable<string> Keys(FcsRecord r) =>
        r.Bools.Keys.Select(k => "b:" + Fold(k)).Concat(r.Floats.Keys.Select(k => "f:" + Fold(k))).Concat(r.Ints.Keys.Select(k => "i:" + Fold(k)))
            .Concat(r.Vector3s.Keys.Select(k => "v3:" + Fold(k))).Concat(r.Vector4s.Keys.Select(k => "v4:" + Fold(k))).Concat(r.Strings.Keys.Select(k => "s:" + Fold(k)))
            .Concat(r.Filenames.Keys.Select(k => "fn:" + Fold(k))).Concat(r.References.Keys.Select(k => "ref:" + Fold(k)));

    static string Fold(string key) => Digits().Replace(key, "#");

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void New_records_have_the_key_sets_of_the_real_ones(string folder)
    {
        SkipWithoutSaves(folder);
        var real = Load(folder);
        var game = Small(out var platoon);

        void Compare(string what, FcsRecord made, IEnumerable<FcsRecord> reals, params string[] allowedMissing) =>
            Check(what, made, reals, k => allowedMissing.Contains(k));

        void Check(string what, FcsRecord made, IEnumerable<FcsRecord> reals, Func<string, bool> allowed)
        {
            var seen = new HashSet<string>();
            var always = (HashSet<string>?)null;
            foreach (var r in reals)
            {
                var keys = Keys(r).ToHashSet();
                seen.UnionWith(keys);
                always = always is null ? keys : [.. always.Intersect(keys)];
            }
            var ours = Keys(made).ToHashSet();
            Assert.True(ours.IsSubsetOf(seen), $"{what}: keys the game never writes: {string.Join(", ", ours.Except(seen))}");
            var missing = always!.Except(ours).Where(k => !allowed(k)).ToList();
            Assert.True(missing.Count == 0, $"{what}: keys every real record has and ours lacks: {string.Join(", ", missing)}");
        }

        var chars = real.Characters.Where(c => c.GameState!.Ints.ContainsKey("TI day")).ToList();
        var c0 = platoon.Characters[0];
        Compare("GAMESTATE_CHARACTER", c0.GameState!, chars.Select(c => c.GameState!));
        Compare("GAMESTATE_AI", c0.Ai!, chars.Select(c => c.Ai!));
        Compare("INVENTORY_STATE", c0.Inventory!, chars.Select(c => c.Inventory!));
        Compare("PLATOON", platoon.Record, real.Platoons.Select(p => p.Record), "mission employer#", "mission target#", "mission town#");
        Compare("GAMESTATE_TOWN", game.Towns[0].Record, real.Towns.Where(t => !t.IsNest).Select(t => t.Record), "ref:trade goods");
        Compare("GAMESTATE_FACTION", game.Factions[1].Record, real.Factions.Where(f => !f.IsPlayer).Select(f => f.Record));
        Compare("GAMESTATE_FACTION (player)", game.Factions[0].Record, real.Factions.Where(f => f.IsPlayer).Select(f => f.Record));
        Compare("WAR_SAVESTATE", game.Factions[1].War!, real.Factions.Select(f => f.War!));
        // CAMERA: the lists of unique characters, the per-dialogue timers and the selection list belong to a game in progress.
        Check("CAMERA", game.Camera.Record, [real.Camera.Record], k => k.Contains("usedUniques") || k.Contains(".mod") || k.Contains("-gamedata.base") || k.Contains("selected_characters"));
    }
}
