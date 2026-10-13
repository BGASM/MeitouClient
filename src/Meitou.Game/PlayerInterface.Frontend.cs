using Meitou.Data.Fcs;
using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Data.Ui;
using Meitou.Ui;
using System.Globalization;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    IReadOnlyList<NewGameStart> starts = [];
    int startIndex;
    Func<IReadOnlyList<SaveSlot>> saveSlots = () => [];
    IReadOnlyList<SaveSlot> slots = [];
    SaveSlot? selectedSlot;
    string slotDetails = "", credits = "";
    bool showCredits;
    bool slotReadable;
    (string Title, string Text, Action Confirm)? confirmation;
    public void ConfigureFrontend(GameDatabase? database, Func<IReadOnlyList<SaveSlot>> enumerate, string creditsText)
    {
        starts = database is null ? [] : NewGameStart.LoadAll(database);
        startIndex = Math.Max(0, starts.ToList().FindIndex(s => s.Name == NewGameStart.DefaultName));
        saveSlots = enumerate; credits = creditsText;
    }
    public void ShowTitle() { faithfulScreens.Open("title"); RefreshSlots(); UpdateMenuPause(); }
    public void CloseFrontend()
    {
        foreach (string id in new[] { "title", "menu", "new-game", "advanced", "save", "load", "message" }) faithfulScreens.Close(id);
        confirmation = null; UpdateMenuPause();
    }
    void Confirm(string title, string text, Action action)
    {
        confirmation = (title, text, action); faithfulScreens.Open("message");
    }
    void RefreshSlots()
    {
        try { slots = saveSlots(); selectedSlot = slots.FirstOrDefault(); ReadSlotDetails(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { slots = []; selectedSlot = null; slotDetails = e.Message; }
    }
    void ReadSlotDetails()
    {
        slotReadable = false;
        if (selectedSlot is not { } slot) { slotDetails = "No saves found."; return; }
        try
        {
            var info = SaveCatalog.ReadInfo(slot);
            slotReadable = true;
            slotDetails = $"File version: {info.Version}\nFaction name: {info.Faction}\nFunds: c.{info.Funds.ToString("N0", CultureInfo.InvariantCulture)}\nMembers: {info.Members}\nLocation: {info.Area}\nDay {info.Day}, {info.Hour:00}:{info.Minute:00}";
        }
        catch (Exception e) when (e is IOException or FormatException or ArgumentException) { slotDetails = "Unable to read save:\n" + e.Message; }
    }
    void OpenSlots(string id)
    {
        RefreshSlots();
        if (id == "save") { selectedSlot = slots.FirstOrDefault(s => s.Writable); ReadSlotDetails(); }
        faithfulScreens.Open(id); if (id == "save") faithfulScreens.SetText(id, "SaveEditBox", "");
    }
    bool HandleFrontendAction(MyGuiEvent e)
    {
        if (e.Screen is "title" or "menu")
        {
            switch (e.Widget)
            {
                case "ResumeButton": faithfulScreens.Close("menu"); break;
                case "NewGameButton": faithfulScreens.Open("new-game"); break;
                case "SaveGameButton": OpenSlots("save"); break;
                case "LoadGameButton": OpenSlots("load"); break;
                case "OptionsButton": ScreenAction?.Invoke(e); break;
                case "CreditsButton": showCredits = !showCredits; break;
                case "ContinueButton": if (slots.FirstOrDefault() is { } last) ScreenAction?.Invoke(new("load", "LoadButton", "load", last.Folder)); break;
                case "ExitButton": case "ExitGameButton": Confirm("EXIT", "Are you sure you want to exit?", () => ScreenAction?.Invoke(new("menu", "ExitGameButton", "exit"))); break;
            }
            return true;
        }
        if (e.Screen == "message")
        {
            var confirm = confirmation; confirmation = null; faithfulScreens.Close("message");
            if (e.Widget == "ButtonA") confirm?.Confirm();
            return true;
        }
        if (e.Screen == "new-game")
        {
            if (e.Widget == "CancelButton") faithfulScreens.Close(e.Screen);
            else if (e.Widget == "LeftButton" && starts.Count > 0) startIndex = (startIndex + starts.Count - 1) % starts.Count;
            else if (e.Widget == "RightButton" && starts.Count > 0) startIndex = (startIndex + 1) % starts.Count;
            else if (e.Widget == "AdvancedButton") faithfulScreens.Open("advanced");
            else if (e.Widget == "BeginButton" && starts.Count > 0)
            {
                void Start() => ScreenAction?.Invoke(new("new-game", "BeginButton", "new-game", starts[startIndex].Id));
                if (session.World.Player.Exists) Confirm("NEW GAME", "Start a new game? Unsaved progress will be lost.", Start); else Start();
            }
            return true;
        }
        if (e.Screen is "save" or "load")
        {
            if (e.Widget == "CancelButton") faithfulScreens.Close(e.Screen);
            else if (e.Widget == "GamesList" && e.Action == "select")
            {
                selectedSlot = slots.FirstOrDefault(s => s.Folder == e.Text); ReadSlotDetails();
                if (e.Screen == "save" && selectedSlot is { } slot) faithfulScreens.SetText("save", "SaveEditBox", slot.Name);
            }
            else if (e.Widget == "LoadButton" && selectedSlot is { } loadSlot)
            {
                void Load() => ScreenAction?.Invoke(new("load", "LoadButton", "load", loadSlot.Folder));
                if (session.World.Player.Exists) Confirm("LOAD GAME", "Load this save? Unsaved progress will be lost.", Load); else Load();
            }
            else if (e.Widget == "SaveButton")
            {
                string name = faithfulScreens.GetText("save", "SaveEditBox");
                try { SaveCatalog.ValidateName(name); }
                catch (ArgumentException ex) { Notice(ex.Message, true); return true; }
                void Save() { ScreenAction?.Invoke(new("save", "SaveButton", "save", name)); faithfulScreens.Close("save"); }
                if (slots.Any(s => s.Writable && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) Confirm("OVERWRITE SAVE", $"Replace '{name}'?", Save); else Save();
            }
            return true;
        }
        return false;
    }
    bool BindFrontend(string id, UiBindings b)
    {
        void Enable(params string[] names) { foreach (string name in names) { b.Disabled.Remove(name); b.Actions[name] = name; } }
        void Paragraph(string name, string text) { b.Text[name] = text; b.Properties[name] = new Dictionary<string, string> { ["WordWrap"] = "true", ["TextAlign"] = "Left Top", ["TextColour"] = MyGuiColours.InkText, ["TextPadding"] = name == "InfoPanel" ? "8 8 8 8" : "12 10 12 10" }; }
        if (id is "title" or "menu")
        {
            Enable("NewGameButton", "LoadGameButton", "OptionsButton", "ExitButton", "ExitGameButton", "ResumeButton", "CreditsButton");
            if (session.World.Player.Exists) Enable("SaveGameButton");
            if (slots.Count > 0 && slotReadable) Enable("ContinueButton");
            if (starts.Count == 0) b.Disabled.Add("NewGameButton");
            b.Properties["CreditsPanel"] = new Dictionary<string, string> { ["Visible"] = showCredits ? "true" : "false" };
            Paragraph("CreditsPanelContent", credits);
            b.Properties["CreditsPanelContent"] = new Dictionary<string, string> { ["WordWrap"] = "true", ["TextAlign"] = "HCenter Top", ["TextColour"] = "1 1 1" };
            b.Text["VersionText"] = "Meitou - Faithful UI";
        }
        else if (id == "new-game")
        {
            Enable("CancelButton", "AdvancedButton");
            if (starts.Count > 0)
            {
                Enable("LeftButton", "RightButton", "BeginButton"); var start = starts[startIndex];
                b.Text["TitleTextBox"] = start.Name;
                Paragraph("DescriptionPanel", $"{start.Description}\n\nDifficulty: {start.Difficulty}\nStarting cash: c.{start.Money.ToString("N0", CultureInfo.InvariantCulture)}\nPlay style: {start.Style}");
            }
        }
        else if (id is "save" or "load")
        {
            Enable("CancelButton");
            var visibleSlots = slots.Where(s => id != "save" || s.Writable).ToArray();
            b.Lists["GamesList"] = visibleSlots.Select(s => new UiListItem(s.Folder, $"{s.Name}   {s.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}   [{(s.Writable ? "Meitou" : "Kenshi")}]")).ToArray();
            b.SelectedItems["GamesList"] = Array.FindIndex(visibleSlots, s => s == selectedSlot);
            Paragraph("InfoPanel", slotDetails);
            if (id == "save") Enable("SaveButton"); else if (selectedSlot is not null && slotReadable) Enable("LoadButton");
            b.Properties[id == "save" ? "SaveButton" : "LoadButton"] = new Dictionary<string, string> { ["DefaultAction"] = "true" };
        }
        else if (id == "message")
        {
            Enable("ButtonA", "ButtonB"); b.Hidden.Add("ButtonC");
            b.Text["Root"] = confirmation?.Title ?? ""; b.Text["MessageText"] = confirmation?.Text ?? "";
            b.Text["ButtonA"] = "YES"; b.Text["ButtonB"] = "CANCEL";
            b.Properties["MessageText"] = new Dictionary<string, string> { ["Static"] = "true", ["WordWrap"] = "true" };
            b.Properties["ButtonA"] = new Dictionary<string, string> { ["DefaultAction"] = "true" };
        }
        else if (id == "advanced")
        {
            foreach (var widget in faithfulScreens.Layout(id).Widgets.Where(w => w.Type is "ScrollBar" or "ComboBox" or "EditBox")) b.Disabled.Add(widget.Name);
        }
        else return false;
        return true;
    }
}
