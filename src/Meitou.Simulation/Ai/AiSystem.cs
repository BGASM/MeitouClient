using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;

namespace Meitou.Simulation.Ai;

/// <summary>Settings of the <see cref="AiSystem"/>; the defaults are the game's (engine choices unless a doc label says otherwise).</summary>
public sealed class AiSettings
{
    /// <summary>A character thinks every this many ticks (staggered by slot), and at once when its squad starts another package.</summary>
    public int ThinkEveryTicks { get; init; } = 15;
    /// <summary>Ticks between two package choices of a squad.</summary>
    public int PackageEveryTicks { get; init; } = 30;
    /// <summary>A character farther than this from its post walks back to it (<see cref="AiBehaviour.Post"/>, <see cref="AiBehaviour.Sleep"/>).</summary>
    public float PostSlack { get; init; } = 12;
    /// <summary>The radius a character mills about its post in (<see cref="AiBehaviour.Mill"/>).</summary>
    public float MillRadius { get; init; } = 25;
    /// <summary>The crowding factor: the score is multiplied by it once per squad mate acting out the same goal type. The original's is per TaskData (<b>Unknown</b>); 1 turns crowding off.</summary>
    public float CrowdFactor { get; init; } = 1;
    /// <summary>The score of the goal being acted out is multiplied by this (<b>Observed</b>, docs/game/ai.md "Scoring"), so a near tie does not flip.</summary>
    public float Continuity { get; init; } = 1.25f;
}

/// <summary>
/// The AI (docs/simulation.md "AI"; the original's in docs/game/ai.md). Squads whose template lists <c>AI packages</c> get a <see cref="Blackboard"/> that picks
/// the package by slot; each member thinks on its own cadence: it scores the goals of its role list, its race's <c>AI Goals</c> and the default idle goal
/// by tier (urgent, then non-urgent, then fluff; the first tier with a positive score wins), weight, hour window, crowding and continuity, and acts out the
/// winner through <see cref="TaskBehaviours"/>. The AI runs only characters that nothing else holds: not the player's, not fighting, fleeing, down,
/// carrying, carried, treating, looting or trading. Roaming platoons keep their travel stand-in (<see cref="PopulationSystem"/>) and get no AI.
/// <list type="bullet">
/// <item><c>Think</c> (parallel): the choice and the act-out write only the character's own state (its task, goal, path flags and <see cref="AiMind"/>);
/// they read the squad's blackboard, which only <c>SlowWorld</c> changes.</item>
/// <item><c>SlowWorld</c> (serial): blackboards for new squads and minds for their members, the package choice, the crowding counts.</item>
/// </list>
/// </summary>
public sealed class AiSystem(PopulationData data, AiSettings? settings = null) : ITickSystem
{
    /// <summary>Every character's default goal (docs/game/ai.md "Default tasks per character", Observed): IDLE at the fluff tier, weight 0.1.</summary>
    public static readonly AiGoal Idle = new(new AiTask("", "IDLE", TaskType.IDLE, TaskPriority.Fluff, TaskTargetType.Specific, TaskEndEvent.Nothing), 0, 100, 0.1f);

    /// <summary>Walks to its post in a row that may end elsewhere before the spot it stands on becomes its post.</summary>
    public const int MaxFailedWalks = 3;

    static readonly TaskPriority[] Tiers = [TaskPriority.Urgent, TaskPriority.NonUrgent, TaskPriority.Fluff];

    public AiSettings Settings { get; } = settings ?? new AiSettings();

    readonly Dictionary<string, IReadOnlyList<SlottedPackage>> templates = [];
    // Squads already looked at (ids are never reused); derived from the squads, so not part of the hash.
    readonly HashSet<int> looked = [];

    public void Think(World world, Partition part)
    {
        var table = world.Characters;
        var prev = table.Previous;
        int every = Math.Max(1, Settings.ThinkEveryTicks);
        for (int i = part.Start; i < part.End; i++)
        {
            if (!prev[i].Alive || table.Cold(i) is not { Ai: { Squad: { Ai: { } board } squad } mind } cold) continue;
            // Cheapest test first: most characters are not due this tick.
            if ((world.Tick + i) % every != 0 && mind.Version == board.Version) continue;
            ref var n = ref table.Next[i];
            if (!Holds(n, cold)) continue;
            var goal = Choose(world, i, cold, mind, squad, board);
            Act(table, i, mind, goal, board.Version);
        }
    }

