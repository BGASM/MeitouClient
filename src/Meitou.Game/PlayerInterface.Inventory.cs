using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    (CharacterId Character, InventoryItemView Item, Vector2 Grab)? inventoryDrag;
    bool InventoryPointerDown(Vector2 p)
    {
        if (!UiVisible || UiMode != UiMode.Faithful || faithfulScreens.HitTest(p) is not { } picked || !picked.Control.Properties.TryGetValue("InventoryItem", out var index) || Selected().FirstOrDefault() is not { ItemGrid: { } grid } lead) return false;
        var item = grid.Items.FirstOrDefault(i => i.Index == int.Parse(index, CultureInfo.InvariantCulture));
        if (item is null || item.Section is not ("main" or "backpack_content")) return false;
        var bounds = picked.Control.Bounds;
        inventoryDrag = (lead.Id, item, (p - new Vector2(bounds.X, bounds.Y)) / new Vector2(bounds.Width, bounds.Height)); return true;
    }
    void InventoryPointerUp(Vector2 p)
    {
        if (inventoryDrag is not { } drag) return;
        inventoryDrag = null;
        if (!UiVisible || UiMode != UiMode.Faithful) return;
        ArrangeUi();
        if (faithfulScreens.HitTest(p) is not { } hit || hit.Screen is not ("inventory" or "bag") || Selected().FirstOrDefault(c => c.Id == drag.Character)?.ItemGrid is not { } grid) return;
        string section = hit.Screen == "bag" ? "backpack_content" : "main";
        var controls = faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent).First(f => f.Id == hit.Screen).Controls;
        var slot = controls.FirstOrDefault(c => c.Name == section);
        if (slot is null || !slot.Bounds.Contains(p.X, p.Y)) return;
        int columns = section == "main" ? grid.Width : grid.BagWidth, rows = section == "main" ? grid.Height : grid.BagHeight;
        int x = (int)MathF.Floor((p.X - slot.Bounds.X) / slot.Bounds.Width * columns - drag.Grab.X * drag.Item.Width + 0.5f);
        int y = (int)MathF.Floor((p.Y - slot.Bounds.Y) / slot.Bounds.Height * rows - drag.Grab.Y * drag.Item.Height + 0.5f);
        var item = drag.Item;
        Say(new MoveInventoryItem(drag.Character, new(item.Index, item.Record, item.Section, item.X, item.Y, item.Quantity, item.Quality), section, x, y) { Tick = session.World.Tick });
    }
    void AddInventoryDrag(List<UiControl> controls)
    {
        if (inventoryDrag is not { } drag) return;
        controls.Add(new("drag-item", "TextBox", new(mouse.X - drag.Grab.X * 120, mouse.Y - drag.Grab.Y * 40, 120, 40), drag.Item.Name, "", "", true,
            new Dictionary<string, string> { ["TextAlign"] = "Center", ["WordWrap"] = "true" }, new(0.25f, 0.3f, 0.25f, 0.85f), Pickable: false));
    }
}
