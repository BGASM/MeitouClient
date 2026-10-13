using Meitou.Data.Fcs;

namespace Meitou.Data.Gameplay.Bodies;

/// <summary>
/// The creature fields of an ANIMAL_CHARACTER record (docs/game/animals.md). The race, the stats groups (<c>combat stats</c>, ...) and <c>stats randomise</c> are read
/// the way a CHARACTER's are; this holds what only animals have. Field names <b>Verified</b> against the install's 43 records; what the game does with each is in the doc.
/// </summary>
public sealed record AnimalData
{
    public required string StringId { get; init; }
    /// <summary>The first <c>weapon</c> reference: the natural weapon (a WEAPON record of a creature skill category, 9 to 20); null for the few animals without one.</summary>
    public string? WeaponId { get; init; }
    /// <summary>The first <c>weapon level</c> reference: a WEAPON_MANUFACTURER (Ancient for nearly all).</summary>
    public string? WeaponLevelId { get; init; }
    /// <summary><c>animal strength</c> (int; <c>CharStatsAnimal</c> +0x230). Its consumer was not found (<b>Unknown</b>); kept, not used.</summary>
    public int AnimalStrength { get; init; }
    /// <summary><c>HP mult</c>, 1 when 0 or below (<b>Observed</b>, <c>FUN_14062f000</c>): the character's slot-0x238 scale (part body scale and blood capacity).</summary>
    public float HpMult { get; init; } = 1;
    /// <summary><c>scale min</c> / <c>scale max</c>: the size the animal has at age 0 and 1 (slot 0x390 = lerp(age, min, max)).</summary>
    public float ScaleMin { get; init; } = 1;
    public float ScaleMax { get; init; } = 1;
    /// <summary><c>lifespan</c>, <c>smell blood</c>, <c>smell eggs</c>, <c>turn rate</c>: read and kept; not used yet.</summary>
    public float Lifespan { get; init; }
    public float SmellBlood { get; init; }
    public float SmellEggs { get; init; }
    public float TurnRate { get; init; }

    /// <summary>The animal's size at <paramref name="age"/> (<c>CharacterAnimal</c> slot 0x390, <b>Observed</b>): lerp(age, scale min, scale max), not clamped.</summary>
    public float Scale(float age) => ScaleMin + (ScaleMax - ScaleMin) * age;

    /// <summary>The animal fields of a record; null when it is not an ANIMAL_CHARACTER.</summary>
    public static AnimalData? From(GameRecord? r)
    {
        if (r is not { Type: FcsRecordType.ANIMAL_CHARACTER }) return null;
        static string? First(GameRecord r, string list) => r.GetReferences(list) is { Count: > 0 } refs ? refs[0].TargetStringId : null;
        float hp = r.GetFloat("HP mult", 1);
        return new AnimalData
        {
            StringId = r.StringId,
            WeaponId = First(r, "weapon"),
            WeaponLevelId = First(r, "weapon level"),
            AnimalStrength = r.GetInt("animal strength"),
            HpMult = hp <= 0 ? 1 : hp,
            ScaleMin = r.GetFloat("scale min", 1),
            ScaleMax = r.GetFloat("scale max", 1),
            Lifespan = r.GetFloat("lifespan"),
            SmellBlood = r.GetFloat("smell blood"),
            SmellEggs = r.GetFloat("smell eggs"),
            TurnRate = r.GetFloat("turn rate"),
        };
    }
}
