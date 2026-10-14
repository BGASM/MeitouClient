using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Trading;
using static Meitou.Tests.Gameplay.EconomyTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Prices in play (docs/game/economy.md sections 5 to 7) on an invented database: a trader faction and town with their cultures, and items of each price class.</summary>
public class TradeTests
{
    static FcsReference Ref(string id, int v0 = 0) => new(id, v0, 0, 0);

    /// <summary>
    /// Factions: Nameless (the player, 0), Traders (1). Towns: Trade Town (Traders, at the origin), Near Town (1000 away), Far Town (500000 away), a nest beside it. The faction's culture
    /// prices cloth at 200 %, the town's the katana at 150 % (and cloth not at all, so the faction's applies). <c>all trade goods</c> holds cloth.
    /// </summary>
    internal static PopulationData Data(params FcsRecord[] extra)
    {
        var db = Db([
            Rec("p-t", FcsRecordType.FACTION, r => r.Name = "Nameless"),
            Rec("f-t", FcsRecordType.FACTION, r => { r.Name = "Traders"; r.References["trade culture"] = [Ref("cf-t")]; }),
            Rec("cf-t", FcsRecordType.ITEMS_CULTURE, r => r.References["trade prices"] = [Ref("cloth-t", 200)]),
            Rec("ct-t", FcsRecordType.ITEMS_CULTURE, r => r.References["trade prices"] = [Ref("katana-t", 150)]),
            Rec("t1-t", FcsRecordType.TOWN, r => { r.Ints["type"] = 2; r.References["faction"] = [Ref("f-t")]; r.References["trade culture"] = [Ref("ct-t")]; }),
            Rec("t2-t", FcsRecordType.TOWN, r => r.Ints["type"] = 2),
            Rec("t3-t", FcsRecordType.TOWN, r => r.Ints["type"] = 3),
            Rec("n-t", FcsRecordType.TOWN, r => r.Ints["type"] = 0),
            Rec("katana-t", FcsRecordType.WEAPON, r => r.Ints["value"] = 500),
            Rec("cloth-t", FcsRecordType.ITEM, r => { r.Ints["value"] = 100; r.Bools["trade item"] = true; r.Ints["stackable"] = 20; }),
            Rec("junk-t", FcsRecordType.ITEM, r => r.Ints["value"] = 100),
            Rec("ration-t", FcsRecordType.ITEM, r => { r.Ints["value"] = 10; r.Ints["item function"] = 3; r.Floats["charges"] = 100; r.Ints["stackable"] = 10; }),
            Rec("cats-t", FcsRecordType.ITEM, r => { r.Name = Market.MoneyItemName; r.Ints["value"] = 1; r.Ints["stackable"] = 100; }),
            Rec("atg-t", FcsRecordType.VENDOR_LIST, r => { r.Name = Market.AllTradeGoods; r.References["items"] = [Ref("cloth-t", 100)]; }),
            .. extra]);
        TownPlacement At(string town, float x) => new("i-" + town, town, new Vector3(x, 0, 0), null);
        return PopulationData.Create(db, [At("t1-t", 0), At("t2-t", 1000), At("t3-t", 500_000), At("n-t", 500_100)]);
    }

    [Fact]
    public void Unit_and_stack_prices_follow_the_item_classes_and_the_trader_factor()
    {
        var data = Data();
        var market = new Market(data, 1);
        var items = data.Items;
        int player = data.PlayerFaction, traders = data.FactionIndex("f-t");
        var trader = new Trader(0, traders, BuyMult: 1.5f);

        // Weapon at level 50: base 17500; T = 1.5 x 1.5 (the town's culture).
        var katana = items.Create("katana-t")!;
        katana.Level = 50;
        Assert.Equal(17500, market.BaseValue(katana));
        Assert.Equal(17500, market.UnitPrice(katana, false, Trader.None, player));
        Assert.Equal(39375, market.UnitPrice(katana, false, trader, player));
        Assert.Equal(9843, market.UnitPrice(katana, true, trader, player));   // x loot price mult GEAR 0.25

        // A trade item: the town names no price for it, so the faction's 200 % applies; the market factor of the town scales the base; it sells at full price unless stolen.
        var cloth = items.Create("cloth-t")!;
        float mf = market.MarketFactor(0, "cloth-t");
        int u = (int)(60 * mf);
        Assert.Equal((int)(3f * u), market.UnitPrice(cloth, false, trader, player));
        Assert.Equal((int)(3f * u), market.UnitPrice(cloth, true, trader, player));
        cloth.StolenFrom = traders;
        Assert.Equal((int)(3f * (int)(u * 0.5f)), market.UnitPrice(cloth, true, trader, player));
        Assert.Equal((int)(3f * u), market.UnitPrice(cloth, true, trader, traders));   // held by its owner's faction it is not stolen

        // A plain item outside the trade goods: no market factor, half price when sold.
        var junk = items.Create("junk-t")!;
        Assert.Equal(90, market.UnitPrice(junk, false, trader, player));
        Assert.Equal(45, market.UnitPrice(junk, true, trader, player));

        // A stack: full units, the last one scaled by its charges (9 x 3 - trunc(9 - 4.5)); a single half-eaten unit is priced by its charges.
        var rations = items.Create("ration-t", 3)!;
        rations.Charges = rations.MaxCharges / 2;
        Assert.Equal(9, market.UnitPrice(rations, false, trader, player));
        Assert.Equal(23, market.StackPrice(rations, false, trader, player));
        rations.Quantity = 1;
        Assert.Equal(4, market.UnitPrice(rations, false, trader, player));   // trunc(1.5 x trunc(6 x 0.5))

        // Money is its value, whoever trades.
        Assert.Equal(1, market.UnitPrice(items.Create("cats-t", 50)!, true, trader, player));
        Assert.Equal(50, market.StackPrice(items.Create("cats-t", 50)!, true, trader, player));
    }

