using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    float InventoryScale => Height / 1080f;
    float InventoryCell => 25 * InventoryScale;
    readonly Dictionary<string, float> inventoryScroll = new(StringComparer.Ordinal);
    readonly Dictionary<string, (UiRect View, float Maximum)> inventoryViews = new(StringComparer.Ordinal);

    void BindInventoryGeometry(string id, UiBindings b)
    {
        b.TemplateScale = InventoryScale;
        if (id == "bag" && Selected().FirstOrDefault()?.ItemGrid is { } bag)
            b.Positions["Root"] = new(0, 0, bag.BagWidth * InventoryCell + 40 * InventoryScale,
                Math.Min(Height * 0.8f, bag.BagHeight * InventoryCell + 108 * InventoryScale));
        if (id == "trader" && session.CurrentSnapshot.Trade is { } trade)
        {
            b.Positions["Root"] = new(0, 0, Math.Min(Width * 0.55f, trade.StockWidth * InventoryCell + 64 * InventoryScale), Height * 0.8f);
            // The runtime stock canvas and its scrollbar replace the editor's preview scroll view.
            b.Hidden.Add("scrollview_backpack_content");
        }
    }

    void PlaceBagWindow() => PlaceInventoryBeside("bag", left: true);

    void PlaceInventoryBeside(string id, bool left)
    {
        var frames = faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent);
        UiRect? Root(string screen) => frames.FirstOrDefault(f => f.Id == screen)?.Controls.FirstOrDefault(c => c.Type == "Window")?.Bounds;
        if (Root(id) is not { } other || Root("inventory") is not { } own) return;
        float gap = 6 * InventoryScale;
        float x = left ? own.X - other.Width - gap : own.X + own.Width + gap;
        // Make room for the pair before the screen host clamps either window to the viewport.
        if (x < 0 || x + other.Width > Width)
        {
            float ownX = left ? Math.Min(Width - own.Width, other.Width + gap) : Math.Max(0, Width - other.Width - gap - own.Width);
            faithfulScreens.SetOffset("inventory", faithfulScreens.GetOffset("inventory") + new Vector2(ownX - own.X, 0));
            x = left ? ownX - other.Width - gap : ownX + own.Width + gap;
        }
        faithfulScreens.SetOffset(id, faithfulScreens.GetOffset(id) + new Vector2(x - other.X, own.Y - other.Y));
    }

    void ArrangeEquipmentGrid(List<UiControl> controls, InventoryView grid)
    {
        foreach (string section in new[] { "main", "backpack_attach", "back", "hip", "shirt", "head", "legs", "boots", "armour", "belt" })
        {
            int index = controls.FindIndex(c => c.Name == section);
            if (index < 0) continue;
            var slot = controls[index];
            int columns = section == "main" ? grid.Width : Math.Max(1, (int)MathF.Round(slot.Bounds.Width / InventoryCell));
            int rows = section == "main" ? grid.Height : Math.Max(1, (int)MathF.Round(slot.Bounds.Height / InventoryCell));
            var bounds = slot.Bounds with { Width = columns * InventoryCell, Height = rows * InventoryCell };
            bool emptyClothing = section is "shirt" or "head" or "legs" or "boots" or "armour" && !grid.Items.Any(i => i.Section == section);
            controls[index] = slot with
            {
                Bounds = bounds,
                Properties = new Dictionary<string, string>(slot.Properties)
                {
                    ["TileSize"] = FormattableString.Invariant($"{InventoryCell} {InventoryCell}"),
                    ["State"] = emptyClothing ? "disabled" : "normal",
                },
            };
        }
    }

    void ArrangeContainerGrid(string id, List<UiControl> controls, int columns, int rows)
    {
        int index = controls.FindIndex(c => c.Name == "backpack_content");
        var root = controls.FirstOrDefault(c => c.Type == "Window");
        if (index < 0 || root is null) return;
        float scale = InventoryScale, cell = InventoryCell;
        var r = root.Bounds;
        float top = (id == "trader" ? 86 : 50) * scale;
        var view = new UiRect(r.X + 18 * scale, r.Y + top, Math.Max(0, r.Width - 40 * scale), Math.Max(0, r.Height - top - 58 * scale));
        float maximum = Math.Max(0, rows * cell - view.Height);
        if (maximum < 0.01f) maximum = 0;
        float scroll = Math.Clamp(inventoryScroll.GetValueOrDefault(id), 0, maximum);
        inventoryScroll[id] = scroll;
        inventoryViews[id] = (view, maximum);
        var slot = controls[index];
        controls[index] = slot with
        {
            Bounds = new(view.X, view.Y - scroll, columns * cell, rows * cell), Clip = view.Intersect(slot.Clip ?? view),
            Properties = new Dictionary<string, string>(slot.Properties) { ["TileSize"] = FormattableString.Invariant($"{cell} {cell}") },
        };
        int arrange = controls.FindIndex(c => c.Name == "ArrangeButton");
        if (arrange >= 0) controls[arrange] = controls[arrange] with { Bounds = new(r.X + (r.Width - 110 * scale) / 2, r.Y + r.Height - 44 * scale, 110 * scale, 32 * scale) };
        if (maximum <= 0) return;
        float barX = r.X + r.Width - 22 * scale, barWidth = 17 * scale, arrow = 14 * scale;
        void Part(string name, UiRect bounds, string skin, string action = "") => controls.Add(new(name, action.Length > 0 ? "Button" : "Widget", bounds, "", action, skin, true,
            new Dictionary<string, string>(), Vector4.Zero, Pickable: action.Length > 0, Clip: root.Clip));
        Part("inventory-scroll-up", new(barX, view.Y, barWidth, arrow), "Kenshi_SmallArrowUpSkin", "inventory-scroll:1");
        Part("inventory-scroll-down", new(barX, view.Y + view.Height - arrow, barWidth, arrow), "Kenshi_SmallArrowDownSkin", "inventory-scroll:-1");
        float track = Math.Max(0, view.Height - arrow * 2), thumb = Math.Min(track, Math.Max(20 * scale, track * view.Height / (rows * cell)));
        Part("inventory-scroll-thumb", new(barX, view.Y + arrow + (track - thumb) * scroll / maximum, barWidth, thumb), "Kenshi_SmallBarSkin");
    }

    bool InventoryWheel(Vector2 p, float steps)
    {
        if (faithfulScreens.HitTest(p) is not { } hit || !inventoryViews.TryGetValue(hit.Screen, out var view) || !view.View.Contains(p.X, p.Y)) return false;
        ScrollInventory(hit.Screen, steps);
        return true;
    }

    void ScrollInventory(string id, float steps)
    {
        if (inventoryViews.TryGetValue(id, out var view))
            inventoryScroll[id] = Math.Clamp(inventoryScroll.GetValueOrDefault(id) - steps * InventoryCell * 3, 0, view.Maximum);
    }

    void AddInventorySummary(List<UiControl> controls, CharacterSnapshot lead)
    {
        string weight = lead.ItemGrid?.TotalWeight.ToString("0", CultureInfo.InvariantCulture) ?? "0";
        if (lead.ItemGrid?.CarryCapacity is { } capacity) weight += "/" + capacity.ToString("0", CultureInfo.InvariantCulture);
        float burden = lead.Hud?.Encumbrance ?? 1;
        string burdenText = burden >= 1 ? "-Weightless" : $"-{(1 - burden) * 100:0}% Encumbrance";
        const string muted = "0.3 0.28 0.25";
        AddLines(controls, "datapanel", [new("Total Weight:", weight), new("", burdenText), new("Disguise:", "-"), new("", ""),
            new("Slave of:", "-", muted, muted), new("Looks like slave:", "-", muted, muted)], 20 * InventoryScale);
    }
}
