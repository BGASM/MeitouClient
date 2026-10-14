using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Data.World;
using static Meitou.Tests.Simulation.SyntheticTown;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>The AI (docs/simulation.md "AI"): the package and goal data as docs/game/ai.md reads them, the package choice, and a town that patrols by day and sleeps at night.</summary>
public class AiTests(ITestOutputHelper output)
{
    static FcsRecord Task(string id, TaskType type, TaskPriority tier, TaskTargetType target = TaskTargetType.Specific)
    {
        var r = Rec(id, FcsRecordType.AI_TASK, type.ToString());
        r.Ints["enum"] = (int)type;
        r.Ints["classification"] = (int)tier;
        r.Ints["targeting"] = (int)target;
        r.Ints["ending"] = 0;
        return r;
    }

    /// <summary>
    /// Two packages: "Town base" (the leader keeps the shop; the others patrol 6 to 22, go to bed 22 to 6 at half weight, and have an urgent goal the engine
    /// does not act out) and "Town" inheriting it (a duplicate shop goal at weight 0.5, which the parent's beats in the leader list and which its squad list takes
    /// by the fallback, and a slave goal); a war package of a class not built (signal 20); a running patrol (signal 23: the leader patrols, the others follow);
    /// a retreat (signal 5: everyone goes home); a timed contract (signal 33: everyone sits). The resident Patrol template lists "Town" at slot 0 and the war at slot 1
    /// unless a test gives it others.
    /// </summary>
    internal static List<FcsRecord> Records()
    {
        var shop = Task("100-t", TaskType.STAND_AT_SHOPKEEPER_NODE, TaskPriority.NonUrgent);
        var patrol = Task("101-t", TaskType.PATROL_TOWN, TaskPriority.NonUrgent);
        var bed = Task("102-t", TaskType.GO_HOME_AND_GO_TO_BED, TaskPriority.NonUrgent);
        var raid = Task("103-t", TaskType.RAID_TOWN, TaskPriority.Urgent);
        var sit = Task("105-t", TaskType.SIT_AROUND, TaskPriority.Fluff);
        var parent = Rec("110-t", FcsRecordType.AI_PACKAGE, "Town base");
        parent.References["Leader AI Goals"] = [Ref("100-t", 0, 100, 0)];
        parent.References["Squad AI Goals"] = [Ref("101-t", 6, 22, 0), Ref("102-t", 22, 6, 50), Ref("103-t", 0, 100, 0)];
        var child = Rec("111-t", FcsRecordType.AI_PACKAGE, "Town");
        child.References["inherits from"] = [Ref("110-t")];
        child.References["Leader AI Goals"] = [Ref("100-t", 0, 0, 50)];
        child.References["Slave AI Goals"] = [Ref("105-t", 0, 100, 0)];
        var war = Rec("112-t", FcsRecordType.AI_PACKAGE, "War");
        war.Ints["signal func"] = 20;
        war.References["Squad AI Goals"] = [Ref("101-t")];
        var walk = Task("120-t", TaskType.PATROL, TaskPriority.NonUrgent);
        var follow = Task("121-t", TaskType.FOLLOW_SQUADLEADER, TaskPriority.NonUrgent, TaskTargetType.Leader);
        var retreat = Task("122-t", TaskType.RETREAT_HOMETOWN, TaskPriority.NonUrgent);
        var outing = Rec("113-t", FcsRecordType.AI_PACKAGE, "Go out on patrol");
        outing.Ints["signal func"] = 23;
        outing.References["Leader AI Goals"] = [Ref("120-t")];
        outing.References["Squad AI Goals"] = [Ref("121-t")];
        var refresh = Rec("114-t", FcsRecordType.AI_PACKAGE, "Retreat and refresh");
        refresh.Ints["signal func"] = 5;
        refresh.References["Leader AI Goals"] = [Ref("122-t")];
        var hired = Rec("115-t", FcsRecordType.AI_PACKAGE, "Timed contract");
        hired.Ints["signal func"] = 33;
        hired.References["Leader AI Goals"] = [Ref("105-t", 0, 100, 0)];
        return [shop, patrol, bed, raid, sit, parent, child, war, walk, follow, retreat, outing, refresh, hired];
    }

