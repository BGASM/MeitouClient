using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    // The character under the cursor, looked up again only when the tick or the cursor moved (a pick walks every character).
    (long Tick, Vector2 Mouse, CharacterSnapshot? Who) hover = (-1, default, null);

    /// <summary>
    /// Over another character: its name and what the AI has it doing (<see cref="CharacterSnapshot.Activity"/>, the game's task text) beside the cursor. The original
    /// shows a mouse-over text of its own (<c>TooltipPanel</c>), whose layout is <b>Unknown</b>; this is a plain label in both UI modes.
    /// </summary>
    void AddHoverLabel(List<UiControl> controls)
    {
        if (inventoryDrag is not null || tradeDrag is not null) return;
        if (MyGuiPresentation.HitTest(controls, mouse.X, mouse.Y) is { } hit)
        {
            if (hit.Properties.TryGetValue("ItemTooltip", out string? tooltip))
            {
                // The item's name, then label and value rows (a tab between them) as the original's tooltip lists them.
                string[] lines = tooltip.Split('\n');
                float scale = InventoryScale, row = 20 * scale, width = Math.Min(Width, 280 * scale), height = 16 * scale + row * lines.Length;
                var box = new UiRect(Math.Clamp(mouse.X + 18 * scale, 0, Width - width), Math.Clamp(mouse.Y + 18 * scale, 0, Height - height), width, height);
                controls.Add(new("inventory-tooltip", "Widget", box, "", "", "Kenshi_GenericTextBoxSkin", true,
                    new Dictionary<string, string> { ["ItemTooltip"] = tooltip }, Vector4.Zero, Pickable: false));
                controls.Add(new("inventory-tooltip-title", "TextBox", new(box.X + 8 * scale, box.Y + 6 * scale, box.Width - 16 * scale, row), lines[0], "", "", true,
                    new Dictionary<string, string> { ["TextAlign"] = "Left Top", ["TextColour"] = TitleColour, ["WordWrap"] = "false" }, Vector4.Zero, Pickable: false));
                var host = new UiControl("inventory-tooltip-rows", "Widget", new(box.X + 4 * scale, box.Y + 6 * scale + row, box.Width - 8 * scale, row * (lines.Length - 1)), "", "", "", true,
                    new Dictionary<string, string>(), Vector4.Zero, Pickable: false);
                controls.Add(host);
                AddLines(controls, host.Name, lines.Skip(1).Select(l => l.Split('\t') is [var label, var value] ? new HudLine(label, value) : new HudLine(l, "")).ToArray(), row);
            }
            return;
        }
        var snapshot = session.CurrentSnapshot;
        if (hover.Tick != snapshot.Tick || hover.Mouse != mouse)
        {
            var id = PickWhere(mouse, c => !c.IsPlayer);
            hover = (snapshot.Tick, mouse, id is { } h ? snapshot.Characters.FirstOrDefault(c => c.Id == h) : null);
        }
        if (hover.Who is not { } who) return;
        var left = new Dictionary<string, string> { ["TextAlign"] = "Left" };
        var shade = new Vector4(0.08f, 0.08f, 0.07f, 0.8f);
        controls.Add(new("hover-name", "TextBox", new(mouse.X + 18, mouse.Y + 10, 200, 20), who.Name, "", "", true, left, shade, Pickable: false));
        if (who.Activity is { } activity)
            controls.Add(new("hover-activity", "TextBox", new(mouse.X + 18, mouse.Y + 30, 200, 20), activity, "", "", true, left, shade,
                TextColour: new Vector4(0.85f, 0.8f, 0.6f, 1), Pickable: false));
    }
}