    [Fact]
    public void Market_factors_spread_by_distance_and_repeat_for_a_seed()
    {
        var data = Data();
        var a = new Market(data, 7);
        float t1 = a.MarketFactor(0, "cloth-t"), t2 = a.MarketFactor(1, "cloth-t");
        Assert.InRange(t1, 0.7f, 1.3f);
        Assert.True(MathF.Abs(t1 - t2) <= 1000 / 1_000_000f * 0.3f * 10 + 1e-6f);   // a town 1000 away may differ by 0.003
        Assert.Equal(1, a.MarketFactor(3, "cloth-t"));   // a nest has no market
        Assert.Equal(1, a.MarketFactor(0, "junk-t"));    // not a trade good
        Assert.Equal(t1, new Market(data, 7).MarketFactor(0, "cloth-t"));
        var spread = Enumerable.Range(0, 20).Select(s => new Market(data, (ulong)s).MarketFactor(2, "cloth-t")).ToList();
        Assert.True(spread.Max() - spread.Min() > 0.1f);   // the far town keeps most of the range
    }

    [Fact]
    public void A_theft_loot_marks_what_it_takes_as_stolen()
    {
        var data = Data();
        var victim = new Inventory();
        victim.Add(data.Items.Create("cloth-t", 5)!);
        var looter = new Inventory();
        var (taken, _) = Meitou.Simulation.Carrying.CarrySystem.TakeAll(looter, victim, stolenFrom: 1);
        Assert.Equal(1, taken);
        var cloth = looter.Items.Single();
        Assert.Equal(1, cloth.StolenFrom);
        Assert.True(cloth.IsStolenFor(0));
        // A clean stack does not swallow a stolen one.
        looter.Add(data.Items.Create("cloth-t", 5)!);
        Assert.Equal(2, looter.Items.Count);
    }

    // ------------------------------------------------------------------ a shop in a world

    /// <summary>A trader template (10 items from one list that always adds 500 cats) and a fence that buys stolen goods.</summary>
    internal static readonly FcsRecord[] Shops =
    [
        Rec("vl-t", FcsRecordType.VENDOR_LIST, r =>
        {
            r.References["items"] = [Ref("cloth-t", 100), Ref("junk-t", 0)];
            r.Floats["money item prob"] = 100;   // percent-looking, as in the data: always pays
            r.Ints["money item min"] = 500;
            r.Ints["money item max"] = 500;
        }),
        Rec("shop-t", FcsRecordType.SQUAD_TEMPLATE, r =>
        {
            r.Bools["is trader"] = true;
            r.References["vendors"] = [Ref("vl-t", 100)];
            r.Ints["vendors fill total amount"] = 10;
            r.Floats["vendors refresh time"] = 24;
        }),
        Rec("fence-t", FcsRecordType.SQUAD_TEMPLATE, r =>
        {
            r.Bools["is trader"] = true;
            r.Bools["buys stolen"] = true;
            r.References["vendors"] = [Ref("vl-t", 100)];
        }),
    ];

    sealed class Bazaar : IDisposable
    {
        public PopulationData Data { get; } = TradeTests.Data(Shops);
        public SimWorld World { get; }
        public TradeSystem Trade { get; }

        public Bazaar()
        {
            Trade = new TradeSystem(Data);
            var ground = new OpenGroundWalkability((_, _) => 150);   // above the water line
            World = new SimWorld(new WorldSettings { Seed = 3 }, ground, [new MovementSystem(new PathService(ground, true)), Trade]);
            World.Player.Faction = Data.PlayerFaction;
            World.Player.Money = 1000;
        }

        public void Dispose() => World.Dispose();

