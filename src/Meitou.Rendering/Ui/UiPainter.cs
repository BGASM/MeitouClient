using System.Numerics;
using Meitou.Data.Textures;
using Meitou.Data.Ui;
using Meitou.Rendering.Gpu;
using Silk.NET.Vulkan;

namespace Meitou.Rendering.Ui;

/// <summary>Native alpha-composited MyGUI atlas slices and styled native control frames.</summary>
public sealed class UiPainter : IDisposable
{
    readonly DebugOverlay overlay;
    readonly MyGuiResources resources;
    readonly Func<string, string?> resolve;
    DebugOverlay? standardFont, paintedFont;
    readonly Dictionary<string, (Texture Texture, SampledTexture Sampled)> textures = new(StringComparer.Ordinal);
    /// <summary>Textures the host draws at run time (the squad portraits), shown by an ImageBox's <c>ImageTexture</c> under their name; not owned.</summary>
    readonly Dictionary<string, (Texture Texture, SampledTexture Sampled)> external = new(StringComparer.Ordinal);

    /// <summary>Makes <paramref name="texture"/> the image an ImageBox shows for <c>ImageTexture</c> <paramref name="name"/> (the caller keeps and frees it).</summary>
    public void SetTexture(string name, Texture texture, SampledTexture sampled) => external[name] = (texture, sampled);

