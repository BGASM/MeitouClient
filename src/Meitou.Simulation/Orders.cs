using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;

namespace Meitou.Simulation;

/// <summary>
/// The toggles of the HUD's orders panel (docs/game/ui-screens.md section 4), per character. Saved as the GAMESTATE_CHARACTER stance bools of the player's characters
/// (docs/formats/save.md) and the <c>stealth</c> bool; what each does here is in docs/simulation.md "Orders".
/// </summary>
[Flags]
public enum CharacterOrders : ushort
{
    None = 0,
    /// <summary>Defensive mode: +20 melee defence (the combat code's guarding state) and no attacks of its own (the button's tooltip, Verified).</summary>
    Block = 1,
    /// <summary>Hold position: answers an attack only within <see cref="Orders.HoldReach"/> and never walks after a target.</summary>
    Hold = 2,
    /// <summary>Chase mode: kept and saved; no effect yet.</summary>
    Chase = 4,
    /// <summary>Non-combat mode: does not answer attacks by itself (an ordered attack still goes).</summary>
    Passive = 8,
    /// <summary>Ranged mode: kept and saved; no effect yet.</summary>
    Ranged = 16,
    /// <summary>Taunt mode: kept and saved; no effect yet.</summary>
    Taunt = 32,
    /// <summary>Sneaking: slower, harder to see and to hear (docs/game/perception.md sections 3 and 5, docs/game/pathfinding.md "Sneaking").</summary>
    Sneak = 64,
    /// <summary>Enables automatic assigned jobs; direct player orders remain available.</summary>
    Jobs = 128,
}

/// <summary>Turns an order on, off (<paramref name="On"/>) or over (null) for the characters, or for the player's selection when the list is empty.</summary>
public sealed record OrdersCommand(IReadOnlyList<CharacterId> Characters, CharacterOrders Order, bool? On = null) : SimCommand;

/// <summary>
/// Steps the speed mode move orders use (the original's <c>cycle_run_speed</c> and the panel's speed arrows) by <paramref name="Step"/>, wrapping through walk, run and no cap;
/// every character takes the first one's new mode.
/// </summary>
public sealed record RunSpeedCommand(IReadOnlyList<CharacterId> Characters, int Step = 1) : SimCommand;

/// <summary>The orders' rules and their save keys.</summary>
public static class Orders
{
    /// <summary>How near an attacker must be for a holding character to answer it (<b>engine choice</b>: about a long weapon's reach; the original's rule is not traced).</summary>
    public const float HoldReach = 40;

    /// <summary>The GAMESTATE_CHARACTER bool of each saved order. Block = defensive and Passive = non combat follow the tooltips; Hold = <c>passive mode</c> is by elimination (<b>Unknown</b>).</summary>
    public static readonly (CharacterOrders Order, string Key)[] SaveKeys =
    [
        (CharacterOrders.Block, "defensive mode"), (CharacterOrders.Chase, "chase mode"), (CharacterOrders.Passive, "non combat mode"),
        (CharacterOrders.Hold, "passive mode"), (CharacterOrders.Ranged, "ranged mode"), (CharacterOrders.Taunt, "taunt mode"), (CharacterOrders.Sneak, "stealth"),
    ];

    public const string SpeedModeKey = "speed mode";
    // Meitou extension: the original's jobs-switch save key is not established.
    public const string JobsKey = "meitou jobs enabled";
    public const CharacterOrders PlayerDefaults = CharacterOrders.Jobs | CharacterOrders.Ranged;

    public static bool Has(CharacterCold? cold, CharacterOrders order) => cold is not null && (cold.Orders & order) != 0;

    /// <summary>Writes the orders and the run speed into a character's GAMESTATE_CHARACTER record.</summary>
    public static void Write(FcsRecord state, CharacterCold cold)
    {
        foreach (var (order, key) in SaveKeys) state.Bools[key] = (cold.Orders & order) != 0;
        state.Bools[JobsKey] = Has(cold, CharacterOrders.Jobs);
        state.Ints[SpeedModeKey] = cold.RunSpeed;
    }