    internal static GameDatabase Database(int residentSquads = 3, params (string Id, int Slot)[] packages) =>
        SyntheticTown.Database(residentSquads, extra: Records(), changePatrol: p => p.References["AI packages"] =
            packages.Length == 0 ? [Ref("111-t", 0), Ref("112-t", 1)] : [.. packages.Select(x => Ref(x.Id, x.Slot))]);

    [Fact]
    public void Task_types_carry_the_game_numbers()
    {
        Assert.Equal(291, Enum.GetValues<TaskType>().Length);
        Assert.Equal(14, (int)TaskType.IDLE);
        Assert.Equal(153, (int)TaskType.GO_HOME_AND_GO_TO_BED);
        Assert.Equal(255, (int)TaskType.SIT_ON_THRONE);
        Assert.Equal(288, (int)TaskType.PROTECT_ALLIES_STAY_IN_TOWN);
        Assert.Equal(290, (int)TaskType.Unnamed290);
        Assert.All(TaskBehaviours.Built, t => Assert.NotEqual(AiBehaviour.None, TaskBehaviours.Of(t)));
        Assert.Equal("Patrolling town", TaskBehaviours.Text(TaskType.PATROL_TOWN));
    }

    [Theory]
    [InlineData(6, 22, 5, false)]
    [InlineData(6, 22, 6, true)]
    [InlineData(6, 22, 22, false)]
    [InlineData(22, 6, 23, true)]
    [InlineData(22, 6, 3, true)]
    [InlineData(22, 6, 6, false)]
    [InlineData(0, 0, 13, true)]
    [InlineData(0, 100, 23, true)]
    public void Hour_windows_wrap_midnight(int start, int end, int hour, bool open)
    {
        var goal = new AiGoal(AiSystem.Idle.Task, start, end, 1);
        Assert.Equal(open, goal.InWindow(hour));
    }

    [Fact]
    public void Packages_load_the_parent_first_and_the_parent_wins()
    {
        var ai = new AiDatabase(Database());
        var town = ai.Package("111-t")!;
        // The leader's shop goal comes from the parent: weight 1 (val2 0), window (0, 100); the child's duplicate is dropped.
        var lead = Assert.Single(town.Goals(AiRole.Leader));
        Assert.Equal((TaskType.STAND_AT_SHOPKEEPER_NODE, 0, 100, 1f), (lead.Task.Type, lead.Start, lead.End, lead.Weight));
        // Squad: the parent's own list, then the child's, which has no squad goals and falls back to its leader goals.
        Assert.Equal([TaskType.PATROL_TOWN, TaskType.GO_HOME_AND_GO_TO_BED, TaskType.RAID_TOWN, TaskType.STAND_AT_SHOPKEEPER_NODE], town.Goals(AiRole.Squad).Select(g => g.Task.Type));
        Assert.Equal(0.5f, town.Goals(AiRole.Squad)[1].Weight);
        Assert.Equal((0, 0, 0.5f), (town.Goals(AiRole.Squad)[3].Start, town.Goals(AiRole.Squad)[3].End, town.Goals(AiRole.Squad)[3].Weight));
        Assert.Equal(town.Goals(AiRole.Squad).Select(g => g.Task.Type), town.Goals(AiRole.Squad2).Select(g => g.Task.Type));
        Assert.Equal([TaskType.SIT_AROUND], town.Goals(AiRole.Slave).Select(g => g.Task.Type));
        Assert.Empty(town.Goals(AiRole.Unused));
        Assert.True(town.IsPlain);
        Assert.False(ai.Package("112-t")!.IsPlain);
        Assert.Equal(0, ai.Problems);
    }

    [Fact]
    public void Template_packages_keep_their_slots()
    {
        var db = Database();
        var ai = new AiDatabase(db);
        var list = ai.Packages(Meitou.Data.Gameplay.SquadTemplate.From(db.Find("10-t")!));
        Assert.Equal([(0, "111-t"), (1, "112-t")], list.Select(p => (p.Slot, p.Package.Id)));
    }

