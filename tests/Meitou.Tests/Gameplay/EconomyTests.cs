using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Economy;

namespace Meitou.Tests.Gameplay;

/// <summary>Item prices (docs/game/economy.md sections 3 and 4) on synthetic records, against the worked examples of section 4.6.</summary>
public class EconomyTests
{
    const uint New = 0x10;
    static readonly PriceConstants K = PriceConstants.BaseGame;

    internal static FcsRecord Rec(string id, FcsRecordType type, Action<FcsRecord>? fill = null)
    {
        var r = new FcsRecord { StringId = id, Flags = New, Name = id, RecordType = type };
        fill?.Invoke(r);
        return r;
    }

    internal static GameDatabase Db(params FcsRecord[] records)
    {
        var file = new FcsFile();
        file.Records.AddRange(records);
        var db = new GameDatabase();
        db.Apply(file, "test.mod");
        return db;
    }

    static FcsRecord Armour(string id, int cls, int material, int value, int slot) => Rec(id, FcsRecordType.ARMOUR, r =>
    {
        r.Ints["class"] = cls;
        r.Ints["material type"] = material;
        r.Ints["value"] = value;
        r.Ints["slot"] = slot;
        r.Floats["relative price mult"] = 1;
        r.References["part coverage"] = [new FcsReference("chest-t", 100, 0, 0)];
    });

    [Fact]
    public void Weapons_and_armour_match_the_worked_examples()
    {
        var db = Db(
            Rec("katana-t", FcsRecordType.WEAPON, r => r.Ints["value"] = 500),
            Rec("edgewalkers-t", FcsRecordType.WEAPON_MANUFACTURER, r => r.Floats["price mod"] = 1.4f),
            Armour("samurai-t", 3, 3, 100, 5),
            Armour("chain-t", 2, 2, 12000, 8));
        var katana = db.Find("katana-t")!;
        // 4.6: quality 0 / 25 / 50 / 75 / 100, raw and with the category row. The 100 may differ by one with the game's own expf.
        int[] raw = [8804, 16142, 43750, 71357, 78696], priced = [3521, 6456, 17500, 28542, 31478];
        for (int i = 0; i < 5; i++)
        {
            int slack = i == 4 ? 1 : 0;
            Assert.InRange(ItemValue.Of(db, katana, i * 25, false, K), raw[i] - slack, raw[i] + slack);
            Assert.InRange(ItemValue.Of(db, katana, i * 25, true, K), priced[i] - slack, priced[i] + slack);
        }
        Assert.Equal(43750, ItemValue.Of(db, katana, 50, false, K));
        Assert.Equal(17500, ItemValue.Of(db, katana, 50, true, K));
        // 500 x 1.4 is 700 in float (a double model gives 699 and 61162).
        var edge = db.Find("edgewalkers-t");
        Assert.Equal(61250, ItemValue.Of(db, katana, 50, false, K, edge));
        Assert.Equal(24500, ItemValue.Of(db, katana, 50, true, K, edge));

        var samurai = db.Find("samurai-t")!;
        Assert.Equal(100, ItemValue.Of(db, samurai, 0, false, K));   // quality 0 takes the record's value
        Assert.Equal(15450, ItemValue.Of(db, samurai, 50, false, K));
        Assert.Equal(60000, ItemValue.Of(db, samurai, 100, false, K));
        Assert.Equal(60, ItemValue.Of(db, samurai, 0, true, K));
        Assert.Equal(9270, ItemValue.Of(db, samurai, 50, true, K));
        Assert.Equal(36000, ItemValue.Of(db, samurai, 100, true, K));
        var chain = db.Find("chain-t")!;
        Assert.Equal(20587, ItemValue.Of(db, chain, 50, false, K));   // slot 8, x1.5 after the truncations
        Assert.Equal(12352, ItemValue.Of(db, chain, 50, true, K));
    }

