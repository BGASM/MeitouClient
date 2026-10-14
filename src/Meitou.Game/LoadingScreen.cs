using System.Diagnostics;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Rendering;
using Meitou.Rendering.Ui;

namespace Meitou.Game;

/// <summary>
/// The loading screen drawn while the game starts and between scenes (docs/engine.md "Loading"): the original's <c>Kenshi_LoadingScreen</c> (one of its
/// <c>loading_screen*.dds</c> pictures full screen, a panel with LOADING... and a tip from <c>data/tips.txt</c>), and Meitou's spinner in the middle with an
/// approximate progress bar under it (the original has neither: engine choice). The bar eases towards the end of the running step by the time the step usually
/// takes, so it moves while a step runs; it never goes back.
/// </summary>
sealed class LoadingScreen : IDisposable
{
    readonly DebugOverlay overlay;
    DebugOverlay? stamp;   // the original's LOADING... face (Kenshi_UI_Messages: MailartRubberstamp-Regular.ttf)
    readonly UiPainter? painter;
    readonly MyGuiResources? resources;
    readonly MyGuiLayout? layout;
    readonly string? picture;
    readonly string tip;
    readonly Stopwatch clock = Stopwatch.StartNew();
    string label = "";
    double from, to, expected = 1, stepStart;
    float shown;

    LoadingScreen(DebugOverlay overlay, UiPainter? painter, MyGuiResources? resources, MyGuiLayout? layout, string? picture, string tip)
    {
        (this.overlay, this.painter, this.resources, this.layout, this.picture, this.tip) = (overlay, painter, resources, layout, picture, tip);
    }

    /// <summary>The screen with the original's layout, picture and tips when they are found (else the spinner and bar on black).</summary>
    public static LoadingScreen Create(DebugOverlay overlay, Func<string, string?> resolve, string? dataDirectory)
    {
        var random = new Random();
        string tip = "";
        if (dataDirectory is not null && Path.Combine(dataDirectory, "tips.txt") is var tipsFile && File.Exists(tipsFile))
        {
            var tips = File.ReadAllLines(tipsFile).Where(l => l.Trim().Length > 0).ToArray();
            if (tips.Length > 0) tip = tips[random.Next(tips.Length)].Trim();
        }
        try
        {
            var resources = MyGuiResources.Load("kenshi_init.xml", resolve);
            var layout = resolve("Kenshi_LoadingScreen.layout") is { } file ? MyGuiTemplates.Expand(MyGuiReader.ReadLayout(file), resources) : null;
            // The pictures the original ships (loading_screen0..10, some numbers missing).
            var pictures = Enumerable.Range(0, 16).Select(i => $"loading_screen{i}.dds").Where(n => resolve(n) is not null).ToArray();
            var painter = new UiPainter(overlay, resources, resolve);
            var stamp = resolve("MailartRubberstamp-Regular.ttf") is { } stampFile ? DebugOverlay.FromFont(overlay.Gpu, stampFile, 64) : null;
            return new(overlay, painter, resources, layout, pictures.Length > 0 ? pictures[random.Next(pictures.Length)] : null, tip) { stamp = stamp };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Xml.XmlException)
        {
            Console.Error.WriteLine($"loading   the original loading screen did not load ({e.Message}); spinner only");
            return new(overlay, null, null, null, null, tip);
        }
    }

    /// <summary>A step starts: its name, the bar's share it covers (<paramref name="start"/> to <paramref name="end"/>, 0..1) and about how long it takes.</summary>
    public void Step(string name, double start, double end, double seconds)
    {
        label = name;
        from = Math.Max(start, shown);
        to = Math.Max(end, from);
        expected = Math.Max(seconds, 0.05);
        stepStart = clock.Elapsed.TotalSeconds;
    }

    /// <summary>The bar's position now: the step's start, eased towards 95 % of its share by the time it has run against the time it usually takes.</summary>
    public float Progress
    {
        get
        {
            double t = (clock.Elapsed.TotalSeconds - stepStart) / expected;
            double eased = from + (to - from) * 0.95 * (1 - Math.Exp(-2.5 * t));
            return shown = Math.Max(shown, (float)Math.Clamp(eased, 0, 1));
        }
    }

