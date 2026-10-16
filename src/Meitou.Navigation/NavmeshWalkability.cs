using System.Numerics;
using Meitou.Data.World;
using Meitou.Simulation;

namespace Meitou.Navigation;

/// <summary>
/// <see cref="IWalkability"/> over the loaded zone meshes. Where a zone's mesh is not there yet (it is built on a worker thread,
/// <see cref="NavMeshService"/>) the open-ground stand-in answers, so the simulation never waits. The world is swapped as a whole
/// (immutable snapshots), so every method is safe from any thread.
/// </summary>
public sealed class NavmeshWalkability : IWalkability
{
    readonly Func<float, float, float> ground;
    readonly IWalkability fallback;
    volatile NavWorld world = NavWorld.Empty;

    /// <param name="routeTerrain">Makes the finer height function the long-range grid is built from (<see cref="CoarseRoute"/>); null: <paramref name="terrainHeight"/>.</param>
    public NavmeshWalkability(Func<float, float, float> terrainHeight, IWalkability? fallback = null, Func<Func<float, float, float>>? routeTerrain = null)
    {
        ground = terrainHeight;
        this.fallback = fallback ?? new OpenGroundWalkability(terrainHeight);
        coarse = new Lazy<CoarseRoute>(() => new CoarseRoute(terrainHeight, routeTerrain));
    }

    /// <summary>The doors' run-time state; shared with every query made through this object.</summary>
    public NavDoors Doors { get; } = new();

    public NavWorld World => world;

    /// <summary>Replaces the loaded meshes; only <see cref="NavMeshService"/> calls it, under its publish lock.</summary>
    internal void SetWorld(NavWorld value) => world = value;

    /// <summary>
    /// How far (in height) a walkable collision surface may be from the mesh for its height to be used instead, and the terrain's where no collision
    /// surface is near (engine choices). The mesh is a simplified surface: about 0.5 off on the flat, but a polygon is planar only between its corners,
    /// so over stairs, ramps and steep slopes it can be 15 off (Observed: the Hub's gate stairs and the ramp north of it). The terrain and the walkable
    /// collision (<see cref="NavSurfaces"/>) are exact.
    /// </summary>
    public const float SnapRange = 16, TerrainSnapRange = 30;

    /// <summary>
    /// How far above a character's height a surface may be and still be the one it stands on (engine choice): above the 5-unit climb, below a storey,
    /// so a walkway or upper floor overhead is not taken for the ground.
    /// </summary>
    public const float StandRise = 12;

    /// <summary>How far (units) off the mesh a walker may stand on the polygon beside it (engine choice: a path hugs corners and edges exactly, and a step can graze past them).</summary>
    public const float EdgeTolerance = 2;

    [ThreadStatic] static List<float>? scratch;

    /// <summary>The terrain height: the loaded zone's own heightmap samples, else the terrain function (whose grid may be coarse far from the start).</summary>
    float Ground(NavWorld w, float x, float z) => w.TryTerrain(x, z, out float h) ? h : ground(x, z);

    /// <summary>
    /// The height a character stands at: the highest walkable surface there, each mesh polygon put on the real surface it stands for (<see cref="Snap"/>);
    /// the terrain's off the mesh. Movement uses the overload with the character's height, which picks the floor under it where floors are stacked.
    /// </summary>
    public float GroundHeight(float x, float z) => TryStand(world, x, z, float.PositiveInfinity, out float y) ? y : Ground(world, x, z);

    /// <summary>As <see cref="GroundHeight(float, float)"/>, for a character at height <paramref name="nearY"/>: the highest surface at most <see cref="StandRise"/> above it.</summary>
    public float GroundHeight(float x, float z, float nearY) => TryStand(world, x, z, nearY, out float y) ? y : Ground(world, x, z);

    public bool IsWalkable(float x, float z)
    {
        var w = world;
        if (!w.Contains(WorldLayout.ZoneOf(x, z))) return fallback.IsWalkable(x, z);
        return w.TryGroundHeight(x, z, out _);
    }

    /// <summary>Whether there is a surface at (x, z) no more than <see cref="StandRise"/> above <paramref name="nearY"/> (a wall top beside the walker is not).</summary>
    public bool IsWalkable(float x, float z, float nearY)
    {
        var w = world;
        if (!w.Contains(WorldLayout.ZoneOf(x, z))) return fallback.IsWalkable(x, z);
        return TryStand(w, x, z, nearY, out float y) && y <= nearY + StandRise;
    }

