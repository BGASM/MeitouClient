using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Combat;

/// <summary>A crossbow in a character's weapon slot: its record and its numbers at the item's quality (docs/game/combat.md "Ranged and turrets").</summary>
public sealed class CrossbowInstance(CrossbowData data, float quality, CrossbowStats stats)
{
    public CrossbowData Data { get; } = data;
    /// <summary>The quality fraction (0 to 1) the stats were lerped at: the item's level x 0.01.</summary>
    public float Quality { get; } = quality;
    public CrossbowStats Stats { get; } = stats;
    /// <summary>Shots one reload fills the crossbow with (<c>num shots</c>).</summary>
    public int NumShots => Math.Max(Data.NumShots, 1);
    /// <summary>No <c>ammo</c> reference: the crossbow never runs dry.</summary>
    public bool InfiniteAmmo => Data.AmmoId.Length == 0;

    /// <summary>The gun of a CROSSBOW at quality fraction <paramref name="q"/> with the CONSTANTS <c>bow damage 1/99</c> and the global damage multiplier (<c>FUN_14043c180</c>).</summary>
    public static CrossbowInstance Create(float q, CrossbowData data, CombatConstants constants, float globalDamageMultiplier) =>
        new(data, q, data.At(q, constants, globalDamageMultiplier));
}

/// <summary>
/// The formulas of the crossbow (docs/game/combat.md "Ranged and turrets"; all <b>Observed</b> in the decompile of <c>kenshi_x64.exe</c> unless the doc says otherwise).
/// Pure functions, tested against the doc.
/// </summary>
public static class RangedFormulas
{
    /// <summary>Seconds the aim must be held before a shot, reset after every shot, every reload and whenever the gun is not ready (<c>FUN_140437ad0</c>).</summary>
    public const float SteadyAim = 0.4f;
    /// <summary>The bleed multiplier of a bolt's damage packet (a constant 2, <c>FUN_140439c90</c>).</summary>
    public const float BoltBleed = 2;
    /// <summary>Degrees per second a spent bolt's nose pitches down once its range is used up (<c>FUN_1404380e0</c>).</summary>
    public const float DropDegreesPerSecond = 8;
    /// <summary>A bolt is dropped this many units past its range (<c>FUN_1404380e0</c>).</summary>
    public const float DropAfter = 5000;
    /// <summary>A terrain hit whose |cos| between the surface normal and the flight is below this grazes off and the bolt is dropped (<c>FUN_1404380e0</c>).</summary>
    public const float Grazing = 0.1f;
    /// <summary>XP rates of the ranged controller (<c>FUN_1408c7250</c>'s per-stat amounts, x dt): crossbows 1.2, perception 3.0, dexterity 0.5 while reloading.</summary>
    public const float CrossbowXp = 1.2f, PerceptionXp = 3f, DexterityReloadXp = 0.5f;
    /// <summary>FRIENDLY_FIRE XP (<c>FUN_1408c7310</c>, dt 1): a bolt that passed a friend, and one that hit a friend.</summary>
    public const float FriendlyPassXp = 20, FriendlyHitXp = 100;

    static float Lerp(float t, float a, float b) => a + (b - a) * t;

    /// <summary>
    /// The general skill blend <c>FUN_140885ce0</c>: 0.01 x (0.6 x skill + 0.4 x dexterity x m), with both stats already times their injury multipliers and m the stats
    /// multiplier (<c>CharStats +0x34</c>, taken as the gear's dexterity product here). The reload uses it with the crossbow skill.
    /// </summary>
    public static float SkillBlend(float skill, float dexterity, float m = 1) => 0.01f * (0.6f * skill + 0.4f * dexterity * m);

    /// <summary>Reload seconds (<c>FUN_140436010</c>): clamp(lerp(s, reload max, reload min), reload min, reload max). Spring Bat's max above its min at high quality is clamped the same way.</summary>
    public static float ReloadTime(float s, float reloadMin, float reloadMax)
    {
        float lo = MathF.Min(reloadMin, reloadMax), hi = MathF.Max(reloadMin, reloadMax);
        return Math.Clamp(Lerp(s, reloadMax, reloadMin), lo, hi);
    }

    /// <summary>
    /// The half-angle of the deviation cone in degrees (<c>FUN_14043a730</c>, the same <c>0.005 x (skill + perception)</c> as <c>FUN_140435160</c> and the turret tooltip, <b>Verified</b>):
    /// max(0, deviation x (1 - t)), t = 0.005 (skill + perception). 0 at skill + perception 200.
    /// </summary>
    public static float Deviation(float deviationAtZero, float skill, float perception) => MathF.Max(0, Lerp(0.005f * (skill + perception), deviationAtZero, 0));

