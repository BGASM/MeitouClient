namespace Meitou.Rendering;

/// <summary>
/// A probe, not a feature: the foliage drawn in the far depth slice (beyond the near slice's 20000 units) or left out of it. The A/B <c>foliage-far</c>
/// (docs/render-foliage.md "Foliage cost in the orbit"); the slice is drawn by default.
/// </summary>
public static class FoliageFarSlice
{
    public static bool Enabled { get; set; } = true;
}
