using System.Numerics;
using Meitou.Data.Textures;
using Meitou.Rendering.Ui;

namespace Meitou.Game;

/// <summary>Continuous window surfaces and reusable button slices from the Meitou UI kit.</summary>
sealed class SettingsMenuArt
{
    public const string Prefix = "meitou:settings:";
    public static string SurfacePrefix => Prefix + "v3:";
    RgbaImage? atlas;

    public void Ensure(UiPainter painter, bool meitou, Func<string, string?>? resolve)
    {
        if (!meitou || painter.HasTexture(SurfacePrefix + "button")) return;
        string path = resolve?.Invoke("Kenshi_UI.png") ?? throw new FileNotFoundException("Kenshi_UI.png not found.");
        atlas = TextureLoader.LoadFile(path, allMips: false).Levels[0];
        painter.SetImage(SurfacePrefix + "button", MenuArt.Plate(atlas, new(612, 766, 118, 45), 160, 40, 1, 0.7f), srgb: false);
        painter.SetImage(SurfacePrefix + "button-hover", MenuArt.Plate(atlas, new(612, 766, 118, 45), 160, 40, 1, 0.85f), srgb: false);
        painter.SetImage(SurfacePrefix + "button-selected", MenuArt.Plate(atlas, new(741, 766, 118, 45), 160, 40, 1, 0.85f), srgb: false);
        painter.SetImage(SurfacePrefix + "paper", MenuArt.Card(atlas, new(352, 457, 164, 81), 160, 40, 1, 0, 1), srgb: false);
        painter.SetImage(SurfacePrefix + "paper-hover", MenuArt.Card(atlas, new(352, 457, 164, 81), 160, 40, 1, 0, 1.1f), srgb: false);
    }

    public string WindowTexture(UiPainter painter, float width, float height)
    {
        int w = Math.Max(1, (int)MathF.Round(width)), h = Math.Max(1, (int)MathF.Round(height));
        string name = $"{SurfacePrefix}window:{w}x{h}";
        if (!painter.HasTexture(name)) painter.SetImage(name, MenuArt.WindowSurface(atlas!, w * 2, h * 2, 2), srgb: false);
        return name;
    }
}

static partial class MenuArt
{
    /// <summary>Rust under the kit's dark tint, with a rounded inner rim and a soft drop shadow.</summary>
    public static RgbaImage Window(RgbaImage atlas) => WindowSurface(atlas, 128, 128, 1);

    public static RgbaImage WindowSurface(RgbaImage atlas, int width, int height, float s)
    {
        int margin = (int)(16 * s);
        var image = new RgbaImage(width + margin * 2, height + margin * 2);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                float px = x + 0.5f - margin, py = y + 0.5f - margin;
                float distance = RoundedDistance(px, py, width, height, 10 * s);
                float shadow = 0.55f * Smooth(1 - Math.Clamp((RoundedDistance(px, py - 6 * s, width, height, 10 * s) + 3 * s) / (14 * s), 0, 1));
                // The full rust crop contains a welded T joint. Use the uninterrupted sheet to its right.
                var texture = Sample(atlas, new(1620, 136, 200, 510), px / width, py / height);
                var colour = Vector3.Lerp(texture, new Vector3(14, 11, 9) / 255, 0.86f);
                if (distance > -s) colour = Vector3.Lerp(colour, new Vector3(175, 166, 139) / 255, 0.12f);
                Put(image, x, y, Over(new(colour, Math.Clamp(0.5f - distance, 0, 1)), new(0, 0, 0, shadow)));
            }
        return image;
    }
}
