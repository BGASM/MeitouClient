using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Trading;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    (CharacterId Character, InventoryItemView Item, Vector2 Grab)? inventoryDrag;
    /// <summary>A stock item of the open trade being dragged out of the trader window (dropped on the character's grid it is bought).</summary>
    (TradeItemView Item, Vector2 Grab)? tradeDrag;

    bool InventoryPointerDown(Vector2 p)
    {
        if (!UiVisible || faithfulScreens.HitTest(p) is not { } picked || !picked.Control.Enabled) return false;
        var bounds = picked.Control.Bounds;
        Vector2 Grab() => (p - new Vector2(bounds.X, bounds.Y)) / new Vector2(bounds.Width, bounds.Height);
        if (picked.Control.Properties.TryGetValue("TradeItem", out var stockIndex))
        {
            if (session.CurrentSnapshot.Trade?.Stock.FirstOrDefault(i => i.Index == int.Parse(stockIndex, CultureInfo.InvariantCulture)) is not { } stock) return false;
            tradeDrag = (stock, Grab());
            return true;
        }
        if (!picked.Control.Properties.TryGetValue("InventoryItem", out var index) || Selected().FirstOrDefault() is not { ItemGrid: { } grid } lead) return false;
        var item = grid.Items.FirstOrDefault(i => i.Index == int.Parse(index, CultureInfo.InvariantCulture));
        if (item is null || !InventoryCommands.IsMovableSection(item.Section)) return false;
        inventoryDrag = (lead.Id, item, Grab());
        return true;
    }

    void InventoryPointerUp(Vector2 p)
    {
        var buying = tradeDrag;
        var moving = inventoryDrag;
        tradeDrag = null;
        inventoryDrag = null;
        if ((buying is null && moving is null) || !UiVisible) return;
        ArrangeUi();
        if (faithfulScreens.HitTest(p) is not { } hit) return;
        var trade = session.CurrentSnapshot.Trade;

        if (buying is { } b)
        {
            // Stock dropped on the customer's pocket or bag grid: a purchase into the cell under the drop.
            if (trade is null || hit.Screen is not ("inventory" or "bag") || Selected().FirstOrDefault(c => c.Id == trade.Customer)?.ItemGrid is not { } grid) return;
            string section = hit.Screen == "bag" ? "backpack_content" : "main";
            int columns = section == "main" ? grid.Width : grid.BagWidth, rows = section == "main" ? grid.Height : grid.BagHeight;
            if (!DropCell(p, hit.Screen, section, columns, rows, b.Grab, b.Item.Width, b.Item.Height, out int x, out int y)) return;
            Say(new BuyItem(b.Item.Token, b.Item.Quantity) { Tick = session.World.Tick, Section = section, X = x, Y = y });
            return;
        }

        var drag = moving!.Value;
        var item = drag.Item;
        var token = new InventoryItemToken(item.Index, item.Record, item.Section, item.X, item.Y, item.Quantity, item.Quality);
        if (hit.Screen == "trader")
        {
            // The customer's item dropped on the trader's stock: a sale, into the stock cell under the drop when it is free.
            if (trade is null || trade.Customer != drag.Character) return;
            int? sx = null;
            int sy = 0;
            if (TraderGrid(hit.Screen) is { } stockGrid && DropCell(p, hit.Screen, stockGrid, trade.StockWidth, trade.StockHeight, drag.Grab, item.Width, item.Height, out int cx, out int cy))
                (sx, sy) = (cx, cy);
            Say(new SellItem(token, item.Quantity) { Tick = session.World.Tick, X = sx, Y = sy });
            return;
        }
        if (hit.Screen is not ("inventory" or "bag") || Selected().FirstOrDefault(c => c.Id == drag.Character)?.ItemGrid is not { } own) return;
        string target = hit.Screen == "bag" ? "backpack_content" : "main";
        if (hit.Screen == "inventory")
        {
            var controls = faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent).First(f => f.Id == hit.Screen).Controls;
            var equipment = controls.LastOrDefault(c => InventoryCommands.IsEquipmentSection(c.Name) && c.Bounds.Contains(p.X, p.Y) && (c.Clip?.Contains(p.X, p.Y) ?? true));
            if (equipment is not null) target = equipment.Name;
        }
        if (InventoryCommands.IsEquipmentSection(target))
        {
            var size = InventoryCommands.SectionSize(target);
            // Clothing and bags occupy one slot; weapon sections retain their grid rows.
            if (target is not ("back" or "hip"))
            {
                Say(new MoveInventoryItem(drag.Character, token, target, 0, 0) { Tick = session.World.Tick });
                return;
            }
            if (DropCell(p, hit.Screen, target, size.Width, size.Height, drag.Grab, item.Width, item.Height, out int wx, out int wy))
                Say(new MoveInventoryItem(drag.Character, token, target, wx, wy) { Tick = session.World.Tick });
            return;
        }
        int cols = target == "main" ? own.Width : own.BagWidth, rws = target == "main" ? own.Height : own.BagHeight;
        if (!DropCell(p, hit.Screen, target, cols, rws, drag.Grab, item.Width, item.Height, out int tx, out int ty)) return;
        Say(new MoveInventoryItem(drag.Character, token, target, tx, ty) { Tick = session.World.Tick });
    }

    /// <summary>The cell of a grid widget (<paramref name="slotName"/> of screen <paramref name="screen"/>) an item lands in when dropped at <paramref name="p"/>, held at <paramref name="grab"/>.</summary>
    bool DropCell(Vector2 p, string screen, string slotName, int columns, int rows, Vector2 grab, int width, int height, out int x, out int y)
    {
        x = y = 0;
        var controls = faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent).First(f => f.Id == screen).Controls;
        var slot = controls.FirstOrDefault(c => c.Name == slotName);
        if (slot is null || columns <= 0 || rows <= 0 || !slot.Bounds.Contains(p.X, p.Y) || slot.Clip is { } clip && !clip.Contains(p.X, p.Y)) return false;
        x = (int)MathF.Floor((p.X - slot.Bounds.X) / slot.Bounds.Width * columns - grab.X * width + 0.5f);
        y = (int)MathF.Floor((p.Y - slot.Bounds.Y) / slot.Bounds.Height * rows - grab.Y * height + 0.5f);
        return true;
    }

    void AddInventoryDrag(List<UiControl> controls)
    {
        if (inventoryDrag is { } drag)
            Preview(drag.Item.Record, drag.Item.Material, drag.Item.Name, drag.Item.Quantity, drag.Item.Width, drag.Item.Height, drag.Grab);
        else if (tradeDrag is { } stock)
            Preview(stock.Item.Record, stock.Item.Material, stock.Item.Name, stock.Item.Quantity, stock.Item.Width, stock.Item.Height, stock.Grab);

        void Preview(string record, string material, string name, int quantity, int columns, int rows, Vector2 grab)
        {
            float width = columns * InventoryCell, height = rows * InventoryCell;
            AddItemCell(controls, "drag-item", new(mouse.X - grab.X * width, mouse.Y - grab.Y * height, width, height),
                record, material, name, quantity, null, "DragItem", 0, null, pickable: false);
        }
    }

    bool CancelInventoryDrag()
    {
        bool captured = inventoryDrag is not null || tradeDrag is not null;
        inventoryDrag = null;
        tradeDrag = null;
        return captured;
    }

    void ValidateInventoryDrag()
    {
        if (faithfulScreens.HasModal) { CancelInventoryDrag(); return; }
        if (faithfulScreens.IsOpen("bag") && Selected().FirstOrDefault()?.ItemGrid is not { BagWidth: > 0, BagHeight: > 0 }) faithfulScreens.Close("bag");
        if (inventoryDrag is { } drag &&
            (!faithfulScreens.IsOpen(drag.Item.Section == "backpack_content" ? "bag" : "inventory") ||
             Selected().FirstOrDefault()?.Id != drag.Character ||
             Selected().FirstOrDefault()?.ItemGrid?.Items.FirstOrDefault(i => i.Index == drag.Item.Index) != drag.Item)) inventoryDrag = null;
        if (tradeDrag is { } stock && (!faithfulScreens.IsOpen("trader") ||
            session.CurrentSnapshot.Trade?.Stock.FirstOrDefault(i => i.Index == stock.Item.Index) != stock.Item)) tradeDrag = null;
    }
}