    static AiPackage Package(string id, int signal = 0, string? leadsTo = null) =>
        new() { Id = id, Name = id, Signal = signal, LeadsTo = leadsTo, Roles = [[], [], [], [], []] };

    [Fact]
    public void The_highest_slot_that_can_start_wins()
    {
        var town = Package("town");
        var patrol = Package("patrol", signal: 6);   // GoOutOnPatrol: not built, so it never starts by itself
        var bar = Package("bar", signal: 0);
        var board = new Blackboard([new(0, town), new(1, patrol)]);
        board.Update();
        Assert.Same(town, board.Current);
        board = new Blackboard([new(0, town), new(2, bar), new(1, patrol)]);
        board.Update();
        Assert.Same(bar, board.Current);
        Assert.Equal(2, board.CurrentSlot);
        int version = board.Version;
        board.Update();
        Assert.Equal(version, board.Version);   // the current package stays
    }

    [Fact]
    public void A_lone_slot_zero_package_starts_even_when_it_cannot()
    {
        var patrol = Package("patrol", signal: 6);
        var board = new Blackboard([new(0, patrol)]);
        board.Update();
        Assert.Same(patrol, board.Current);
        // Two such packages in slot 0: neither can start.
        board = new Blackboard([new(0, patrol), new(0, Package("other", signal: 6))]);
        board.Update();
        Assert.Null(board.Current);
    }

    static SimWorld Town(int threads, out PopulationData data) => Town(threads, out data, null, null);