    /// <summary>The highest real surface at (x, z) at most <see cref="StandRise"/> above <paramref name="nearY"/>, else the lowest; false off the mesh.</summary>
    bool TryStand(NavWorld w, float x, float z, float nearY, out float y)
    {
        var heights = scratch ??= [];
        heights.Clear();
        w.HeightsAt(x, z, heights, float.IsFinite(nearY) ? nearY : 0, float.IsFinite(nearY) ? EdgeTolerance : 0);
        y = float.MinValue;
        if (heights.Count == 0) return false;
        float lowest = float.MaxValue;
        foreach (float h in heights)
        {
            float s = Snap(w, x, z, h);
            if (s <= nearY + StandRise && s > y) y = s;
            lowest = MathF.Min(lowest, s);
        }
        if (y == float.MinValue) y = lowest;
        return true;
    }

    /// <summary>
    /// The real surface a mesh height <paramref name="h"/> stands for: the walkable collision surface nearest it within <see cref="SnapRange"/> unless the
    /// terrain is nearer; else the terrain within <see cref="TerrainSnapRange"/>; else the mesh's own height.
    /// </summary>
    float Snap(NavWorld w, float x, float z, float h)
    {
        float t = Ground(w, x, z);
        float terrain = h > WorldWater.Height ? MathF.Abs(t - h) : float.MaxValue;
        if (w.TrySurfaceNear(x, z, h, MathF.Min(SnapRange, terrain), out float s)) return s;
        return terrain <= TerrainSnapRange ? t : h;
    }

    public PathResult FindPath(Vector3 from, Vector3 to) => FindPath(from, to, NavAgent.Human);

    /// <summary>The long-range router over the terrain, for goals in zones whose navmesh is not loaded (its grid is built on first use).</summary>
    public CoarseRoute Coarse => coarse.Value;
    readonly Lazy<CoarseRoute> coarse;

    /// <summary>Spacing (units) of the points along a long route tried as the end of its navmesh part (engine choice).</summary>
    public const float HandoverStep = 150;
    /// <summary>How far (units) the end of the navmesh part keeps from a zone that is not loaded (engine choice).</summary>
    public const float HandoverMargin = 120;

    /// <summary>
    /// A path for a given agent (footprint radius, water cost, closed doors). With both ends on loaded zones, the navmesh path; otherwise a long route
    /// (<see cref="CoarseRoute"/>) whose first part, as far as the loaded zones reach, is a navmesh path, and the rest provisional
    /// (<see cref="PathResult.Exact"/>). Where the terrain gives no route either, the open-ground stand-in answers.
    /// </summary>
    public PathResult FindPath(Vector3 from, Vector3 to, NavAgent agent)
    {
        var w = world;
        bool fromLoaded = w.Contains(WorldLayout.ZoneOf(from.X, from.Z));
        if (!fromLoaded || !w.Contains(WorldLayout.ZoneOf(to.X, to.Z))) return LongRoute(w, from, to, agent, fromLoaded);
        var exact = Exact(w, from, to, agent);
        if (exact.Found) return exact;
        // Unreachable over the loaded zones (shut in, or the way round leads through zones not loaded): as near as can be got, then on to the goal
        // provisionally, which asks again a few times on the way (MovementSystem) before the walker gives up.
        var part = Toward(w, from, to, agent);
        if (!part.Found) return exact;
        if (Vector2.Distance(new(part.Points[^1].X, part.Points[^1].Z), new(to.X, to.Z)) <= NavQuery.GoalSnap) return part;
        List<Vector3> points = [.. part.Points, to];
        Ahead(w, points, part.Points.Count);
        return new PathResult(points) { Exact = part.Points.Count };
    }

    /// <summary>How far (units) along the provisional part of a route the zones are asked for (<see cref="RouteZones"/>), so the walker finds rocks,
    /// walls and cliffs the terrain grid does not know before it gets there (engine choice: about a zone).</summary>
    public const float Lookahead = 4000;

    readonly System.Collections.Concurrent.ConcurrentDictionary<ZoneCoordinate, long> routeZones = new();

