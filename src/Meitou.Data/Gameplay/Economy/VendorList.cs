using Meitou.Data.Fcs;

namespace Meitou.Data.Gameplay.Economy;

/// <summary>One thing a vendor list can give: an item record and its relative chance.</summary>
public readonly record struct VendorEntry(string Id, FcsRecordType Type, int Weight);

/// <summary>
/// A VENDOR_LIST (docs/game/economy.md sections 9.2 and 13): the weighted items a shop, a container or a nest's loot is rolled from, and the money roll.
/// <para>The names of its reference lists are not written down in the docs (<c>items</c>, <c>weapons</c>, <c>clothing</c>, ...), so every list is read and its entries kept by the
/// type of the record they point at: item kinds become <see cref="Entries"/>, WEAPON_MANUFACTURERs <see cref="Manufacturers"/>. The game merges a list's entries into one weighted
/// table (the item-group picker of factions-squads-towns.md section 8, <b>Observed</b>). The weight is the entry's v0, "relative chance" in the editor; a v0 of 0 counts as 100 as in the
/// generic weighted pick (the vendor lists' own reading of 0 is <b>Unknown</b>, docs/game/economy.md Unknowns: 472 entries are (0, 100, 0)).</para>
/// </summary>
public sealed record VendorList
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary><c>items count</c>: the number of items a caller that asks for "the list's own count" takes (40 when below 0).</summary>
    public int ItemsCount { get; init; }
    /// <summary><c>money item prob</c>: compared with a roll in [0, 1), so the data's percent-looking values of 1 and more always give money (section 13, <b>Verified</b>).</summary>
    public float MoneyItemProb { get; init; }
    public int MoneyItemMin { get; init; }
    public int MoneyItemMax { get; init; }
    /// <summary>The items, weapons, armour, crossbows, limbs, containers, maps and blueprints of every list, in the record's order.</summary>
    public IReadOnlyList<VendorEntry> Entries { get; init; } = [];
    /// <summary>The WEAPON_MANUFACTURERs a weapon from the list may be made by (v0 the weight, 0 counting as 100).</summary>
    public IReadOnlyList<RecordLink> Manufacturers { get; init; } = [];

    /// <summary>The record types a vendor list hands out as items.</summary>
    public static bool IsItemType(FcsRecordType t) => t is FcsRecordType.ITEM or FcsRecordType.WEAPON or FcsRecordType.ARMOUR or FcsRecordType.CROSSBOW
        or FcsRecordType.LIMB_REPLACEMENT or FcsRecordType.CONTAINER or FcsRecordType.MAP_ITEM or FcsRecordType.BLUEPRINT;

    public static VendorList From(GameDatabase db, GameRecord r)
    {
        var entries = new List<VendorEntry>();
        var makers = new List<RecordLink>();
        foreach (string list in r.ReferenceLists)
            foreach (var e in r.GetReferences(list))
            {
                if (db.Find(e.TargetStringId) is not { } target) continue;
                if (IsItemType(target.Type)) entries.Add(new VendorEntry(target.StringId, target.Type, e.Values.Value0 == 0 ? 100 : e.Values.Value0));
                else if (target.Type == FcsRecordType.WEAPON_MANUFACTURER) makers.Add(RecordLink.From(e));
            }
        int count = RecordReading.Int(r, "items count", 40);
        return new VendorList
        {
            Id = r.StringId,
            Name = r.Name,
            ItemsCount = count < 0 ? 40 : count,
            MoneyItemProb = RecordReading.Float(r, "money item prob", 0),
            MoneyItemMin = RecordReading.Int(r, "money item min", 0),
            MoneyItemMax = RecordReading.Int(r, "money item max", 0),
            Entries = entries,
            Manufacturers = makers,
        };
    }
}
