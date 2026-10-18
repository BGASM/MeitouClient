using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.World;
using Meitou.Navigation;
using Meitou.Simulation;
using Xunit.v3;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>
/// The simulation on the navmesh of The Hub (the install's data): a move order across the walled town goes through its gates, and a long one to Squin
/// loads its zones on the way as the game does and arrives.
/// </summary>
[Slow]
public class HubNavigationTests
{
    [Fact]
    public async Task A_move_order_across_the_hub_walks_round_the_buildings_through_the_gates_to_the_goal()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var db = InstallData.FullLoadOrder!;
        var levels = InstallData.Levels!;
        var dir = Path.Combine(Path.GetTempPath(), "meitou-hubnav-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var map = TerrainHeightmap.Open(install!);
            using var nav = new NavSystem(install!, db, levels, (x, z) => (float)map.HeightAt(x, z), cache: new NavMeshCache(dir));
            await nav.LoadZone(new ZoneCoordinate(20, 32));
            await nav.LoadZone(new ZoneCoordinate(21, 32));

            var data = PopulationData.Create(db, levels.Towns());
            var population = new PopulationSystem(data);
            var paths = new PathService(nav.Walkability, synchronous: true);
            using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false }, nav.Walkability,
                [new PlayerSystem(), new MovementSystem(paths)]);
            var squad = population.StartPlayer(world, NewGameStart.Find(NewGameStart.LoadAll(db), NewGameStart.DefaultName)!);
            var leader = squad.Leader;
            var start = world.Characters.Previous[leader.Slot].Position;
            var goal = new Vector3(-50400, 0, 3200);   // across the wall from the start, outside the town
            var straight = Vector2.Distance(new(start.X, start.Z), new(goal.X, goal.Z));
            world.Commands.Enqueue(new MoveOrder([leader], goal) { Tick = 0 });

            float walked = 0;
            var last = start;
            bool onMesh = true;
            for (int i = 0; i < 4000; i++)
            {
                world.RunTick();
                var p = world.Characters.Previous[leader.Slot].Position;
                walked += Vector2.Distance(new(last.X, last.Z), new(p.X, p.Z));
                onMesh &= nav.Walkability.IsWalkable(p.X, p.Z);
                last = p;
                if (Vector2.Distance(new(p.X, p.Z), new(goal.X, goal.Z)) < MovementSystem.ArriveRadius) break;
            }
            Assert.True(Vector2.Distance(new(last.X, last.Z), new(goal.X, goal.Z)) < MovementSystem.ArriveRadius + 5, $"ended {last} for goal {goal}");
            Assert.True(onMesh, "never left the walkable mesh");
            Assert.True(walked > straight * 1.1f, $"walked {walked:0} for a straight {straight:0}: a detour round the wall");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_long_move_order_from_the_hub_to_squin_loads_its_zones_on_the_way_and_arrives()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var db = InstallData.FullLoadOrder!;
        var levels = InstallData.Levels!;
        var dir = Path.Combine(Path.GetTempPath(), "meitou-longnav-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var map = TerrainHeightmap.Open(install!);
            using var nav = new NavSystem(install!, db, levels, (x, z) => (float)map.HeightAt(x, z), cache: new NavMeshCache(dir));
            await nav.LoadZone(new ZoneCoordinate(20, 32));
            await nav.LoadZone(new ZoneCoordinate(21, 32));

            var data = PopulationData.Create(db, levels.Towns());
            var population = new PopulationSystem(data);
            var paths = new PathService(nav.Walkability, synchronous: true);
            using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false }, nav.Walkability,
                [new PlayerSystem(), new MovementSystem(paths)]);
            var squad = population.StartPlayer(world, NewGameStart.Find(NewGameStart.LoadAll(db), NewGameStart.DefaultName)!);
            var leader = squad.Leader;
            var start = world.Characters.Previous[leader.Slot].Position;
            var squin = levels.Towns().First(t => db.Find(t.TownId)?.Name == "Squin").Position;
            var goal = new Vector3(squin.X, 0, squin.Z);
            var straight = Vector2.Distance(new(start.X, start.Z), new(goal.X, goal.Z));
            world.Commands.Enqueue(new MoveOrder([leader], goal) { Tick = 0 });

            // The game's UpdateNav, once a second: the zones round the walker and those its long route leads into load (waited for, as a still does),
            // zones idle for 30 s unload, and the walker asks for its path again when a mesh went live.
            var loaded = new Dictionary<ZoneCoordinate, int>();
            float walked = 0, best = float.MaxValue;
            int bestTick = 0, tick = 0;
            var last = start;
            for (; tick < 200_000; tick++)
            {
                if (tick % 30 == 0)
                {
                    var wanted = ZoneActivation.ZonesAround(last, 1).Concat(nav.Walkability.RouteZones(TimeSpan.FromSeconds(30))).Distinct().ToList();
                    var fresh = wanted.Where(z => !loaded.ContainsKey(z)).ToList();
                    foreach (var z in wanted) loaded[z] = tick;
                    await Task.WhenAll(fresh.Select(z => (Task)nav.LoadZone(z)));
                    foreach (var (z, seen) in loaded.ToList())
                        if (tick - seen > 900) { nav.UnloadZone(z); loaded.Remove(z); }
                    if (fresh.Count > 0) world.Commands.Enqueue(new RepathCommand { Tick = world.Tick });
                }
                world.RunTick();
                var p = world.Characters.Previous[leader.Slot].Position;
                walked += Vector2.Distance(new(last.X, last.Z), new(p.X, p.Z));
                last = p;
                float left = Vector2.Distance(new(p.X, p.Z), new(goal.X, goal.Z));
                if (left < MovementSystem.ArriveRadius) break;
                if (left < best - 50) { best = left; bestTick = tick; }
                Assert.True(tick - bestTick < 30 * 120, $"no progress for 2 minutes at {p}, {left:0} from the goal (walked {walked:0} of a straight {straight:0})");
            }
            Assert.True(Vector2.Distance(new(last.X, last.Z), new(goal.X, goal.Z)) < MovementSystem.ArriveRadius + 5, $"ended {last} for goal {goal} after {tick} ticks");
            Assert.True(walked < straight * 1.5f, $"walked {walked:0} for a straight {straight:0}");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
