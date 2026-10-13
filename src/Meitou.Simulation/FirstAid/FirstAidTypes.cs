namespace Meitou.Simulation.FirstAid;

/// <summary>
/// Orders the medics (the selection when the list is empty) to treat <see cref="Patient"/> with a first aid kit: walk to it, then bandage it until nothing is left to bandage or the kit
/// is used up. A patient among the medics treats itself. The player's first aid order (the original's context menu verb "First Aid", <c>FIRST_AID_ORDER</c>); ignored for a medic that
/// is knocked out or fighting.
/// </summary>
public sealed record TreatOrder(IReadOnlyList<CharacterId> Medics, CharacterId Patient) : SimCommand;

/// <summary>Orders the characters (the selection when the list is empty) to sit down and rest. Any other order ends it.</summary>
public sealed record RestOrder(IReadOnlyList<CharacterId> Characters) : SimCommand;

/// <summary>
/// Switches the medic job on or off for the characters (the selection when the list is empty): the original's <c>medic</c> toggle (<c>JOB_MEDIC</c>). While it is on, an idle
/// character with a kit treats a hurt squad mate by itself. <see cref="On"/> null flips each character's own switch.
/// </summary>
public sealed record MedicJobCommand(IReadOnlyList<CharacterId> Characters, bool? On = null) : SimCommand;

/// <summary>The numbers of <see cref="FirstAidSystem"/>; all engine choices (the original's first aid task is only partly decoded, docs/game/character-stats.md "First aid").</summary>
public sealed class FirstAidSettings
{
    /// <summary>The medic treats from this far (X/Z units) and a path to the patient ends here; above the movement system's arrival radius (10), so a medic that has walked up is in reach.</summary>
    public float Reach { get; init; } = 14;
    /// <summary>A moving medic or patient (units per second) is not still: the original needs the patient to stand still (<c>targetStandingStill</c>), a knocked-out one always does.</summary>
    public float StillSpeed { get; init; } = 1;
    /// <summary>A kit in a squad mate's inventory is used when this close (the same reach as sharing food).</summary>
    public float KitReach { get; init; } = 60;
    /// <summary>Ticks between looks for a patient by characters that treat by themselves (the retaliation system's rhythm).</summary>
    public int AutoEveryTicks { get; init; } = 15;
    /// <summary>Characters that treat by themselves look for a patient this far (X/Z units) from them, in their own squad.</summary>
    public float AutoReach { get; init; } = 250;
    /// <summary>NPCs with a kit treat hurt squad mates without an order (a stand-in for the original's medic job and AI goals until stage 9). The player's characters do only with the medic job on.</summary>
    public bool AutoNpcs { get; init; } = true;
}
