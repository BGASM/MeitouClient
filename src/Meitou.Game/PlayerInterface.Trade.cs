using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Trading;
using Meitou.Ui;

namespace Meitou.Game;

/// <summary>
/// Trading (docs/simulation.md "Trade"), shown while the world publishes a trade (<see cref="TradeView"/>).
/// <list type="bullet">
/// <item>Both UI modes: the trader's stock is an inventory window (the original's trader variant of the inventory GUI, <c>140155ee0</c>) beside the character's
/// inventory window, and items are dragged between the grids: stock dropped on the pocket or bag grid is bought into that cell, the character's items dropped on the stock are sold. Which
/// layout the trader variant loads is <b>Observed</b> by its name: <c>Kenshi_InventoryTraderWindow</c>. Prices belong to tooltips, not overlaid on the icons.
/// Closing the trader window ends the trade.</item>
/// </list>
/// A stolen item that the trader is no fence for is marked: selling it may get the seller caught.
/// </summary>
sealed partial class PlayerInterface
{
    int stockPage, goodsPage;
    /// <summary>The trader whose window the Faithful interface opened (none: no trade shown) and whether its closing was already sent.</summary>
    CharacterId shownTrader = CharacterId.None;
    bool tradeCloseSent;

    /// <summary>Opens the trader beside the inventory when a trade opens, and ends the trade when the player closes its window.</summary>
    void SyncTradeScreens()
    {
        if (faithfulScreens is null) return;
        var trade = session.CurrentSnapshot.Trade;
        if (trade is null)
        {
            tradeDrag = null;
            if (!shownTrader.IsNone && faithfulScreens.IsOpen("trader")) faithfulScreens.Close("trader");
            shownTrader = CharacterId.None;
            tradeCloseSent = false;
            return;
        }
        if (trade.Trader != shownTrader)
        {
            tradeDrag = null;
            shownTrader = trade.Trader;
            tradeCloseSent = false;
            if (!faithfulScreens.IsOpen("inventory")) faithfulScreens.Open("inventory");
            faithfulScreens.Open("trader");
            PlaceTraderWindow();
            return;
        }
        if (!faithfulScreens.IsOpen("trader") && !tradeCloseSent)
        {
            Say(new CloseTrade { Tick = session.World.Tick });
            tradeCloseSent = true;
        }
    }

    /// <summary>The stock window opens to the right of the character, as observed in the reference screenshot.</summary>
    void PlaceTraderWindow() => PlaceInventoryBeside("trader", left: false);

    /// <summary>The grid widget of an inventory window: the bag's contents grid, a pocket grid, else the largest widget drawn with the inventory slot skin.</summary>
    static string? GridIn(IReadOnlyList<UiControl> controls) =>
        controls.FirstOrDefault(c => c.Name == "backpack_content")?.Name ?? controls.FirstOrDefault(c => c.Name == "main")?.Name
        ?? controls.Where(c => c.Skin.Contains("InventorySlot", StringComparison.Ordinal)).OrderByDescending(c => c.Bounds.Width * c.Bounds.Height).FirstOrDefault()?.Name;

    string? TraderGrid(string screen) => GridIn(faithfulScreens.Arrange(Width, Height, BindFaithfulScreen, AddFaithfulContent).First(f => f.Id == screen).Controls);

    /// <summary>The trader's name in the caption; its purse is added to the data panel after arranging.</summary>
    void BindTraderScreen(UiBindings b)
    {
        var trade = session.CurrentSnapshot.Trade;
        b.Text["Root"] = trade?.TraderName.ToUpperInvariant() ?? "TRADER";
        b.Properties["datapanel"] = new Dictionary<string, string> { ["SkipCaption"] = "true" };
    }

