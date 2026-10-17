namespace Meitou.Simulation;

/// <summary>
/// The constants mixed into the seed keys of the population's random streams, named in one place so two streams never share one by accident.
/// Changing any value re-rolls what it keys (and every state hash that follows), so they are pinned by the golden hashes.
/// </summary>
internal static class SeedSalts
{
    /// <summary>XORed into the key of a bar squad (a resident squad's key has none).</summary>
    public const ulong BarSquad = 0xBA45UL;
    /// <summary>XORed with the hash of the start-off's id into the player's start key.</summary>
    public const ulong PlayerStart = 0x57A27UL;
    /// <summary>XORed into the tick of a roaming squad's choice of its next town.</summary>
    public const ulong RoamNext = 0x7A6E7UL;
    /// <summary>XORed into the key of a roaming squad a town's pool makes.</summary>
    public const ulong RoamingPool = 0x20A4UL;
    /// <summary>Added to the count of slave squads in the key of the next slave squad.</summary>
    public const ulong SlaveSquad = 0x5A1AUL;

    /// <summary>XORed into the key of a homeless squad of an area sector (6.2).</summary>
    public const ulong HomelessSquad = 0x5EC7UL;
    /// <summary>XORed into the key of a squad a nest makes (6.3).</summary>
    public const ulong NestSquad = 0x4E57UL;
    /// <summary>XORed into the key of the layout of the nests of a biome group (which nest, which cell, which spots).</summary>
    public const ulong NestLayout = 0x1A70UL;

    /// <summary>Counter of the roll that picks the template of a homeless squad.</summary>
    public const ulong HomelessPick = 6;
    /// <summary>Counter of the roll that picks the template of a nest's squad.</summary>
    public const ulong NestPick = 7;
    /// <summary>Counter base of the placement tries of a wild squad (the try is added).</summary>
    public const ulong WildPlace = 2000;

    /// <summary>Counter (under <see cref="RngPurpose.Spawn"/>) of the roll that decides whether a bar squad is present.</summary>
    public const ulong BarChance = 77;
    /// <summary>Counter of the roll that picks which squad template a town's roaming pool draws.</summary>
    public const ulong RoamingPick = 5;
    /// <summary>Counter base of the roll for the look of member n of a town squad (n is added).</summary>
    public const ulong MemberLook = 1000;
    /// <summary>Counter base of the roll for the look of member n of the player's squad (n is added).</summary>
    public const ulong PlayerLook = 5000;
    /// <summary>Counters (under <see cref="RngPurpose.Spawn"/>, keyed by the character's id) of a new NPC's bounty roll and its fuzz (<see cref="Crime.SpawnBounties"/>).</summary>
    public const ulong BountyChance = 0xB0C7UL, BountyFuzz = 0xB0F2UL;
    /// <summary>The AI's patrol timer and route (<see cref="Ai.AiSystem"/>).</summary>
    public const ulong AiPatrol = 0x5041UL;
    /// <summary>A platoon's short-range patrol point off screen (<see cref="Ai.PlatoonJobs"/>).</summary>
    public const ulong PlatoonPatrol = 0x5050UL;

    /// <summary>Town residents in their buildings (<c>PopulationHomes.cs</c>): counter base of the shuffle of a town's buildings, the counter of the node a squad is
    /// made at, counter base of the housemate counts, and the salt XORed into a housed squad's key.</summary>
    public const ulong HomeOrder = 0x40DE00UL, HomeSpot = 0x40DE5UL, Housemates = 0x40A7E00UL, ResidentHome = 0x2E51DUL;
}