    public UiPainter(DebugOverlay overlay, MyGuiResources resources, Func<string, string?> resolve)
    {
        this.overlay = overlay;
        this.resources = resources;
        this.resolve = resolve;
        // Upload before rendering starts. Missing atlas files remain an explicit diagnostic.
        try
        {
            if (resolve("Exo2-SemiBold.ttf") is { } standard) standardFont = DebugOverlay.FromFont(overlay.Gpu, standard);
            if (resolve("Sentencia_1.ttf") is { } painted) paintedFont = DebugOverlay.FromFont(overlay.Gpu, painted);
            foreach (string name in resources.Skins.Values.Select(s => s.Texture).Where(s => s.Length > 0).Distinct())
            {
                LoadTexture(name);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Draw(IReadOnlyList<UiControl> controls, int width, int height, Vector2 mouse, string? active)
    {
        for (int uploads = 0; uploads < IconUploadsPerFrame && decodedIcons.TryDequeue(out var icon); uploads++) Upload(icon.Name, icon.Image);
        foreach (var c in controls)
        {
            var b = c.Bounds;
            string widgetState = c.Properties.GetValueOrDefault("State") ?? (!c.Enabled ? "disabled" : active == c.Action && active is not null ? "pushed" : b.Contains(mouse.X, mouse.Y) && c.Action.Length > 0 ? "highlighted" : "normal");
            var clip = (c.Clip ?? new(0, 0, width, height)).Intersect(new(0, 0, width, height));
            if (b.Intersect(clip).Width <= 0 || b.Intersect(clip).Height <= 0) continue;
            if (b.Width <= 0 || b.Height <= 0) continue;
            if (c.Background.W > 0)
            {
                var colour = c.Type == "Button" && c.Action == active ? c.ActiveColour ?? new Vector4(0.25f, 0.5f, 0.4f, c.Background.W)
                    : c.Type == "Button" && b.Contains(mouse.X, mouse.Y) ? c.HoverColour ?? c.Background : c.Background;
                if (!c.Enabled) colour = c.DisabledColour ?? colour * new Vector4(0.6f, 0.6f, 0.6f, 1);
                overlay.Rect(b.X, b.Y, b.X + b.Width, b.Y + b.Height, colour);
            }
            overlay.Flush(width, height, clip);
            if (c.Type == "ImageBox" && MyGuiImages.Resolve(resources, c.Properties) is { } image && (!image.Texture.StartsWith(IconPrefix, StringComparison.Ordinal) || textures.ContainsKey(image.Texture)))
            {
                var loaded = LoadTexture(image.Texture);
                var source = image.Source.Width > 0 && image.Source.Height > 0 ? image.Source : new UiRect(0, 0, loaded.Texture.Desc.Width, loaded.Texture.Desc.Height);
                // ImageFlipV: an image drawn by the renderer (bottom row first, as its targets keep it) shown the right way up.
                bool flip = c.Properties.GetValueOrDefault("ImageFlipV") == "true";
                float v0 = source.Y / loaded.Texture.Desc.Height, v1 = (source.Y + source.Height) / loaded.Texture.Desc.Height;
                if (flip) (v0, v1) = (v1, v0);
                overlay.Image(loaded.Sampled, new(b.X, b.Y, b.X + b.Width, b.Y + b.Height),
                    new(source.X / loaded.Texture.Desc.Width, v0,
                        (source.X + source.Width) / loaded.Texture.Desc.Width, v1),
                    Colour(c.Properties.GetValueOrDefault("Colour"), Vector4.One) * new Vector4(1, 1, 1, Alpha(c.Properties)));
                overlay.Flush(width, height, clip);
            }
            if (resources.Skins.TryGetValue(c.Skin, out var skin) && textures.TryGetValue(skin.Texture, out var atlas))
            {
                // A toggled button (Checked) uses MyGUI's *_checked states, else looks pushed.
                bool on = c.Properties.GetValueOrDefault("Checked") == "true";
                string state = on ? widgetState + "_checked" : widgetState;
                foreach (var part in skin.Parts.Where(p => p.Type is "SubSkin" or "MainSkin" or "TileRect"))
                {
                    if (!part.States.TryGetValue(state, out var source) && !(on && part.States.TryGetValue("pushed", out source)) && !part.States.TryGetValue("normal", out source)) continue;
                    var dest = part.Offset.AlignTo(b, skin.Width, skin.Height, part.Align);
                    var tint = Colour(c.Properties.GetValueOrDefault("Colour"), Vector4.One) * new Vector4(1, 1, 1, Alpha(c.Properties));
                    if (part.Type == "TileRect")
                    {
                        var properties = part.StateProperties?.GetValueOrDefault(state) ?? part.StateProperties?.GetValueOrDefault("normal");
                        string? tileSize = c.Properties.GetValueOrDefault("TileSize") ?? properties?.GetValueOrDefault("TileSize");
                        var size = tileSize?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        float tileW = size?.Length == 2 ? float.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture) : source.Width;
                        float tileH = size?.Length == 2 ? float.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture) : source.Height;
                        if (properties?.GetValueOrDefault("TileH") != "true") tileW = dest.Width;
                        if (properties?.GetValueOrDefault("TileV") != "true") tileH = dest.Height;
                        if (tileW > 0 && tileH > 0)
                            for (float tileY = dest.Y; tileY < dest.Y + dest.Height; tileY += tileH)
                                for (float tileX = dest.X; tileX < dest.X + dest.Width; tileX += tileW)
                                {
                                    float pieceW = Math.Min(tileW, dest.X + dest.Width - tileX), pieceH = Math.Min(tileH, dest.Y + dest.Height - tileY);
                                    overlay.Image(atlas.Sampled, new(tileX, tileY, tileX + pieceW, tileY + pieceH),
                                        new(source.X / atlas.Texture.Desc.Width, source.Y / atlas.Texture.Desc.Height,
                                            (source.X + source.Width * pieceW / tileW) / atlas.Texture.Desc.Width, (source.Y + source.Height * pieceH / tileH) / atlas.Texture.Desc.Height), tint);
                                }
                        continue;
                    }
                    // Crop the image and UVs to the viewport together.
                    float x0 = Math.Max(0, dest.X), y0 = Math.Max(0, dest.Y), x1 = Math.Min(width, dest.X + dest.Width), y1 = Math.Min(height, dest.Y + dest.Height);
                    if (x1 <= x0 || y1 <= y0 || dest.Width <= 0 || dest.Height <= 0) continue;
                    float tw = atlas.Texture.Desc.Width, th = atlas.Texture.Desc.Height;
                    overlay.Image(atlas.Sampled, new(x0, y0, x1, y1), new(
                        (source.X + (x0 - dest.X) / dest.Width * source.Width) / tw,
                        (source.Y + (y0 - dest.Y) / dest.Height * source.Height) / th,
                        (source.X + (x1 - dest.X) / dest.Width * source.Width) / tw,
                        (source.Y + (y1 - dest.Y) / dest.Height * source.Height) / th), tint);
                }
                overlay.Flush(width, height, clip);
            }
            if (c.Type == "Button" && c.Enabled && c.Action == active && c.ActiveColour is { } accent)
                overlay.Rect(b.X + 4, b.Y + b.Height - 4, b.X + b.Width - 4, b.Y + b.Height - 2, accent);
            overlay.Flush(width, height, clip);
            bool focused = c.Properties.GetValueOrDefault("Focused") == "true";
            if (c.Caption.Length == 0 && !focused) continue;
            var textArea = UiTextLayout.ContentRect(resources, c);
            if (textArea.Width <= 0 || textArea.Height <= 0) continue;
            var textClip = clip.Intersect(textArea);
            string fontName = c.Properties.GetValueOrDefault("FontName") ?? "";
            var font = fontName.Contains("Painted", StringComparison.Ordinal) || fontName.Contains("Banner", StringComparison.Ordinal) ? paintedFont ?? standardFont ?? overlay : standardFont ?? overlay;
            var textColour = MyGuiColours.Text(resources, c, widgetState, c.Enabled ? DebugOverlay.TextColour : new(0.45f, 0.43f, 0.38f, 1));
            font.Target = overlay.Target;
            float scale = c.TextScale ?? (c.Skin.Length > 0 ? Math.Clamp(height / 1080f, 0.5f, 1.5f) : 1);
            scale = Math.Min(scale, textArea.Height / font.LineHeight);
            string text = c.Caption.Replace("\\n", "\n", StringComparison.Ordinal);
            var lines = c.Properties.GetValueOrDefault("WordWrap") == "true" ? font.WrapText(text, textArea.Width, scale) : text.ReplaceLineEndings("\n").Split('\n').Select(line => font.FitText(line, textArea.Width, scale)).ToArray();
            string align = c.Properties.GetValueOrDefault("TextAlign") ?? "Left VCenter";
            float y = textArea.Y + (align.Contains("Bottom", StringComparison.Ordinal) ? Math.Max(0, textArea.Height - font.LineHeight * scale * lines.Count) : align.Split(' ').Any(a => a is "Center" or "VCenter") ? Math.Max(0, (textArea.Height - font.LineHeight * scale * lines.Count) / 2) : 0);
            if (focused)
            {
                int Index(string key) => int.TryParse(c.Properties.GetValueOrDefault(key), out int at) ? Math.Clamp(at, 0, text.Length) : 0;
                int selection = Index("SelectionStart"), length = Index("SelectionLength"), caret = Index("CaretIndex");
                if (length > 0)
                {
                    float start = textArea.X + font.MeasureText(text[..selection], scale);
                    float end = textArea.X + font.MeasureText(text[..Math.Min(selection + length, text.Length)], scale);
                    overlay.Rect(start, y, end, y + font.LineHeight * scale, new(0.25f, 0.4f, 0.45f, 0.8f));
                }
                if ((System.Diagnostics.Stopwatch.GetTimestamp() / (System.Diagnostics.Stopwatch.Frequency / 2)) % 2 == 0)
                {
                    float caretX = textArea.X + font.MeasureText(text[..caret], scale);
                    overlay.Rect(caretX, y, caretX + 1, y + font.LineHeight * scale, textColour);
                }
                overlay.Flush(width, height, textClip);
            }
            foreach (string line in lines)
            {
                float textWidth = font.MeasureText(line, scale);
                float x = textArea.X + (align.Contains("Right", StringComparison.Ordinal) ? textArea.Width - textWidth : align.Split(' ').Any(a => a is "Center" or "HCenter") ? (textArea.Width - textWidth) / 2 : 0);
                if (new Meitou.Data.Ui.UiRect(x, y, textWidth, font.LineHeight * scale).Intersect(textClip).Width > 0)
                    font.TextScaled(line, x, y, textColour, scale);
                y += font.LineHeight * scale;
            }
            font.Flush(width, height, textClip);
        }
        overlay.Flush(width, height);
    }

    static float Alpha(IReadOnlyDictionary<string, string> properties) => float.TryParse(properties.GetValueOrDefault("Alpha"), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out float value) ? Math.Clamp(value, 0, 1) : 1;
    static Vector4 Colour(string? value, Vector4 fallback)
    {
        if (value is null) return fallback;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4)) return fallback;
        var numbers = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out numbers[i])) return fallback;
        return new(numbers[0], numbers[1], numbers[2], parts.Length == 4 ? numbers[3] : 1);
    }

    /// <summary>
    /// Whether the image file <paramref name="path"/> (an item icon) is on the GPU and can be shown as an ImageBox's <c>ImageTexture</c> of <see cref="IconTexture"/>.
    /// The first ask starts decoding it on a worker, <see cref="Draw"/> uploads it when done; a file that cannot be read stays false.
    /// </summary>
    public bool IconReady(string path)
    {
        string name = IconTexture(path);
        if (textures.ContainsKey(name)) return true;
        if (requestedIcons.Add(name))
            Task.Run(() =>
            {
                try { decodedIcons.Enqueue((name, TextureLoader.LoadFile(path, allMips: false).Levels[0])); }
                catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException) { }
            });
        return false;
    }

    /// <summary>The <c>ImageTexture</c> name under which the image file <paramref name="path"/> is shown once <see cref="IconReady"/>.</summary>
    public static string IconTexture(string path) => IconPrefix + path;
    const string IconPrefix = "file:";
    const int IconUploadsPerFrame = 4;
    readonly HashSet<string> requestedIcons = new(StringComparer.Ordinal);
    readonly System.Collections.Concurrent.ConcurrentQueue<(string Name, RgbaImage Image)> decodedIcons = new();

    (Texture Texture, SampledTexture Sampled) LoadTexture(string name)
    {
        if (external.TryGetValue(name, out var given) || textures.TryGetValue(name, out given)) return given;
        string path = resolve(name) ?? throw new FileNotFoundException($"MyGUI texture not found: {name}");
        return Upload(name, TextureLoader.LoadFile(path, allMips: false).Levels[0]);
    }

    (Texture Texture, SampledTexture Sampled) Upload(string name, RgbaImage image)
    {
        var gpu = overlay.Gpu;
        using var upload = gpu.Uploads.Begin();
        var texture = upload.Create(new TextureDesc(Format.R8G8B8A8Unorm, image.Width, image.Height, Name: $"UI {name}"));
        var sampled = new SampledTexture(gpu.Samplers.Get(SamplerDesc.FromGl(TextureMinFilter.Linear, TextureMagFilter.Linear,
            TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, false, DepthFunction.Lequal, false, 1, false, 0)), texture.View(), texture.Image);
        upload.Write(texture, 0, 0, new Rect2D(default, new Extent2D((uint)image.Width, (uint)image.Height)), image.Pixels);
        return textures[name] = (texture, sampled);
    }

    public void Dispose()
    {
        foreach (var t in textures.Values) t.Texture.Dispose();
        textures.Clear();
        standardFont?.Dispose(); paintedFont?.Dispose();
        standardFont = null; paintedFont = null;
    }
}
