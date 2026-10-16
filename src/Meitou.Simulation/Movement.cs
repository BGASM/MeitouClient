using System.Numerics;

namespace Meitou.Simulation;

/// <summary>What a character is doing (<see cref="CharacterHot.Task"/>). The first tasks of the movement stage; the AI packages replace them later.</summary>
public enum CharacterTask : byte
{
    Idle = 0,
    /// <summary>Walks to random places within the squad's home radius, waiting between them.</summary>
    Wander = 1,
    /// <summary>Walks to <see cref="CharacterHot.Goal"/> (a move order) and then idles.</summary>
    GoTo = 2,
    /// <summary>Keeps its formation place beside the squad leader.</summary>
    Follow = 3,
    /// <summary>Fights the target of an <see cref="Combat.AttackOrder"/> (the combat system owns the character's attacks and reactions; movement leaves it alone).</summary>
    Attack = 4,
    /// <summary>Walks to <see cref="CharacterCold.TreatTarget"/> and bandages it with a first aid kit (<see cref="FirstAid.FirstAidSystem"/> owns it; the movement system leaves it alone).</summary>
    Treat = 5,
    /// <summary>Sits on the ground resting: blood recovers at <c>resting heal rate mult</c> and hunger falls at <c>bed hunger rate</c> (<see cref="FirstAid.FirstAidSystem"/>); any other order replaces it.</summary>
    Rest = 6,
    /// <summary>Walks to <see cref="CharacterCold.CarryTarget"/> and lifts it onto the left shoulder (<see cref="Carrying.CarrySystem"/> owns it; the original's <c>LIFT_PERSON</c>).</summary>
    PickUp = 7,
    /// <summary>Sets the carried character down (<see cref="Carrying.CarrySystem"/>; the original's <c>PUT_DOWN_OBJECT</c>).</summary>
    PutDown = 8,
    /// <summary>Walks to <see cref="CharacterCold.CarryTarget"/> (a downed or dead character) and takes everything from its inventory that fits (<see cref="Carrying.CarrySystem"/>).</summary>
    Loot = 9,
    /// <summary>Creeps up behind <see cref="CharacterCold.CarryTarget"/> and swings to knock it out (<see cref="Carrying.CarrySystem"/>; the original's <c>Task_StealthKO</c>).</summary>
    StealthKo = 10,
}

/// <summary>Bits of <see cref="CharacterHot.Flags"/> the movement system uses.</summary>
[Flags]
public enum MoveFlags : ushort
{
    None = 0,
    /// <summary>A path to <see cref="CharacterHot.Goal"/> is wanted; the next Schedule step asks the path service.</summary>
    NeedPath = 1,
    /// <summary>A path request is in flight.</summary>
    Pending = 2,
    /// <summary>The character is following <see cref="CharacterCold.Path"/>.</summary>
    HasPath = 4,
    /// <summary>A path is wanted, asked for or being followed.</summary>
    AnyPath = NeedPath | Pending | HasPath,
}

/// <summary>Speed modes (docs/game/pathfinding.md "Speed"), also the HUD speed icon's index (Walk, Jog, Run, Group): 0 caps at the race walk speed, 1 at 55, 2 does not cap, 3 keeps to the slowest of the squad.</summary>
public static class SpeedMode
{
    public const byte Walk = 0, Run = 1, Free = 2, Group = 3;
    /// <summary>The number of modes the HUD arrows cycle through.</summary>
    public const int Count = 4;
    public const float RunCap = 55;

    public static float Cap(byte mode, float walkSpeed) => mode switch { Walk => walkSpeed, Run => RunCap, _ => 999 };
}

/// <summary>The animation layers a character plays for its movement state (<see cref="CharacterHot.Animation"/>): the names are the skeleton animations of docs/animation.md.</summary>
public static class AnimationLayers
{
    public const string Idle = "idle_stand_relax";
    public const string WalkLower = "walk lower", WalkUpper = "walk upper";

    public static IReadOnlyList<AnimationLayer> For(ushort animation, float time) => animation == 0
        ? [new AnimationLayer(Idle, time, 1)]
        : [new AnimationLayer(WalkLower, time, 1), new AnimationLayer(WalkUpper, time, 1)];
}
