using Meitou.Content;
using Meitou.Data.Gameplay;
using Meitou.Data.Textures;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Rendering.Gpu;
using Meitou.Rendering.Gpu.Core;

namespace Meitou.Tests.Rendering;

/// <summary>The item icons the character renderer draws (docs/game/ui-screens.md "Item icons") against the pictures the game saved of the same items.</summary>
public class ItemIconRenderTests
{
    static VulkanDevice? TryCreate()
    {
        try { return VulkanDevice.Create(new VulkanDeviceOptions { Validation = false }); }
        catch (Exception e) when (e is VulkanException or DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { return null; }
    }

    /// <summary>Draws the icons of <paramref name="keys"/> in a headless context; the RGBA texels (rows bottom first, as the targets are) by key and the texture sizes.</summary>
    static Dictionary<IconKey, (byte[] Pixels, int Width, int Height)> Draw(GameInstall install, IReadOnlyList<IconKey> keys)
    {
        using var device = TryCreate();
        Assert.SkipWhen(device is null, "No Vulkan 1.3 device");
        using var ctx = new GpuContext(device!);
        using var characters = new CharacterRenderer(ctx, install, InstallData.FullLoadOrder!, new AssetLocator(install)) { LoadBudget = 0 };
        var result = new Dictionary<IconKey, (byte[], int, int)>();
        for (int frame = 0; frame < 40 && result.Count < keys.Count; frame++)
        {
            ctx.BeginFrame();
            foreach (var key in keys) characters.TryGetIcon(key, out _);
            characters.Update(default);
            characters.DrawIcons();
            ctx.Finish();
            if (characters.PendingIcons == 0)
                foreach (var key in keys)
                    if (characters.TryGetIcon(key, out var icon)) result[key] = (ctx.ReadBack(icon.Texture, 4), icon.Texture.Desc.Width, icon.Texture.Desc.Height);
        }
        return result;
    }

    /// <summary>Share of the pixels of <paramref name="a"/> and <paramref name="b"/> (same size) that are drawn in at least one that are drawn in both; <paramref name="flipB"/> reads b upside down.</summary>
    static double Overlap(byte[] a, byte[] b, int w, int h, bool flipA, bool flipB)
    {
        int both = 0, either = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool ina = a[((flipA ? h - 1 - y : y) * w + x) * 4 + 3] > 127, inb = b[((flipB ? h - 1 - y : y) * w + x) * 4 + 3] > 127;
                if (ina && inb) both++;
                if (ina || inb) either++;
            }
        return either == 0 ? 0 : (double)both / either;
    }

    [Fact]
    [Slow]
    public void Icons_drawn_from_the_mesh_cover_the_same_part_of_the_picture_as_the_games_saved_ones()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null || InstallData.FullLoadOrder is null, "no Kenshi install");
        var cache = Path.Combine(install!.DataDirectory, "icons");
        // The first saved picture of each kind: an armour, a weapon, a container (bag) and a plain item, base game and without a colour part.
        var wanted = new List<(IconKey Key, string File)>();
        foreach (var type in new[] { Meitou.Data.Fcs.FcsRecordType.CONTAINER, Meitou.Data.Fcs.FcsRecordType.WEAPON, Meitou.Data.Fcs.FcsRecordType.ARMOUR, Meitou.Data.Fcs.FcsRecordType.ITEM })
            foreach (var file in Directory.EnumerateFiles(cache, "*gamedata.base.*gamedata.base.png").OrderBy(f => f, StringComparer.Ordinal))
            {
                if (ItemIconCache.Parse(Path.GetFileNameWithoutExtension(file)) is not var (item, material, colour) || colour.Length > 0) continue;
                if (InstallData.FullLoadOrder!.Find(item) is not { } record || record.Type != type || !ItemIcons.Renders(record)) continue;
                wanted.Add((new IconKey(item, material), file));
                if (wanted.Count(w => InstallData.FullLoadOrder.Find(w.Key.Record)!.Type == type) == 2) break;
            }
        Assert.NotEmpty(wanted);
        var drawn = Draw(install, [.. wanted.Select(w => w.Key)]);
        string dump = Path.Combine(Path.GetTempPath(), "meitou-icon-test");
        Directory.CreateDirectory(dump);
        foreach (var (key, file) in wanted)
        {
            Assert.True(drawn.TryGetValue(key, out var ours), $"no icon drawn for {key}");
            var theirs = TextureLoader.LoadImage(File.ReadAllBytes(file));
            Assert.Equal((theirs.Width, theirs.Height), (ours.Width, ours.Height));
            double overlap = Overlap(ours.Pixels, theirs.Pixels, ours.Width, ours.Height, flipA: false, flipB: true);
            var flipped = new byte[ours.Pixels.Length];
            for (int y = 0; y < ours.Height; y++) ours.Pixels.AsSpan((ours.Height - 1 - y) * ours.Width * 4, ours.Width * 4).CopyTo(flipped.AsSpan(y * ours.Width * 4));
            string name = Path.GetFileNameWithoutExtension(file);
            PngWriter.Write(Path.Combine(dump, name + ".ours.png"), ours.Width, ours.Height, flipped);
            File.Copy(file, Path.Combine(dump, name + ".game.png"), overwrite: true);
            Assert.True(overlap >= 0.9, $"{name}: the drawn icon covers {overlap:0.00} of the saved one's picture (ours {ours.Width}x{ours.Height})");
        }
    }
}
