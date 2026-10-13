using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;

namespace Meitou.Simulation.Combat;

/// <summary>
/// Picking a technique, pure given the rolls (docs/game/combat.md "Technique choice, block and dodge"). The attacker-side choice was not read in the original (<b>Unknown</b>); the
/// rules here are the engine's, built on the record fields: weapon type flags, the disabled / block / dodge / prone / animal fields, the skill range, and the distance fields.
/// </summary>
public static class TechniqueChooser
{
    /// <summary>
    /// The attack techniques usable now, by weighted random pick on <c>chance</c> (<paramref name="roll"/> in [0, 1)); -1 when none. A technique is a candidate when it is not disabled, not a
    /// block or dodge, its weapon type flags include the wielder's, it is for the wielder's weapon category (<paramref name="category"/>, <see cref="Fits"/>), the standing / prone flag
    /// matches, the skill is within <c>min skill</c> to <c>max skill</c>, and the gap between the two bodies is within both the weapon's reach and the technique's distance: <c>attack distance min
    /// vs static</c> against a target that stands still, <c>attack distance</c> against one that moves (a negative distance means the technique is not for that case:
    /// "Cut left static" has -999 for a moving target, "Cut left" has -10 for a static one).
    /// </summary>
    public static int ChooseAttack(IReadOnlyList<CombatTechnique> all, WeaponKinds weaponKind, int category, bool prone, float skill, float gap, bool targetMoving, float weaponReach, float roll)
    {
        float total = 0;
        for (int i = 0; i < all.Count; i++) total += Weight(all[i], weaponKind, category, prone, skill, gap, targetMoving, weaponReach);
        if (total <= 0) return -1;
        float target = roll * total;
        int last = -1;
        for (int i = 0; i < all.Count; i++)
        {
            float w = Weight(all[i], weaponKind, category, prone, skill, gap, targetMoving, weaponReach);
            if (w <= 0) continue;
            last = i;
            if (target < w) return i;
            target -= w;
        }
        return last;
    }

    static float Weight(CombatTechnique t, WeaponKinds kind, int category, bool prone, float skill, float gap, bool moving, float weaponReach)
    {
        if (t.Disabled || t.IsBlock || t.IsDodge || !Fits(t, kind, category) || t.IsProne != prone) return 0;
        if (skill < t.MinSkill || skill > t.MaxSkill) return 0;
        float distance = Distance(t, moving);
        if (distance < 0 || gap > MathF.Min(distance, weaponReach)) return 0;
        return MathF.Max(t.Chance, 0);
    }

    /// <summary>The weapon types a technique is valid for, matched on the type bits (the "1 handed" flag is not tested).</summary>
    internal static bool FitsKind(CombatTechnique t, WeaponKinds kind) => (t.Kinds & kind & ~WeaponKinds.OneHanded) != 0;

    /// <summary>
    /// Whether a technique is for a fighter whose weapon category is <paramref name="category"/> (<see cref="Fighter.TechniqueCategory"/>: 5 for fists, 9 to 20 for a creature's natural
    /// weapon). The record's <c>animal</c> reads as the one weapon category the technique is for, 0 meaning "by the weapon type flags" (docs/game/animals.md: the martial arts techniques
    /// have 5 and the unarmed flag, the creature ones 9 to 20 and no flag at all, <b>Verified</b> over the records; the reading is the engine's). A creature (9 and up) takes only its
    /// own; anyone else the flagged ones (<c>animal</c> 0) of its kind and those of its exact category.
    /// </summary>
    internal static bool Fits(CombatTechnique t, WeaponKinds kind, int category) =>
        category >= FirstCreatureCategory ? t.Animal == category : t.Animal == 0 ? FitsKind(t, kind) : t.Animal == category;

    /// <summary>The first creature attack category (9, ELEPHANT).</summary>
    public const int FirstCreatureCategory = 9;

