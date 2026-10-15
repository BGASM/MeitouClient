using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Saving;

/// <summary>
/// A saved character's kit both ways (docs/simulation.md "Saves"): the INVENTORY_ITEM_STATEs of its inventory into an <see cref="Inventory"/> and back, and the worn
/// items with its CHARACTER_APPEARANCE into the <see cref="Loadout"/> its look is drawn from.
/// </summary>
public static class SavedKit
{
    /// <summary>The sections a worn ARMOUR sits in (the save's names; docs/formats/save.md).</summary>
    static readonly HashSet<string> Worn = new(StringComparer.Ordinal) { "head", "shirt", "armour", "legs", "boots" };

    /// <summary>
    /// The items as an inventory, at their saved sections and cells. Items whose record the game data lacks are left out and named in <paramref name="notes"/>. The main
    /// grid is 8 wide and as tall as the saved items need, at least 10 (the vanilla screenshot's pocket grid, <b>Observed</b>, docs/game/ui-screens.md).
    /// </summary>
    public static Inventory ToInventory(IReadOnlyList<SaveItem> items, ItemFactory factory, List<string>? notes = null)
    {
        int height = Inventory.MainHeight;
        foreach (var s in items)
            if (s.Section == "main" && factory.Info(s.BaseId) is { } info) height = Math.Max(height, s.Y + Math.Max(info.Height, 1));
        var inventory = new Inventory(Inventory.MainWidth, height);
        Fill(inventory, items, factory, notes);
        return inventory;
    }

    static void Fill(Inventory inventory, IReadOnlyList<SaveItem> items, ItemFactory factory, List<string>? notes)
    {
        foreach (var s in items)
        {
            if (factory.Create(s.BaseId, s.Quantity, s.Quality) is not { } item)
            {
                notes?.Add($"item {s.BaseId} is not in the game data; left out");
                continue;
            }
            item.Section = s.Section;
            item.X = s.X;
            item.Y = s.Y;
            item.Level = s.Level;
            item.MaterialId = s.MaterialId;
            item.CompanyId = s.CompanyId;
            item.Charges = s.Charges;
            item.Saved = s.Source;
            if (s.Contents is { } contents && item.Contents is { } bag) Fill(bag, contents, factory, notes);
            inventory.Items.Add(item);
        }
    }

    /// <summary>The inventory as saved items, a container with its contents; each keeps the record it was read from (<see cref="ItemInstance.Saved"/>).</summary>
    public static List<SaveItem> FromInventory(Inventory inventory) => [.. inventory.Items.Select(FromItem)];

    static SaveItem FromItem(ItemInstance i) => new()
    {
        BaseId = i.Record, MaterialId = i.MaterialId, CompanyId = i.CompanyId, Section = i.Section, X = i.X, Y = i.Y, Quantity = i.Quantity, Level = i.Level,
        ItemFunction = i.ItemFunction, Quality = i.Quality, Charges = i.Charges, Source = i.Saved,
        Contents = i.Contents is { } bag ? FromInventory(bag) : null,
    };

    /// <summary>
    /// The loadout a saved character is drawn from: its CHARACTER_APPEARANCE (race, sex, head, hair, beard, skin, sliders: the body-file keys) and what it wears: ARMOUR in the
    /// clothing sections, the backpack, the weapons and crossbow at <c>hip</c> and <c>back</c> (company = manufacturer, material = model). Null without an appearance record
    /// or a race. <paramref name="seed"/> only names it (nothing is rolled).
    /// </summary>
    public static Loadout? LoadoutOf(FcsRecord? appearance, IReadOnlyList<SaveItem> items, GameDatabase db, int seed)
    {
        if (appearance is null) return null;
        var file = new AppearanceFile { Record = appearance };
        if (file.RaceId is not { } raceId || db.Find(raceId) is not { } race) return null;
        var loadout = new Loadout
        {
            Seed = seed, Race = race, Female = file.Female, Appearance = file, Generated = true,
            MorphIndex = appearance.Ints.GetValueOrDefault("morph index", -1),
        };
        foreach (var s in items)
        {
            if (db.Find(s.BaseId) is not { } record) continue;
            GameRecord? Find(string id) => id.Length > 0 ? db.Find(id) : null;
            if (record.Type == FcsRecordType.ARMOUR && Worn.Contains(s.Section))
                loadout.Clothing.Add(new LoadoutItem(record) { Quality = (int)s.Quality, Material = Find(s.MaterialId) });
            else if (s.Section == "backpack_attach")
                loadout.Backpack = new LoadoutItem(record) { Quality = (int)s.Quality };
            else if (s.Section is "hip" or "back" && record.Type is FcsRecordType.WEAPON or FcsRecordType.CROSSBOW)
            {
                var weapon = new LoadoutWeapon(record) { Section = s.Section, Row = s.Y, Level = s.Level, Manufacturer = Find(s.CompanyId), Model = Find(s.MaterialId) };
                if (record.Type == FcsRecordType.CROSSBOW) loadout.Crossbow ??= weapon;
                else loadout.Weapons.Add(weapon);
            }
        }
        // Equip order: hip first, then back (the first weapon is the one a fight draws).
        loadout.Weapons.Sort((a, b) => string.CompareOrdinal(a.Section == "hip" ? "0" : "1", b.Section == "hip" ? "0" : "1"));
        return loadout;
    }
}
