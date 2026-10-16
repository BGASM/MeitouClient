using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Simulation;

/// <summary>One animation playing on a character: the skeleton animation's name, its time in seconds and its blend weight.</summary>
public readonly record struct AnimationLayer(string Name, float Time, float Weight);

/// <summary>What a character is doing about its body: nothing, bandaging someone (<see cref="CharacterTask.Treat"/>) or resting (<see cref="CharacterTask.Rest"/>).</summary>
public enum CareKind { None, Treating, Resting }

/// <summary>
/// The health the interface shows: blood (0..1 of capacity), the worst body part (fraction of its maximum HP), hunger (3 fed .. 0 starving) and whether it is out cold or dead; for the player's
/// characters also whether first aid has anything to bandage (<see cref="NeedsAid"/>), what the character is doing (<see cref="Care"/>) and its medic job switch.
/// </summary>
public readonly record struct BodyStatus(float Blood, float WorstPart, float Hunger, bool Unconscious, bool Dead, bool NeedsAid = false, CareKind Care = CareKind.None, bool MedicJob = false, bool RescueJob = false);
public sealed record HealthPartView(string Id, string Name, float Flesh, float Stun, float Bandage, float Rig, float Maximum, bool Robotic);
/// <summary>A line of the HUD's stats panel: the label, the value the character fights with and the value it would have unhurt (docs/game/ui-screens.md section 4).</summary>
public readonly record struct HudStatLine(string Label, float Value, float Base);
/// <summary>
/// What the HUD shows of a selected character besides <see cref="BodyStatus"/> (docs/game/ui-screens.md section 4): the blood in units and its capacity, the bleeding
/// rate, the encumbrance factor (1 unburdened) and the stats panel's lines.
/// </summary>
public sealed record HudView(float Blood, float BloodCapacity, float Bleeding, float Encumbrance, IReadOnlyList<HudStatLine> Stats, ExtendedStats? Extended = null);
/// <summary>
/// The information panel's extended lines (docs/game/ui-screens.md section 4): the XP rates as the fraction the panel shows as a percentage (Toughness as a plain multiplier),
/// the KO point (negative HP), the run speed in units per second with the unhurt run value it is measured against, and the hunger rate as a multiple of the race's.
/// </summary>
public readonly record struct ExtendedStats(float AthleticsXp, float StrengthXp, float ToughnessXp, float KoPoint, float RunSpeed, float RunValue, float HungerRate);
public sealed record InventoryItemView(int Index, string Record, string Name, string Section, int X, int Y, int Width, int Height, int Quantity, float Weight, float Quality)
{
    /// <summary>The item's material record (<c>material sid</c>; "" none), which picks its icon.</summary>
    public string Material { get; init; } = "";
}
public sealed record InventoryView(int Width, int Height, int BagWidth, int BagHeight, IReadOnlyList<InventoryItemView> Items)
{
    public float? CarryCapacity { get; init; }
    public float TotalWeight { get; init; }
}