    [Fact]
    public void Plain_items_crafted_items_and_the_swapped_rows()
    {
        var constants = Rec("c-t", FcsRecordType.CONSTANTS, r =>
        {
            r.Floats["global price mult"] = 2;
            r.Floats["robotics price mult"] = 3;
            r.Floats["crossbow price mult"] = 5;
        });
        var db = Db(
            constants,
            Rec("cloth-t", FcsRecordType.ITEM, r => r.Ints["value"] = 100),
            Rec("wheat-t", FcsRecordType.ITEM, r => { r.Ints["value"] = 10; r.Bools["food crop"] = true; }),
            Rec("relic-t", FcsRecordType.ITEM, r => { r.Ints["value"] = 777; r.Bools["artifact"] = true; }),
            Rec("bread-t", FcsRecordType.ITEM, r =>
            {
                r.Ints["value"] = 9999;   // ignored: the ingredients price it
                r.Floats["profitability"] = 0.2f;
                r.References["ingredients"] = [new FcsReference("cloth-t", 100, 0, 0), new FcsReference("wheat-t", 50, 0, 0)];
            }),
            Rec("bow-t", FcsRecordType.CROSSBOW, r => { r.Ints["value"] = 1000; r.Ints["value 1"] = 5000; }),
            Rec("arm-t", FcsRecordType.LIMB_REPLACEMENT, r => { r.Ints["value"] = 1000; r.Ints["value 1"] = 5000; }),
            Rec("box-t", FcsRecordType.CONTAINER, r => r.Ints["value"] = 321));
        var b = PriceConstants.BaseGame;
        Assert.Equal(60, ItemValue.Of(db, db.Find("cloth-t")!, 0, true, b));    // trade price mult, every plain item
        Assert.Equal(5, ItemValue.Of(db, db.Find("wheat-t")!, 0, true, b));     // food price mult, floor(x + 0.5)
        Assert.Equal(777, ItemValue.Of(db, db.Find("relic-t")!, 0, true, b));   // artifacts skip the row
        Assert.Equal(100, ItemValue.Of(db, db.Find("cloth-t")!, 0, false, b));
        // 60 x 1.0 x 1.2 = 72 and 5 x 0.5 x 1.2 = 3.
        Assert.Equal(75, ItemValue.Of(db, db.Find("bread-t")!, 0, true, b));
        Assert.Equal(321, ItemValue.Of(db, db.Find("box-t")!, 80, true, b));
        Assert.Equal(2000, ItemValue.Of(db, db.Find("bow-t")!, 50, false, b));   // lerp(0.25, 1000, 5000)

        // A CONSTANTS record: global pre-multiplies the category rows only; crossbows read the robotics row, limbs the crossbow row.
        var k = PriceConstants.FromDatabase(db);
        Assert.Equal(0.8f, k.SwordPriceMult, 6);
        Assert.Equal(0.5f, k.LootPriceMult, 6);
        Assert.Equal(2000 * 6, ItemValue.Of(db, db.Find("bow-t")!, 50, true, k));
        Assert.Equal(2000 * 10, ItemValue.Of(db, db.Find("arm-t")!, 50, true, k));
    }
}

/// <summary>The same against the installed game: the probe numbers of docs/game/economy.md.</summary>
[Slow]
public class EconomyInstallTests
{
    [Fact]
    public void The_base_games_price_block_items_and_shops_match_the_docs_probe()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        Assert.Equal(PriceConstants.BaseGame, PriceConstants.FromDatabase(db));   // section 3: the base values every fallback uses
        var katana = db.OfType(FcsRecordType.WEAPON).Single(r => r.Name == "Katana");
        Assert.Equal(500, katana.GetInt("value"));
        Assert.Equal(17500, ItemValue.Of(db, katana, 50, true, PriceConstants.BaseGame));
        var samurai = db.OfType(FcsRecordType.ARMOUR).First(r => r.Name == "Samurai Armour");
        Assert.Equal(9270, ItemValue.Of(db, samurai, 50, true, PriceConstants.BaseGame));
        var goods = db.OfType(FcsRecordType.VENDOR_LIST).Single(r => r.Name == "all trade goods");
        Assert.Equal(75, VendorList.From(db, goods).Entries.Count);   // 7.2: the items that get a market factor
        Assert.Contains(db.OfType(FcsRecordType.SQUAD_TEMPLATE), r => Meitou.Data.Gameplay.SquadTemplate.From(r).HasShop);
    }
}