        public CharacterId Spawn(string name, Vector2 at, int faction, bool player, string template = "p")
        {
            var squad = new Squad { Id = World.Squads.NextId(), TemplateId = template, Faction = faction, Town = 0, HomeCentre = at, HomeRadius = 10 };
            World.Squads.Add(squad);
            var cold = new Meitou.Tests.Combat.Combatant { Name = name }.MakeCold();
            cold.Faction = faction;
            cold.IsPlayer = player;
            cold.Inventory = new Inventory();
            cold.SquadId = squad.Id;
            var id = World.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 150, at.Y), Health = 100, MaxSpeed = 60, WalkSpeed = 15, Task = (byte)CharacterTask.Idle }, cold, 0);
            squad.Members.Add(id);
            squad.Leader = id;
            return id;
        }

        public Squad SquadOf(CharacterId id) => World.Squads.Find(World.Characters.Cold(id.Slot)!.SquadId)!;
        public Inventory Bag(CharacterId id) => World.Characters.Cold(id.Slot)!.Inventory!;
        public void Say(SimCommand c) => World.Commands.Enqueue(c with { Tick = World.Tick });
        public TradeView View => World.Snapshot.Trade!;
    }

    [Fact]
    public void A_trader_squad_gets_a_shop_and_the_player_buys_and_sells_at_its_prices()
    {
        using var b = new Bazaar();
        var hero = b.Spawn("hero", new Vector2(0, 0), b.Data.PlayerFaction, true);
        var shopkeeper = b.Spawn("shopkeeper", new Vector2(10, 0), b.Data.FactionIndex("f-t"), false, "shop-t");
        b.World.RunTicks(1);
        var shop = b.SquadOf(shopkeeper).Shop!;
        int units = shop.Stock.All().Sum(i => i.Quantity);
        Assert.InRange(units, 8, 12);                            // round(U(0.8, 1.2) x 10)
        Assert.Equal(3000 + 500, b.SquadOf(shopkeeper).Money);   // a small stock is worth less than 12000: the floor, plus the money roll
        Assert.Equal(1 + (long)(24 / BodySystem.HoursPerTick) - 1, shop.NextRestockTick);

        b.Say(new OpenTrade(hero, shopkeeper) { Tick = 0 });
        b.World.RunTicks(2);
        var view = b.View;
        Assert.Equal(units, view.Stock.Sum(i => i.Quantity));
        Assert.True(b.World.Snapshot.Characters.Single(c => c.Id == shopkeeper).IsTrader);

        // Buy one unit of the first stack: its unit price leaves the purse and enters the shop's.
        var first = view.Stock[0];
        b.Say(new BuyItem(first.Token, 1) { Tick = 0 });
        b.World.RunTicks(1);
        Assert.Equal(1000 - first.UnitPrice, b.World.Player.Money);
        Assert.Equal(3500 + first.UnitPrice, b.SquadOf(shopkeeper).Money);
        Assert.Equal(1, b.Bag(hero).All().Sum(i => i.Quantity));

        // Sell it back: the sell price (a trade item at full price, junk at half).
        var mine = b.View.Goods.Single();
        b.Say(new SellItem(mine.Token, 1) { Tick = 0 });
        b.World.RunTicks(1);
        Assert.Equal(1000 - first.UnitPrice + mine.Price, b.World.Player.Money);
        Assert.Empty(b.Bag(hero).All());

        // Too poor: nothing moves.
        b.World.Player.Money = 0;
        b.Say(new BuyItem(b.View.Stock[0].Token, 1) { Tick = 0 });
        b.World.RunTicks(1);
        Assert.Contains("You can't afford that.", b.World.TakeMessages());
        Assert.Empty(b.Bag(hero).All());

        // Walking away closes the trade; the stock is replaced when the refresh time comes.
        b.Say(new MoveOrder([hero], new Vector3(-300, 150, 0)) { Tick = 0 });
        b.World.RunTicks(300);
        Assert.Null(b.World.Snapshot.Trade);
        b.World.RunTicks((int)(shop.NextRestockTick - b.World.Tick) + 15);
        Assert.Equal(2, shop.Fills);
    }

    [Fact]
    public void Stolen_goods_are_only_safe_with_a_fence()
    {
        Assert.Equal(0.4f, TradeSystem.FenceChance(0, false), 5);
        Assert.Equal(1.25f, TradeSystem.FenceChance(100, false), 5);
        Assert.Equal(0.2f, TradeSystem.FenceChance(0, true), 5);   // halved when it was stolen from the shop's own faction

        using var b = new Bazaar();
        var hero = b.Spawn("hero", new Vector2(0, 0), b.Data.PlayerFaction, true);
        var fence = b.Spawn("fence", new Vector2(10, 0), b.Data.FactionIndex("f-t"), false, "fence-t");
        var loot = b.Data.Items.Create("cloth-t", 2)!;
        loot.StolenFrom = b.Data.FactionIndex("f-t");
        b.Bag(hero).Add(loot);
        b.World.RunTicks(1);
        b.Say(new OpenTrade(hero, fence) { Tick = 0 });
        b.World.RunTicks(2);
        var goods = b.View.Goods.Single();
        Assert.True(goods.Stolen);
        b.Say(new SellItem(goods.Token, 2) { Tick = 0 });
        b.World.RunTicks(1);
        Assert.Equal(1000 + goods.Price, b.World.Player.Money);
        Assert.Empty(b.Bag(hero).All());
    }
}