    /// <summary>Whether the AI runs the character now: it has a mind and nothing else holds it (a fight, a flight, an order, a carry, a trade, a knock-out).</summary>
    public static bool Holds(in CharacterHot n, CharacterCold cold)
    {
        if (cold.Ai is not { } mind || cold.IsPlayer || cold.InCombat || cold.Fleeing || cold.Engaged || !cold.CarriedBy.IsNone || !cold.Carrying.IsNone || cold.Medical is { Incapacitated: true }) return false;
        return (CharacterTask)n.Task switch
        {
            CharacterTask.Idle or CharacterTask.Wander or CharacterTask.Follow => true,
            // The walk to its post, and lying or sitting there, are the AI's own.
            CharacterTask.GoTo => mind.Behaviour is AiBehaviour.Post or AiBehaviour.Sleep,
            CharacterTask.Rest => mind.Behaviour is AiBehaviour.Sleep or AiBehaviour.Sit,
            _ => false,
        };
    }

    /// <summary>The role list a member takes (docs/game/ai.md "Goal loading"): the squad leader the leader's, else by its squad role.</summary>
    static AiRole RoleOf(Squad squad, CharacterId id, CharacterCold cold) =>
        squad.Leader == id || cold.Role == (int)SquadRole.Leader ? AiRole.Leader
        : cold.Role == (int)SquadRole.Squad2 ? AiRole.Squad2
        : cold.Role == (int)SquadRole.Slave ? AiRole.Slave
        : AiRole.Squad;

    AiGoal Choose(World world, int slot, CharacterCold cold, AiMind mind, Squad squad, Blackboard board)
    {
        var id = world.Characters.IdOf(slot);
        var roles = board.Roles[(int)RoleOf(squad, id, cold)];
        var race = mind.RaceGoals;
        int hour = world.Hour;
        foreach (var tier in Tiers)
        {
            AiGoal? best = null;
            float bestScore = 0;
            void Consider(AiGoal g)
            {
                if (g.Task.Priority != tier || !g.InWindow(hour)) return;
                float desire = Desire(world, TaskBehaviours.Of(g.Task.Type), squad, id);
                if (desire <= 0) return;
                int crowd = board.Crowd.GetValueOrDefault(g.Task.Type) - (mind.Goal?.Task.Type == g.Task.Type ? 1 : 0);
                float score = desire * g.Weight * (crowd > 0 ? MathF.Pow(Settings.CrowdFactor, crowd) : 1);
                if (mind.Goal is { } current && current.Task.Type == g.Task.Type && current.Task.Target == g.Task.Target) score *= Settings.Continuity;
                if (score > bestScore) (best, bestScore) = (g, score);
            }
            // Index loops: a foreach over the interface would box an enumerator per list and pass.
            for (int k = 0; k < roles.Count; k++) Consider(roles[k]);
            for (int k = 0; k < race.Count; k++) Consider(race[k]);
            Consider(Idle);
            if (best is not null) return best;
        }
        return Idle;
    }

    /// <summary>How much the character wants a goal (the TaskData desire functions are not decoded): 1 for a goal it can act out, 0 for the rest.</summary>
    static float Desire(World world, AiBehaviour behaviour, Squad squad, CharacterId self) => behaviour switch
    {
        AiBehaviour.None => 0,
        AiBehaviour.Follow => squad.Leader.IsNone || squad.Leader == self || !world.Characters.IsAlive(squad.Leader) ? 0 : 1,
        _ => 1,
    };

