using System.Diagnostics;
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Faction hostility over the installed game's data: towns keep the peace among friends, hostile characters in sight fight.</summary>
[Slow]
public class TownAggroTests(ITestOutputHelper output)
{
    const StandardParts Fighting = StandardParts.Population | StandardParts.Movement | StandardParts.Pursuit | StandardParts.Body | StandardParts.Combat
        | StandardParts.Retaliation | StandardParts.Aggro;

    static PopulationData Data()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        // Its own data per test: the relation table is live and shared by the worlds built on one PopulationData.
        // With the looks: a CHARACTER without a race reference gets its race from the look, and without a race there is no body and no fighter.
        var db = InstallData.BaseGame!;
        return PopulationData.Create(db, InstallData.Levels!.Towns(), new GeneratedAppearances(db, InstallData.Install!.Root));
    }

    static (SimWorld World, StandardSystemSet Systems) Make(PopulationData data, Vector3 at, StandardParts parts = Fighting, float multiplier = 1, int threads = 1)
    {
        var walk = new OpenGroundWalkability((_, _) => 300);
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
        {
            Parts = parts, SynchronousPaths = true,
            Population = new PopulationSettings { CheckEveryTicks = 1, SquadSizeMultiplier = multiplier },
            Combat = new CombatOptions { SelfApproach = false, TickMedical = false, RecordLog = true },
            Aggro = new AggroSettings { RecordLog = true },
        });
        var world = new SimWorld(new WorldSettings { Seed = 1, Threads = threads, PublishSnapshots = false }, walk, systems.Systems);
        world.Commands.Enqueue(new FocusCommand(at) { Tick = 0 });
        return (world, systems);
    }

    static readonly string[] Towns = ["The Hub", "Squin", "Stack", "Admag", "Mongrel", "Shark", "Sho-Battai", "Waystation"];

    [Fact]
    public void Towns_keep_the_peace_between_friends_and_every_fight_is_between_enemies()
    {
        var data = Data();
        var relations = data.Relations;
        int checkedTowns = 0, totalFighters = 0;
        foreach (string name in Towns)
        {
            if (data.Sites.FirstOrDefault(s => s.Town.Name == name) is not { } site) continue;
            checkedTowns++;
            var (world, systems) = Make(data, site.Position);
            using var _ = world;
            world.RunTicks(1800);
            var table = world.Characters;
            int alive = 0, fighters = 0;
            for (int s = 0; s < table.HighWater; s++) if (table.Previous[s].Alive) { alive++; if (table.Cold(s)!.Fighter is not null) fighters++; }
            totalFighters += fighters;
            // Every fight the aggro started is between a faction and one it is hostile to, neither coexisting with the other.
            foreach (var e in systems.Aggro!.Log)
                Assert.True(AggroSystem.IsEnemy(relations, e.AttackerFaction, e.TargetFaction), $"{name}: {data.Factions[e.AttackerFaction].Name} attacked {data.Factions[e.TargetFaction].Name}");
            // Every blow is between enemies one way or the other (the other side answers: retaliation), never within a faction or between allies.
            var factionsAtBlows = new HashSet<(int, int)>();
            foreach (var b in systems.Combat!.Log)
            {
                int fa = table.Cold(b.Attacker)?.Faction ?? -1, fd = table.Cold(b.Defender)?.Faction ?? -1;
                if (fa < 0 || fd < 0) continue;   // the slot was reused after a corpse was removed
                Assert.NotEqual(fa, fd);
                Assert.True(AggroSystem.IsEnemy(relations, fa, fd) || AggroSystem.IsEnemy(relations, fd, fa), $"{name}: a blow between {data.Factions[fa].Name} and {data.Factions[fd].Name}");
                Assert.False(relations.IsAlly(fa, fd) || relations.IsAlly(fd, fa));
                factionsAtBlows.Add((fa, fd));
            }
            var factionsHere = world.Squads.All.Select(s => s.Faction).Where(f => f >= 0).Distinct().Select(f => data.Factions[f].Name);
            output.WriteLine($"{name}: {alive} characters ({fighters} fighters) of {string.Join(", ", factionsHere)}; {systems.Aggro.Started} fights started, {systems.Combat.Log.Count} blows" +
                (factionsAtBlows.Count > 0 ? $" between {string.Join("; ", factionsAtBlows.Select(p => $"{data.Factions[p.Item1].Name} -> {data.Factions[p.Item2].Name}"))}" : ""));
        }
        Assert.True(checkedTowns >= 3, "the towns are placed in the world");
        Assert.True(totalFighters > 100, "the residents can fight");
        Assert.Equal(0, relations.Changes);   // no player: nobody's deeds changed the table
    }

    [Fact]
    public void A_hostile_character_in_sight_of_the_players_attacks_and_the_idle_player_character_defends_itself()
    {
        var data = Data();
        var start = NewGameStart.Find(NewGameStart.LoadAll(data.Db), NewGameStart.DefaultName)!;
        // The sandbox's character: the first human of the default start's squad.
        string record = "";
        foreach (var link in start.Squad)
        {
            if (data.Db.Find(link.Id) is not { } r) continue;
            if (!SquadFactory.IsTemplate(r)) { record = link.Id; break; }
            if (SquadFactory.Plan(data.Db, SquadTemplate.From(r), 1, 0, 0, IWorldStates.None).Members.FirstOrDefault(m => !m.IsAnimal) is { } member) { record = member.RecordId; break; }
        }
        Assert.NotEqual("", record);
        var walk = new OpenGroundWalkability((_, _) => 300);
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
        {
            Parts = Fighting & ~StandardParts.Population | StandardParts.Player, SynchronousPaths = true,
            Combat = new CombatOptions { SelfApproach = false, TickMedical = false, RecordLog = true },
            Aggro = new AggroSettings { RecordLog = true },
        });
        var population = new PopulationSystem(data, new PopulationSettings());
        using var world = new SimWorld(new WorldSettings { Seed = 3, PublishSnapshots = false }, walk, systems.Systems);
        var me = population.SpawnSandbox(world, record, new Vector2(5000, 5000), player: true);
        var foe = population.SpawnSandbox(world, record, new Vector2(5150, 5000), player: false);
        var table = world.Characters;
        int mine = table.Cold(me.Slot)!.Faction, theirs = table.Cold(foe.Slot)!.Faction;
        Assert.True(AggroSystem.IsEnemy(data.Relations, theirs, mine), "the sandbox's foe is of a faction hostile to the player's");
        // The sandbox's characters stand idle; the foe looks round within 15 ticks.
        world.RunTicks(60);
        foreach (var id in new[] { me, foe })
        {
            var c = table.Cold(id.Slot)!;
            output.WriteLine($"{id}: {table.Previous[id.Slot].Position} task {table.Next[id.Slot].Task} fighter {c.Fighter is not null} combat {c.InCombat} faction {c.Faction} race {c.Race?.VisionRangeMult} player {c.IsPlayer}");
        }
        foreach (var e in systems.Aggro!.Log) output.WriteLine(e.ToString());
        Assert.Contains(systems.Aggro!.Log, e => e.Attacker == foe && e.Target == me);
        Assert.True(table.Cold(me.Slot)!.InCombat, "the idle player character fights back");
        int ticks = 60;
        while (ticks < 20000 && !table.Cold(me.Slot)!.Medical!.Incapacitated && !table.Cold(foe.Slot)!.Medical!.Incapacitated) { world.RunTick(); ticks++; }
        Assert.Contains(systems.Combat!.Log, b => b.Attacker == foe.Slot && b.Defender == me.Slot);
        Assert.Contains(systems.Combat.Log, b => b.Attacker == me.Slot && b.Defender == foe.Slot);
        Assert.Equal(0, data.Relations.Changes);   // self-defence is not an assault
        output.WriteLine($"{data.Factions[theirs].Name} against {data.Factions[mine].Name}: down after {ticks} ticks, {systems.Combat.Log.Count} blows");
    }

    /// <summary>Times every phase of the system it wraps (one thread: the bench runs the world on the caller).</summary>
    sealed class Timed(ITickSystem inner) : ITickSystem
    {
        public long Ticks;
        public readonly long[] Phase = new long[5];
        long Time(Action a) { long t0 = Stopwatch.GetTimestamp(); a(); return Stopwatch.GetTimestamp() - t0; }
        public void Inputs(SimWorld world, IReadOnlyList<SimCommand> commands) => Ticks += Time(() => inner.Inputs(world, commands));
        public void Schedule(SimWorld world) { long t = Time(() => inner.Schedule(world)); Ticks += t; Phase[0] += t; }
        public void Act(SimWorld world, Partition partition, EffectBuffer effects) { long t = Time(() => inner.Act(world, partition, effects)); Ticks += t; Phase[1] += t; }
        public void Apply(SimWorld world, in Effect effect) { var e = effect; Ticks += Time(() => inner.Apply(world, e)); }
        public void SlowWorld(SimWorld world) { long t = Time(() => inner.SlowWorld(world)); Ticks += t; Phase[2] += t; }
    }

    [Fact, Bench]
    public void The_cost_of_the_aggro_is_recorded()
    {
        var data = Data();
        var hub = data.Sites.Single(s => s.Town.Name == "The Hub");
        // Each size twice: the first runs pay for the JIT.
        foreach (float multiplier in new[] { 1f, 10f, 1f, 10f })
        {
            var (unused, systems) = Make(data, hub.Position, Fighting, multiplier);
            unused.Dispose();   // only its systems are wanted, in a world of the bench's own
            var walk = new OpenGroundWalkability((_, _) => 300);
            var timed = new Timed(systems.Aggro!);
            var list = systems.Systems.Select(s => s == systems.Aggro ? timed : s).ToList();
            using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false }, walk, list);
            world.Commands.Enqueue(new FocusCommand(hub.Position) { Tick = 0 });
            world.RunTicks(120);   // the town loaded
            timed.Ticks = 0;
            Array.Clear(timed.Phase);
            var watch = Stopwatch.StartNew();
            const int Ticks = 1800;
            world.RunTicks(Ticks);
            double total = watch.Elapsed.TotalMilliseconds / Ticks, aggro = timed.Ticks * 1000.0 / Stopwatch.Frequency / Ticks;
            var mults = Enumerable.Range(0, world.Characters.HighWater).Where(s => world.Characters.Previous[s].Alive).Select(s => world.Characters.Cold(s)!.Race?.VisionRangeMult ?? 0).Distinct().Order();
            output.WriteLine($"The Hub x{multiplier}: {world.Characters.Count} characters (vision range mult {string.Join(", ", mults)}); {total:0.000} ms per tick, of which the aggro {aggro * 1000:0.0} us (schedule, act, slow world: {string.Join(" / ", timed.Phase.Take(3).Select(p => (p * 1e6 / Stopwatch.Frequency / Ticks).ToString("0.0")))} us; {systems.Aggro!.Started} fights started)");
        }
    }
}