    /// <summary>The stock in the trader window's grid, each with what the stack costs.</summary>
    void AddTraderContent(List<UiControl> controls)
    {
        if (session.CurrentSnapshot.Trade is not { } trade || GridIn(controls) is not { } gridName) return;
        AddLines(controls, "datapanel", [new("Money:", "c." + Money(trade.ShopMoney))], 20 * InventoryScale);
        int gridIndex = controls.FindIndex(c => c.Name == gridName);
        var slot = controls[gridIndex];
        int columns = Math.Max(1, trade.StockWidth), rows = Math.Max(1, trade.StockHeight);
        controls[gridIndex] = slot with
        {
            Properties = new Dictionary<string, string>(slot.Properties, StringComparer.Ordinal) { ["TileSize"] = FormattableString.Invariant($"{slot.Bounds.Width / columns} {slot.Bounds.Height / rows}") },
        };
        var area = slot.Bounds;
        foreach (var item in trade.Stock)
        {
            if (tradeDrag?.Item.Index == item.Index) continue;
            AddItemCell(controls, $"trade-item-{item.Index}",
                new(area.X + item.X * area.Width / columns, area.Y + item.Y * area.Height / rows, item.Width * area.Width / columns, item.Height * area.Height / rows),
                item.Record, item.Material, item.Name, item.Quantity, $"c.{Money(item.Price)}", "TradeItem", item.Index, slot.Clip);
        }
    }

    /// <summary>While trading, what one of the customer's items fetches (null: no trade, or another character's grid).</summary>
    string? SellCaption(CharacterId owner, int index)
    {
        if (session.CurrentSnapshot.Trade is not { } trade || trade.Customer != owner || trade.Goods.FirstOrDefault(g => g.Index == index) is not { } goods) return null;
        return $"c.{Money(goods.Price)}" + (goods.Stolen && !trade.BuysStolen ? " (stolen!)" : goods.Stolen ? " (stolen)" : "");
    }