/// <summary>A character as the renderer needs it after one tick; <c>Appearance</c> is null for one that is simulated but has nothing to draw (a failed build, a world without looks): the host shows a marker (docs/simulation.md, "Threading", step 8).</summary>
public sealed record CharacterSnapshot(CharacterId Id, CharacterAppearance? Appearance, Vector3 Position, float Yaw, IReadOnlyList<AnimationLayer> Animations)
{
    /// <summary>The owner faction (a position in the world's faction list; -1 none), for debug markers.</summary>
    public int Faction { get; init; } = -1;
    /// <summary>A weapon is in hand (<c>CharacterCold.DrawnWeapon</c>): the renderer shows the drawn weapon meshes instead of the sheathed one.</summary>
    public bool WeaponDrawn { get; init; }
    public string Name { get; init; } = "";
    public int SquadId { get; init; } = -1;
    /// <summary>One of the player's characters, and whether it is selected.</summary>
    public bool IsPlayer { get; init; }
    /// <summary>A member of a squad with a shop (right click trades).</summary>
    public bool IsTrader { get; init; }
    public bool Selected { get; init; }
    /// <summary>Another faction's character the HUD shows (<see cref="InspectCommand"/>): it carries the same HUD details as a selected one but is not selected.</summary>
    public bool Inspected { get; init; }
    /// <summary>The orders panel's toggles and the move speed mode (<see cref="CharacterCold.Orders"/>, <see cref="CharacterCold.RunSpeed"/>).</summary>
    public CharacterOrders Orders { get; init; }
    public byte RunSpeed { get; init; } = SpeedMode.Free;
    /// <summary>The path being followed from the next waypoint, for selected characters only (the host draws it).</summary>
    public IReadOnlyList<Vector3> Path { get; init; } = [];
    /// <summary>Null for a character without a body (an animal).</summary>
    public BodyStatus? Body { get; init; }
    /// <summary>What a selected character carries, one line per item (empty for the others).</summary>
    public IReadOnlyList<string> Inventory { get; init; } = [];
    /// <summary>A selected character's bounties, one line per faction that wants it (empty for the others and for one nobody wants).</summary>
    public IReadOnlyList<Crime.BountyLine> Bounties { get; init; } = [];
    /// <summary>A selected character's trained stats (the host formats them); null for the others and for a character without stats.</summary>
    public SkillSummary? Skills { get; init; }
    public IReadOnlyList<float> AllSkills { get; init; } = [];
    public IReadOnlyList<float> SkillXpMultipliers { get; init; } = [];
    public IReadOnlyList<HealthPartView> HealthParts { get; init; } = [];
    /// <summary>The HUD's numbers for a selected character (null for the others).</summary>
    public HudView? Hud { get; init; }
    public InventoryView? ItemGrid { get; init; }
    /// <summary>
    /// The character carrying this one (none when not carried). A carried body has its carrier's position and facing; the renderer should hang it from the carrier's
    /// <see cref="AttachBone"/> (the original attaches it there) with the <c>carry me</c> pose.
    /// </summary>
    public CharacterId CarriedBy { get; init; } = CharacterId.None;
    /// <summary>The carrier's bone a carried body hangs from (<c>Bip01 L UpperArm</c>), null when not carried.</summary>
    public string? AttachBone { get; init; }
    /// <summary>The size the body is drawn at: an animal's lerp(age, <c>scale min</c>, <c>scale max</c>) (its <c>MedicalState.ScaleK</c>), 1 for people.</summary>
    public float Scale { get; init; } = 1;
    /// <summary>The character this one carries (none when not carrying).</summary>
    public CharacterId Carrying { get; init; } = CharacterId.None;
    /// <summary>The goal the AI is acting out, in the game's words (<c>Patrolling town</c>, <c>Shopkeeper</c>); null when the AI does not run the character now.</summary>
    public string? Activity { get; init; }
    /// <summary>
    /// A sneaking player character's state for its stealth tag (docs/game/perception.md "Spotting a sneaker"): <see cref="StealthExposure.Detected"/> seen,
    /// <see cref="StealthExposure.Noticed"/> almost seen, anything lower hidden; null when not a sneaking player character.
    /// </summary>
    public StealthExposure? Stealth { get; init; }
    /// <summary>The hostile NPCs that noticed or detected this sneaking player character in the last 4 s.</summary>
    public IReadOnlyList<WatcherView> Watchers { get; init; } = [];
}

/// <summary>The trained stats the HUD shows for the selection.</summary>
public readonly record struct SkillSummary(float Attack, float Defence, float Dodge, float Toughness, float Strength, float Athletics);

/// <summary>
/// What a tick publishes for drawing. Immutable once published: the host keeps the last two and interpolates between them by the
/// frame's alpha, as it does for the camera; a character only in the newer one appears without interpolation.
/// </summary>
public sealed record WorldSnapshot(long Tick, IReadOnlyList<CharacterSnapshot> Characters)
{
    public static readonly WorldSnapshot Empty = new(-1, []);

    /// <summary>Crossbow bolts in flight and stuck in the ground (the renderer may draw them; empty when there are none).</summary>
    public IReadOnlyList<ProjectileSnapshot> Projectiles { get; init; } = [];
    /// <summary>The open trade (null: none).</summary>
    public Trading.TradeView? Trade { get; init; }
    /// <summary>What the player knows of each placed town (<see cref="PlayerState.Towns"/>).</summary>
    public IReadOnlyList<TownKnowledge> Towns { get; init; } = [];
    /// <summary>The camps and nests the player knows (<see cref="PlayerState.Camps"/>).</summary>
    public IReadOnlyList<CampView> Camps { get; init; } = [];
}

/// <summary>
/// A bolt as the renderer needs it: the nose position, the unit direction of flight, the stretch of its mesh along its length (max(1, speed x 0.005) in flight, 1 once
/// stuck; <c>FUN_1404380e0</c>), whether it is stuck in the ground, and the <c>live ammo</c> mesh of the crossbow that shot it.
/// </summary>
public readonly record struct ProjectileSnapshot(int Id, Vector3 Position, Vector3 Direction, float Stretch, bool Stuck, string Mesh);

/// <summary>A system that has projectiles to publish (the combat system).</summary>
public interface IProjectileSource
{
    /// <summary>The projectiles after the tick (a new list; empty when none).</summary>
    IReadOnlyList<ProjectileSnapshot> PublishProjectiles();
}
