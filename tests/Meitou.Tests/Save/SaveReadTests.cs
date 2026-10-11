using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using static Meitou.Tests.Save.SaveSamples;

namespace Meitou.Tests.Save;

/// <summary>Reading saves: the file layer on synthetic data, then the user's saves in place (docs/formats/save.md).</summary>
public class SaveReadTests
{
    [Fact]
    public void Strings_that_are_not_utf8_survive_a_round_trip()
    {
        // The ids of TERRAIN_DECALS instances are raw 4-byte integers in a string slot.
        var file = new SaveFile();
        var decals = new FcsRecord { Type = (int)FcsRecordType.TERRAIN_DECALS, StringId = "1--INGAME" };
        decals.Instances.Add(new FcsInstance { Id = LosslessUtf8.GetString([0x81, 0, 0, 0]), Target = "-" });
        decals.Instances.Add(new FcsInstance { Id = LosslessUtf8.GetString([0xC3, 0xA9, 0xFF, 0xFE]), Target = "é" });
        file.Records.Add(decals);
        var bytes = file.ToBytes();
        var back = SaveFile.Read(bytes);
        Assert.Equal(bytes, back.ToBytes());
        Assert.Equal("é\udcff\udcfe", back.Records[0].Instances[1].Id);
    }

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void Every_file_of_a_save_reads_and_writes_back_to_the_same_bytes(string folder)
    {
        SkipWithoutSaves(folder);
        int files = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = File.ReadAllBytes(path);
            var file = SaveFile.Read(bytes);
            Assert.True(bytes.AsSpan().SequenceEqual(file.ToBytes()), $"{Path.GetRelativePath(folder, path)} differs after a round trip");
            files++;
        }
        Assert.True(files > 2);
    }

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void Zones_overlay_the_base_placements_by_instance_id(string folder)
    {
        SkipWithoutSaves(folder);
        var install = Meitou.Content.GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var game = Load(folder);
        var level = WorldLevelData.Load(install!);
        int checkedZones = 0;
        foreach (var zone in game.Zones.Values)
        {
            level.Zones.TryGetValue(zone.Coordinate, out var baseDb);
            var merged = zone.Overlay(baseDb);
            if (zone.BuildingList is null) { Assert.True(merged.Count == (baseDb?.Find(WorldLevelData.BuildingListId)?.Instances.Count ?? 0)); continue; }
            var saved = zone.BuildingList.Instances.Select(i => i.Id).ToHashSet();
            // Everything the save lists is in the result with the saved target; every base placement the save lacks is kept.
            Assert.All(zone.BuildingList.Instances, i => Assert.Equal(i.Target, merged.Single(m => m.InstanceId == i.Id).Target));
            if (baseDb?.Find(WorldLevelData.BuildingListId) is { } list)
            {
                Assert.All(list.Instances.Keys, id => Assert.Contains(merged, m => m.InstanceId == id));
                Assert.All(merged.Where(m => m.Overrides), m => Assert.Contains(m.InstanceId, saved));
            }
            checkedZones++;
        }
        Assert.True(checkedZones > 0);
    }
}