    /// <summary>
    /// The height factor (<c>FUN_140435f60</c>): f = (|dy| - 20) / 280 capped at 1 (not floored, so a small |dy| gives a small negative f); above the target (dy &gt; 0)
    /// 1 + 0.5 f, level or below 1 - 0.4 f; 1 for a target at height exactly 0 (a guard in the code). dy = barrel height - target height.
    /// </summary>
    public static float HeightFactor(float barrelY, float targetY)
    {
        if (targetY == 0) return 1;
        float dy = barrelY - targetY;
        float f = MathF.Min(1, (MathF.Abs(dy) - 20) / 280);
        return dy > 0 ? 1 + 0.5f * f : 1 - 0.4f * f;
    }

    /// <summary>In range (<c>FUN_140436860</c>): squared distance &lt; h x range^2 (the factor multiplies the squared range, unlike the bolt's own range: a quirk kept).</summary>
    public static bool InRange(float distanceSquared, float range, float heightFactor) => distanceSquared < heightFactor * range * range;

    /// <summary>The flight of one bolt of a crossbow (R = W = 1): its damage from a roll w in [0, 1), its range and its speed (<c>FUN_14043a730</c> step 7).</summary>
    public static (float Damage, float Range, float Speed) Bolt(in CrossbowStats stats, float heightFactor, float w) =>
        ((stats.PierceMin + w * (stats.PierceMax - stats.PierceMin)) * heightFactor, stats.Range * heightFactor, stats.ShotSpeed * (0.8f * heightFactor + 0.2f));

    /// <summary>The chance a bolt passes a friend in its way (<c>FUN_14043a730</c> step 8): min(1, FRIENDLY_FIRE x 0.015).</summary>
    public static float FriendlyAvoidance(float friendlyFire) => MathF.Min(1, MathF.Max(0, friendlyFire) * 0.015f);

    /// <summary>
    /// A direction tilted off <paramref name="dir"/> (unit) by <paramref name="degrees"/> about an axis perpendicular to it, turned by <paramref name="spin"/> (0 to 1 of a turn) round
    /// <paramref name="dir"/> (Ogre's <c>Vector3::randomDeviant</c>). With a tilt of u x D degrees for a uniform u the shots fill a cone of half-angle D, denser in the middle.
    /// </summary>
    public static Vector3 Deviate(Vector3 dir, float degrees, float spin)
    {
        if (degrees <= 0) return dir;
        // Any vector perpendicular to dir, turned round dir by the spin, is the axis of the tilt.
        var helper = MathF.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var axis = Vector3.Normalize(Vector3.Cross(dir, helper));
        axis = Vector3.Transform(axis, Quaternion.CreateFromAxisAngle(dir, spin * MathF.Tau));
        return Vector3.Normalize(Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(axis, degrees * (MathF.PI / 180))));
    }
}

/// <summary>Finding and using up a crossbow's ammunition in an inventory: the main inventory first, then the worn backpack (<c>CharacterAnimal</c> slot 47, <c>FUN_1405cb5b0</c>).</summary>
public static class Ammunition
{
    /// <summary>The first item of record <paramref name="ammoId"/>: in the character's own sections, then in the backpack's contents; null when there is none.</summary>
    public static ItemInstance? Find(Inventory? inventory, string ammoId)
    {
        if (inventory is null || ammoId.Length == 0) return null;
        foreach (var i in inventory.Items)
            if (i.Record == ammoId && i.Type != FcsRecordType.CONTAINER) return i;
        if (inventory.Backpack?.Contents is { } bag)
            foreach (var i in bag.Items)
                if (i.Record == ammoId) return i;
        return null;
    }

    /// <summary>
    /// Uses one charge (one bolt) of the first matching item (<c>FUN_140437ce0</c>: charges minus 1, the item removed at 0). A stack of several units refills from
    /// <see cref="ItemInstance.MaxCharges"/> and loses one unit instead (<b>engine</b>: how charges and stacks combine is <b>Unknown</b>). False when there is none.
    /// </summary>
    public static bool UseOne(Inventory? inventory, string ammoId)
    {
        if (Find(inventory, ammoId) is not { } item) return false;
        item.Charges -= 1;
        if (item.Charges > 0) return true;
        if (item.Quantity > 1)
        {
            item.Quantity--;
            item.Charges = item.MaxCharges > 0 ? item.MaxCharges : 1;
        }
        else inventory!.RemoveOne(item);
        return true;
    }

    /// <summary>The bolts left: the charges of every matching item and backpack item (stacks count their full units).</summary>
    public static int Count(Inventory? inventory, string ammoId)
    {
        if (inventory is null || ammoId.Length == 0) return 0;
        float n = 0;
        foreach (var i in inventory.All())
            if (i.Record == ammoId) n += i.Charges + (i.Quantity - 1) * MathF.Max(i.MaxCharges, 1);
        return (int)n;
    }
}
