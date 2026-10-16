using Meitou.Data.Ui;
using Vk = Silk.NET.Vulkan;

namespace Meitou.Rendering;

/// <summary>
/// The UI depth stamp (docs/render-post.md, "UI depth stamp"): depth 0 is written into every depth slice under the controls that are fully opaque
/// (<see cref="UiPainter.StableOpaqueRects"/>), so the scene's fragments there fail the depth test before they are shaded. The rectangles are inset
/// by <see cref="Inset"/> render pixels where they meet the scene, covering the jitter and the neighbourhood the upscalers and FXAA read across the edge.
/// </summary>
public static class UiDepthStamp
{
    public static bool Enabled { get; set; } = true;

    /// <summary>Render pixels the stamp stays away from the rim of a control.</summary>
    public const int Inset = 8;

    /// <summary>The <paramref name="holes"/> (display pixels, drawn at <paramref name="displayWidth"/> x <paramref name="displayHeight"/>) as rectangles of the render target; empty when the sizes disagree.</summary>
    internal static Vk.Rect2D[] RenderRects(IReadOnlyList<UiRect> holes, int holesWidth, int holesHeight, int displayWidth, int displayHeight, int renderWidth, int renderHeight)
    {
        if (holes.Count == 0 || holesWidth != displayWidth || holesHeight != displayHeight || displayWidth <= 0 || displayHeight <= 0) return [];
        float sx = renderWidth / (float)displayWidth, sy = renderHeight / (float)displayHeight;
        var rects = new List<Vk.Rect2D>(holes.Count);
        foreach (var h in holes)
        {
            // No inset where the control meets the edge of the picture: nothing there is read across it.
            int x0 = h.X <= 0 ? 0 : (int)MathF.Ceiling(h.X * sx) + Inset, y0 = h.Y <= 0 ? 0 : (int)MathF.Ceiling(h.Y * sy) + Inset;
            int x1 = h.X + h.Width >= displayWidth ? renderWidth : (int)MathF.Floor((h.X + h.Width) * sx) - Inset;
            int y1 = h.Y + h.Height >= displayHeight ? renderHeight : (int)MathF.Floor((h.Y + h.Height) * sy) - Inset;
            // The scene targets hold the picture upside down (bottom row first), so the rows are mirrored.
            if (x1 > x0 && y1 > y0) rects.Add(new Vk.Rect2D(new Vk.Offset2D(x0, renderHeight - y1), new Vk.Extent2D((uint)(x1 - x0), (uint)(y1 - y0))));
        }
        return [.. rects];
    }
}
