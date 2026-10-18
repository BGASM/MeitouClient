using System.Numerics;
using Meitou.Data.World;

namespace Meitou.Navigation;

/// <summary>
/// Joins the kept regions of adjacent zones. Each zone is pruned by its own seeds only (so its cached mesh does not depend on what else was loaded); when a
/// zone arrives, the points of every kept edge on a shared border seed the neighbour's full mesh, and the other way round, until nothing grows. The result
/// is the same in any load order (docs/game/pathfinding.md, "Own builder", zone borders; the original's generator takes the midpoints of already built
/// neighbours' border edges as seeds, order dependent, but ships prebuilt tiles).
/// </summary>
internal static class ZoneKeptClosure
{
    static readonly (int Dx, int Dz)[] Sides = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    /// <summary>
    /// Grows the kept polygons across the borders of <paramref name="zone"/> and onward through <paramref name="full"/> (the loaded zones' meshes with their
    /// pruned polygons). Returns the zones whose kept set grew, <paramref name="zone"/> included when it did.
    /// </summary>
    public static HashSet<ZoneCoordinate> Spread(IReadOnlyDictionary<ZoneCoordinate, ZoneNavMesh> full, ZoneCoordinate zone, NavBuildSettings s)
    {
        var changed = new HashSet<ZoneCoordinate>();
        var queue = new Queue<ZoneCoordinate>([zone]);
        while (queue.Count > 0)
        {
            var a = queue.Dequeue();
            var ma = full[a];
            foreach (var (dx, dz) in Sides)
            {
                var b = new ZoneCoordinate(a.X + dx, a.Y + dz);
                if (!full.TryGetValue(b, out var mb)) continue;
                if (SeedPruner.Extend(mb, BorderSeeds(ma, dx, dz), s) && changed.Add(b)) queue.Enqueue(b);
                if (SeedPruner.Extend(ma, BorderSeeds(mb, -dx, -dz), s) && changed.Add(a)) queue.Enqueue(a);
            }
        }
        return changed;
    }

    /// <summary>The ends (moved in by a unit) and the middle of each kept edge of <paramref name="mesh"/> on its border facing (dx, dz).</summary>
    static List<Vector3> BorderSeeds(ZoneNavMesh mesh, int dx, int dz)
    {
        var seeds = new List<Vector3>();
        foreach (var (p, k) in mesh.BorderEdges(dx, dz, skipLinked: true))
        {
            var poly = mesh.Polygons[p];
            var va = mesh.Vertices[poly[k]];
            var vb = mesh.Vertices[poly[(k + 1) % poly.Length]];
            float length = Vector3.Distance(va, vb);
            if (length < 1e-3f) continue;
            float t = Math.Min(0.5f, 1 / length);
            seeds.Add(Vector3.Lerp(va, vb, t));
            seeds.Add(Vector3.Lerp(va, vb, 0.5f));
            seeds.Add(Vector3.Lerp(va, vb, 1 - t));
        }
        return seeds;
    }
}