    /// <summary>Reads them back; absent keys leave the character's defaults.</summary>
    public static void Read(FcsRecord state, CharacterCold cold)
    {
        foreach (var (order, key) in SaveKeys)
            if (state.Bools.TryGetValue(key, out bool on)) cold.Orders = on ? cold.Orders | order : cold.Orders & ~order;
        if (state.Bools.TryGetValue(JobsKey, out bool jobs)) cold.Orders = jobs ? cold.Orders | CharacterOrders.Jobs : cold.Orders & ~CharacterOrders.Jobs;
        if (state.Ints.TryGetValue(SpeedModeKey, out int mode) && mode is >= 0 and <= SpeedMode.Group) cold.RunSpeed = (byte)mode;
    }

    /// <summary>
    /// The stealth term s of a character in [0, 1] (<c>FUN_140884850</c>): its stealth skill x 0.75 while carrying someone, x 0.01. The original also multiplies in
    /// encumbrance, armour, race and injury factors; here only the limb multiplier of the stat is (<b>engine reduction</b>).
    /// </summary>
    public static float Stealth(CharacterCold cold)
    {
        if (cold.Stats is not { } stats) return 0;
        float s = stats[StatsEnumerated.Stealth] * stats.Scale * (cold.Medical?.StatMultiplier(StatsEnumerated.Stealth) ?? 1);
        if (!cold.Carrying.IsNone) s *= 0.75f;
        return Math.Clamp(s * 0.01f, 0, 1);
    }

    /// <summary>
    /// The sneaking speed (docs/game/pathfinding.md "Sneaking", Observed): X = speed - 11, X = min(X, lerp(12, X, clamp((s - 0.1) / 0.8))), then X + 11, at least 11.
    /// A clumsy sneaker creeps at 23 units a second; from stealth 90 on the cap is gone.
    /// </summary>
    public static float SneakSpeed(float speed, float stealth)
    {
        float x = speed - 11;
        float t = Math.Clamp((stealth - 0.1f) / 0.8f, 0, 1);
        x = MathF.Min(x, 12 + (x - 12) * t);
        return MathF.Max(x + 11, 11);
    }

    /// <summary>
    /// The sneaking target's view factor (docs/game/perception.md section 3, Verified constants): v x 0.75 when v is at least 0.5, else x 0.5, then x lerp(s, 1, 0.6).
    /// The original's stealth detection that builds up over checks (section 4, step 5) is not modelled: a target that passes this test is seen at once.
    /// </summary>
    public static float SneakView(float v, float stealth) => v * (v >= 0.5f ? 0.75f : 0.5f) * (1 + (0.6f - 1) * stealth);

    /// <summary>A sneaking mover's noise range: lerp(s, 450, 80) (section 5, Verified constants; clear weather).</summary>
    public static float SneakNoise(float stealth) => 450 + (80 - 450) * stealth;

    /// <summary>The chance to hear a sneaking target at <paramref name="distance"/>: 0.1 x (1 - s) x (1 - d^2 / N^2) (section 5; the 0.05 squad case is not modelled).</summary>
    public static float SneakHearChance(float distance, float noise, float stealth) =>
        noise > 0 && distance < noise ? 0.1f * (1 - stealth) * (1 - distance * distance / (noise * noise)) : 0;

    /// <summary>Whether a holding character at <paramref name="at"/> may answer an attacker at <paramref name="attacker"/>.</summary>
    public static bool InHoldReach(Vector3 at, Vector3 attacker)
    {
        float dx = attacker.X - at.X, dz = attacker.Z - at.Z;
        return dx * dx + dz * dz <= HoldReach * HoldReach;
    }

    /// <summary>Applies the order commands (the player system's inputs).</summary>
    internal static void Apply(World world, SimCommand command)
    {
        var table = world.Characters;
        switch (command)
        {
            case OrdersCommand set:
                foreach (var id in set.Characters.Count > 0 ? set.Characters : world.Player.Selection.ToArray())
                {
                    if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true } cold) continue;
                    bool on = set.On ?? (cold.Orders & set.Order) == 0;
                    cold.Orders = on ? cold.Orders | set.Order : cold.Orders & ~set.Order;
                }
                break;
            case RunSpeedCommand speed:
                int? mode = null;
                foreach (var id in speed.Characters.Count > 0 ? speed.Characters : world.Player.Selection.ToArray())
                {
                    if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true } cold) continue;
                    mode ??= ((cold.RunSpeed + speed.Step) % SpeedMode.Count + SpeedMode.Count) % SpeedMode.Count;
                    cold.RunSpeed = (byte)mode.Value;
                }
                break;
        }
    }
}
