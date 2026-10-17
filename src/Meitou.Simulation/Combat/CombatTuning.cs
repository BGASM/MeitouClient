namespace Meitou.Simulation.Combat;

/// <summary>Timing constants of the duel loop: the verified ones say so, the rest are engine choices where the research stops (docs/simulation.md "Combat").</summary>
public static class CombatTuning
{
    /// <summary>Seconds a fighter waits before its first attack after being ordered (a uniform draw between the two; engine choice).</summary>
    public const float PauseMin = 0.15f, PauseMax = 0.6f;
    /// <summary>
    /// Seconds a fighter waits after its attack by how its last blow ended (<b>Verified</b>, FUN_14060a2f0, docs/game/combat.md "Pacing"): 0.6 after a hit, 0.3 blocked,
    /// 0.2 dodged; a miss (not in the original's list) waits as a dodge (engine choice). A hit that finds no reaction clip still cancels the swing and waits <see cref="InterruptPause"/>.
    /// </summary>
    public const float HitPause = 0.6f, BlockedPause = 0.3f, DodgedPause = 0.2f, MissedPause = 0.2f, InterruptPause = 0.2f;
    /// <summary>Seconds between a staggered fighter's chances to break out of its hit reaction into a block (<b>Verified</b>, the original's +0x124).</summary>
    public const float BreakOutSeconds = 1.2f;
    /// <summary>Seconds without any defence after a defence roll found nothing (<b>Verified</b>, the original's +0x14c), and the wait after a block ends with nothing coming (+0x148, 0.2 s).</summary>
    public const float NoDefenceSeconds = 1.2f, AfterBlockPause = 0.2f;
    /// <summary>A swing is given up to defend while it is under this far through and a blow coming at the fighter is past this far (<b>Verified</b>, FUN_1406083d0(0.5, 0.5)).</summary>
    public const float AbortOwnProgress = 0.5f, AbortIncomingProgress = 0.5f;
    /// <summary>Seconds a fighter that lost the initiative roll leaves its opponent free to swing at it (engine choice; the original sets the opponent to attack at once).</summary>
    public const float YieldSeconds = 1;
    /// <summary>How far (world units) a defender looks for blows coming at it.</summary>
    public const float ReactionScanRadius = 60;
    /// <summary>A blow lands if the gap between the two bodies is still within the reach it started with plus this slack; else it misses.</summary>
    public const float ReachSlack = 6;
    /// <summary>Seconds a clip plays at when its length is not known.</summary>
    public const float DefaultClipSeconds = 1;
    /// <summary>A fighter closes in until the gap between bodies is this (units), when it moves by itself.</summary>
    public const float CloseInGap = 3;

    /// <summary>The footprint radius of a human, used when a character has no race data.</summary>
    public const float DefaultFootprint = 4;

    /// <summary>The radius a body counts with when the gap between two bodies is taken: the race's <c>pathfind footprint radius</c>.</summary>
    internal static float Footprint(CharacterCold? c) => c?.Race?.PathfindFootprintRadius ?? DefaultFootprint;
}
