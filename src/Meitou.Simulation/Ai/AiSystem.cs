using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.World;

namespace Meitou.Simulation.Ai;

/// <summary>Settings of the <see cref="AiSystem"/>; the defaults are the game's (engine choices unless a doc label says otherwise).</summary>
public sealed class AiSettings
{
    /// <summary>A character thinks every this many ticks (staggered by slot), and at once when its squad starts another package or its patrol moves on.</summary>
    public int ThinkEveryTicks { get; init; } = 15;
    /// <summary>Ticks between two package choices of a squad.</summary>
    public int PackageEveryTicks { get; init; } = 30;
    /// <summary>A character farther than this from its spot (node, bed, post) walks back to it.</summary>
    public float PostSlack { get; init; } = 12;
    /// <summary>The radius a character mills about its post in (<see cref="AiBehaviour.Mill"/>).</summary>
    public float MillRadius { get; init; } = 25;
    /// <summary>The crowding factor: the score is multiplied by it once per squad mate acting out the same goal type. The original's is per TaskData (<b>Unknown</b>); 1 turns crowding off.</summary>
    public float CrowdFactor { get; init; } = 1;
    /// <summary>The score of the goal being acted out is multiplied by this (<b>Observed</b>, docs/game/ai.md "Scoring"), so a near tie does not flip.</summary>
    public float Continuity { get; init; } = 1.25f;
    /// <summary>A patrol package may start this many game hours after the last one started (uniform), and the first one within <see cref="FirstPatrolHours"/> of the squad appearing.</summary>
    public float PatrolEveryHoursMin { get; init; } = 6;
    public float PatrolEveryHoursMax { get; init; } = 12;
    public float FirstPatrolHours { get; init; } = 4;
    /// <summary>A patrol gives up after this many game hours.</summary>
    public float PatrolGiveUpHours { get; init; } = 6;
    /// <summary>A patrol's route: this many points round the home town, between these multiples of the town's radius from its centre, then home.</summary>
    public int PatrolPoints { get; init; } = 4;
    public float PatrolRadiusMin { get; init; } = 1.5f;
    public float PatrolRadiusMax { get; init; } = 3f;
    /// <summary>The patrol's leader is at a route point within this distance.</summary>
    public float RouteArrive { get; init; } = 40;
}

/// <summary>Starts a contract package on a squad for some game hours (docs/game/ai.md "Dialogue and AI": dialogue's <c>AI contract</c>; the dialogue itself is not built).</summary>
public sealed record StartContract(int SquadId, string PackageId, float Hours) : SimCommand;

/// <summary>
/// The AI (docs/simulation.md "AI"; the original's in docs/game/ai.md). Squads whose template lists <c>AI packages</c> get a <see cref="Blackboard"/> that picks
/// the package by slot and package class; each member thinks on its own cadence: it scores the goals of its role list, its race's <c>AI Goals</c> and the
/// default idle goal by tier (urgent, then non-urgent, then fluff; the first tier with a positive score wins), weight, hour window, crowding and continuity,
/// acts out the winner through <see cref="TaskBehaviours"/> (in its home building's nodes and beds where it has one), and takes its combat stance from its
/// combat goals (<see cref="AggroSystem"/> reads it). The AI runs only characters that nothing else holds: not the player's, not fighting, fleeing, down,
/// carrying, carried, treating, looting or trading. Roaming platoons keep their travel stand-in (<see cref="PopulationSystem"/>; their off-screen mode follows
/// their package, <see cref="PlatoonJobs"/>).
/// <list type="bullet">
/// <item><c>Think</c> (parallel): the stance, the choice and the act-out write only the character's own state (its task, goal, path flags and <see cref="AiMind"/>);
/// they read the squad's blackboard, which only the serial steps change.</item>
/// <item><c>Inputs</c> (serial): contracts. <c>SlowWorld</c> (serial): blackboards, homes and minds for new squads; on the package cadence the intact test, the package
/// choice, the patrol's progress and the crowding counts.</item>
/// </list>
/// </summary>
public sealed class AiSystem(PopulationData data, AiSettings? settings = null) : ITickSystem
{
    /// <summary>Every character's default goal (docs/game/ai.md "Default tasks per character", Observed): IDLE at the fluff tier, weight 0.1.</summary>
    public static readonly AiGoal Idle = new(new AiTask("", "IDLE", TaskType.IDLE, TaskPriority.Fluff, TaskTargetType.Specific, TaskEndEvent.Nothing), 0, 100, 0.1f);