    /// <summary>Acts out a goal: sets the movement task it maps to, unless the character is already doing it.</summary>
    void Act(CharacterTable table, int slot, AiMind mind, AiGoal goal, int version)
    {
        var behaviour = TaskBehaviours.Of(goal.Task.Type);
        mind.Goal = goal;
        mind.Behaviour = behaviour;
        mind.Version = version;
        mind.WanderRadius = 0;
        if (behaviour is not (AiBehaviour.Post or AiBehaviour.Sleep)) mind.Walking = false;
        ref var n = ref table.Next[slot];
        var here = new Vector2(n.Position.X, n.Position.Z);
        bool away = Vector2.DistanceSquared(here, mind.Post) > Settings.PostSlack * Settings.PostSlack;
        switch (behaviour)
        {
            case AiBehaviour.Mill:
                mind.WanderCentre = mind.Post;
                mind.WanderRadius = Settings.MillRadius;
                Become(table, slot, CharacterTask.Wander);
                break;
            case AiBehaviour.Roam:
                Become(table, slot, CharacterTask.Wander);
                break;
            case AiBehaviour.Follow:
                Become(table, slot, CharacterTask.Follow);
                break;
            case AiBehaviour.Post:
            case AiBehaviour.Sleep:
                if (mind.Walking && n.Task != (byte)CharacterTask.GoTo)
                {
                    // The walk is over: arrived, or it ended elsewhere (no way there, or a fight took it). A post it keeps failing to reach moves to where it stands.
                    mind.Walking = false;
                    mind.FailedWalks = away ? mind.FailedWalks + 1 : 0;
                    if (mind.FailedWalks >= MaxFailedWalks)
                    {
                        mind.Post = here;
                        mind.FailedWalks = 0;
                        away = false;
                    }
                }
                if (away)
                {
                    WalkTo(table, slot, mind.Post);
                    mind.Walking = true;
                }
                else if (n.Task == (byte)CharacterTask.GoTo) { }   // arriving: the movement system ends the walk
                else Become(table, slot, behaviour == AiBehaviour.Sleep ? CharacterTask.Rest : CharacterTask.Idle);
                break;
            case AiBehaviour.Sit:
                Become(table, slot, CharacterTask.Rest);
                break;
        }
    }

    /// <summary>Switches to a standing task (wander, follow, idle, rest), dropping a walk in progress; nothing when it is doing it already.</summary>
    static void Become(CharacterTable table, int slot, CharacterTask task)
    {
        ref var n = ref table.Next[slot];
        if (n.Task == (byte)task) return;
        MovementSystem.Stop(table, slot);
        n.Task = (byte)task;
        n.TaskTime = 0;
    }

    /// <summary>Sends it walking to a point, unless it is walking there already.</summary>
    static void WalkTo(CharacterTable table, int slot, Vector2 to)
    {
        ref var n = ref table.Next[slot];
        if (n.Task == (byte)CharacterTask.GoTo && Vector2.DistanceSquared(n.Goal, to) < 1) return;
        MovementSystem.GoTo(table, slot, to, SpeedMode.Walk);
    }

    public void SlowWorld(World world)
    {
        var table = world.Characters;
        bool packages = world.Tick % Math.Max(1, Settings.PackageEveryTicks) == 0;
        foreach (var squad in world.Squads.All)
        {
            if (squad.Ai is null)
            {
                if (!looked.Add(squad.Id) || !Runs(world, squad)) continue;
                squad.Ai = new Blackboard(PackagesOf(squad.TemplateId));
                squad.Ai.Update();
            }
            else if (packages) squad.Ai.Update();
            else continue;   // the members and the crowding counts are looked at on the package cadence only (and when the squad appears)
            var board = squad.Ai;
            board.Crowd.Clear();
            foreach (var m in squad.Members)
            {
                if (!table.TryResolveNext(m, out int s)) continue;
                var cold = table.Cold(s)!;
                if (cold.Ai is null)
                {
                    var p = table.Next[s].Position;
                    cold.Ai = new AiMind { Squad = squad, Post = new Vector2(p.X, p.Z), RaceGoals = cold.Race is { } race ? data.Ai.RaceGoals(race.StringId) : [] };
                }
                if (cold.Ai.Goal is { } g) board.Crowd[g.Task.Type] = board.Crowd.GetValueOrDefault(g.Task.Type) + 1;
            }
        }
    }

    /// <summary>The AI runs a squad that is not the player's, not a roaming platoon's active half, and whose template lists AI packages.</summary>
    bool Runs(World world, Squad squad)
    {
        if (squad.PlatoonId >= 0 || squad.Faction == data.PlayerFaction && data.PlayerFaction >= 0) return false;
        foreach (var m in squad.Members)
            if (world.Characters.TryResolveNext(m, out int s) && world.Characters.Cold(s)!.IsPlayer) return false;
        return PackagesOf(squad.TemplateId).Count > 0;
    }

    IReadOnlyList<SlottedPackage> PackagesOf(string templateId)
    {
        if (templates.TryGetValue(templateId, out var known)) return known;
        IReadOnlyList<SlottedPackage> list = data.Db.Find(templateId) is { } r && SquadFactory.IsTemplate(r) ? data.Ai.Packages(SquadTemplate.From(r)) : [];
        templates[templateId] = list;
        return list;
    }
}
