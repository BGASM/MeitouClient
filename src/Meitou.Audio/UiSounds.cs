using Meitou.Data;
using Meitou.Data.Fcs;

namespace Meitou.Audio;

/// <summary>
/// One interface sound (docs/audio.md "Interface"): an event on the GUI object, with the switch (or state) the game sets first. <see cref="Item"/> is the record id of an inventory
/// item whose <c>Inventory_Items</c> value the host looks up (<see cref="UiSounds.ItemSound"/>).
/// </summary>
public readonly record struct UiSoundEvent(string Event, string? Group = null, string? Value = null, bool State = false, string? Item = null);

/// <summary>
/// Plays the interface's sounds on the GUI object, 110 (<b>Verified</b>: every handler of the exe posts on it; <c>FUN_140257d00</c> and the handlers listed in docs/audio.md "Interface").
/// Unconditional like the original's handlers; the callers decide when.
/// </summary>
public sealed class UiSounds(WwisePlayer player, Func<GameDatabase?> database)
{
    /// <summary>The game object of the interface.</summary>
    public const ulong GuiObject = 110;

    static readonly string[] Weapon = ["Weapon_Metal", "Weapon_Wood"];
    static readonly string[] Armour = ["Fabric", "Leather", "Robotic_Component", "Steel_Bar"];
    static readonly string[] Items =
    [
        "Backpack", "Building_Material", "Fabric", "Flour", "Food", "Kits", "Leather", "Luxury", "Narcotics", "Ore", "Potato", "Robotic_Component", "Rum", "Steel_Bar",
        "Tools", "Water", "Wheat", "Armor_Plating", "Blueprints", "Meat",
    ];

    /// <summary>Events posted since the start.</summary>
    public int Posted { get; private set; }

    public void Play(in UiSoundEvent e)
    {
        if (e.Group is { } group && e.Value is { } value)
        {
            if (e.State) player.SetState(group, value);
            else player.SetSwitch(group, value, GuiObject);
        }
        else if (e.Item is { } item && ItemSound(item) is { } sound) player.SetSwitch("Inventory_Items", sound, GuiObject);
        player.Post(e.Event, GuiObject);
        Posted++;
    }

    /// <summary>
    /// The <c>Inventory_Items</c> value of an item (<b>Verified</b>, <c>FUN_14075e360</c> and its tables): a WEAPON or CROSSBOW by <c>material</c> (Weapon_Metal, Weapon_Wood), ARMOUR by <c>material type</c>
    /// (Fabric, Leather, Robotic_Component, Steel_Bar), ITEM, CONTAINER, NEST_ITEM and MAP_ITEM by <c>inventory sound</c> (Backpack to Meat), LIMB_REPLACEMENT Robotic_Component; null for any other record.
    /// </summary>
    public string? ItemSound(string recordId)
    {
        if (database()?.Find(recordId) is not { } r) return null;
        static string? Of(string[] names, int i) => (uint)i < names.Length ? names[i] : null;
        return r.Type switch
        {
            FcsRecordType.WEAPON or FcsRecordType.CROSSBOW => Of(Weapon, r.GetInt("material")),
            FcsRecordType.ARMOUR => Of(Armour, r.GetInt("material type")),
            FcsRecordType.ITEM or FcsRecordType.CONTAINER or FcsRecordType.NEST_ITEM or FcsRecordType.MAP_ITEM => Of(Items, r.GetInt("inventory sound")),
            FcsRecordType.LIMB_REPLACEMENT => "Robotic_Component",
            _ => null,
        };
    }
}
