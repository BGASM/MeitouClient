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
using Meitou.Simulation.Trading;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Ui;

/// <summary>The trade window (docs/simulation.md "Trade"): its buttons become trade commands at the world's tick.</summary>
public class TradeUiTests
{
    [Fact, Slow]
    public void The_trade_windows_buy_button_buys_at_the_next_tick()
    {
        var install = GameInstall.Locate(); Assert.SkipWhen(install is null, "No Kenshi install configured");
        var data = TradeTests.Data(TradeTests.Shops);
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 150, systems: [new TradeSystem(data)]);
        var world = session.World;
        CharacterId Spawn(string name, float x, int faction, bool player, string template)
        {
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = template, Faction = faction, Town = 0 };
            world.Squads.Add(squad);
            var id = world.Characters.Spawn(new CharacterHot { Position = new Vector3(x, 150, 0), Health = 100 },
                new CharacterCold { Name = name, Faction = faction, IsPlayer = player, Inventory = new Inventory(), SquadId = squad.Id }, 0);
            squad.Members.Add(id);
            return id;
        }
        var hero = Spawn("hero", 0, data.PlayerFaction, true, "p");
        var keeper = Spawn("keeper", 10, data.FactionIndex("f-t"), false, "shop-t");
        world.Player.Faction = data.PlayerFaction;
        world.Player.Money = 100_000;
        world.Player.Selection.Add(hero);
        world.RunTick();   // the shop opens on the first look at the squads
        world.Commands.Enqueue(new OpenTrade(hero, keeper) { Tick = world.Tick });
        world.RunTicks(2);

        var resources = OgreScriptResources.ForInstall(install!, LoadOrder.FromInstall(install!));
        var ui = new PlayerInterface(session, (_, _) => 0, c => c.Position) { Width = 1600, Height = 900 };
        ui.LoadUi(UiMode.Faithful, name => resources.Find(name));
        try
        {
            var price = world.Snapshot.Trade!.Stock[0].UnitPrice;
            var button = ui.UiFrame.Last(c => c.Name == "stock-one-0");
            ui.MouseDown(MouseButton.Left, new(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2), false);
            Assert.Equal(100_000, world.Player.Money);   // nothing moves before the tick
            world.RunTick();
            Assert.Equal(100_000 - price, world.Player.Money);
            Assert.Single(world.Characters.Cold(hero.Slot)!.Inventory!.All());
        }
        finally { ui.DisposeUi(); }
    }
}