    /// <summary>Walks to a spot in a row that may end elsewhere before it gives the spot up.</summary>
    public const int MaxFailedWalks = 3;

    static readonly TaskPriority[] Tiers = [TaskPriority.Urgent, TaskPriority.NonUrgent, TaskPriority.Fluff];

    public AiSettings Settings { get; } = settings ?? new AiSettings();
    PopulationData Data => data;

    readonly Dictionary<string, (IReadOnlyList<SlottedPackage> Packages, SquadTemplate? Template)> templates = [];
    // Squads already looked at (ids are never reused); derived from the squads, so not part of the hash.
    readonly HashSet<int> looked = [];
    readonly Rules rules = new();

    // ------------------------------------------------------------------ think (parallel)

    public void Think(World world, Partition part)
    {
        var table = world.Characters;
        var prev = table.Previous;
        int every = Math.Max(1, Settings.ThinkEveryTicks);
        int hour = world.Hour;
        for (int i = part.Start; i < part.End; i++)
        {
            if (!prev[i].Alive || table.Cold(i) is not { Ai: { Squad: { Ai: { } board } squad } mind } cold) continue;
            // Cheapest test first: most characters are not due this tick.
            if ((world.Tick + i) % every != 0 && mind.Version == board.Version) continue;
            var id = table.IdOf(i);
            var roles = board.Roles[(int)RoleOf(squad, id, cold)];
            if (!ReferenceEquals(mind.StanceFrom, roles) || mind.StanceHour != hour)
            {
                // The stance changes only with the role list (a package switch) and the hour.
                mind.Stance = StanceOf(mind, roles, hour);
                (mind.StanceFrom, mind.StanceHour) = (roles, hour);
            }
            ref var n = ref table.Next[i];
            if (!Holds(n, cold)) continue;
            var goal = Choose(world, id, mind, squad, board, roles, hour);
            Act(world, i, mind, goal, board);
        }
    }