    /// <summary>The zones without a mesh that a provisional route passed through within <see cref="Lookahead"/> of its navmesh part, in the last
    /// <paramref name="maxAge"/>: the host loads them, and every walker asks for its path again when they are live.</summary>
    public IEnumerable<ZoneCoordinate> RouteZones(TimeSpan maxAge)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var (zone, at) in routeZones)
            if (System.Diagnostics.Stopwatch.GetElapsedTime(at, now) <= maxAge) yield return zone;
            else routeZones.TryRemove(zone, out _);
    }

    void Ahead(NavWorld w, List<Vector3> points, int exact)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float left = Lookahead;
        for (int i = Math.Max(exact, 1); i < points.Count && left > 0; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            float length = Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
            for (float d = 0; d <= MathF.Min(length, left); d += HandoverStep)
            {
                var p = Vector3.Lerp(a, b, d / MathF.Max(length, 1e-3f));
                var zone = WorldLayout.ZoneOf(p.X, p.Z);
                if (!w.Contains(zone)) routeZones[zone] = now;
            }
            left -= length;
        }
    }

    /// <summary>The navmesh path to <paramref name="to"/>, or to the reachable point nearest it (<see cref="NavQuery.FindPathToward"/>), on the real ground.</summary>
    PathResult Toward(NavWorld w, Vector3 from, Vector3 to, NavAgent agent)
    {
        var path = new NavQuery(w, Doors).FindPathToward(from, to, agent);
        return path.Found ? new PathResult([.. path.Points.Select(p => new Vector3(p.X, GroundHeight(p.X, p.Z, p.Y) is var y && MathF.Abs(y - p.Y) <= TerrainSnapRange ? y : p.Y, p.Z))]) : path;
    }

    PathResult Exact(NavWorld w, Vector3 from, Vector3 to, NavAgent agent)
    {
        var path = new NavQuery(w, Doors).FindPath(from, to, agent);
        if (!path.Found) return path;
        // Path points are on the simplified surface: put them on the real ground.
        return new PathResult([.. path.Points.Select(p => new Vector3(p.X, GroundHeight(p.X, p.Z, p.Y) is var y && MathF.Abs(y - p.Y) <= TerrainSnapRange ? y : p.Y, p.Z))]);
    }

    /// <summary>
    /// The coarse route, with its start replaced by a navmesh path to the farthest point of it that lies well inside the loaded zones (the handover).
    /// </summary>
    PathResult LongRoute(NavWorld w, Vector3 from, Vector3 to, NavAgent agent, bool fromLoaded)
    {
        var route = Coarse.Find(from, to, agent.WaterFactor);
        if (route.Count == 0) return fallback.FindPath(from, to);
        if (!fromLoaded) { Ahead(w, route, 1); return new PathResult(route) { Exact = 1 }; }

        // The handover: the farthest point along the route, in steps, that lies well inside the loaded zones, and the index of the route point after it.
        (int Next, Vector3 Point)? handover = null;
        for (int i = 1; i < route.Count; i++)
        {
            var a = route[i - 1];
            var b = route[i];
            float length = Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
            bool left = false;
            for (float d = HandoverStep; d < length + HandoverStep; d += HandoverStep)
            {
                var p = Vector3.Lerp(a, b, MathF.Min(d, length) / MathF.Max(length, 1e-3f));
                if (!WellInside(w, p)) { left = true; break; }
                handover = (i, p);
            }
            if (left) break;
        }
        if (handover is not var (next, point)) { Ahead(w, route, 1); return new PathResult(route) { Exact = 1 }; }

        // The navmesh path there, or to the reachable point nearest it (it may stand in a building, or behind a wall or a cliff the terrain grid
        // does not know); from such a point the rest of the route is looked for again.
        var target = new Vector3(point.X, GroundHeight(point.X, point.Z, point.Y), point.Z);
        var part = Toward(w, from, target, agent);
        if (!part.Found) { Ahead(w, route, 1); return new PathResult(route) { Exact = 1 }; }
        var points = part.Points.ToList();
        var end = points[^1];
        bool reached = Vector2.Distance(new(end.X, end.Z), new(target.X, target.Z)) <= NavQuery.GoalSnap;
        var rest = reached ? route.Skip(next).ToList() : Coarse.Find(end, to, agent.WaterFactor).Skip(1).ToList();
        if (rest.Count == 0) rest = [.. route.Skip(next)];
        int exact = points.Count;
        points.AddRange(rest);
        Ahead(w, points, exact);
        return new PathResult(points) { Exact = exact };
    }

    /// <summary>Whether the point and everything within <see cref="HandoverMargin"/> of it lies on loaded zones.</summary>
    static bool WellInside(NavWorld w, Vector3 p)
    {
        for (int dz = -1; dz <= 1; dz += 2)
            for (int dx = -1; dx <= 1; dx += 2)
                if (!w.Contains(WorldLayout.ZoneOf(p.X + dx * HandoverMargin, p.Z + dz * HandoverMargin))) return false;
        return true;
    }
}