    static SimWorld Town(int threads, out PopulationData data, PopulationData? given, AiSettings? settings, double startHours = 24 + 13)
    {
        data = given ?? SyntheticTown.Data(Database());
        var walk = new OpenGroundWalkability(Ground);
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
        {
            Parts = StandardParts.Population | StandardParts.Movement | StandardParts.Ai, SynchronousPaths = true, Ai = settings,
        }).Systems;
        var world = new SimWorld(new WorldSettings { Seed = 5, Threads = threads, StartHours = startHours, MinPartitionSize = 4 }, walk, systems);
        world.Commands.Enqueue(new FocusCommand(Centre) { Tick = 0 });
        return world;
    }

    [Fact]
    public void Residents_keep_shop_by_day_and_go_to_bed_at_night()
    {
        using var world = Town(1, out _);
        world.RunTicks(90);
        var squads = world.Squads.All.Where(s => s.TemplateId == "10-t").ToList();
        Assert.NotEmpty(squads);
        var table = world.Characters;
        foreach (var squad in squads)
        {
            Assert.Equal("111-t", squad.Ai?.Current?.Id);
            foreach (var m in squad.Members)
            {
                Assert.True(table.TryResolveNext(m, out int s));
                var mind = table.Cold(s)!.Ai!;
                var task = (CharacterTask)table.Previous[s].Task;
                if (m == squad.Leader)
                {
                    Assert.Equal(TaskType.STAND_AT_SHOPKEEPER_NODE, mind.Goal?.Task.Type);
                    Assert.Contains(task, new[] { CharacterTask.Idle, CharacterTask.GoTo });
                }
                else
                {
                    // 13:00: the patrol (6 to 22) beats the bed (out of window) and the shop at 0.5; the raid is urgent but not acted out.
                    Assert.Equal(TaskType.PATROL_TOWN, mind.Goal?.Task.Type);
                    Assert.Equal(CharacterTask.Wander, task);
                }
            }
        }
        var leader = world.Snapshot.Characters.Single(c => c.Id == squads[0].Leader);
        Assert.Equal("Shopkeeper", leader.Activity);

        // 22:30: the patrol is out of its window; the members walk back to their spots and lie down.
        world.GameHours = 24 + 22.5;
        world.RunTicks(900);
        foreach (var squad in squads)
            foreach (var m in squad.Members.Where(m => m != squad.Leader))
            {
                Assert.True(table.TryResolveNext(m, out int s));
                var mind = table.Cold(s)!.Ai!;
                Assert.Equal(TaskType.GO_HOME_AND_GO_TO_BED, mind.Goal?.Task.Type);
                Assert.Equal(CharacterTask.Rest, (CharacterTask)table.Previous[s].Task);
                var p = table.Previous[s].Position;
                Assert.InRange(Vector2.Distance(new Vector2(p.X, p.Z), mind.Post), 0, 12 + 1);
            }
        Assert.Contains(world.Snapshot.Characters, c => c.Activity == "Going to bed");
    }

    [Fact]
    public void Squads_without_packages_keep_the_old_routine()
    {
        using var world = Town(1, out _);
        world.RunTicks(60);
        var folk = world.Squads.All.Where(s => s.TemplateId == "12-t").ToList();
        Assert.NotEmpty(folk);
        Assert.All(folk, s => Assert.Null(s.Ai));
        Assert.All(folk.SelectMany(s => s.Members), m => Assert.Null(world.Characters.Cold(m.Slot)!.Ai));
    }

    [Fact]
    public void The_AI_is_the_same_at_any_thread_count()
    {
        ulong Run(int threads)
        {
            using var world = Town(threads, out _);
            world.RunTicks(120);
            world.GameHours = 24 + 23;
            world.RunTicks(240);
            return world.StateHash();
        }
        Assert.Equal(Run(1), Run(4));
    }

    [Fact]
    public void The_AI_lets_go_of_a_character_something_else_holds()
    {
        using var world = Town(1, out _);
        world.RunTicks(60);
        var squad = world.Squads.All.First(s => s.TemplateId == "10-t");
        var m = squad.Members.First(m => m != squad.Leader);
        var cold = world.Characters.Cold(m.Slot)!;
        var hot = world.Characters.Previous[m.Slot];
        Assert.True(AiSystem.Holds(hot, cold));
        cold.InCombat = true;
        Assert.False(AiSystem.Holds(hot, cold));
        cold.InCombat = false;
        hot.Task = (byte)CharacterTask.Attack;
        Assert.False(AiSystem.Holds(hot, cold));
        hot.Task = (byte)CharacterTask.Rest;   // resting by order, not the AI's sleep
        Assert.False(AiSystem.Holds(hot, cold));
    }

    /// <summary>The counts docs/game/ai.md gives for the merged base game (gamedata.base, Newwworld, Dialogue, rebirth), read through <see cref="AiDatabase"/>.</summary>
    [Fact, Slow]
    public void The_base_game_AI_records_load_as_documented()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var packages = db.OfType(FcsRecordType.AI_PACKAGE).ToList();
        var tasks = db.OfType(FcsRecordType.AI_TASK).ToList();
        Assert.Equal(219, packages.Count);
        Assert.Equal(124, tasks.Count);
        int Entries(string list) => packages.Sum(p => p.GetReferences(list).Count);
        Assert.Equal((804, 561, 58, 64), (Entries("Leader AI Goals"), Entries("Squad AI Goals"), Entries("Squad2 AI Goals"), Entries("Slave AI Goals")));
        var ai = new AiDatabase(db);
        Assert.All(tasks, t => Assert.InRange((int)ai.Task(t.StringId)!.Type, 1, 290));
        Assert.All(tasks, t => Assert.InRange((int)ai.Task(t.StringId)!.Priority, 1, 3));
        foreach (var p in packages) Assert.NotNull(ai.Package(p.StringId));
        Assert.Equal(0, ai.Problems);
        var templates = Meitou.Data.Gameplay.SquadTemplate.LoadAll(db);
        int withPackages = templates.Count(t => ai.Packages(t).Count > 0);
        var used = tasks.Select(t => ai.Task(t.StringId)!.Type).Distinct().ToList();
        var built = used.Where(t => TaskBehaviours.Of(t) != AiBehaviour.None).ToList();
        output.WriteLine($"{withPackages} templates with packages; {built.Count} of {used.Count} task types used acted out: {string.Join(", ", built)}");
        Assert.Equal(0, ai.Problems);
    }

    // ------------------------------------------------------------------ homes, package classes, stances, platoons

    /// <summary>The synthetic town with two buildings: a shop (a shopkeeper node, two beds) east of the centre and a house (three beds) west of it.</summary>
    static PopulationData WithHomes(GameDatabase db, out TownBuilding shop, out TownBuilding house)
    {
        Vector3 At(float dx, float dz) => new(Centre.X + dx, 300, Centre.Z + dz);
        shop = new TownBuilding("shop", "b-shop", "Shop", At(80, 0), 0, true, false,
            [new HomeSpot(HomeSpotKind.Shopkeeper, At(80, 12), 0), new HomeSpot(HomeSpotKind.Bed, At(90, 0), 0), new HomeSpot(HomeSpotKind.Bed, At(90, -10), 0)]);
        house = new TownBuilding("house", "b-house", "House", At(-80, 0), 0, false, false,
            [new HomeSpot(HomeSpotKind.Bed, At(-90, 0), 0), new HomeSpot(HomeSpotKind.Bed, At(-90, 10), 0), new HomeSpot(HomeSpotKind.Bed, At(-90, 20), 0)]);
        return PopulationData.Create(db, [new TownPlacement("inst", "30-t", Centre, null)], buildings: new Dictionary<string, List<TownBuilding>> { ["inst"] = [shop, house] });
    }

    static float Off(SimWorld world, CharacterId id, Vector3 spot)
    {
        var p = world.Characters.Previous[id.Slot].Position;
        return Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(spot.X, spot.Z));
    }

    [Fact]
    public void Squads_get_homes_keep_shop_at_its_node_and_sleep_in_their_beds()
    {
        var data = WithHomes(Database(2), out var shop, out var house);
        using var world = Town(1, out _, data, null);
        world.RunTicks(400);
        var squads = world.Squads.All.Where(s => s.TemplateId == "10-t").ToList();
        Assert.Equal(2, squads.Count);
        // One home each, none shared.
        Assert.Equal(2, squads.Select(s => s.Ai!.Home).Distinct().Count());
        Assert.All(squads, s => Assert.Contains(s.Ai!.Home, new[] { shop, house }));
        // The shop's leader stands at the shopkeeper node; the house has no node, so its leader keeps its own post.
        var shopSquad = squads.Single(s => s.Ai!.Home == shop);
        Assert.InRange(Off(world, shopSquad.Leader, shop.Spots[0].Position), 0, 13);
        Assert.Equal(CharacterTask.Idle, (CharacterTask)world.Characters.Previous[shopSquad.Leader.Slot].Task);

        world.GameHours = 24 + 22.5;
        world.RunTicks(1200);
        foreach (var squad in squads)
        {
            var beds = squad.Ai!.Home!.Of(HomeSpotKind.Bed).Select(b => b.Position).ToList();
            foreach (var m in squad.Members.Where(m => m != squad.Leader))
            {
                Assert.Equal(CharacterTask.Rest, (CharacterTask)world.Characters.Previous[m.Slot].Task);
                Assert.InRange(beds.Min(b => Off(world, m, b)), 0, 13);
            }
        }
    }

    [Fact]
    public void A_patrol_walks_its_route_round_the_town_and_comes_home()
    {
        var data = SyntheticTown.Data(Database(1, ("111-t", 0), ("113-t", 1)));
        var settings = new AiSettings { FirstPatrolHours = 0, PatrolPoints = 3, PatrolRadiusMin = 0.4f, PatrolRadiusMax = 0.5f };
        using var world = Town(1, out _, data, settings);
        world.RunTicks(60);
        var squad = world.Squads.All.Single(s => s.TemplateId == "10-t");
        var board = squad.Ai!;
        Assert.Equal(PackageClass.PatrolRunning, board.CurrentClass);
        Assert.Equal(4, board.Route.Length);   // three points and home
        var table = world.Characters;
        Assert.Equal(TaskType.PATROL, table.Cold(squad.Leader.Slot)!.Ai!.Goal?.Task.Type);
        Assert.Equal(CharacterTask.GoTo, (CharacterTask)table.Previous[squad.Leader.Slot].Task);
        var mate = squad.Members.First(m => m != squad.Leader && table.Cold(m.Slot)!.Role != (int)SquadRole.Leader);
        Assert.Equal(TaskType.FOLLOW_SQUADLEADER, table.Cold(mate.Slot)!.Ai!.Goal?.Task.Type);
        Assert.Equal(CharacterTask.Follow, (CharacterTask)table.Previous[mate.Slot].Task);
        Assert.Equal("Patrolling", world.Snapshot.Characters.Single(c => c.Id == squad.Leader).Activity);
        // Out round the town and back: the patrol finishes and the town package runs again; the next patrol is hours away.
        double started = world.GameHours;
        for (int t = 0; t < 6000 && board.CurrentClass == PackageClass.PatrolRunning; t += 30) world.RunTicks(30);
        Assert.Equal("111-t", board.Current?.Id);
        Assert.InRange(board.NextPatrol, started + 6, started + 12);
    }

    [Fact]
    public void A_broken_squad_retreats_to_its_home_town()
    {
        var data = SyntheticTown.Data(Database(1, ("111-t", 0), ("114-t", 2)));
        using var world = Town(1, out _, data, null);
        world.RunTicks(60);
        var squad = world.Squads.All.Single(s => s.TemplateId == "10-t");
        var board = squad.Ai!;
        Assert.Equal("111-t", board.Current?.Id);
        Assert.True(board.Intact);
        // All but one gone, of a squad counted as ten: fewer than 0.2 x 10 are able.
        var table = world.Characters;
        var last = squad.Members[^1];
        foreach (var m in squad.Members.Take(squad.Members.Count - 1).ToList()) table.Remove(m);
        board.Size = 10;
        // Sent away from town by an order (the AI leaves an ordered walk alone); there it wants to go home.
        world.Commands.Enqueue(new MoveOrder([last], new Vector3(Centre.X + 900, 300, Centre.Z)) { Tick = world.Tick });
        world.RunTicks(30);
        Assert.False(board.Intact);
        Assert.Equal(PackageClass.Retreat, board.CurrentClass);
        for (int t = 0; t < 1500 && Off(world, last, Centre) < 850; t += 30) world.RunTicks(30);
        world.RunTicks(60);
        Assert.Equal(TaskType.RETREAT_HOMETOWN, table.Cold(last.Slot)!.Ai!.Goal?.Task.Type);
        world.RunTicks(2000);
        // Home: within the town, where going home is no longer wanted.
        Assert.InRange(Off(world, last, Centre), 0, 300);
        Assert.NotEqual(TaskType.RETREAT_HOMETOWN, table.Cold(last.Slot)!.Ai!.Goal?.Task.Type);
    }

    [Fact]
    public void A_contract_overrides_the_slots_until_its_hours_are_over()
    {
        using var world = Town(1, out _);
        world.RunTicks(60);
        var squad = world.Squads.All.First(s => s.TemplateId == "10-t");
        world.Commands.Enqueue(new StartContract(squad.Id, "115-t", 2) { Tick = world.Tick });
        world.RunTicks(30);
        var board = squad.Ai!;
        Assert.Equal("115-t", board.Contract?.Id);
        Assert.Equal("115-t", board.Current?.Id);
        Assert.Equal(CharacterTask.Rest, (CharacterTask)world.Characters.Previous[squad.Leader.Slot].Task);   // the contract's leader goal: sit around
        world.GameHours += 2.5;
        world.RunTicks(60);
        Assert.Null(board.Contract);
        Assert.Equal("111-t", board.Current?.Id);
        Assert.NotEqual(CharacterTask.Rest, (CharacterTask)world.Characters.Previous[squad.Leader.Slot].Task);
    }

    [Fact]
    public void The_stance_comes_from_the_combat_goals_in_their_hours()
    {
        var mind = new AiMind { Squad = new Squad { Id = 0, TemplateId = "t" } };
        AiGoal G(TaskType type, int start = 0, int end = 100) => new(new AiTask("", "", type, TaskPriority.Urgent, TaskTargetType.Specific, TaskEndEvent.Nothing), start, end, 1);
        Assert.Equal(AiStance.Aggressive, AiSystem.StanceOf(mind, [G(TaskType.PATROL_TOWN)], 13));   // no combat goal at all: the old rule
        Assert.Equal(AiStance.Defensive, AiSystem.StanceOf(mind, [G(TaskType.PROTECT_ALLIES)], 13));
        Assert.Equal(AiStance.Passive, AiSystem.StanceOf(mind, [G(TaskType.ATTACK_ENEMIES, 22, 6)], 13));   // a combat goal, out of its hours
        Assert.Equal(AiStance.Aggressive, AiSystem.StanceOf(mind, [G(TaskType.PROTECT_ALLIES), G(TaskType.ATTACK_ENEMIES, 22, 6)], 23));
        Assert.Equal(AiStance.AggressiveNeutrals, AiSystem.StanceOf(mind, [G(TaskType.ATTACK_ENEMIES_AND_NEUTRALS), G(TaskType.TERRITORIAL_AGGRESSION_BUT_DONT_LEAVE_HOME)], 13));
    }

    [Fact]
    public void A_platoons_package_gives_its_off_screen_job()
    {
        var job = Rec("116-t", FcsRecordType.AI_PACKAGE, "Patrol near home");
        job.Ints["unloaded func"] = 2;
        job.References["Squad AI Goals"] = [Ref("101-t")];
        var db = SyntheticTown.Database(roaming: true, extra: [.. Records(), job], changeFolk: t => t.References["AI packages"] = [Ref("116-t", 0)]);
        var ai = new AiDatabase(db);
        var folk = Meitou.Data.Gameplay.SquadTemplate.From(db.Find("12-t")!);
        Assert.Equal(("116-t", PlatoonJob.PatrolShortRange), (PlatoonJobs.For(ai, folk).Package?.Id, PlatoonJobs.For(ai, folk).Job));
        Assert.Equal(PlatoonJob.GoHome, PlatoonJobs.JobOf(4));
        Assert.Equal(PlatoonJob.None, PlatoonJobs.JobOf(1));   // no branch in the original

        var data = SyntheticTown.Data(db);
        using var world = Town(1, out _, data, null);
        world.RunTicks(300);
        var platoons = world.Platoons.All.ToList();
        Assert.NotEmpty(platoons);
        foreach (var p in platoons)
        {
            Assert.Equal("116-t", p.Package);
            Assert.Equal(PlatoonJob.PatrolShortRange, p.Job);
            var home = new Vector2(data.Sites[p.Origin].Position.X, data.Sites[p.Origin].Position.Z);
            if (p.Point is { } point) Assert.InRange(Vector2.Distance(point, home), 0, PlatoonJobs.PatrolRange);
        }
        Assert.Contains(platoons, p => p.Point is not null || p.WaitLeft > 0);
    }

    [Fact]
    public void Node_kinds_beds_and_yaw_are_read_from_the_data()
    {
        Assert.Equal(HomeSpotKind.Shopkeeper, TownBuildings.NodeKind("Node Shopkeeper"));
        Assert.Equal(HomeSpotKind.Guard, TownBuildings.NodeKind("node guard 2"));
        Assert.Equal(HomeSpotKind.Bar, TownBuildings.NodeKind("Node Barman"));
        Assert.Equal(HomeSpotKind.General, TownBuildings.NodeKind("Node"));
        var bed = Rec("200-t", FcsRecordType.BUILDING, "Bed");
        var function = Rec("201-t", FcsRecordType.BUILDING_FUNCTIONALITY, "Bed function");
        function.Ints["function"] = TownBuildings.BedFunction;
        bed.References["functionality"] = [Ref("201-t")];
        var chair = Rec("202-t", FcsRecordType.BUILDING, "Chair");
        var file = new FcsFile();
        file.Records.AddRange([bed, function, chair]);
        var db = new GameDatabase();
        db.Apply(file, "t.mod");
        var cache = new Dictionary<string, bool>();
        Assert.True(TownBuildings.IsBed(db, db.Find("200-t")!, cache));
        Assert.False(TownBuildings.IsBed(db, db.Find("202-t")!, cache));
        Assert.Equal(MathF.PI / 2, TownBuildings.YawOf(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)), 4);
    }

    [Fact]
    public void A_roaming_platoons_package_is_saved_and_loaded()
    {
        var near = Rec("116-t", FcsRecordType.AI_PACKAGE, "Patrol near home");
        near.Ints["unloaded func"] = 2;
        var stay = Rec("117-t", FcsRecordType.AI_PACKAGE, "Stay home");
        stay.Ints["unloaded func"] = 4;
        var db = SyntheticTown.Database(roaming: true, extra: [.. Records(), near, stay], changeFolk: t => t.References["AI packages"] = [Ref("116-t", 0), Ref("117-t", 0)]);
        var data = SyntheticTown.Data(db);
        var builder = Meitou.Simulation.Saving.SaveCapture.PrepareBase(data);
        var saved = builder.AddPlatoon("Testfolk_7", "Testfolk", "20-t", "12-t", new Vector3(Centre.X + 500, 300, Centre.Z));
        saved.Record.Strings["basetown"] = "30-t";
        saved.Record.Strings["currentPackage"] = "117-t";   // not the one the slot rule picks (116-t, the first)
        builder.Game.UpdateDerived();
        string folder = Path.Combine(Path.GetTempPath(), "meitou-ai-" + Guid.NewGuid().ToString("N"));
        try
        {
            builder.Game.Write(folder);
            using var world = new SimWorld(new WorldSettings { Seed = 3, PublishSnapshots = false }, new OpenGroundWalkability(Ground), [new PlayerSystem()]);
            var loaded = Meitou.Simulation.Saving.SaveLoader.Load(world, Meitou.Data.Save.SaveGame.Load(folder), data);
            var p = Assert.Single(world.Platoons.All);
            Assert.Equal(("117-t", PlatoonJob.GoHome), (p.Package, p.Job));
            // Captured again: the package it runs now is written back.
            p.Package = "116-t";
            var again = Meitou.Simulation.Saving.SaveCapture.Capture(world, data, new Meitou.Simulation.Saving.SaveClock(1, 12, 0), loaded);
            Assert.Equal("116-t", again.Platoons.Single(x => x.Name == "Testfolk_7").Record.Strings["currentPackage"]);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void A_bar_package_takes_the_squad_to_the_bar_to_sit()
    {
        var bar = Rec("118-t", FcsRecordType.AI_PACKAGE, "Hang out at the bar");
        bar.Ints["signal func"] = 17;
        bar.References["Leader AI Goals"] = [Ref("105-t", 0, 100, 0)];
        bar.References["Squad AI Goals"] = [Ref("105-t", 0, 100, 0)];
        var db = SyntheticTown.Database(1, extra: [.. Records(), bar], changePatrol: p => p.References["AI packages"] = [Ref("118-t", 0)]);
        Vector3 At(float dx, float dz) => new(Centre.X + dx, 300, Centre.Z + dz);
        var tavern = new TownBuilding("bar", "b-bar", "Bar", At(0, 120), 0, false, true,
            [new HomeSpot(HomeSpotKind.Bar, At(0, 110), 0), new HomeSpot(HomeSpotKind.Bar, At(8, 110), 0), new HomeSpot(HomeSpotKind.Bed, At(0, 130), 0)]);
        var house = new TownBuilding("house", "b-house", "House", At(-80, 0), 0, false, false, [new HomeSpot(HomeSpotKind.Bed, At(-90, 0), 0)]);
        var data = PopulationData.Create(db, [new TownPlacement("inst", "30-t", Centre, null)], buildings: new Dictionary<string, List<TownBuilding>> { ["inst"] = [tavern, house] });
        using var world = Town(1, out _, data, null);
        world.RunTicks(900);
        var squad = world.Squads.All.Single(s => s.TemplateId == "10-t");
        Assert.Equal(PackageClass.Bar, squad.Ai!.CurrentClass);
        Assert.Same(tavern, squad.Ai.Home);
        var seats = tavern.Of(HomeSpotKind.Bar).Select(b => b.Position).ToList();
        foreach (var m in squad.Members)
        {
            Assert.Equal(CharacterTask.Rest, (CharacterTask)world.Characters.Previous[m.Slot].Task);
            Assert.InRange(seats.Min(b => Off(world, m, b)), 0, 13);
        }
    }
}
