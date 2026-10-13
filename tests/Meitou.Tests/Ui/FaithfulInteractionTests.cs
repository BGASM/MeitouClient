using System.Numerics;
using Meitou.Content;
using Meitou.Data;
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

public class FaithfulInteractionTests
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
}
