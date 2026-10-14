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
    /// <summary>Walks to its node in the home building (shopkeeper, guard, throne or general, by goal), else to its post, and stands there.</summary>
    Post = 3,
    /// <summary>Keeps its place beside the squad leader (<see cref="CharacterTask.Follow"/>); the leader itself cannot.</summary>
    Follow = 4,
    /// <summary>Walks to its bed in the home building (else the home, else its post) and lies down there (<see cref="CharacterTask.Rest"/>).</summary>
    Sleep = 5,
    /// <summary>Sits down (<see cref="CharacterTask.Rest"/>): at a bar or general node of its home, else inside it; without a home where it is.</summary>
    Sit = 6,
    /// <summary>While a patrol package runs, the leader walks the patrol's route and the others follow it; otherwise as <see cref="Roam"/>.</summary>
    Patrol = 7,
    /// <summary>Goes back to its home town (its home building, else the town's centre); wanted at 0.5 while away from it and not at all at home (docs/game/perception.md 7.2, Observed).</summary>
    GoHome = 8,
}

/// <summary>The engine's table from <see cref="TaskType"/> to <see cref="AiBehaviour"/>, and the TaskData display text (docs/game/ai-tasks.md "TaskData registry", Verified) of the types it acts out.</summary>
public static class TaskBehaviours
{
    static readonly Dictionary<TaskType, (AiBehaviour Behaviour, string Text)> Table = new()
    {
        [TaskType.IDLE] = (AiBehaviour.Mill, "Idleness"),
        [TaskType.WANDERER] = (AiBehaviour.Roam, "Wandering"),
        [TaskType.PATROL_TOWN] = (AiBehaviour.Roam, "Patrolling town"),
        [TaskType.PATROL] = (AiBehaviour.Patrol, "Patrolling"),
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
        [TaskType.RETREAT_HOMETOWN] = (AiBehaviour.GoHome, "Retreating"),
        [TaskType.RUN_AWAY_HOMETOWN] = (AiBehaviour.GoHome, "Retreating fast"),
    };

    /// <summary>
    /// The combat goals and the stance each gives (<b>engine reading</b>: how the original turns a sighting into an attack is <b>Unknown</b>, docs/game/perception.md;
    /// these goals are the urgent ones its packages list for it). SELF_PRESERVATION (in the <c>AI Goals</c> of 30 races) is not one: fighting back when attacked is the retaliation rule's.
    /// </summary>
    static readonly Dictionary<TaskType, AiStance> Stances = new()
    {
        [TaskType.ATTACK_ENEMIES] = AiStance.Aggressive,
        [TaskType.ATTACK_ENEMIES_AND_NEUTRALS] = AiStance.AggressiveNeutrals,
        [TaskType.TERRITORIAL_AGGRESSION_BUT_DONT_LEAVE_HOME] = AiStance.Territorial,
        [TaskType.PROTECT_ALLIES] = AiStance.Defensive,
        [TaskType.PROTECT_OWN_SQUAD] = AiStance.Defensive,
        [TaskType.PROTECT_ALLIES_STAY_IN_TOWN] = AiStance.Defensive,
        [TaskType.ATTACK_TROUBLE_MAKERS] = AiStance.Defensive,
        [TaskType.ATTACK_ENEMIES_OF_MY_SLAVEMASTER] = AiStance.Defensive,
        [TaskType.BODYGUARD] = AiStance.Defensive,
    };

    // Flat tables by type number for the think's hot loop (a dictionary lookup per goal was a third of the think).
    const int TypeCount = 291;
    static readonly AiBehaviour[] Behaviours = BuildBehaviours();
    static readonly sbyte[] StanceTable = BuildStances();

    static AiBehaviour[] BuildBehaviours()
    {
        var a = new AiBehaviour[TypeCount];
        foreach (var (type, e) in Table) a[(int)type] = e.Behaviour;
        return a;
    }

    static sbyte[] BuildStances()
    {
        var a = new sbyte[TypeCount];
        Array.Fill(a, (sbyte)-1);
        foreach (var (type, s) in Stances) a[(int)type] = (sbyte)s;
        return a;
    }

    /// <summary>The stance a combat goal gives; null for a goal that is not one.</summary>
    public static AiStance? StanceOf(TaskType type) => (uint)type < TypeCount && StanceTable[(int)type] >= 0 ? (AiStance)StanceTable[(int)type] : null;

    /// <summary>The goal types that are combat goals.</summary>
    public static IEnumerable<TaskType> CombatGoals => Stances.Keys;

    public static AiBehaviour Of(TaskType type) => (uint)type < TypeCount ? Behaviours[(int)type] : AiBehaviour.None;

    /// <summary>The game's display text of a type the engine acts out; the enum name for the rest.</summary>
    public static string Text(TaskType type) => Table.TryGetValue(type, out var e) ? e.Text : type.ToString();

    /// <summary>Every type the engine acts out.</summary>
    public static IEnumerable<TaskType> Built => Table.Keys;
}
