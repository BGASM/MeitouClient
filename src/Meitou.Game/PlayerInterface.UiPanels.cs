using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    void AddDetailsPanel(List<UiControl> controls)
    {
        float w = Math.Min(620, Width - 32), h = Math.Min(460, Height - 64);
        float x = (Width - w) / 2f, y = Math.Max(16, (Height - h) / 2f - 70);
        var lead = Selected().FirstOrDefault();
        var title = new Vector4(0.89f, 0.84f, 0.71f, 1);
        var ink = new Vector4(0.82f, 0.8f, 0.71f, 1);
        void Add(string id, UiRect bounds, string caption = "", string skin = "", string action = "", Vector4 background = default,
            bool painted = false, bool pickable = false, float scale = 1)
        {
            controls.Add(new(id, action.Length > 0 ? "Button" : "Widget", bounds, caption, action, skin, true,
                new Dictionary<string, string> { ["FontName"] = painted ? "Kenshi_PaintedTextFont_Medium" : "Kenshi_StandardFont_Medium", ["TextAlign"] = action.Length > 0 ? "Center" : "Left VCenter" },
                background, painted ? title : ink, Pickable: pickable || action.Length > 0, TextScale: scale));
        }
        Add("details", new(x, y, w, h), skin: "Kenshi_GenericWindowSkin", background: new(0.12f, 0.11f, 0.09f, 1), pickable: true);
        Add("details-header", new(x + 6, y + 6, w - 12, 38), skin: "Kenshi_GenericWindowHeaderSkin");
        Add("details-title", new(x + 22, y + 12, w - 140, 28), openPanel!.ToUpperInvariant(), painted: true, scale: 1.25f);
        Add("details-close", new(x + w - 92, y + 12, 68, 28), "CLOSE", "Kenshi_Button1Skin", "meitou:close", painted: true);
        Add("details-name", new(x + 24, y + 58, w - 48, 28), lead?.Name ?? "No character selected", painted: true, scale: 1.15f);
        Add("details-rule", new(x + 24, y + 96, w - 48, 1), background: new(0.43f, 0.38f, 0.29f, 1));
        Add("details-footer", new(x + 24, y + h - 38, w - 48, 24), openPanel == "inventory" ? "Carried items and equipment - viewing only" : "Current trained skills of the selected character", scale: 0.85f);
        if (lead is null) { Add("details-empty", new(x + 24, y + 122, w - 48, 30), "Select a character to view this panel."); return; }
        if (openPanel == "inventory")
        {
            var lines = lead.Inventory;
            Add("inventory-weight", new(x + w - 230, y + 60, 206, 24), lines.FirstOrDefault() ?? "Empty inventory");
            Add("inventory-label", new(x + 24, y + 108, w - 48, 26), "EQUIPMENT & CARRIED ITEMS", painted: true);
            int count = Math.Max(0, (int)((h - 188) / 30));
            var items = lines.Skip(1).Take(count).ToArray();
            if (items.Length == 0) Add("inventory-empty", new(x + 24, y + 152, w - 48, 28), "No items carried.");
            for (int i = 0; i < items.Length; i++)
            {
                float row = y + 144 + i * 30;
                Add($"inventory-row-{i}", new(x + 24, row, w - 48, 28), background: new(0.19f, 0.18f, 0.15f, i % 2 == 0 ? 0.9f : 0.5f));
                Add($"inventory-slot-{i}", new(x + 28, row + 2, 24, 24), skin: "Kenshi_InventorySlotSkin");
                int metadata = items[i].LastIndexOf(" (", StringComparison.Ordinal);
                string name = metadata >= 0 ? items[i][..metadata] : items[i];
                Add($"inventory-item-{i}", new(x + 62, row + 2, w - 102, 24), name);
            }
        }
        else if (lead.Skills is { } s)
        {
            var skills = new[] { ("Melee attack", s.Attack), ("Melee defence", s.Defence), ("Dodge", s.Dodge), ("Toughness", s.Toughness), ("Strength", s.Strength), ("Athletics", s.Athletics) };
            Add("stats-label", new(x + 24, y + 108, w - 48, 26), "ATTRIBUTES & COMBAT", painted: true);
            for (int i = 0; i < skills.Length; i++)
            {
                float row = y + 148 + i * 40;
                Add($"skill-name-{i}", new(x + 24, row, 170, 24), skills[i].Item1);
                Add($"skill-value-{i}", new(x + w - 76, row, 48, 24), $"{skills[i].Item2:0.0}");
                Add($"skill-track-{i}", new(x + 208, row + 9, w - 304, 8), background: new(0.08f, 0.08f, 0.06f, 1));
                Add($"skill-fill-{i}", new(x + 208, row + 9, (w - 304) * Math.Clamp(skills[i].Item2 / 100, 0, 1), 8), background: new(0.48f, 0.46f, 0.29f, 1));
            }
        }
    }
}