    /// <summary>Whether the AI runs the character now: it has a mind and nothing else holds it (a fight, a flight, an order, a carry, a trade, a knock-out).</summary>
    public static bool Holds(in CharacterHot n, CharacterCold cold)
    {
        if (cold.Ai is not { } mind || cold.IsPlayer || cold.InCombat || cold.Fleeing || cold.Engaged || cold.IsImprisoned || !cold.CarriedBy.IsNone || !cold.Carrying.IsNone || cold.Medical is { Incapacitated: true }) return false;
        return (CharacterTask)n.Task switch
        {
            CharacterTask.Idle or CharacterTask.Wander or CharacterTask.Follow => true,
            // The walks to its spots, and lying or sitting there, are the AI's own.
            CharacterTask.GoTo => mind.Behaviour is AiBehaviour.Post or AiBehaviour.Sleep or AiBehaviour.Sit or AiBehaviour.GoHome or AiBehaviour.Patrol,
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

    /// <summary>
    /// The stance from the combat goals of its role list and race in their hours (the most aggressive wins); a character whose lists hold no combat goal at all keeps
    /// <see cref="AiStance.Aggressive"/>, the rule every NPC had before the AI (how the original turns a sighting into an attack is <b>Unknown</b>).
    /// </summary>
    internal static AiStance StanceOf(AiMind mind, IReadOnlyList<AiGoal> roles, int hour)
    {
        bool any = false;
        var stance = AiStance.Passive;
        void Take(AiGoal g)
        {
            if (TaskBehaviours.StanceOf(g.Task.Type) is not { } s) return;
            any = true;
            if (g.InWindow(hour) && s > stance) stance = s;
        }
        for (int k = 0; k < roles.Count; k++) Take(roles[k]);
        for (int k = 0; k < mind.RaceGoals.Count; k++) Take(mind.RaceGoals[k]);
        mind.HasCombatGoals = any;
        return any ? stance : AiStance.Aggressive;
    }

    AiGoal Choose(World world, CharacterId id, AiMind mind, Squad squad, Blackboard board, IReadOnlyList<AiGoal> roles, int hour)
    {
        var race = mind.RaceGoals;
        foreach (var tier in Tiers)
        {
            AiGoal? best = null;
            float bestScore = 0;
            void Consider(AiGoal g)
            {
                if (g.Task.Priority != tier || !g.InWindow(hour)) return;
                float desire = Desire(world, TaskBehaviours.Of(g.Task.Type), squad, board, id);
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

    /// <summary>
    /// How much the character wants a goal (the TaskData desire functions are not decoded): 1 for a goal it can act out, 0 for the rest; going home 0.5 while
    /// away from the home town and 0 in it (docs/game/perception.md 7.2, Observed).
    /// </summary>
    float Desire(World world, AiBehaviour behaviour, Squad squad, Blackboard board, CharacterId self) => behaviour switch
    {
        AiBehaviour.None => 0,
        AiBehaviour.Follow => squad.Leader.IsNone || squad.Leader == self || !world.Characters.IsAlive(squad.Leader) ? 0 : 1,
        AiBehaviour.GoHome => AtHomeTown(world, squad, board, self) ? 0 : 0.5f,
        _ => 1,
    };

    bool AtHomeTown(World world, Squad squad, Blackboard board, CharacterId self)
    {
        if (board.Town < 0 || !world.Characters.IsAlive(self)) return true;
        var site = data.Sites[board.Town];
        var p = world.Characters.Previous[self.Slot].Position;
        float r = site.Town.SizeRadius * site.Town.TownRadiusMult;
        return Vector2.DistanceSquared(new Vector2(p.X, p.Z), new Vector2(site.Position.X, site.Position.Z)) <= r * r;
    }

    /// <summary>Acts out a goal: sets the movement task it maps to, unless the character is already doing it.</summary>
    void Act(World world, int slot, AiMind mind, AiGoal goal, Blackboard board)
    {
        var table = world.Characters;
        var type = goal.Task.Type;
        var behaviour = TaskBehaviours.Of(type);
        if (mind.Goal is { } before && before.Task.Type != type) { mind.Walking = false; mind.FailedWalks = 0; }
        mind.Goal = goal;
        mind.Behaviour = behaviour;
        mind.Version = board.Version;
        mind.WanderRadius = 0;
        ref var n = ref table.Next[slot];
        bool leader = mind.Squad.Leader == table.IdOf(slot);
        switch (behaviour)
        {
            case AiBehaviour.Mill:
                // Round its post; at a borrowed home (the bar of a bar package) round that.
                mind.WanderCentre = board.Home is { } borrowed && borrowed != board.OwnHome ? Flat(borrowed.Position) : mind.Post;
                mind.WanderRadius = Settings.MillRadius;
                Become(table, slot, CharacterTask.Wander);
                break;
            case AiBehaviour.Roam:
                Become(table, slot, CharacterTask.Wander);
                break;
            case AiBehaviour.Follow:
                Become(table, slot, CharacterTask.Follow);
                break;
            case AiBehaviour.Patrol:
                if (board.CurrentClass is PackageClass.Patrol or PackageClass.PatrolRunning && board.RouteIndex < board.Route.Length)
                {
                    if (!leader) Become(table, slot, CharacterTask.Follow);
                    else WalkTo(table, slot, board.Route[board.RouteIndex], board.CurrentClass == PackageClass.PatrolRunning ? SpeedMode.Run : SpeedMode.Walk);
                }
                else Become(table, slot, CharacterTask.Wander);
                break;
            case AiBehaviour.Post:
            case AiBehaviour.Sleep:
            case AiBehaviour.GoHome:
                var (target, home) = SpotFor(mind, board, behaviour, type);
                if (GoToSpot(table, slot, mind, target, home, type == TaskType.RUN_AWAY_HOMETOWN ? SpeedMode.Run : SpeedMode.Walk))
                    Become(table, slot, behaviour == AiBehaviour.Sleep ? CharacterTask.Rest : CharacterTask.Idle);
                break;
            case AiBehaviour.Sit:
                // With a home, a seat in it (a bar or general node, else inside); without, where it stands.
                if (board.Home is null || mind.HomeUnreachable) Become(table, slot, CharacterTask.Rest);
                else
                {
                    var (seat, inHome) = SpotFor(mind, board, behaviour, type);
                    if (GoToSpot(table, slot, mind, seat, inHome, SpeedMode.Walk)) Become(table, slot, CharacterTask.Rest);
                }
                break;
        }
    }

    /// <summary>
    /// Where a goal is acted out: a node of the home building by goal (shopkeeper, guard, throne, general; any node when the home has none of that kind), a bed for
    /// sleep (the home itself when it has no bed), the home or the town for going home; else (no home, or its spots kept failing) the character's post. True in
    /// <c>Home</c> when the spot is the home's.
    /// </summary>
    (Vector2 Target, bool Home) SpotFor(AiMind mind, Blackboard board, AiBehaviour behaviour, TaskType type)
    {
        if (behaviour == AiBehaviour.GoHome)
        {
            if (board.Home is { } h && !mind.HomeUnreachable) return (Flat(h.Position), true);
            if (board.Town >= 0) return (Flat(data.Sites[board.Town].Position), true);
            return (mind.Squad.HomeCentre, true);
        }
        if (board.Home is not { } home || mind.HomeUnreachable || type is TaskType.HOLD_POSITION or TaskType.MAN_THE_GATE) return (mind.Post, false);
        if (behaviour == AiBehaviour.Sit)
        {
            if ((Pick(home, HomeSpotKind.Bar, mind.MemberIndex) ?? Pick(home, HomeSpotKind.General, mind.MemberIndex)) is { } seat) return (Flat(seat.Position), true);
            return (Flat(home.Position), true);
        }
        HomeSpotKind kind = behaviour == AiBehaviour.Sleep ? HomeSpotKind.Bed : type switch
        {
            TaskType.STAND_AT_SHOPKEEPER_NODE => HomeSpotKind.Shopkeeper,
            TaskType.STAND_AT_GUARD_NODE_HOMEBUILDING or TaskType.STAND_AT_GUARD_NODE_HOMEBUILDING_INDOORS_ONLY or TaskType.STAND_AT_GUARD_NODE_HOMETOWN_OUTSIDE => HomeSpotKind.Guard,
            TaskType.SIT_ON_THRONE => HomeSpotKind.Throne,
            _ => HomeSpotKind.General,
        };
        if (Pick(home, kind, mind.MemberIndex) is { } spot) return (Flat(spot.Position), true);
        if (kind != HomeSpotKind.Bed && Pick(home, null, mind.MemberIndex) is { } any) return (Flat(any.Position), true);
        // No bed: it sleeps in the house; a node goal without nodes stays at its own post, except staying home.
        if (kind == HomeSpotKind.Bed || type == TaskType.STAY_IN_HOME) return (Flat(home.Position), true);
        return (mind.Post, false);
    }

    /// <summary>The <paramref name="index"/>-th spot of a kind (null: any node) of a building, round robin; null when it has none.</summary>
    static HomeSpot? Pick(TownBuilding home, HomeSpotKind? kind, int index)
    {
        int count = 0;
        foreach (var s in home.Spots) if (kind is { } k ? s.Kind == k : s.Kind != HomeSpotKind.Bed) count++;
        if (count == 0) return null;
        int want = index % count;
        foreach (var s in home.Spots)
            if ((kind is { } k ? s.Kind == k : s.Kind != HomeSpotKind.Bed) && want-- == 0) return s;
        return null;
    }

    static Vector2 Flat(Vector3 p) => new(p.X, p.Z);

    /// <summary>
    /// Walks the character to a spot; true when it is there (and not walking). A walk that ends away from the spot counts as failed; after
    /// <see cref="MaxFailedWalks"/> in a row it gives the home's spots up (<see cref="AiMind.HomeUnreachable"/>), or makes the spot it stands on its post.
    /// </summary>
    bool GoToSpot(CharacterTable table, int slot, AiMind mind, Vector2 target, bool home, byte mode)
    {
        ref var n = ref table.Next[slot];
        var here = new Vector2(n.Position.X, n.Position.Z);
        bool away = Vector2.DistanceSquared(here, target) > Settings.PostSlack * Settings.PostSlack;
        if (mind.Walking && n.Task != (byte)CharacterTask.GoTo)
        {
            // The walk is over: arrived, or it ended elsewhere (no way there, or a fight took it).
            mind.Walking = false;
            mind.FailedWalks = away ? mind.FailedWalks + 1 : 0;
            if (mind.FailedWalks >= MaxFailedWalks)
            {
                mind.FailedWalks = 0;
                if (home) mind.HomeUnreachable = true;
                else mind.Post = here;
                return false;   // the next think picks the new spot
            }
        }
        if (away)
        {
            WalkTo(table, slot, target, mode);
            mind.Walking = true;
            return false;
        }
        return n.Task != (byte)CharacterTask.GoTo;   // arriving: the movement system ends the walk
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

    /// <summary>Sends it walking (or running) to a point, unless it is going there already.</summary>
    static void WalkTo(CharacterTable table, int slot, Vector2 to, byte mode)
    {
        ref var n = ref table.Next[slot];
        if (n.Task == (byte)CharacterTask.GoTo && Vector2.DistanceSquared(n.Goal, to) < 1) { n.Mode = mode; return; }
        MovementSystem.GoTo(table, slot, to, mode);
    }

    // ------------------------------------------------------------------ serial steps

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is not StartContract c || world.Squads.Find(c.SquadId) is not { } squad || data.Ai.Package(c.PackageId) is not { } package) continue;
            if (squad.Ai is null)
            {
                if (IsPlayers(world, squad)) continue;
                Attach(world, squad);
            }
            rules.Bind(this, world, squad);
            squad.Ai!.StartContract(package, world.GameHours, c.Hours, rules);
        }
    }

    public void SlowWorld(World world)
    {
        var table = world.Characters;
        bool cadence = world.Tick % Math.Max(1, Settings.PackageEveryTicks) == 0;
        foreach (var squad in world.Squads.All)
        {
            if (squad.Ai is null)
            {
                if (!looked.Add(squad.Id) || !Runs(world, squad)) continue;
                Attach(world, squad);
            }
            else if (!cadence) continue;   // the rest is looked at on the package cadence only (and when the squad appears)
            var board = squad.Ai!;
            board.Intact = IsIntact(world, squad, board);
            rules.Bind(this, world, squad);
            board.Update(rules, world.GameHours);
            AdvanceRoute(world, squad, board);
            board.Crowd.Clear();
            for (int k = 0; k < squad.Members.Count; k++)
            {
                if (!table.TryResolveNext(squad.Members[k], out int s)) continue;
                var cold = table.Cold(s)!;
                if (cold.Ai is null)
                {
                    var p = table.Next[s].Position;
                    cold.Ai = new AiMind { Squad = squad, MemberIndex = k, Post = new Vector2(p.X, p.Z), RaceGoals = cold.Race is { } race ? data.Ai.RaceGoals(race.StringId) : [] };
                }
                if (cold.Ai.Goal is { } g) board.Crowd[g.Task.Type] = board.Crowd.GetValueOrDefault(g.Task.Type) + 1;
            }
        }
    }

    void Attach(World world, Squad squad)
    {
        var (packages, template) = TemplateOf(squad.TemplateId);
        var home = AssignHome(world, squad, template);
        // The template's initial door state applies to the home it is given (docs/game/locks.md "Doors"); the first squad's wins, a saved building keeps its own.
        if (home is not null) world.Fixtures.ApplyInitialDoorState(home.InstanceId, template?.InitialDoorState ?? 0);
        ulong roll = Rng.Hash(world.Seed, (ulong)squad.Id, RngPurpose.Think, SeedSalts.AiPatrol);
        squad.Ai = new Blackboard(packages)
        {
            Town = squad.Town, Size = squad.Members.Count, OwnHome = home, Home = home,
            NextPatrol = world.GameHours + Rng.Float(roll) * Settings.FirstPatrolHours,
        };
    }

    /// <summary>The AI runs a squad that is not the player's, not a roaming platoon's active half, and whose template lists AI packages.</summary>
    bool Runs(World world, Squad squad) => squad.PlatoonId < 0 && !IsPlayers(world, squad) && TemplateOf(squad.TemplateId).Packages.Count > 0;

    bool IsPlayers(World world, Squad squad)
    {
        if (squad.Faction == data.PlayerFaction && data.PlayerFaction >= 0) return true;
        foreach (var m in squad.Members)
            if (world.Characters.TryResolveNext(m, out int s) && world.Characters.Cold(s)!.IsPlayer) return true;
        return false;
    }

    (IReadOnlyList<SlottedPackage> Packages, SquadTemplate? Template) TemplateOf(string templateId)
    {
        if (templates.TryGetValue(templateId, out var known)) return known;
        var template = data.Db.Find(templateId) is { } r && SquadFactory.IsTemplate(r) ? SquadTemplate.From(r) : null;
        var entry = (template is null ? (IReadOnlyList<SlottedPackage>)[] : data.Ai.Packages(template), template);
        templates[templateId] = entry;
        return entry;
    }

    /// <summary>
    /// The squad's own building (<b>engine choice</b>: how the original picks a home is <b>Unknown</b>, docs/game/factions-squads-towns.md 6.4): among its town's
    /// buildings no other squad has, the nearest of the template's <c>building</c>; else by <c>building designation</c> a shop (SHOP) or the bar (BAR, shared);
    /// else the nearest with beds, else the nearest at all. None for a squad without a town or a town without building data.
    /// </summary>
    TownBuilding? AssignHome(World world, Squad squad, SquadTemplate? template)
    {
        if (squad.Town < 0 || data.Sites[squad.Town].Buildings is not { Count: > 0 } buildings) return null;
        var taken = new HashSet<TownBuilding>(ReferenceEqualityComparer.Instance);
        foreach (var other in world.Squads.All)
            if (other.Ai is { OwnHome: { } h } && other.Town == squad.Town) taken.Add(h);
        var at = new Vector2(squad.Position.X, squad.Position.Z);
        TownBuilding? Nearest(Func<TownBuilding, bool> accept, bool shared = false)
        {
            TownBuilding? best = null;
            float bestD = float.MaxValue;
            foreach (var b in buildings)
            {
                if (!accept(b) || (!shared && taken.Contains(b))) continue;
                float d = Vector2.DistanceSquared(at, Flat(b.Position));
                if (d < bestD) (best, bestD) = (b, d);
            }
            return best;
        }
        if (template?.Building is { } wanted && Nearest(b => b.BuildingId == wanted.Id) is { } own) return own;
        if (template?.BuildingDesignation == 1 && Nearest(b => b.IsShop) is { } shop) return shop;
        if (template?.BuildingDesignation == 3 && Nearest(b => b.IsBar, shared: true) is { } bar) return bar;
        return Nearest(b => b.Beds > 0) ?? Nearest(_ => true);
    }

    /// <summary>
    /// The intact rule of docs/game/perception.md 7.3 (Observed): able members are alive and not down; a squad of fewer than 4 is intact while its leader is up
    /// (while retreating, while anyone is up); a bigger one while at least 0.2 of its size is able (0.4 when the leader is down).
    /// </summary>
    static bool IsIntact(World world, Squad squad, Blackboard board)
    {
        var table = world.Characters;
        int able = 0;
        foreach (var m in squad.Members)
            if (table.TryResolveNext(m, out int s) && table.Cold(s)!.Medical is not { Incapacitated: true }) able++;
        bool leaderDown = squad.Leader.IsNone || !table.TryResolveNext(squad.Leader, out int ls) || table.Cold(ls)!.Medical is { Incapacitated: true };
        int size = Math.Max(board.Size, 1);
        if (size < 4) return board.CurrentClass == PackageClass.Retreat ? able > 0 : !leaderDown;
        return able >= size * (leaderDown ? 0.4f : 0.2f);
    }

    /// <summary>A running patrol moves on when its leader reaches the route point; the members think again at once.</summary>
    void AdvanceRoute(World world, Squad squad, Blackboard board)
    {
        if (board.CurrentClass is not (PackageClass.Patrol or PackageClass.PatrolRunning) || board.RouteIndex >= board.Route.Length) return;
        if (!world.Characters.TryResolveNext(squad.Leader, out int s)) return;
        var p = world.Characters.Next[s].Position;
        if (Vector2.Distance(new Vector2(p.X, p.Z), board.Route[board.RouteIndex]) > Settings.RouteArrive) return;
        board.RouteIndex++;
        board.Touch();
    }

    /// <summary>The package-class rules of <see cref="Blackboard.Update"/>, bound to the squad being updated (serial steps only).</summary>
    sealed class Rules : IPackageRules
    {
        AiSystem system = null!;
        World world = null!;
        Squad squad = null!;

        public void Bind(AiSystem s, World w, Squad q) => (system, world, squad) = (s, w, q);

        public bool CanStart(Blackboard board, AiPackage package, PackageClass kind) => kind switch
        {
            PackageClass.Retreat => !board.Intact,
            PackageClass.Patrol or PackageClass.PatrolRunning => board.Town >= 0 && world.GameHours >= board.NextPatrol && world.Characters.IsAlive(squad.Leader),
            // With building data, only where the town has a bar; without, the package runs and the squad keeps its spots.
            PackageClass.Bar => board.Town < 0 || system.Data.Sites[board.Town].Buildings.Count == 0 || system.TownBar(board.Town) is not null,
            PackageClass.BarOutdoors => true,
            _ => false,
        };

        public bool Finished(Blackboard board, AiPackage package, PackageClass kind) => kind switch
        {
            PackageClass.Retreat => board.Intact,
            PackageClass.Patrol or PackageClass.PatrolRunning => board.RouteIndex >= board.Route.Length || world.GameHours >= board.PatrolGiveUp || !world.Characters.IsAlive(squad.Leader),
            _ => false,
        };

        public void Started(Blackboard board, AiPackage package, PackageClass kind)
        {
            switch (kind)
            {
                case PackageClass.Patrol:
                case PackageClass.PatrolRunning:
                    system.LayRoute(world, squad, board);
                    break;
                case PackageClass.Bar:
                    if (board.Town >= 0 && system.TownBar(board.Town) is { } bar) board.Home = bar;
                    break;
                case PackageClass.BarOutdoors:
                    board.Home = null;
                    break;
            }
        }
    }

    TownBuilding? TownBar(int site)
    {
        foreach (var b in data.Sites[site].Buildings) if (b.IsBar) return b;
        return null;
    }

    /// <summary>
    /// A patrol's route (<b>engine choice</b>: the original's <c>GoOutOnPatrol</c> is not decoded): <see cref="AiSettings.PatrolPoints"/> points round the home town at
    /// seeded angles and distances of 1.5 to 3 town radii, each pulled in towards the town until walkable (dropped when none is), then home. The next patrol may start
    /// 6 to 12 game hours after this one started; this one gives up after 6.
    /// </summary>
    void LayRoute(World world, Squad squad, Blackboard board)
    {
        if (board.Town < 0) { board.Route = []; return; }
        var site = data.Sites[board.Town];
        var centre = Flat(site.Position);
        float r = MathF.Max(site.Town.SizeRadius * site.Town.TownRadiusMult, 100);
        ulong key = Rng.Mix((ulong)squad.Id << 20 ^ (ulong)board.Version);
        float start = Rng.Float(Rng.Hash(world.Seed, key, RngPurpose.Think, SeedSalts.AiPatrol)) * MathF.Tau;
        var route = new List<Vector2>();
        for (int k = 0; k < Settings.PatrolPoints; k++)
        {
            float angle = start + k * MathF.Tau / Settings.PatrolPoints;
            float f = Rng.Float(Rng.Hash(world.Seed, key, RngPurpose.Think, (ulong)k + 1));
            float d = r * (Settings.PatrolRadiusMin + f * (Settings.PatrolRadiusMax - Settings.PatrolRadiusMin));
            for (int tries = 0; tries < 4; tries++, d *= 0.75f)
            {
                var p = centre + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * d;
                if (!world.Walkability.IsWalkable(p.X, p.Y)) continue;
                route.Add(p);
                break;
            }
        }
        route.Add(board.OwnHome is { } home ? Flat(home.Position) : centre);
        board.Route = [.. route];
        board.RouteIndex = 0;
        float every = Settings.PatrolEveryHoursMin + Rng.Float(Rng.Hash(world.Seed, key, RngPurpose.Think, 0x50)) * (Settings.PatrolEveryHoursMax - Settings.PatrolEveryHoursMin);
        board.NextPatrol = world.GameHours + every;
        board.PatrolGiveUp = world.GameHours + Settings.PatrolGiveUpHours;
    }
}