    void AddTradePanel(List<UiControl> controls)
    {
        if (session.CurrentSnapshot.Trade is not { } trade) { stockPage = goodsPage = 0; return; }
        float w = Math.Min(900, Width - 32), h = Math.Min(560, Height - 64);
        float x = (Width - w) / 2f, y = Math.Max(16, (Height - h) / 2f - 40);
        var ink = new Vector4(0.82f, 0.8f, 0.71f, 1);
        var title = new Vector4(0.89f, 0.84f, 0.71f, 1);
        var warn = new Vector4(0.86f, 0.42f, 0.32f, 1);
        void Add(string id, UiRect bounds, string caption = "", string skin = "", string action = "", Vector4 background = default, bool painted = false, bool pickable = false,
            float scale = 1, string argument = "", string align = "Left VCenter", Vector4? colour = null)
        {
            controls.Add(new(id, action.Length > 0 ? "Button" : "Widget", bounds, caption, action, skin, true,
                new Dictionary<string, string> { ["FontName"] = painted ? "Kenshi_PaintedTextFont_Medium" : "Kenshi_StandardFont_Medium", ["TextAlign"] = action.Length > 0 ? "Center" : align },
                background, colour ?? (painted ? title : ink), Pickable: pickable || action.Length > 0, Argument: argument, TextScale: scale));
        }
        Add("trade", new(x, y, w, h), skin: "Kenshi_GenericWindowSkin", background: new(0.12f, 0.11f, 0.09f, 1), pickable: true);
        Add("trade-header", new(x + 6, y + 6, w - 12, 38), skin: "Kenshi_GenericWindowHeaderSkin");
        Add("trade-title", new(x + 22, y + 12, w - 140, 28), $"TRADING WITH {trade.TraderName.ToUpperInvariant()}", painted: true, scale: 1.2f);
        Add("trade-close", new(x + w - 92, y + 12, 68, 28), "CLOSE", "Kenshi_Button1Skin", "meitou:trade.close", painted: true);

        float col = (w - 72) / 2;
        int rows = Math.Max(1, (int)((h - 170) / 30));
        Column("stock", trade.Stock, x + 24, $"SHOP   c.{Money(trade.ShopMoney)}", "meitou:buy", ref stockPage, buying: true);
        Column("goods", trade.Goods, x + 48 + col, $"YOU   c.{Money(trade.PlayerMoney)}", "meitou:sell", ref goodsPage, buying: false);
        Add("trade-footer", new(x + 24, y + h - 34, w - 48, 22),
            trade.BuysStolen ? "This trader buys stolen goods." : "Selling stolen goods here risks getting caught: a thief's skill and the item's owner decide the odds.", scale: 0.85f);

        void Column(string id, IReadOnlyList<TradeItemView> items, float cx, string heading, string action, ref int page, bool buying)
        {
            Add($"{id}-label", new(cx, y + 58, col, 26), heading, painted: true);
            Add($"{id}-rule", new(cx, y + 90, col, 1), background: new(0.43f, 0.38f, 0.29f, 1));
            int pages = Math.Max(1, (items.Count + rows - 1) / rows);
            page = Math.Clamp(page, 0, pages - 1);
            if (items.Count == 0) Add($"{id}-empty", new(cx, y + 104, col, 26), buying ? "Nothing for sale." : "Nothing to sell.");
            for (int i = 0; i < rows && page * rows + i < items.Count; i++)
            {
                var item = items[page * rows + i];
                float row = y + 100 + i * 30;
                bool risky = !buying && item.Stolen && !trade.BuysStolen;
                Add($"{id}-row-{i}", new(cx, row, col, 28), background: new(0.19f, 0.18f, 0.15f, i % 2 == 0 ? 0.9f : 0.5f));
                string name = item.Quantity > 1 ? $"{item.Name} x{item.Quantity}" : item.Name;
                if (item.Stolen) name += " (stolen)";
                Add($"{id}-name-{i}", new(cx + 6, row + 2, col - 210, 24), name, colour: risky ? warn : null, scale: 0.9f);
                Add($"{id}-price-{i}", new(cx + col - 200, row + 2, 90, 24), $"c.{Money(item.Price)}", align: "Right VCenter", scale: 0.9f);
                string arg = item.Index.ToString(CultureInfo.InvariantCulture);
                Add($"{id}-one-{i}", new(cx + col - 102, row + 2, 44, 24), "1", "Kenshi_Button1Skin", action, argument: arg + ":1", scale: 0.85f);
                if (item.Quantity > 1)
                    Add($"{id}-all-{i}", new(cx + col - 54, row + 2, 50, 24), "ALL", "Kenshi_Button1Skin", action, argument: arg + ":" + item.Quantity.ToString(CultureInfo.InvariantCulture), scale: 0.85f);
            }
            if (pages > 1)
            {
                float py = y + 100 + rows * 30 + 4;
                Add($"{id}-prev", new(cx, py, 40, 24), "<", "Kenshi_Button1Skin", "meitou:trade.page", argument: id + ":-1");
                Add($"{id}-pageno", new(cx + 48, py, 120, 24), $"{page + 1} / {pages}", scale: 0.85f);
                Add($"{id}-next", new(cx + 172, py, 40, 24), ">", "Kenshi_Button1Skin", "meitou:trade.page", argument: id + ":1");
            }
        }
    }

    static string Money(long cats) => cats.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A trade window button: "index:quantity" for buy and sell, "column:step" for the pages.</summary>
    void TradeAction(string action, string argument)
    {
        if (action == "trade.close") { Say(new CloseTrade { Tick = session.World.Tick }); return; }
        int colon = argument.IndexOf(':');
        if (colon < 0) return;
        string head = argument[..colon];
        int number = int.Parse(argument[(colon + 1)..], CultureInfo.InvariantCulture);
        if (action == "trade.page")
        {
            if (head == "stock") stockPage += number;
            else goodsPage += number;
            return;
        }
        if (session.CurrentSnapshot.Trade is not { } trade) return;
        int index = int.Parse(head, CultureInfo.InvariantCulture);
        if (action == "buy" && trade.Stock.FirstOrDefault(i => i.Index == index) is { } stock) Say(new BuyItem(stock.Token, number) { Tick = session.World.Tick });
        else if (action == "sell" && trade.Goods.FirstOrDefault(i => i.Index == index) is { } goods) Say(new SellItem(goods.Token, number) { Tick = session.World.Tick });
    }
}
