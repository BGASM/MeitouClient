using System.Numerics;

namespace Meitou.Navigation;

/// <summary>
/// The original generator keeps only the regions within 4 units of a seed point (regionPruningSettings, minRegionArea 1e8 makes every region "too small" otherwise),
/// and the largest region when none qualifies. This is that pass over the polygon graph: roofs, closed yards and cliff tops without a seed are dropped
/// (docs/game/pathfinding.md, "Region pruning by seeds").
/// </summary>
internal static class SeedPruner
{
    /// <summary>Marks the polygons not connected to any seed as not kept. Returns how many seeds found a polygon.</summary>
    public static int Prune(ZoneNavMesh mesh, ZoneGeometry g, NavBuildSettings s)
    {
        int n = mesh.PolygonCount;
        if (n == 0) return 0;
        var keep = new bool[n];
        var queue = new Queue<int>();
        int matched = Match(mesh, g.Seeds, s, keep, queue);

        if (matched == 0 && g.ClippedAtBorder)
        {
            // Part of an interior whose doors are across a zone border: the neighbour's kept border edges reach it (ZoneKeptClosure).
            for (int p = 0; p < n; p++) mesh.Kept[p] = false;
            return 0;
        }
        if (matched == 0)
        {
            // "All regions are below the area threshold and too far from a seed point. Keeping the largest region."
            var component = Components(mesh);
            int best = component.GroupBy(c => c).OrderByDescending(c => c.Count()).First().Key;
            for (int p = 0; p < n; p++) if (component[p] == best) { keep[p] = true; }
            for (int p = 0; p < n; p++) mesh.Kept[p] = keep[p];
            return 0;
        }

        Spread(mesh, keep, queue);
        for (int p = 0; p < n; p++) mesh.Kept[p] = keep[p];
        return matched;
    }

    /// <summary>Also keeps the regions of <paramref name="mesh"/> that <paramref name="seeds"/> reach (no fallback); true when a polygon was added.</summary>
    public static bool Extend(ZoneNavMesh mesh, IReadOnlyList<Vector3> seeds, NavBuildSettings s)
    {
        if (mesh.PolygonCount == 0 || seeds.Count == 0) return false;
        var keep = (bool[])mesh.Kept.Clone();
        var queue = new Queue<int>();
        Match(mesh, seeds, s, keep, queue);
        if (queue.Count == 0) return false;
        Spread(mesh, keep, queue);
        for (int p = 0; p < keep.Length; p++) mesh.Kept[p] = keep[p];
        return true;
    }

    /// <summary>Marks the polygons each seed claims (queued when newly kept). Returns how many seeds found a polygon, kept already or not.</summary>
    static int Match(ZoneNavMesh mesh, IReadOnlyList<Vector3> seeds, NavBuildSettings s, bool[] keep, Queue<int> queue)
    {
        // The mesh's own point index (independent of which polygons are kept).
        var ix = mesh.Index;
        int matched = 0;
        foreach (var seed in seeds)
        {
            bool found = false;
            // Engine choice: a seed that finds nothing within the slack is retried with three times the slack (a seed's Y is where it was authored).
            foreach (float slack in new[] { s.SeedHeightSlack, 3 * s.SeedHeightSlack })
            {
                float reach = s.SeedDistance;
                int cx0 = ix.Col(seed.X - reach - mesh.BoundsMin.X), cx1 = ix.Col(seed.X + reach - mesh.BoundsMin.X);
                int cz0 = ix.Row(seed.Z - reach - mesh.BoundsMin.Y), cz1 = ix.Row(seed.Z + reach - mesh.BoundsMin.Y);
                for (int z = cz0; z <= cz1; z++)
                    for (int x = cx0; x <= cx1; x++)
                        foreach (int p in ix.Cells[z * ix.Columns + x])
                        {
                            if (keep[p]) { found = true; continue; }
                            if (seed.X < ix.Min[p].X - reach || seed.X > ix.Max[p].X + reach || seed.Z < ix.Min[p].Y - reach || seed.Z > ix.Max[p].Y + reach) continue;
                            if (mesh.DistanceXZ(p, seed.X, seed.Z) > reach) continue;
                            float y = mesh.HeightAt(p, Math.Clamp(seed.X, ix.Min[p].X, ix.Max[p].X), Math.Clamp(seed.Z, ix.Min[p].Y, ix.Max[p].Y));
                            if (Math.Abs(y - seed.Y) > slack) continue;
                            keep[p] = true;
                            queue.Enqueue(p);
                            found = true;
                        }
                if (found) break;
            }
            if (found) matched++;
        }
        return matched;
    }

    static void Spread(ZoneNavMesh mesh, bool[] keep, Queue<int> queue)
    {
        while (queue.Count > 0)
        {
            int p = queue.Dequeue();
            foreach (int q in mesh.NeighboursOf(p))
                if (!keep[q]) { keep[q] = true; queue.Enqueue(q); }
        }
    }

    static int[] Components(ZoneNavMesh mesh)
    {
        var id = new int[mesh.PolygonCount];
        Array.Fill(id, -1);
        int next = 0;
        var stack = new Stack<int>();
        for (int p = 0; p < id.Length; p++)
        {
            if (id[p] >= 0) continue;
            id[p] = next;
            stack.Push(p);
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                foreach (int q in mesh.NeighboursOf(c)) if (id[q] < 0) { id[q] = next; stack.Push(q); }
            }
            next++;
        }
        return id;
    }
}
