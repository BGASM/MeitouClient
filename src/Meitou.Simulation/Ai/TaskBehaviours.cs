using Meitou.Data.Gameplay.Ai;

namespace Meitou.Simulation.Ai;

/// <summary>
/// How the engine acts out a goal it chose (docs/simulation.md "AI"). The original runs a <c>Task_*</c> class and its GOAP plan per goal
/// (docs/game/ai-tasks.md); towns here have no buildings, beds, doors or nodes yet, so a goal maps onto the movement tasks that exist.
/// A goal whose type has no behaviour is never chosen.
/// </summary>
public enum AiBehaviour : byte
{
    /// <summary>Not built: the goal scores 0.</summary>
    None = 0,
    /// <summary>Stands about near its post, stepping a few metres now and then (<see cref="CharacterTask.Wander"/> in <see cref="AiSettings.MillRadius"/>).</summary>
    Mill = 1,
    /// <summary>Walks to random places in the squad's home (the town) and waits between them (<see cref="CharacterTask.Wander"/>).</summary>
    Roam = 2,
    /// <summary>Walks to its post and stands there (the stand-in for the shopkeeper, guard and general nodes of a home building).</summary>
    Post = 3,
    /// <summary>Keeps its place beside the squad leader (<see cref="CharacterTask.Follow"/>); the leader itself cannot.</summary>
    Follow = 4,
    /// <summary>Walks to its post and lies down there (<see cref="CharacterTask.Rest"/>; beds are not modelled).</summary>
    Sleep = 5,
    /// <summary>Sits down where it is (<see cref="CharacterTask.Rest"/>).</summary>
    Sit = 6,
}

/// <summary>The engine's table from <see cref="TaskType"/> to <see cref="AiBehaviour"/>, and the TaskData display text (docs/game/ai-tasks.md "TaskData registry", Verified) of the types it acts out.</summary>
public static class TaskBehaviours
{
    static readonly Dictionary<TaskType, (AiBehaviour Behaviour, string Text)> Table = new()
    {
        [TaskType.IDLE] = (AiBehaviour.Mill, "Idleness"),
        [TaskType.WANDERER] = (AiBehaviour.Roam, "Wandering"),
        [TaskType.PATROL_TOWN] = (AiBehaviour.Roam, "Patrolling town"),
        [TaskType.PATROL] = (AiBehaviour.Roam, "Patrolling"),
        [TaskType.RELAX_IN_TOWN_PACKAGE] = (AiBehaviour.Roam, "Relaxing"),
        [TaskType.SHOPPING] = (AiBehaviour.Roam, "Shopping"),
        [TaskType.STAND_AT_SHOPKEEPER_NODE] = (AiBehaviour.Post, "Shopkeeper"),
        [TaskType.STAND_AT_GUARD_NODE_HOMEBUILDING] = (AiBehaviour.Post, "Static guard"),
        [TaskType.STAND_AT_GUARD_NODE_HOMEBUILDING_INDOORS_ONLY] = (AiBehaviour.Post, "Static guard indoors"),
        [TaskType.STAND_AT_GUARD_NODE_HOMETOWN_OUTSIDE] = (AiBehaviour.Post, "Static town guard"),
        [TaskType.STAND_AT_GENERAL_NODE] = (AiBehaviour.Post, "general node"),
        [TaskType.HOLD_POSITION] = (AiBehaviour.Post, "Holding position"),
        [TaskType.STAY_IN_HOME] = (AiBehaviour.Post, "Staying home"),
        [TaskType.MAN_THE_GATE] = (AiBehaviour.Post, "Gate guard"),
        [TaskType.SIT_ON_THRONE] = (AiBehaviour.Post, "Reigning"),
        [TaskType.FOLLOW_SQUADLEADER] = (AiBehaviour.Follow, "Following leader"),
        [TaskType.FOLLOW_SLAVEMASTER] = (AiBehaviour.Follow, "Following master"),
        [TaskType.BODYGUARD] = (AiBehaviour.Follow, "Bodyguard"),
        [TaskType.GO_HOME_AND_GO_TO_BED] = (AiBehaviour.Sleep, "Going to bed"),
        [TaskType.GO_HOME_AND_GO_TO_BED_SECURE] = (AiBehaviour.Sleep, "Going to bed"),
        [TaskType.SLEEP_ON_FLOOR] = (AiBehaviour.Sleep, "Sleeping on ground"),
        [TaskType.SIT_AROUND] = (AiBehaviour.Sit, "Sitting around"),
    };

    public static AiBehaviour Of(TaskType type) => Table.TryGetValue(type, out var e) ? e.Behaviour : AiBehaviour.None;

    /// <summary>The game's display text of a type the engine acts out; the enum name for the rest.</summary>
    public static string Text(TaskType type) => Table.TryGetValue(type, out var e) ? e.Text : type.ToString();

    /// <summary>Every type the engine acts out.</summary>
    public static IEnumerable<TaskType> Built => Table.Keys;
}
