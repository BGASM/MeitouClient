using System.Globalization;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;
using Meitou.Rendering.Ui;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    ItemIcons? itemIcons;

    /// <summary>The source of the item icons the inventory and trader grids draw (null: only names).</summary>
    public void SetItemIcons(ItemIcons? icons) => itemIcons = icons;

    /// <summary>The records' names with the item icons the renderer is drawing (<see cref="UiPainter.RenderedIcons"/>): the host connects it to the character renderer.</summary>
    public void SetRenderedIcons(Func<string, (Meitou.Rendering.Gpu.Texture Texture, Meitou.Rendering.Gpu.SampledTexture Sampled)?>? lookup) { if (uiPainter is not null) uiPainter.RenderedIcons = lookup; }

    /// <summary>
    /// The cell of one item in an inventory or trader grid: its icon filling <paramref name="area"/> with the quantity and <paramref name="extra"/> (a price) in the corner, or, while it has
    /// none (no file, nothing drawn yet), the dark box with the item's name. The box is the pickable control and carries <paramref name="property"/>.
    /// </summary>
    void AddItemCell(List<UiControl> controls, string name, UiRect area, string record, string material, string itemName, int quantity, string? extra, string property, int index, UiRect? clip)
    {
        string count = quantity > 1 ? $"x{quantity}" : "";
        Dictionary<string, string>? image = null;
        if (itemIcons?.Choose(record, material) is { } choice && uiPainter is { } painter)
        {
            if (choice is { Source: ItemIconSource.File, File: { } file } && painter.IconReady(file))
                image = new() { ["ImageTexture"] = UiPainter.IconTexture(file) };
            else if (choice.Source == ItemIconSource.Render && UiPainter.RenderedIconTexture(record, material) is { } rendered && painter.RenderedIconReady(rendered))
                image = new() { ["ImageTexture"] = rendered, ["ImageFlipV"] = "true" };
        }
        bool shown = image is not null;
        if (shown) controls.Add(new($"{name}-icon", "ImageBox", area, "", "", "", true, image!, System.Numerics.Vector4.Zero, Clip: clip));
        string caption = shown ? string.Join("\n", new[] { count, extra }.Where(s => !string.IsNullOrEmpty(s))) : string.Join("\n", new[] { itemName + (count.Length > 0 ? " " + count : ""), extra }.Where(s => !string.IsNullOrEmpty(s)));
        controls.Add(new(name, "TextBox", area, caption, "", "", true,
            new Dictionary<string, string> { ["WordWrap"] = "true", ["TextAlign"] = shown ? "Right Bottom" : "Center", ["FontName"] = "Kenshi_StandardFont_Medium", [property] = index.ToString(CultureInfo.InvariantCulture) },
            shown ? System.Numerics.Vector4.Zero : new(0.2f, 0.2f, 0.16f, 0.6f), Pickable: true, TextScale: 0.8f, Clip: clip));
    }
}
