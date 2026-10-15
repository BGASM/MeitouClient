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
    int lastMemberClick = -1;
    bool infoExpanded;
    long lastMemberClickAt;
    MyGuiLayout faithfulHud = null!;
    MyGuiResources uiResources = new();
    readonly UiHost uiHost = new();
    IDisposable? nativeHudRegistration;
    UiPainter? uiPainter;
    string? openPanel;
    MyGuiScreenHost faithfulScreens = null!;
    public event Action<MyGuiEvent>? ScreenAction;

    /// <summary>The viewed floor of the interior view (the host's): read for the HUD's floor text, stepped by its arrows.</summary>
    public (Func<int> Get, Action<int> Step)? Floor { get; set; }
    public bool UiVisible { get; set; } = true;
    internal IReadOnlyList<UiControl> UiFrame => ArrangeUi();

    public void LoadUi(UiMode mode, Func<string, string?> resolve)
    {
        UiMode = mode;
        uiResolve = resolve;
        uiResources = MyGuiResources.Load("kenshi_init.xml", resolve);
        faithfulHud = MyGuiTemplates.Expand(MyGuiReader.ReadLayout(resolve("Kenshi_MainPanel.layout") ?? throw new FileNotFoundException("Kenshi_MainPanel.layout not found.")), uiResources);
        faithfulScreens = new(uiResources);
        foreach (var (id, file, modal) in FaithfulScreenFiles)
            faithfulScreens.Register(id, MyGuiReader.ReadLayout(resolve(file + ".layout") ?? throw new FileNotFoundException(file)), modal,
                centreRoot: id is "new-game" or "save" or "load");
        faithfulScreens.Activated += FaithfulAction;
        uiHost.Diagnostic += d => Console.Error.WriteLine($"ui        {d.Extension} {d.Operation}: {d.Error.Message}");
        nativeHudRegistration = uiHost.Register(new NativeHudExtension(ExecuteUiAction));
    }

    /// <summary>The original layouts the faithful mode opens as screens: id, layout file, modal.</summary>
    internal static readonly (string Id, string File, bool Modal)[] FaithfulScreenFiles =
    [
        ("inventory", "Kenshi_InventoryCharacterWindow", false), ("stats", "Kenshi_StatsWindow", false),
        ("overview", "Kenshi_OverviewWindow", false), ("menu", "Kenshi_MainMenuPopupPanel", true),
        ("title", "Kenshi_MainMenu", true), ("new-game", "Kenshi_NewGamePanel", true),
        ("advanced", "Kenshi_AdvancedOptionsWindow", true), ("save", "Kenshi_SaveGamePanel", true),
        ("load", "Kenshi_LoadGamePanel", true), ("message", "Kenshi_MessageBox", true),
        ("bag", "Kenshi_InventoryGenericWindow", false), ("limbs", "Kenshi_LimbReplacementWindow", false), ("trader", "Kenshi_InventoryTraderWindow", false),
    ];

    /// <summary>The expanded layout and the current bindings of the HUD (<c>"hud"</c>) or of a faithful screen, for the layout audit test.</summary>
    internal (MyGuiLayout Layout, UiBindings Bindings) FaithfulBindings(string id) =>
        id == "hud" ? (faithfulHud, BindUi()) : (faithfulScreens.Layout(id), BindFaithfulScreen(id));

    public void CreateUiPainter(DebugOverlay overlay, Func<string, string?> resolve) => uiPainter = new(overlay, uiResources, resolve);
    public void DisposeUi() { uiPainter?.Dispose(); uiPainter = null; nativeHudRegistration?.Dispose(); nativeHudRegistration = null; }
    /// <summary>Future code-mod loader entry point; disposal removes the extension's owned registrations.</summary>
    public IDisposable RegisterUiExtension(IUiExtension extension) => uiHost.Register(extension);
    public bool ContainsUi(Vector2 p)
    {
        if (!UiVisible) return false;
        var controls = ArrangeUi();
        return UseScreenInput && faithfulScreens.Contains(p) || MyGuiPresentation.HitTest(controls, p.X, p.Y) is not null;
    }

    public void SetUiMode(UiMode mode)
    {
        UiMode = mode;
        dragFrom = null;
        dragging = false;
        steering = false;
        mapDrag = null;
        inventoryDrag = null;
        menuOpen = false;
        UpdateMenuPause();
    }

    UiBindings BindUi()
    {
        var b = new UiBindings();
        var lead = Lead();
        b.Text["clock"] = $"{session.Clock.DayText}  {session.Clock.TimeText}";
        b.Text["DayText"] = session.Clock.DayText;
        b.Text["TimeText"] = session.Clock.TimeText;
        // The exe's money format string is c.{1,num} (docs/game/ui-screens.md section 1): no space after the point.
        b.Text["MoneyAmountText"] = "c." + session.World.Player.Money.ToString("N0", CultureInfo.InvariantCulture);
        // The Meitou HUD's title; the faithful name bar draws its own lines over the widget (PlayerInterface.HudText.cs).
        b.Text["NameText"] = lead?.Name ?? "No character selected";
        b.Properties["NameText"] = new Dictionary<string, string> { ["SkipCaption"] = "true" };
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
        b.Text["ExtendedStatsText"] = "";
        // The exe sets the money label without the layout's colon (docs/game/ui-screens.md section 4).
        b.Text["MoneyLabelText"] = "Money";
        b.Text["BountyText"] = lead is null || lead.Bounties.Count == 0 ? "" : $"Bounty c.{lead.Bounties.Sum(v => v.Amount)}";
        for (int i = 0; i < ButtonSpeeds.Length; i++) b.Actions[$"TimeSpeedButton{i + 1}"] = $"meitou:speed.{i}";
        b.Actions["ShortcutInventoryButton"] = "meitou:inventory";
        b.Actions["ShortcutStatsButton"] = "meitou:stats";
        foreach (string name in new[] { "ShortcutMapButton", "ShortcutTechButton", "ShortcutSquadButton" }) b.Actions[name] = "faithful:" + name;
        b.Actions["OrdersMedicButton"] = "meitou:medic";
        b.Actions["OrdersRescueButton"] = "meitou:rescue";
        BindOrders(b, lead);
        foreach (string name in new[] { "LoadingPanel", "TownPanel", "TooltipPanel", "MessagePanel", "BiomeTextPanel", "JobsPanel" }) b.Hidden.Add(name);
        // The viewed floor (the exe's "Floor {1,num}", docs/game/ui-screens.md section 4; docs/formats/zones.md "Interior view"); the arrows step it as PageUp / PageDown do.
        b.Text["FloorText"] = $"Floor {Floor?.Get() ?? 0}";
        b.Actions["FloorArrowUp"] = "meitou:floor-up";
        b.Actions["FloorArrowDown"] = "meitou:floor-down";
        if (lead is null || lead.Bounties.Count == 0) b.Hidden.Add("BountyPanel");
        // The information panel starts collapsed (as the original's, docs/game/ui-screens.md section 4): the extending part (name, state lines, extended stats) slid down
        // until its bottom is the panel's, behind the stats panel, so only the name and the state lines show; the ^ button between them toggles it.
        if (!infoExpanded && faithfulHud.Find("InformationPanel") is { } info && faithfulHud.Find("ExtendingPanel") is { } extending)
        {
            var infoRect = info.Position.RelativeTo(new(0, 0, Width, Height), true);
            var rect = extending.Position.RelativeTo(infoRect, true);
            b.Positions["ExtendingPanel"] = rect with { Y = infoRect.Y + infoRect.Height - rect.Height };
        }
        b.Actions["ExtendedStatsButton"] = "meitou:extendedstats";
        if (session.Simulation.Speed != 0) b.Hidden.Add("PausedPanel");
        BindMedicalBars(b, lead);
        b.Hidden.Add("JobsPanelInfo");
        // The squad bar has one tab per squad of the player's; Meitou has one squad, so the first page shows, captioned with its name.
        foreach (var widget in faithfulHud.Find("SquadPanel")?.Children.Where(w => w.Type == "TabItem").Skip(1) ?? []) b.HiddenWidgets.Add(widget);
        b.Text["SquadPanel@tab0"] = SquadTabCaption(lead);
        if (lead is null) { b.Hidden.Add("MedicalPanel"); b.Hidden.Add("InformationPanel"); }
        // Buttons without a backend are disabled, except those the original never greys out (the build and floor buttons look live there in the open).
        foreach (var w in faithfulHud.Widgets.Where(w => w.Type == "Button" && !b.Actions.ContainsKey(w.Name) && w.Name is not ("BuildButton" or "FloorArrowUp" or "FloorArrowDown"))) b.Disabled.Add(w.Name);
        if (Selected().Count == 0)
            foreach (string name in new[] { "OrdersMedicButton", "OrdersRescueButton", "medic", "rescue", "stop" }) b.Disabled.Add(name);
        return b;
    }

    /// <summary>
    /// The squad tab's caption. The original captions a tab with its squad's name, which for the player's first squad reads like <c>Nameless_0</c>: the faction's
    /// name and the squad's number (<b>Observed</b>; where the original makes it is <b>Unknown</b>). Meitou's player squad keeps its start's template name, so the
    /// tab shows the faction's name and 0.
    /// </summary>
    string SquadTabCaption(Meitou.Simulation.CharacterSnapshot? lead)
    {
        string? faction = FactionName?.Invoke(lead?.Faction ?? session.World.Player.Faction);
        return string.IsNullOrEmpty(faction) ? "Squad 1" : faction + "_0";
    }

    IReadOnlyList<UiControl> ArrangeUi()
    {
        SyncTradeScreens();
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
        if (!TitleOpen) controls.AddRange(uiHost.Arrange(UiMode, Width, Height, b, slots));
        var centreText = new Dictionary<string, string> { ["TextAlign"] = "Center" };
        if (UiMode == UiMode.Faithful)
        {
            if (controls.FirstOrDefault(c => c.Name == "SquadPanel") is { } squadPanel) AddSquadPortraits(controls, squadPanel.Bounds);
            AddHudText(controls);
        }
        if (UseScreenInput)
        {
            foreach (var screen in faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent))
            {
                controls.AddRange(screen.Controls.Where(c => UiMode == UiMode.Faithful ||
                    !c.Properties.TryGetValue("ImageResource", out var resource) || resource != "Kenshi_TitleScreen"));
                if (screen.Id == "title" && UiMode == UiMode.Meitou && !showCredits && TitleLogoReady())
                {
                    float logoWidth = Math.Min(Width * 0.3f, Height * 0.6f);
                    controls.Add(new("MeitouTitle", "ImageBox", new(Width * 0.975f - logoWidth, Height * 0.04f, logoWidth, logoWidth * 0.469f), "", "", "", true,
                        new Dictionary<string, string> { ["ImageTexture"] = TitleLogoTexture }, Vector4.Zero, Pickable: false));
                }
                else if (screen.Id == "title" && UiMode == UiMode.Meitou && !showCredits)
                    controls.Add(new("MeitouTitle", "TextBox", new(Width * 0.6f, Height * 0.04f, Width * 0.37f, Height * 0.14f), "KENSHI", "", "", true,
                        new Dictionary<string, string> { ["FontName"] = "Meitou_TitleFont", ["TextAlign"] = "Right VCenter" },
                        Vector4.Zero, new Vector4(0.89f, 0.84f, 0.71f, 1), TextScale: Height / 720f));
            }
        }
        if (openPanel is not null && !FrontendActive) AddDetailsPanel(controls);
        if (UiMode == UiMode.Meitou && menuOpen) AddMenuPanel(controls);
        AddInventoryDrag(controls);
        AddContextMenu(controls);
        AddHoverLabel(controls);
        return controls;
    }

    bool UiMouseDown(Vector2 p)
    {
        if (!UiVisible) return false;
        var controls = ArrangeUi();
        if (InventoryPointerDown(p)) return true;
        if (MapPointerDown(p)) return true;
        if (UseScreenInput && faithfulScreens.PointerDown(p)) { UpdateMenuPause(); return true; }
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
            if (UiMode == UiMode.Faithful || action == "inventory")
            {
                if (faithfulScreens.IsOpen(action))
                {
                    faithfulScreens.Close(action);
                    if (action == "inventory") { faithfulScreens.Close("bag"); faithfulScreens.Close("limbs"); }
                }
                else faithfulScreens.Open(action);
            }
            else openPanel = openPanel == action ? null : action;
        }
        else if (action == "close") openPanel = null;
        else if (action == "extendedstats") infoExpanded = !infoExpanded;
        else if (action is "floor-up" or "floor-down") Floor?.Step(action == "floor-up" ? 1 : -1);
        else if (action == "menu") MenuAction(argument);
        else if (action == "context") ContextAction(argument);
        else if (action == "squad") Select(PlayerCharacters(), false);
        else if (action == "member")
        {
            // A portrait click selects, a double click centres and follows (docs/game/ui-input.md section 7).
            int index = int.Parse(argument, CultureInfo.InvariantCulture);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            bool doubleClick = index == lastMemberClick && System.Diagnostics.Stopwatch.GetElapsedTime(lastMemberClickAt, now).TotalSeconds <= DoubleTapSeconds;
            (lastMemberClick, lastMemberClickAt) = (doubleClick ? -1 : index, now);
            SelectMember(index, false, doubleClick);
        }
        else if (action == "stop") Say(new StopCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (action == "medic") Say(new Meitou.Simulation.FirstAid.MedicJobCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (action == "rescue") Say(new Meitou.Simulation.Carrying.RescueJobCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (action is "buy" or "sell" or "trade.close" or "trade.page") TradeAction(action, argument);
        else if (action.StartsWith("order.", StringComparison.Ordinal) && Enum.TryParse(action[6..], out CharacterOrders order))
            Say(new OrdersCommand(SelectedIds(), order) { Tick = session.World.Tick });
        else if (action.StartsWith("runspeed.", StringComparison.Ordinal))
        {
            int step = int.Parse(action[9..], CultureInfo.InvariantCulture);
            Say(new RunSpeedCommand(SelectedIds(), step) { Tick = session.World.Tick });
            if (Selected().FirstOrDefault() is { } lead) Notice("Move speed: " + RunSpeedName((byte)((((lead.RunSpeed + step) % 3) + 3) % 3)));
        }
    }

    void DrawGameUi(DebugOverlay overlay, int width, int height) =>
        uiPainter?.Draw(ArrangeUi(), width, height, mouse, $"meitou:speed.{Array.IndexOf(ButtonSpeeds, session.Simulation.Speed)}");

    /// <summary>For screenshot reviews: open the same panel through its registered button action.</summary>
    public void ShowUiPanel(string? panel)
    {
        openPanel = null;
        menuOpen = false;
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
        if (panel == "menu") { ToggleMenu(); return; }
        if (panel is not ("inventory" or "stats")) throw new ArgumentException("Unknown UI panel.", nameof(panel));
        var control = ArrangeUi().First(c => c.Action == $"meitou:{panel}");
        uiHost.Dispatch(control);
    }
}
