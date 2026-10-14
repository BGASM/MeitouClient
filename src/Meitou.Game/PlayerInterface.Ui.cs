using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Rendering;
using Meitou.Rendering.Ui;
using Meitou.Simulation;
using Meitou.Ui;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    public UiMode UiMode { get; set; }
    MyGuiLayout faithfulHud = null!;
    MyGuiResources uiResources = new();
    readonly UiHost uiHost = new();
    IDisposable? nativeHudRegistration;
    UiPainter? uiPainter;
    string? openPanel;
    MyGuiScreenHost faithfulScreens = null!;
    public event Action<MyGuiEvent>? ScreenAction;
    public bool UiVisible { get; set; } = true;
    internal IReadOnlyList<UiControl> UiFrame => ArrangeUi();

    public void LoadUi(UiMode mode, Func<string, string?> resolve)
    {
        UiMode = mode;
        uiResources = MyGuiResources.Load("kenshi_init.xml", resolve);
        faithfulHud = MyGuiTemplates.Expand(MyGuiReader.ReadLayout(resolve("Kenshi_MainPanel.layout") ?? throw new FileNotFoundException("Kenshi_MainPanel.layout not found.")), uiResources);
        faithfulScreens = new(uiResources);
        foreach (var (id, file, modal) in new[] {
            ("inventory", "Kenshi_InventoryCharacterWindow", false), ("stats", "Kenshi_StatsWindow", false),
            ("overview", "Kenshi_OverviewWindow", false), ("menu", "Kenshi_MainMenuPopupPanel", true),
            ("title", "Kenshi_MainMenu", true), ("new-game", "Kenshi_NewGamePanel", true),
            ("advanced", "Kenshi_AdvancedOptionsWindow", true), ("save", "Kenshi_SaveGamePanel", true),
            ("load", "Kenshi_LoadGamePanel", true), ("message", "Kenshi_MessageBox", true),
            ("bag", "Kenshi_InventoryGenericWindow", false), ("limbs", "Kenshi_LimbReplacementWindow", false) })
            faithfulScreens.Register(id, MyGuiReader.ReadLayout(resolve(file + ".layout") ?? throw new FileNotFoundException(file)), modal,
                centreRoot: id is "new-game" or "save" or "load");
        faithfulScreens.Activated += FaithfulAction;
        uiHost.Diagnostic += d => Console.Error.WriteLine($"ui        {d.Extension} {d.Operation}: {d.Error.Message}");
        nativeHudRegistration = uiHost.Register(new NativeHudExtension(ExecuteUiAction));
    }

    public void CreateUiPainter(DebugOverlay overlay, Func<string, string?> resolve) => uiPainter = new(overlay, uiResources, resolve);
    public void DisposeUi() { uiPainter?.Dispose(); uiPainter = null; nativeHudRegistration?.Dispose(); nativeHudRegistration = null; }
    /// <summary>Future code-mod loader entry point; disposal removes the extension's owned registrations.</summary>
    public IDisposable RegisterUiExtension(IUiExtension extension) => uiHost.Register(extension);
    public bool ContainsUi(Vector2 p)
    {
        if (!UiVisible) return false;
        var controls = ArrangeUi();
        return UiMode == UiMode.Faithful && faithfulScreens.Contains(p) || MyGuiPresentation.HitTest(controls, p.X, p.Y) is not null;
    }

    public void SetUiMode(UiMode mode)
    {
        UiMode = mode;
        dragFrom = null;
        dragging = false;
        steering = false;
        mapDrag = null;
        inventoryDrag = null;
        UpdateMenuPause();
    }

    UiBindings BindUi()
    {
        var b = new UiBindings();
        var lead = Selected().FirstOrDefault();
        b.Text["clock"] = $"{session.Clock.DayText}  {session.Clock.TimeText}";
        b.Text["DayText"] = session.Clock.DayText;
        b.Text["TimeText"] = session.Clock.TimeText;
        b.Text["MoneyAmountText"] = "c. " + session.World.Player.Money.ToString("N0", CultureInfo.InvariantCulture);
        b.Text["NameText"] = lead?.Name ?? "No character selected";
        b.Text["HealthText"] = lead?.Body is { } body ? body.Dead ? "Dead" : body.Unconscious ? "Unconscious" : $"Blood {body.Blood * 100:0}%  Health {body.WorstPart * 100:0}%" : "";
        b.Text["hunger"] = lead?.Body is { } hungry ? $"Hunger {hungry.Hunger:0.00} / 3" : "";
        b.Text["condition"] = lead?.Body is { } status ? status.Dead ? "Dead" : status.Unconscious ? "Unconscious" : status.NeedsAid ? "Needs first aid" : "Conscious" : "No character selected";
        b.Text["blood"] = lead?.Body is { } blood ? $"Blood   {blood.Blood * 100:0}%" : "Blood --";
        b.Text["body"] = lead?.Body is { } worst ? $"Worst body part   {worst.WorstPart * 100:0}%" : "Body --";
        b.Text["bloodFraction"] = (lead?.Body?.Blood ?? 0).ToString(CultureInfo.InvariantCulture);
        b.Text["bodyFraction"] = (lead?.Body?.WorstPart ?? 0).ToString(CultureInfo.InvariantCulture);
        b.Text["hungerFraction"] = ((lead?.Body?.Hunger ?? 0) / 3).ToString(CultureInfo.InvariantCulture);
        var roster = session.CurrentSnapshot.Characters.Where(c => c.IsPlayer).ToArray();
        b.Text["squad.count"] = roster.Length.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < Math.Min(8, roster.Length); i++) b.Text[$"squad.{i}.name"] = roster[i].Name;
        b.Text["members"] = $"{PlayerCharacters().Count} members | {Selected().Count} selected";
        b.Text["StateText"] = b.Text["hunger"];
        foreach (string name in new[] { "NameText", "StateText" }) b.Properties[name] = new Dictionary<string, string>
            { ["TextColour"] = MyGuiColours.InkText, ["TextPadding"] = "4 2 4 2" };
        b.Text["StatsText"] = lead?.Skills is { } s ? $"Atk {s.Attack:0} Def {s.Defence:0}" : "";
        b.Text["ExtendedStatsText"] = "";
        b.Text["BountyText"] = lead is null || lead.Bounties.Count == 0 ? "" : $"Bounty c.{lead.Bounties.Sum(v => v.Amount)}";
        for (int i = 0; i < ButtonSpeeds.Length; i++) b.Actions[$"TimeSpeedButton{i + 1}"] = $"meitou:speed.{i}";
        b.Actions["ShortcutInventoryButton"] = "meitou:inventory";
        b.Actions["ShortcutStatsButton"] = "meitou:stats";
        foreach (string name in new[] { "ShortcutMapButton", "ShortcutTechButton", "ShortcutSquadButton" }) b.Actions[name] = "faithful:" + name;
        b.Actions["OrdersMedicButton"] = "meitou:medic";
        b.Actions["OrdersRescueButton"] = "meitou:rescue";
        foreach (string name in new[] { "LoadingPanel", "TownPanel", "TooltipPanel", "MessagePanel", "BiomeTextPanel", "FloorPanel", "BuildPanel", "JobsPanel" }) b.Hidden.Add(name);
        if (session.Simulation.Speed != 0) b.Hidden.Add("PausedPanel");
        BindMedicalBars(b, lead);
        b.Hidden.Add("JobsPanelInfo");
        foreach (var widget in faithfulHud.Widgets.Where(w => w.Type == "TabItem")) b.HiddenWidgets.Add(widget);
        if (lead is null) { b.Hidden.Add("MedicalPanel"); b.Hidden.Add("InformationPanel"); }
        foreach (var w in faithfulHud.Widgets.Where(w => w.Type == "Button" && !b.Actions.ContainsKey(w.Name))) b.Disabled.Add(w.Name);
        if (Selected().Count == 0)
            foreach (string name in new[] { "OrdersMedicButton", "OrdersRescueButton", "medic", "rescue", "stop" }) b.Disabled.Add(name);
        return b;
    }

    IReadOnlyList<UiControl> ArrangeUi()
    {
        var b = BindUi();
        var controls = UiMode == UiMode.Faithful && !faithfulScreens.IsOpen("title") ? MyGuiPresentation.Arrange(faithfulHud, Width, Height, b, uiResources.Layers).ToList() : [];
        UiRect FaithfulSlot(string name) => controls.FirstOrDefault(c => c.Name == name)?.Bounds ?? default;
        var slots = new Dictionary<string, UiRect>
        {
            [UiSlots.Overlay] = new(0, 0, Width, Height),
            [UiSlots.Toolbar] = UiMode == UiMode.Meitou ? new(Math.Max(16, Width - 360), Math.Max(16, Height - 204), Math.Max(0, Math.Min(344, Width - 32)), 188) : FaithfulSlot("TimeMoneyPanel"),
            [UiSlots.Selection] = UiMode == UiMode.Meitou ? new(16, Math.Max(16, Height - 260), Math.Max(0, Math.Min(324, Width - 32)), 244) : FaithfulSlot("InformationPanel"),
            [UiSlots.Squad] = UiMode == UiMode.Meitou ? new(352, Math.Max(16, Height - 158), Math.Max(0, Width - 724), 142) : FaithfulSlot("SquadPanel"),
        };
        controls.AddRange(uiHost.Arrange(UiMode, Width, Height, b, slots));
        var centreText = new Dictionary<string, string> { ["TextAlign"] = "Center" };
        if (UiMode == UiMode.Faithful)
        {
            if (controls.FirstOrDefault(c => c.Name == "SquadPanel") is { } squadPanel)
            {
                var members = session.CurrentSnapshot.Characters.Where(c => c.IsPlayer).Take(8).ToArray();
                float memberWidth = Math.Min(100, squadPanel.Bounds.Width / Math.Max(1, members.Length));
                for (int i = 0; i < members.Length; i++)
                    controls.Add(new($"member-{i}", "Button", new(squadPanel.Bounds.X + i * memberWidth, squadPanel.Bounds.Y + 24, memberWidth - 4, 40),
                        members[i].Name, "meitou:member", "", true, centreText, members[i].Selected ? new(0.25f, 0.35f, 0.3f, 1) : new(0.12f, 0.15f, 0.16f, 1), Argument: i.ToString(CultureInfo.InvariantCulture)));
            }
        }
        if (UiMode == UiMode.Faithful)
            foreach (var screen in faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent)) controls.AddRange(screen.Controls);
        else if (openPanel is not null) AddDetailsPanel(controls);
        AddTradePanel(controls);
        if (UiMode == UiMode.Faithful) AddInventoryDrag(controls);
        return controls;
    }

    bool UiMouseDown(Vector2 p)
    {
        if (!UiVisible) return false;
        var controls = ArrangeUi();
        if (InventoryPointerDown(p)) return true;
        if (MapPointerDown(p)) return true;
        if (UiMode == UiMode.Faithful && faithfulScreens.PointerDown(p)) { UpdateMenuPause(); return true; }
        if (MyGuiPresentation.HitTest(controls, p.X, p.Y) is not { } c) return false;
        if (c.Action.StartsWith("faithful:", StringComparison.Ordinal))
        {
            faithfulScreens.Open("overview");
            faithfulScreens.SetTab("overview", "TabsMain", c.Name == "ShortcutTechButton" ? 2 : c.Name == "ShortcutSquadButton" ? 4 : 0);
            return true;
        }
        uiHost.Dispatch(c);
        return true;
    }

    void ExecuteUiAction(string action, string argument)
    {
        if (action.StartsWith("speed:", StringComparison.Ordinal)) PressButton(int.Parse(action[6..], CultureInfo.InvariantCulture));
        else if (action is "inventory" or "stats")
        {
            if (UiMode == UiMode.Faithful) { if (faithfulScreens.IsOpen(action)) faithfulScreens.Close(action); else faithfulScreens.Open(action); }
            else openPanel = openPanel == action ? null : action;
        }
        else if (action == "close") openPanel = null;
        else if (action == "squad") Select(PlayerCharacters(), false);
        else if (action == "member")
        {
            int index = int.Parse(argument, CultureInfo.InvariantCulture);
            var members = PlayerCharacters();
            if (index >= 0 && index < members.Count) Select([members[index]], false);
        }
        else if (action == "stop") Say(new StopCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (action == "medic") Say(new Meitou.Simulation.FirstAid.MedicJobCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (action == "rescue") Say(new Meitou.Simulation.Carrying.RescueJobCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (action is "buy" or "sell" or "trade.close" or "trade.page") TradeAction(action, argument);
    }

    void DrawGameUi(DebugOverlay overlay, int width, int height) =>
        uiPainter?.Draw(ArrangeUi(), width, height, mouse, $"meitou:speed.{Array.IndexOf(ButtonSpeeds, session.Simulation.Speed)}");

    /// <summary>For screenshot reviews: open the same panel through its registered button action.</summary>
    public void ShowUiPanel(string? panel)
    {
        openPanel = null;
        faithfulScreens.CloseAll();
        UpdateMenuPause();
        if (panel is null) return;
        if (UiMode == UiMode.Faithful && panel is "overview" or "menu" or "new-game" or "save" or "load" or "title")
        {
            if (panel == "title") ShowTitle();
            else if (panel is "save" or "load") OpenSlots(panel);
            else { faithfulScreens.Open(panel); if (panel == "overview") faithfulScreens.SetTab(panel, "TabsMain", 0); }
            UpdateMenuPause(); return;
        }
        if (panel is not ("inventory" or "stats")) throw new ArgumentException("Unknown UI panel.", nameof(panel));
        var control = ArrangeUi().First(c => c.Action == $"meitou:{panel}");
        uiHost.Dispatch(control);
    }
}