    /// <summary>
    /// The technique's distance for a moving or a standing target. A creature technique's 0 (most of them, for one case or both) reads as contact: <see cref="CombatTuning.CloseInGap"/>,
    /// the gap the pursuit closes to (an <b>engine choice</b>: read literally, a goat's only technique, 0 / 0, could never strike, nor a gorillo a moving target).
    /// </summary>
    static float Distance(CombatTechnique t, bool moving)
    {
        float d = moving ? t.AttackDistance : t.AttackDistanceMinVsStatic;
        return t.Animal != 0 && d == 0 ? CombatTuning.CloseInGap : d;
    }

    /// <summary>The distance the technique can strike at (gap between bodies), for the reach the attack is chosen at.</summary>
    public static float ReachOf(CombatTechnique t, bool targetMoving, float weaponReach) => MathF.Min(Distance(t, targetMoving), weaponReach);

    /// <summary>The front directions (1 down, 2 left, 3 right, 4 thrust, 5 up, 6 pierced, 0 default) and the rear ones (7 to 9) are two classes; a wrong-direction block stays in the blow's class.</summary>
    internal static bool SameClass(int a, int b) => (a >= 7) == (b >= 7);

    /// <summary>
    /// The defender's reaction (<c>FUN_140887970</c>, <b>Observed</b>), given the block roll and the pick roll. Candidates are the block or dodge techniques of the defender's weapon type,
    /// standing posture and creature kind. A fighter with a weapon that can block rolls to block: on success the candidates are the techniques whose direction equals the blow's (the
    /// base game's "Dodge back" rows are flagged as blocks too, so they are among them), on failure the block techniques of a different direction in the same class, never a dodge,
    /// so the blow passes. A fighter without one is dodge-capable only when the roll against <paramref name="dodgeChance"/> succeeds, and then picks among the dodge techniques. The pick is
    /// weighted on <c>chance</c>. -1 when there is no reaction.
    /// </summary>
    public static int ChooseReaction(IReadOnlyList<CombatTechnique> all, WeaponKinds weaponKind, int category, bool prone, bool canBlock, int blowDirection,
        float blockChancePercent, float dodgeChancePercent, float chanceRoll, float pickRoll)
    {
        ReactionPool pool;
        if (canBlock) pool = chanceRoll * 100 < blockChancePercent ? ReactionPool.Facing : ReactionPool.WrongDirection;
        else if (chanceRoll * 100 < dodgeChancePercent) pool = ReactionPool.Dodges;
        else return -1;
        float total = 0;
        for (int i = 0; i < all.Count; i++) total += ReactionWeight(all[i], weaponKind, category, prone, pool, blowDirection);
        if (total <= 0) return -1;
        float target = pickRoll * total;
        int last = -1;
        for (int i = 0; i < all.Count; i++)
        {
            float w = ReactionWeight(all[i], weaponKind, category, prone, pool, blowDirection);
            if (w <= 0) continue;
            last = i;
            if (target < w) return i;
            target -= w;
        }
        return last;
    }

    /// <summary>Which techniques a reaction is picked from.</summary>
    enum ReactionPool
    {
        /// <summary>A block roll that succeeded: the block techniques whose direction is the blow's.</summary>
        Facing,
        /// <summary>A block roll that failed: block techniques of another direction in the blow's class, never a dodge.</summary>
        WrongDirection,
        /// <summary>A fighter without a blocking weapon that rolled a dodge: the dodge techniques.</summary>
        Dodges,
    }

    static float ReactionWeight(CombatTechnique t, WeaponKinds kind, int category, bool prone, ReactionPool pool, int blowDirection)
    {
        if (t.Disabled || t.IsStumbleDodge || !(t.IsBlock || t.IsDodge) || !Fits(t, kind, category) || t.IsProne != prone) return 0;
        bool inPool = pool switch
        {
            ReactionPool.Facing => t.IsBlock && t.AttackDirection1 == blowDirection,
            ReactionPool.WrongDirection => t.IsBlock && !t.IsDodge && t.AttackDirection1 != blowDirection && SameClass(t.AttackDirection1, blowDirection),
            _ => t.IsDodge,
        };
        return inPool ? MathF.Max(t.Chance, 0) : 0;
    }
}
