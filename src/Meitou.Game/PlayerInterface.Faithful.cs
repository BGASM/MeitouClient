using System.Globalization;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ui;
using Meitou.Engine.Input;
using Meitou.Ui;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    string selectedStat = "";
    public bool UiCapturesPointer => UiVisible && UiMode == UiMode.Faithful && (faithfulScreens.CapturesPointer || mapDrag is not null || inventoryDrag is not null || tradeDrag is not null);
    /// <summary>Keys the host keeps for itself (the sandbox's letters), so the faithful window shortcuts leave them alone.</summary>
    public HashSet<Key> ReservedKeys { get; } = [];
    void UpdateMenuPause() => session.Simulation.RequestedPause = UiMode == UiMode.Faithful && faithfulScreens.HasModal;
    public bool UiKey(Key key, bool ctrl = false, bool shift = false)
    {
        if (!UiVisible || UiMode != UiMode.Faithful) return false;
        if (key == Meitou.Engine.Input.Key.Escape)
        {
            if (faithfulScreens.IsOpen("title") && faithfulScreens.OpenScreens.Count == 1) return true;
            // Esc presses the last button of the top message box (Return the first, docs/game/ui-input.md section 5).
            if (faithfulScreens.IsOpen("message")) HandleFrontendAction(new("message", "ButtonB", "ButtonB"));
            else if (!faithfulScreens.Escape()) faithfulScreens.Open("menu");
            UpdateMenuPause();
            return true;
        }
        if (faithfulScreens.HasKeyboardFocus)
        {
            UiEditKey? editing = key switch { Meitou.Engine.Input.Key.Left => UiEditKey.Left, Meitou.Engine.Input.Key.Right => UiEditKey.Right,
                Meitou.Engine.Input.Key.Home => UiEditKey.Home, Meitou.Engine.Input.Key.End => UiEditKey.End, Meitou.Engine.Input.Key.Backspace => UiEditKey.Backspace,
                Meitou.Engine.Input.Key.Delete => UiEditKey.Delete, Meitou.Engine.Input.Key.A when ctrl => UiEditKey.SelectAll, _ => null };
            if (editing is { } editKey) faithfulScreens.EditKey(editKey, shift);
            if (key == Meitou.Engine.Input.Key.Tab) faithfulScreens.FocusNext(shift);
            if (key == Meitou.Engine.Input.Key.Enter) faithfulScreens.Submit();
            return true;
        }
        if (key == Meitou.Engine.Input.Key.Tab && faithfulScreens.FocusNext(shift)) return true;
        if (key == Meitou.Engine.Input.Key.Enter && faithfulScreens.Submit()) return true;
        if (faithfulScreens.HasModal) return true;
        if (ReservedKeys.Contains(key)) return false;
        if (key is Meitou.Engine.Input.Key.I or Meitou.Engine.Input.Key.C) { ExecuteUiAction(key == Meitou.Engine.Input.Key.I ? "inventory" : "stats", ""); return true; }
        // toggle_map / toggle_research / toggle_crafting (M / T / Y) ask for overview tabs 0 / 2 / 3 (docs/game/ui-input.md section 6); toggle_faction (tab 1) is unbound.
        // Pressing the key of the tab already showing closes the window (engine choice: the original's toggle rule is not traced).
        int? tab = key switch { Meitou.Engine.Input.Key.M => 0, Meitou.Engine.Input.Key.T => 2, Meitou.Engine.Input.Key.Y => 3, _ => null };
        if (tab is { } t)
        {
            if (faithfulScreens.IsOpen("overview") && faithfulScreens.GetTab("overview", "TabsMain") == t) faithfulScreens.Close("overview");
            else { faithfulScreens.Open("overview"); faithfulScreens.SetTab("overview", "TabsMain", t); }
            return true;
        }
        return false;
    }
    public void UiText(char value) { if (UiVisible && UiMode == UiMode.Faithful) faithfulScreens.TextInput(value); }
    public bool UiScroll(Vector2 p, float steps)
    {
        if (!UiVisible || UiMode != UiMode.Faithful) return false;
        ArrangeUi();
        if (MapWheel(p, steps)) return true;
        return faithfulScreens.Wheel(p, steps);
    }
    void FaithfulAction(MyGuiEvent e)
    {
        if (HandleFrontendAction(e)) UpdateMenuPause();
        else if (e.Screen == "stats" && e.Action == "select") selectedStat = e.Text;
        else if (e.Screen == "overview") MapAction(e.Widget);
        else if (e.Screen is "inventory" or "bag")
        {
            if (e.Widget == "OpenBagButton") faithfulScreens.Open("bag");
            else if (e.Widget == "LimbsButton") faithfulScreens.Open("limbs");
            else if (e.Widget == "ArrangeButton" && Selected().FirstOrDefault() is { } lead) Say(new Meitou.Simulation.Items.ArrangeInventory(lead.Id) { Tick = session.World.Tick });
        }
    }
    UiBindings BindFaithfulScreen(string id)
    {
        var b = new UiBindings();
        var layout = faithfulScreens.Layout(id);
        var lead = Selected().FirstOrDefault();
        foreach (var button in layout.Widgets.Where(w => w.Type == "Button" && !w.Properties.ContainsKey("TemplateEvent"))) b.Disabled.Add(button.Name);
        if (BindFrontend(id, b)) return b;
        else if (id == "trader") BindTraderScreen(b);
        else if (id is "inventory" or "stats" or "bag" or "limbs")
        {
            b.Text["Root"] = (id == "stats" ? "CHARACTER STATS" : id == "limbs" ? "LIMBS" : id == "bag" ? "BACKPACK" : "INVENTORY") + " - " + (lead?.Name ?? "No character selected");
            if (id is "inventory" or "bag" or "limbs")
            {
                b.Text["datapanel"] = lead?.Inventory.FirstOrDefault() ?? "Select a character";
                b.Properties["datapanel"] = new Dictionary<string, string> { ["TextColour"] = MyGuiColours.InkText, ["TextPadding"] = "8 6 8 6" };
                b.Text["CapacityText"] = lead?.ItemGrid is { } bagGrid ? $"{bagGrid.BagWidth} x {bagGrid.BagHeight}" : "No backpack";
                b.Properties["CapacityText"] = new Dictionary<string, string> { ["Static"] = "true" };
                foreach (string name in new[] { "ArrangeButton", "LimbsButton" }) { b.Disabled.Remove(name); b.Actions[name] = name; if (lead is null) b.Disabled.Add(name); }
                if (lead?.ItemGrid is { BagWidth: > 0, BagHeight: > 0 }) { b.Disabled.Remove("OpenBagButton"); b.Actions["OpenBagButton"] = "OpenBagButton"; }
            }
            else
            {
                UiListItem Skill(StatsEnumerated s) => new(((int)s).ToString(CultureInfo.InvariantCulture), $"{StatLabel(s)}  {(lead?.AllSkills.ElementAtOrDefault((int)s) ?? 0):0}");
                b.Lists["AttributesPanel"] = StatPanels[0].Select(Skill).ToArray();
                for (int i = 1; i < StatPanels.Length; i++) b.Lists[$"Skills{i}Panel"] = StatPanels[i].Select(Skill).ToArray();
                if (int.TryParse(selectedStat, out int stat) && stat > 0 && stat < StatsInfo.Count)
                    b.Text["Description1Panel"] = $"{StatLabel((StatsEnumerated)stat)}\nCurrent level: {(lead?.AllSkills.ElementAtOrDefault(stat) ?? 0):0.00}";
                b.Text["Description2Panel"] = lead?.Body is { } body ? $"Blood {body.Blood * 100:0}%\nHunger {body.Hunger:0.00}\n{(body.Dead ? "Dead" : body.Unconscious ? "Unconscious" : "Conscious")}" : "";
                foreach (string name in new[] { "Description1Panel", "Description2Panel" }) b.Properties[name] = new Dictionary<string, string> { ["WordWrap"] = "true", ["TextAlign"] = "Left Top", ["TextColour"] = MyGuiColours.InkText, ["TextPadding"] = "8 8 8 8" };
                b.Properties["AttributesPanel"] = new Dictionary<string, string> { ["TextColour"] = "0.92 0.92 0.88" };
            }
        }
        else if (id == "overview")
        {
            b.Text["Root"] = "OVERVIEW";
            foreach (string name in new[] { "MapCenterButton", "MapZoomInButton", "MapZoomOutButton" }) { b.Disabled.Remove(name); b.Actions[name] = name; }
            foreach (var tab in layout.Widgets.Where(w => w.Type == "TabItem")) b.Properties[tab.Name] = new Dictionary<string, string> { ["Visible"] = "true" };
            b.Text["FactionNameText"] = "Player";
            b.Disabled.Add("FactionNameText");
            b.Text["FactionSizeText"] = PlayerCharacters().Count.ToString(CultureInfo.InvariantCulture);
            b.Text["MessagesTextBox"] = string.Join('\n', uiMessages);
            b.Properties["MessagesTextBox"] = new Dictionary<string, string> { ["Static"] = "true", ["WordWrap"] = "true", ["TextAlign"] = "Left Top" };
            b.Lists["SquadsPanel"] = session.CurrentSnapshot.Characters.Where(c => c.IsPlayer).Select(c => new UiListItem(c.Id.ToString(), c.Name)).ToArray();
            // Editor preview markers are unrelated to the current camera and orders.
            b.Hidden.Add("CameraMarker"); b.Hidden.Add("MovemementMarker");
        }
        return b;
    }
    /// <summary>
    /// The 33 record stats by panel: the attributes, then Skills1..4. The groups are the STATS record's field groups (docs/game/character-stats.md: attributes;
    /// attack, defence, dodge, unarmed; athletics, swimming; engineer, medic, robotics, science; the thief four; the six trades; the nine weapons), with the weapons
    /// split into melee and ranged. Which group goes in which of the four panels, and the order inside one, are <b>engine choices</b> (the layout's panel captions are not read).
    /// </summary>
    internal static readonly StatsEnumerated[][] StatPanels =
    [
        [StatsEnumerated.Strength, StatsEnumerated.Dexterity, StatsEnumerated.Toughness, StatsEnumerated.Perception],
        [StatsEnumerated.MeleeAttack, StatsEnumerated.MeleeDefence, StatsEnumerated.Dodge, StatsEnumerated.MartialArts,
            StatsEnumerated.Katanas, StatsEnumerated.Sabres, StatsEnumerated.Hackers, StatsEnumerated.HeavyWeapons, StatsEnumerated.Blunt, StatsEnumerated.Polearms],
        [StatsEnumerated.Crossbows, StatsEnumerated.Turrets, StatsEnumerated.FriendlyFire, StatsEnumerated.Athletics, StatsEnumerated.Swimming],
        [StatsEnumerated.Stealth, StatsEnumerated.Lockpicking, StatsEnumerated.Thieving, StatsEnumerated.Assassination],
        [StatsEnumerated.Science, StatsEnumerated.Engineering, StatsEnumerated.Robotics, StatsEnumerated.Medic,
            StatsEnumerated.SmithingWeapon, StatsEnumerated.SmithingArmour, StatsEnumerated.SmithingBow, StatsEnumerated.Labouring, StatsEnumerated.Farming, StatsEnumerated.Cooking],
    ];

    static string StatLabel(StatsEnumerated stat) => stat switch
    {
        StatsEnumerated.Thieving => "Thievery", StatsEnumerated.HeavyWeapons => "Heavy weapons",
        StatsEnumerated.MeleeAttack => "Melee attack", StatsEnumerated.MeleeDefence => "Melee defence",
        StatsEnumerated.SmithingWeapon => "Weapon smithing", StatsEnumerated.SmithingArmour => "Armour smithing",
        StatsEnumerated.SmithingBow => "Crossbow smithing", StatsEnumerated.FriendlyFire => "Precision shooting",
        StatsEnumerated.MartialArts => "Martial arts",
        StatsEnumerated.Medic => "Field medic", _ => stat.ToString(),
    };
    void AddFaithfulContent(string id, List<UiControl> controls)
    {
        AddMapContent(id, controls);
        if (id == "trader") { AddTraderContent(controls); return; }
        if (id is not ("inventory" or "bag" or "limbs") || Selected().FirstOrDefault() is not { ItemGrid: { } grid } lead) return;
        string gridSection = id == "bag" ? "backpack_content" : "main";
        int columns = id == "bag" ? grid.BagWidth : grid.Width, rows = id == "bag" ? grid.BagHeight : grid.Height;
        int mainIndex = controls.FindIndex(c => c.Name == gridSection);
        if (mainIndex >= 0)
        {
            var slot = controls[mainIndex];
            var props = new Dictionary<string, string>(slot.Properties, StringComparer.Ordinal) { ["TileSize"] = FormattableString.Invariant($"{slot.Bounds.Width / Math.Max(1, columns)} {slot.Bounds.Height / Math.Max(1, rows)}") };
            controls[mainIndex] = slot with { Properties = props };
        }
        foreach (var item in grid.Items.Where(i => id == "bag" ? i.Section == "backpack_content" : id == "limbs" ? i.Section.EndsWith("_replace", StringComparison.Ordinal) : i.Section != "backpack_content"))
        {
            if (controls.FirstOrDefault(c => c.Name == item.Section) is not { } slot) continue;
            var area = slot.Bounds;
            if (item.Section == gridSection && columns > 0 && rows > 0) area = new(area.X + item.X * area.Width / columns, area.Y + item.Y * area.Height / rows, item.Width * area.Width / columns, item.Height * area.Height / rows);
            string caption = item.Name + (item.Quantity > 1 ? $" x{item.Quantity}" : "");
            if (SellCaption(lead.Id, item.Index) is { } price) caption += "\n" + price;
            controls.Add(new($"inventory-item-{item.Index}", "TextBox", area, caption, "", "", true,
                new Dictionary<string, string> { ["WordWrap"] = "true", ["TextAlign"] = "Center", ["FontName"] = "Kenshi_StandardFont_Medium", ["InventoryItem"] = item.Index.ToString(CultureInfo.InvariantCulture) }, new(0.2f, 0.2f, 0.16f, 0.6f), Pickable: true, TextScale: 0.8f, Clip: slot.Clip));
        }
    }
    readonly List<string> uiMessages = [];
    void BindMedicalBars(UiBindings b, Meitou.Simulation.CharacterSnapshot? lead)
    {
        var parts = lead?.HealthParts.Take(7).ToArray() ?? [];
        for (int i = 1; i <= 9; i++)
        {
            string name = $"LifeBar{i}";
            b.Hidden.Add(name + "Value"); b.Hidden.Add(name + "Tooltip");
            foreach (string layer in new[] { "Red", "Yellow", "White", "Crushed", "Robot" }) b.Hidden.Add(name + layer);
            float fraction; string text;
            if (i <= parts.Length)
            {
                var part = parts[i - 1]; float hp = part.Flesh - part.Stun + part.Rig;
                fraction = part.Maximum > 0 ? hp / part.Maximum : 0;
                text = $"{part.Name}  {hp:0}/{part.Maximum:0}";
                if (part.Robotic) { b.Hidden.Remove(name + "Robot"); b.Hidden.Add(name + "Green"); b.Progress[name + "Robot"] = fraction; }
            }
            else if (i == parts.Length + 1 && lead?.Body is { } blood) { fraction = blood.Blood; text = $"Blood  {blood.Blood * 100:0}%"; }
            else if (i == parts.Length + 2 && lead?.Body is { } food) { fraction = food.Hunger / 3; text = $"Hunger  {food.Hunger * 100:0}/300"; }
            else { b.Hidden.Add(name); continue; }
            b.Progress[name + "Green"] = fraction;
            b.Text[name + "Datapanel"] = text;
            b.Properties[name + "Datapanel"] = new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextAlign"] = "Left VCenter", ["TextColour"] = MyGuiColours.InkText, ["TextPadding"] = "4 0 4 0" };
        }
    }
}
