using System.Numerics;

namespace Meitou.Navigation;

/// <summary>
/// The walkable collision triangles of a zone's buildings and foliage (exterior and interiors), kept beside the navmesh for heights. The mesh's
/// polygons are only planar between their corners, so on stairs and ramps a polygon spanning several steps lies several units above or below the
/// treads; a character's height is taken from the real surface the polygon stands for (<see cref="NavmeshWalkability.GroundHeight(float, float, float)"/>).
/// </summary>
public sealed class NavSurfaces
{
    /// <summary>Grid cell of the index (units).</summary>
    const float Cell = 32;

    /// <summary>Three corners per triangle, world coordinates.</summary>
    public Vector3[] Corners { get; }
    readonly Vector2 min;
    readonly int columns, rows;
    readonly int[][] cells;

    public NavSurfaces(Vector3[] corners)
    {
        Corners = corners;
        if (corners.Length == 0) { cells = []; return; }
        var lo = new Vector2(float.MaxValue);
        var hi = new Vector2(float.MinValue);
        foreach (var c in corners) { lo = Vector2.Min(lo, new(c.X, c.Z)); hi = Vector2.Max(hi, new(c.X, c.Z)); }
        min = lo;
        columns = (int)((hi.X - lo.X) / Cell) + 1;
        rows = (int)((hi.Y - lo.Y) / Cell) + 1;
        var lists = new List<int>?[columns * rows];
        for (int t = 0; t < corners.Length / 3; t++)
        {
            var (x0, x1, z0, z1) = Range(t);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) (lists[z * columns + x] ??= []).Add(t);
        }
        cells = [.. lists.Select(l => l is null ? [] : l.ToArray())];
    }

    public int TriangleCount => Corners.Length / 3;

    (int X0, int X1, int Z0, int Z1) Range(int t)
    {
        var a = Corners[t * 3]; var b = Corners[t * 3 + 1]; var c = Corners[t * 3 + 2];
        return (Col(MathF.Min(a.X, MathF.Min(b.X, c.X))), Col(MathF.Max(a.X, MathF.Max(b.X, c.X))), Row(MathF.Min(a.Z, MathF.Min(b.Z, c.Z))), Row(MathF.Max(a.Z, MathF.Max(b.Z, c.Z))));
    }

    int Col(float x) => Math.Clamp((int)((x - min.X) / Cell), 0, columns - 1);
    int Row(float z) => Math.Clamp((int)((z - min.Y) / Cell), 0, rows - 1);

    /// <summary>The height of the surface at (x, z) nearest to <paramref name="y"/>, when one is within <paramref name="range"/>.</summary>
    public bool TryNear(float x, float z, float y, float range, out float height)
    {
        height = y;
        if (cells.Length == 0) return false;
        int cx = (int)MathF.Floor((x - min.X) / Cell), cz = (int)MathF.Floor((z - min.Y) / Cell);
        if (cx < 0 || cz < 0 || cx >= columns || cz >= rows) return false;
        float best = range;
        bool found = false;
        foreach (int t in cells[cz * columns + cx])
        {
            var a = Corners[t * 3]; var b = Corners[t * 3 + 1]; var c = Corners[t * 3 + 2];
            if (!NavGeometry.Barycentric(x, z, a, b, c, out float l1, out float l2, out float l3) || l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
            float h = l1 * a.Y + l2 * b.Y + l3 * c.Y;
            float d = MathF.Abs(h - y);
            if (d > best) continue;
            best = d;
            height = h;
            found = true;
        }
        return found;
    }

    /// <summary>The walkable non-terrain triangles of gathered geometry (buildings and foliage) whose box touches the zone box grown by <paramref name="margin"/>.</summary>
    internal static Vector3[] From(ZoneGeometry g, Vector2 zoneMin, Vector2 zoneMax, float margin = 8)
    {
        var list = new List<Vector3>();
        var v = g.Vertices;
        for (int t = g.BuildingFirstTriangle; t < g.TriangleCount; t++)
        {
            if (g.Areas[t] != NavArea.Ground) continue;
            int ia = g.Indices[t * 3] * 3, ib = g.Indices[t * 3 + 1] * 3, ic = g.Indices[t * 3 + 2] * 3;
            var a = new Vector3(v[ia], v[ia + 1], v[ia + 2]); var b = new Vector3(v[ib], v[ib + 1], v[ib + 2]); var c = new Vector3(v[ic], v[ic + 1], v[ic + 2]);
            if (MathF.Max(a.X, MathF.Max(b.X, c.X)) < zoneMin.X - margin || MathF.Min(a.X, MathF.Min(b.X, c.X)) > zoneMax.X + margin
                || MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) < zoneMin.Y - margin || MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) > zoneMax.Y + margin) continue;
            list.Add(a); list.Add(b); list.Add(c);
        }
        return [.. list];
    }
}
