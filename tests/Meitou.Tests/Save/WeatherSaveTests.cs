using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using static Meitou.Tests.Save.SaveSamples;

namespace Meitou.Tests.Save;

/// <summary>The weather of a save (docs/formats/save.md "BIOMES"): a real save's regions are restored into a scheduler and written back with no key changed.</summary>
public class WeatherSaveTests
{
    /// <summary>
    /// Every entry of the record: text, integers and bools as they are, floats and vectors as number lists. <c>_w_wi_time</c> is left out: the game counts it as it runs and
    /// Meitou writes (now - start) x 20/11 (docs/formats/save.md), so it only agrees for a weather that ran at the game's pace.
    /// </summary>
    static Dictionary<string, object> Entries(FcsRecord r) => r.Strings.Select(k => ("s:" + k.Key, (object)k.Value)).Concat(r.Ints.Select(k => ("i:" + k.Key, (object)k.Value)))
        .Concat(r.Bools.Select(k => ("b:" + k.Key, (object)k.Value)))
        .Concat(r.Floats.Where(k => !k.Key.EndsWith("_w_wi_time", StringComparison.Ordinal)).Select(k => ("f:" + k.Key, (object)new[] { k.Value })))
        .Concat(r.Vector3s.Select(k => ("v:" + k.Key, (object)new[] { k.Value.X, k.Value.Y, k.Value.Z }))).ToDictionary(e => e.Item1, e => e.Item2);

    /// <summary>Equal, floats within what a direction rebuilt from its angle loses (the wind direction is stored as a vector, written from the angle).</summary>
    static bool Same(object? a, object? b) => a is float[] x && b is float[] y ? x.Length == y.Length && x.Zip(y).All(p => Math.Abs(p.First - p.Second) <= 1e-5f * Math.Max(1, Math.Abs(p.First))) : Equals(a, b);

    static string Show(object? v) => v is float[] f ? string.Join(",", f) : v?.ToString() ?? "(none)";

    [Theory, Trait("Category", "Slow")]
    [MemberData(nameof(SaveSamples.Names), MemberType = typeof(SaveSamples))]
    public void A_real_saves_weather_is_restored_and_written_back_with_no_key_changed(string folder)
    {
        SkipWithoutSaves(folder);
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var save = Load(folder);
        var biomes = save.Biomes;
        Assert.SkipWhen(biomes is null, "The save has no BIOMES record");
        var read = SaveWeather.Read(biomes!);
        Assert.NotEmpty(read);

        var now = new WeatherTime(save.Camera.Day, save.Camera.Hour + save.Camera.Minute / 60.0);
        var db = InstallData.FullLoadOrder!;
        var world = WeatherWorld.Create(db, InstallData.Install!, 1, now);
        world.Restore(read, now);

        // The scheduler holds what the save said (the list positions are the scheduler's own).
        var restored = world.Snapshot().ToDictionary(s => s.RegionId, StringComparer.Ordinal);
        int matched = 0;
        foreach (var region in read)
        {
            if (!restored.TryGetValue(region.RegionId, out var held)) continue;   // a region the loaded data lacks keeps nothing
            Assert.Equal(region with { SeasonIndex = 0, WeatherEntryIndex = 0 }, held with { SeasonIndex = 0, WeatherEntryIndex = 0 });
            matched++;
        }
        Assert.True(matched > 0);

        // Written back at the save's own time: no key of the record differs. The loaded save is shared, so it is written on a copy.
        var copy = new FcsRecord { Type = biomes!.Type, StringId = biomes.StringId, Name = biomes.Name };
        foreach (var (k, v) in biomes.Strings) copy.Strings[k] = v;
        foreach (var (k, v) in biomes.Ints) copy.Ints[k] = v;
        foreach (var (k, v) in biomes.Floats) copy.Floats[k] = v;
        foreach (var (k, v) in biomes.Vector3s) copy.Vector3s[k] = v;
        var was = Entries(copy);
        SaveWeather.Write(copy, world.Snapshot(), now, db);
        var after = Entries(copy);
        var changed = was.Keys.Union(after.Keys).Where(k => !Same(was.GetValueOrDefault(k), after.GetValueOrDefault(k))).ToList();
        Assert.True(changed.Count == 0, $"{changed.Count} BIOMES entries differ, e.g. {string.Join("; ", changed.Take(6).Select(k => $"{k} {Show(was.GetValueOrDefault(k))} -> {Show(after.GetValueOrDefault(k))}"))}");
    }
}