    /// <summary>Starts the bar again (a new load between scenes).</summary>
    public void Restart() { shown = 0; from = to = 0; }

    public void Draw(int width, int height)
    {
        overlay.Rect(0, 0, width, height, new Vector4(0.02f, 0.02f, 0.018f, 1));
        overlay.Flush(width, height);
        float progress = Progress;
        if (painter is not null && layout is not null && resources is not null)
        {
            var b = new UiBindings();
            if (picture is not null) b.Properties["BackgroundImage"] = new Dictionary<string, string> { ["ImageTexture"] = picture };
            b.Text["LoadingText"] = stamp is null ? (label.Length > 0 ? $"LOADING... {label}" : "LOADING...") : "";
            b.Text["MessageText"] = tip;
            b.Properties["MessageText"] = new Dictionary<string, string> { ["Static"] = "true", ["WordWrap"] = "true", ["TextAlign"] = "Center", ["FontName"] = "Kenshi_FloaterFont_Large" };
            var controls = MyGuiPresentation.Arrange(layout, width, height, b, resources.Layers);
            painter.Draw(controls, width, height, new Vector2(-1, -1), null);
            // LOADING... in the stamp face where the layout has it, and what is loading after it, smaller.
            if (stamp is not null && controls.FirstOrDefault(c => c.Name == "LoadingText") is { } slot)
            {
                float scale = slot.Bounds.Height * 0.7f / stamp.LineHeight, x = slot.Bounds.X + 14, top = slot.Bounds.Y + (slot.Bounds.Height - stamp.LineHeight * scale) / 2;
                stamp.Target = overlay.Target;
                stamp.TextScaled("LOADING...", x + 2, top + 2, new Vector4(0, 0, 0, 0.8f), scale);
                float end = x + stamp.TextScaled("LOADING...", x, top, new Vector4(0.87f, 0.85f, 0.81f, 1), scale);
                stamp.Flush(width, height);
                if (label.Length > 0) overlay.Text(label, end + 16, slot.Bounds.Y + (slot.Bounds.Height - overlay.LineHeight) / 2, new Vector4(0.62f, 0.6f, 0.52f, 1));
            }
        }
        else if (label.Length > 0)
        {
            overlay.Text(label, (width - label.Length * overlay.CharWidth) / 2, height / 2f + 70, DebugOverlay.TextColour);
        }

        // The spinner: twelve spokes round the middle of the screen, the bright one going round once a second.
        float cx = width / 2f, cy = height / 2f, inner = 16, outer = 34;
        double turn = clock.Elapsed.TotalSeconds % 1;
        const int Spokes = 12;
        float w = MathF.Min(360, width - 32), x0 = cx - w / 2, y0 = cy + 56;
        overlay.Rect(x0 - 14, cy - 50, x0 + w + 14, y0 + 26, new Vector4(0, 0, 0, 0.45f));   // a dark backdrop behind the spinner and the bar
        for (int i = 0; i < Spokes; i++)
        {
            float a = i * MathF.Tau / Spokes;
            float behind = (float)(((turn * Spokes - i) % Spokes + Spokes) % Spokes) / Spokes;   // 0 at the head, towards 1 behind it
            var colour = new Vector4(0.9f, 0.84f, 0.7f, 0.15f + 0.85f * (1 - behind));
            overlay.Line(cx + MathF.Sin(a) * inner, cy - MathF.Cos(a) * inner, cx + MathF.Sin(a) * outer, cy - MathF.Cos(a) * outer, 4, colour);
        }
        // The bar under it.
        overlay.Rect(x0 - 2, y0 - 2, x0 + w + 2, y0 + 12, new Vector4(0.35f, 0.31f, 0.24f, 1));
        overlay.Rect(x0, y0, x0 + w, y0 + 10, new Vector4(0.08f, 0.07f, 0.06f, 1));
        overlay.Rect(x0, y0, x0 + w * progress, y0 + 10, new Vector4(0.78f, 0.66f, 0.42f, 1));
        overlay.Flush(width, height);
    }

    public void Dispose() { painter?.Dispose(); stamp?.Dispose(); }
}
