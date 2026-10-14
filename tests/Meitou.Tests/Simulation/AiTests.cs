using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
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
    /// does not act out) and "Town" inheriting it (a duplicate shop goal at weight 0.5, which the parent's beats in the leader list and which its squad list takes by the fallback, and a slave goal); plus a patrol package of a
    /// class not built (signal 6). The resident Patrol template lists "Town" at slot 0 and the patrol at slot 1.
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
        var outing = Rec("112-t", FcsRecordType.AI_PACKAGE, "Go out on patrol");
        outing.Ints["signal func"] = 6;
        outing.References["Squad AI Goals"] = [Ref("101-t")];
        return [shop, patrol, bed, raid, sit, parent, child, outing];
    }

    internal static GameDatabase Database() =>
        SyntheticTown.Database(extra: Records(), changePatrol: p => p.References["AI packages"] = [Ref("111-t", 0), Ref("112-t", 1)]);

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

    static SimWorld Town(int threads, out PopulationData data)
    {
        data = SyntheticTown.Data(Database());
        var walk = new OpenGroundWalkability(Ground);
        var systems = SyntheticTown.Standard(data, walk, StandardParts.Population | StandardParts.Movement | StandardParts.Ai).Systems;
        var world = new SimWorld(new WorldSettings { Seed = 5, Threads = threads, StartHours = 24 + 13, MinPartitionSize = 4 }, walk, systems);
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
}
