using System.Globalization;
using System.Numerics;
using Meitou.Data.Save;
using Meitou.Data.Ui;

namespace Meitou.Game;

/// <summary>
/// The Meitou mode's game menu (Esc): resume, save to a new slot or over the chosen one, load the chosen save (Meitou's own or the original game's), options and exit.
/// It drives the host through the same <see cref="ScreenAction"/> events as the faithful dialogs, and pauses the game while open.
/// </summary>
sealed partial class PlayerInterface
{
    bool menuOpen;
    int menuPage;
    // A second press of LOAD, SAVE OVER or EXIT within the menu confirms it (the first one names what it will do in the footer).
    string? menuArmed;

    void ToggleMenu()
    {
        menuOpen = !menuOpen;
        menuArmed = null;
        if (menuOpen) { openPanel = null; RefreshSlots(); menuPage = 0; }
        UpdateMenuPause();
    }

    void CloseMenu()
    {
        menuOpen = false;
        menuArmed = null;
        UpdateMenuPause();
    }

    void MenuAction(string argument)
    {
        string armed = menuArmed ?? "";
        menuArmed = null;
        if (argument.StartsWith("slot:", StringComparison.Ordinal))
        {
            selectedSlot = slots.FirstOrDefault(s => s.Folder == argument[5..]);
            ReadSlotDetails();
            return;
        }
        if (argument.StartsWith("page:", StringComparison.Ordinal)) { menuPage += int.Parse(argument[5..], CultureInfo.InvariantCulture); return; }
        switch (argument)
        {
            case "resume": CloseMenu(); break;
            case "save":
                ScreenAction?.Invoke(new("save", "SaveButton", "save", "Meitou " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)));
                CloseMenu();
                break;
            case "overwrite" when selectedSlot is { Writable: true } over:
                if (armed != argument) { menuArmed = argument; return; }
                ScreenAction?.Invoke(new("save", "SaveButton", "save", over.Name));
                CloseMenu();
                break;
            case "load" when selectedSlot is { } load && slotReadable:
                if (session.World.Player.Exists && armed != argument) { menuArmed = argument; return; }
                ScreenAction?.Invoke(new("load", "LoadButton", "load", load.Folder));
                CloseMenu();
                break;
            case "options": CloseMenu(); ScreenAction?.Invoke(new("menu", "OptionsButton", "options")); break;
            case "exit":
                if (armed != argument) { menuArmed = argument; return; }
                ScreenAction?.Invoke(new("menu", "ExitGameButton", "exit"));
                break;
        }
    }

    void AddMenuPanel(List<UiControl> controls)
    {
        float w = Math.Min(760, Width - 32), h = Math.Min(520, Height - 64);
        float x = (Width - w) / 2f, y = Math.Max(16, (Height - h) / 2f - 40);
        var ink = new Vector4(0.82f, 0.8f, 0.71f, 1);
        var title = new Vector4(0.89f, 0.84f, 0.71f, 1);
        var muted = new Vector4(0.62f, 0.6f, 0.52f, 1);
        void Add(string id, UiRect bounds, string caption = "", string skin = "", string argument = "", Vector4 background = default, bool painted = false, bool pickable = false,
            float scale = 1, string align = "Left VCenter", Vector4? colour = null, bool enabled = true)
        {
            bool button = argument.Length > 0;
            controls.Add(new(id, button ? "Button" : "Widget", bounds, caption, button ? "meitou:menu" : "", skin, enabled,
                new Dictionary<string, string> { ["FontName"] = painted ? "Kenshi_PaintedTextFont_Medium" : "Kenshi_StandardFont_Medium", ["TextAlign"] = button && align == "Left VCenter" ? "Center" : align },
                background, colour ?? (painted ? title : ink), Pickable: pickable || button, Argument: argument, TextScale: scale));
        }
        Add("menu", new(x, y, w, h), skin: "Kenshi_GenericWindowSkin", background: new(0.12f, 0.11f, 0.09f, 1), pickable: true);
        Add("menu-header", new(x + 6, y + 6, w - 12, 38), skin: "Kenshi_GenericWindowHeaderSkin");
        Add("menu-title", new(x + 22, y + 12, w - 44, 28), "GAME PAUSED", painted: true, scale: 1.25f);

        // The commands.
        bool haveGame = session.World.Player.Exists;
        float bx = x + 24, bw = 200;
        var commands = new (string Arg, string Caption, bool Enabled)[]
        {
            ("resume", "RESUME", true),
            ("save", "SAVE NEW", haveGame),
            ("overwrite", menuArmed == "overwrite" ? "CONFIRM SAVE" : "SAVE OVER", haveGame && selectedSlot is { Writable: true }),
            ("load", menuArmed == "load" ? "CONFIRM LOAD" : "LOAD", selectedSlot is not null && slotReadable),
            ("options", "OPTIONS", true),
            ("exit", menuArmed == "exit" ? "CONFIRM EXIT" : "EXIT", true),
        };
        for (int i = 0; i < commands.Length; i++)
            Add($"menu-{commands[i].Arg}", new(bx, y + 64 + i * 44, bw, 34), commands[i].Caption, "Kenshi_Button1Skin", commands[i].Arg, painted: true, enabled: commands[i].Enabled);

        // The saves: Meitou's own (written here) and the original game's (read only), newest first.
        float lx = bx + bw + 24, lw = w - (lx - x) - 24;
        Add("menu-saves", new(lx, y + 58, lw, 26), "SAVES", painted: true);
        Add("menu-rule", new(lx, y + 88, lw, 1), background: new(0.43f, 0.38f, 0.29f, 1));
        int rows = Math.Max(1, (int)((h - 260) / 30));
        int pages = Math.Max(1, (slots.Count + rows - 1) / rows);
        menuPage = Math.Clamp(menuPage, 0, pages - 1);
        if (slots.Count == 0) Add("menu-empty", new(lx, y + 98, lw, 26), "No saves found.");
        for (int i = 0; i < rows && menuPage * rows + i < slots.Count; i++)
        {
            var slot = slots[menuPage * rows + i];
            bool chosen = selectedSlot?.Folder == slot.Folder;
            float row = y + 96 + i * 30;
            Add($"menu-slot-{i}", new(lx, row, lw, 28), "", argument: "slot:" + slot.Folder,
                background: chosen ? new(0.33f, 0.29f, 0.19f, 1) : new(0.19f, 0.18f, 0.15f, i % 2 == 0 ? 0.9f : 0.5f));
            Add($"menu-slot-name-{i}", new(lx + 8, row + 2, lw - 190, 24), (slot.Writable ? "[Meitou] " : "[Kenshi] ") + slot.Name, scale: 0.9f);
            Add($"menu-slot-date-{i}", new(lx + lw - 176, row + 2, 168, 24), slot.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                align: "Right VCenter", colour: muted, scale: 0.85f);
        }
        float py = y + 96 + rows * 30 + 4;
        if (pages > 1)
        {
            Add("menu-prev", new(lx, py, 40, 24), "<", "Kenshi_Button1Skin", "page:-1");
            Add("menu-pageno", new(lx + 48, py, 120, 24), $"{menuPage + 1} / {pages}", scale: 0.85f);
            Add("menu-next", new(lx + 172, py, 40, 24), ">", "Kenshi_Button1Skin", "page:1");
        }
        Add("menu-details", new(lx, py + 32, lw, y + h - py - 76), slotDetails, align: "Left Top", scale: 0.85f);
        string footer = menuArmed switch
        {
            "load" => "Press CONFIRM LOAD to load the chosen save. Unsaved progress will be lost.",
            "overwrite" => $"Press CONFIRM SAVE to replace '{selectedSlot?.Name}'.",
            "exit" => "Press CONFIRM EXIT to quit. Unsaved progress will be lost.",
            _ => "Kenshi's saves load here but are never written: saving makes a Meitou save.",
        };
        Add("menu-footer", new(x + 24, y + h - 34, w - 48, 22), footer, colour: menuArmed is null ? muted : null, scale: 0.85f);
    }
}
