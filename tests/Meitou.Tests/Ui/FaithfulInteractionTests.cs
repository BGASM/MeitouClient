using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ogre;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Engine.Input;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Tests.Simulation;
using Meitou.Ui;

namespace Meitou.Tests.Ui;

public class FaithfulInteractionTests(ITestOutputHelper output)
{
    static PlayerInterface Ui(WorldSession session)
    {
        var install = GameInstall.Locate(); Assert.SkipWhen(install is null, "No Kenshi install configured");
        var resources = OgreScriptResources.ForInstall(install, LoadOrder.FromInstall(install));
        var ui = new PlayerInterface(session, (_, _) => 0, c => c.Position) { Width = 1600, Height = 900 };
        ui.LoadUi(UiMode.Faithful, name => resources.Find(name));
        ui.ConfigureFrontend(SyntheticTown.Database(), () => [], ""); return ui;
    }
    static Vector2 Centre(UiControl c) => new(c.Bounds.X + c.Bounds.Width / 2, c.Bounds.Y + c.Bounds.Height / 2);
    static void Click(PlayerInterface ui, string name) => ui.MouseDown(MouseButton.Left, Centre(ui.UiFrame.Last(c => c.Name == name)), false);

    [Fact, Slow]
    public void OriginalTitleNewGameAndExitConfirmationProduceHostRequestsAndPauseTheWorld()
    {
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0);
        var ui = Ui(session); MyGuiEvent? request = null; ui.ScreenAction += e => request = e;
        try
        {
            ui.ShowTitle(); Assert.True(session.Simulation.RequestedPause);
            Click(ui, "NewGameButton"); Assert.Contains(ui.UiFrame, c => c.Name == "BeginButton" && c.Enabled);
            Click(ui, "BeginButton"); Assert.Equal("new-game", request!.Action); Assert.False(session.World.Player.Exists);
            ui.UiKey(Key.Escape); Click(ui, "ExitButton");
            Assert.Contains(ui.UiFrame, c => c.Name == "ButtonA");
            Click(ui, "ButtonB"); Assert.Equal("new-game", request.Action);
            Click(ui, "ExitButton"); Click(ui, "ButtonA"); Assert.Equal("exit", request.Action);
            ui.CloseFrontend(); Assert.False(session.Simulation.RequestedPause);
        }
        finally { ui.DisposeUi(); }
    }

    [Fact, Slow]
    public void PocketDragRunsThroughTheOriginalWindowAndOnlyMovesAtASimulationTick()
    {
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: [new PlayerSystem()]);
        var inventory = new Inventory(8, 6);
        var item = new ItemInstance { Record = "item", Name = "Test item", Width = 1, Height = 1 };
        inventory.Items.Add(item);
        var character = session.World.Characters.Spawn(new(), new() { Name = "Player", IsPlayer = true, Inventory = inventory }, 0);
        session.World.Player.Faction = 0; session.World.Player.Selection.Add(character); session.World.RunTick();
        var ui = Ui(session);
        try
        {
            ui.ShowUiPanel("inventory");
            var source = ui.UiFrame.Single(c => c.Name == "inventory-item-0");
            var slot = ui.UiFrame.Single(c => c.Name == "main").Bounds;
            var target = new Vector2(slot.X + slot.Width * 2.5f / 8, slot.Y + slot.Height * 1.5f / 6);
            ui.MouseDown(MouseButton.Left, Centre(source), false); Assert.True(ui.UiCapturesPointer);
            ui.MouseMove(target); ui.MouseUp(MouseButton.Left, target, false);
            Assert.Equal((0, 0), (item.X, item.Y));
            session.World.RunTick(); Assert.Equal((2, 1), (item.X, item.Y)); Assert.False(ui.UiCapturesPointer);
        }
        finally { ui.DisposeUi(); }
    }

    /// <summary>Names the HUD bindings carry for the Meitou mode's native HUD only; the faithful layout has no widget for them.</summary>
    static readonly HashSet<string> NativeOnly = ["clock", "hunger", "condition", "blood", "body", "bloodFraction", "bodyFraction", "hungerFraction", "squad.count", "members"];

    [Fact, Slow]
    public void EveryWidgetTheFaithfulModeFillsExistsInTheOriginalLayout()
    {
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: [new PlayerSystem()]);
        var character = session.World.Characters.Spawn(new(), new() { Name = "Player", IsPlayer = true, Inventory = new Inventory(8, 6) }, 0);
        session.World.Player.Faction = 0; session.World.Player.Selection.Add(character); session.World.RunTick();
        var ui = Ui(session);
        var missing = new List<string>();
        try
        {
            foreach (string id in PlayerInterface.FaithfulScreenFiles.Select(f => f.Id).Prepend("hud"))
            {
                var (layout, b) = ui.FaithfulBindings(id);
                var names = layout.Widgets.Select(w => w.Name).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
                bool Native(string n) => id == "hud" && (NativeOnly.Contains(n) || n.StartsWith("squad.", StringComparison.Ordinal));
                // What the screen shows or answers to must land on a real widget; hiding or styling an absent one is only reported.
                foreach (string n in b.Text.Keys.Concat(b.Actions.Keys).Concat(b.Lists.Keys).Concat(b.Progress.Keys).Distinct())
                    if (!names.Contains(n) && !Native(n)) missing.Add($"{id}: {n}");
                foreach (string n in b.Hidden.Concat(b.Disabled).Concat(b.Properties.Keys).Distinct().Where(n => !names.Contains(n) && !Native(n)))
                    output.WriteLine($"{id}: hides or styles absent widget {n}");
                // Named widgets with an editor caption that the code neither fills nor hides keep showing the layout's sample text.
                foreach (var w in layout.Widgets.Where(w => w.Name.Length > 0 && w.Properties.TryGetValue("Caption", out string? c) && c.Length > 0))
                    if (!b.Text.ContainsKey(w.Name) && !b.Hidden.Contains(w.Name))
                        output.WriteLine($"{id}: {w.Type} {w.Name} keeps its caption \"{w.Properties["Caption"]}\"");
            }
        }
        finally { ui.DisposeUi(); }
        Assert.True(missing.Count == 0, "bound to widgets the original layouts lack: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheStatsPanelsShowEachRecordStatOnce()
    {
        var shown = PlayerInterface.StatPanels.SelectMany(p => p).ToArray();
        Assert.Equal(shown.Length, shown.Distinct().Count());
        Assert.Equal(StatsInfo.RecordStats.Order(), shown.Order());
        Assert.Equal(5, PlayerInterface.StatPanels.Length);
    }
}
