using System.Globalization;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    ItemIcons? itemIcons;

    /// <summary>The source of the item icons the inventory and trader grids draw (null: only names).</summary>
    public void SetItemIcons(ItemIcons? icons) => itemIcons = icons;

    /// <summary>
    /// The cell of one item in an inventory or trader grid: its icon filling <paramref name="area"/> with the quantity and <paramref name="extra"/> (a price) in the corner, or, while it has
    /// none (no file, or still loading), the dark box with the item's name. The box is the pickable control and carries <paramref name="property"/>.
    /// </summary>
    void AddItemCell(List<UiControl> controls, string name, UiRect area, string record, string material, string itemName, int quantity, string? extra, string property, int index, UiRect? clip)
    {
        string count = quantity > 1 ? $"x{quantity}" : "";
        string? icon = itemIcons?.Find(record, material);
        bool shown = icon is not null && uiPainter?.IconReady(icon) == true;
        if (shown)
            controls.Add(new($"{name}-icon", "ImageBox", area, "", "", "", true, new Dictionary<string, string> { ["ImageTexture"] = Meitou.Rendering.Ui.UiPainter.IconTexture(icon!) }, System.Numerics.Vector4.Zero, Clip: clip));
        string caption = shown ? string.Join("\n", new[] { count, extra }.Where(s => !string.IsNullOrEmpty(s))) : string.Join("\n", new[] { itemName + (count.Length > 0 ? " " + count : ""), extra }.Where(s => !string.IsNullOrEmpty(s)));
        controls.Add(new(name, "TextBox", area, caption, "", "", true,
            new Dictionary<string, string> { ["WordWrap"] = "true", ["TextAlign"] = shown ? "Right Bottom" : "Center", ["FontName"] = "Kenshi_StandardFont_Medium", [property] = index.ToString(CultureInfo.InvariantCulture) },
            shown ? System.Numerics.Vector4.Zero : new(0.2f, 0.2f, 0.16f, 0.6f), Pickable: true, TextScale: 0.8f, Clip: clip));
    }
}
