using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.Ui;

/// <summary>Map pan/zoom in normalized texture coordinates. Projection is supplied by the game's adapter.</summary>
public sealed class UiMapView
{
    public Vector2 Centre { get; private set; } = new(0.5f);
    public float Zoom { get; private set; } = 1;
    public void CentreOn(Vector2 normalized) => Centre = Clamp(normalized);
    Vector2 Clamp(Vector2 value) => Vector2.Clamp(value, new(0.5f / Zoom), new(1 - 0.5f / Zoom));
    public UiRect ImageBounds(UiRect viewport)
    {
        float size = Math.Max(viewport.Width, viewport.Height) * Zoom;
        return new(viewport.X + viewport.Width / 2 - Centre.X * size, viewport.Y + viewport.Height / 2 - Centre.Y * size, size, size);
    }
    public Vector2 At(UiRect viewport, Vector2 pixel)
    {
        var image = ImageBounds(viewport);
        return new((pixel.X - image.X) / image.Width, (pixel.Y - image.Y) / image.Height);
    }
    public void ZoomAt(UiRect viewport, Vector2 pixel, float factor)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 || !float.IsFinite(factor) || factor <= 0) return;
        var before = At(viewport, pixel);
        Zoom = Math.Clamp(Zoom * factor, 1, 64);
        float size = Math.Max(viewport.Width, viewport.Height) * Zoom;
        var delta = pixel - new Vector2(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        Centre = Clamp(before - delta / size);
    }
    public void Pan(UiRect viewport, Vector2 delta)
    {
        if (viewport.Width > 0 && viewport.Height > 0) Centre = Clamp(Centre - delta / (Math.Max(viewport.Width, viewport.Height) * Zoom));
    }
}
