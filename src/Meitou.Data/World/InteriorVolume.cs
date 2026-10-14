using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Content;
using Meitou.Data.Fcs;
using Meitou.Data.Physics;

namespace Meitou.Data.World;

/// <summary>
/// The indoor volume of a placed building (docs/formats/zones.md, "Interior view"): the collision of its <c>interior mask</c> parts placed with the building's
/// node (position, rotation, <c>scale</c>), kept as the convex footprint of the hull on the ground plane and its height range. A point inside is indoors.
/// </summary>
/// <param name="PlacementId">The building's instance id (what <see cref="PlacedMesh.PlacementId"/> carries for its parts).</param>
/// <param name="Footprint">Convex polygon of the hull in world X/Z, counter-clockwise.</param>
public sealed record InteriorVolume(string PlacementId, Vector2[] Footprint, float YMin, float YMax)
{
    /// <summary>Whether <paramref name="p"/> is inside the footprint and height range, both widened by <paramref name="margin"/>.</summary>
    public bool Contains(Vector3 p, float margin = 0)
    {
        if (p.Y < YMin - margin || p.Y > YMax + margin || Footprint.Length < 3) return false;
        var q = new Vector2(p.X, p.Z);
        for (int i = 0; i < Footprint.Length; i++)
        {
            var a = Footprint[i];
            var e = Footprint[(i + 1) % Footprint.Length] - a;
            float len = e.Length();
            if (len < 1e-4f) continue;
            // Counter-clockwise: inside is to the left of every edge.
            if ((e.X * (q.Y - a.Y) - e.Y * (q.X - a.X)) / len < -margin) return false;
        }
        return true;
    }

    /// <summary>
    /// The volume of a building placed at <paramref name="position"/> / <paramref name="rotation"/>, or null when it has no <c>interior mask</c> part with a
    /// readable collision file, or is a gateway (walked under, not entered). <paramref name="collision"/> loads a record's collision path (shared, cached).
    /// </summary>
    public static InteriorVolume? For(GameDatabase db, GameRecord building, string placementId, Vector3 position, Quaternion rotation, InteriorCollisions collision)
    {
        if (building.GetBool("is gateway")) return null;
        float scale = building.GetFloat("scale", 1);
        var node = WorldObjectLayout.InstanceTransform(position, rotation, new Vector3(scale > 0 ? scale : 1));
        var points = new List<Vector2>();
        float yMin = float.MaxValue, yMax = float.MinValue;
        foreach (var r in building.GetReferences("interior mask"))
        {
            if (db.Find(r.TargetStringId) is not { Type: FcsRecordType.BUILDING_PART } mask) continue;
            if (collision.Vertices(mask.GetPath("xml collision")) is not { } vertices) continue;
            foreach (var v in vertices)
            {
                var w = Vector3.Transform(v, node);
                points.Add(new Vector2(w.X, w.Z));
                yMin = MathF.Min(yMin, w.Y);
                yMax = MathF.Max(yMax, w.Y);
            }
        }
        if (points.Count < 3) return null;
        var hull = ConvexHull2(points);
        return hull.Length < 3 ? null : new InteriorVolume(placementId, hull, yMin, yMax);
    }

    /// <summary>Andrew's monotone chain: the convex hull of <paramref name="points"/>, counter-clockwise (X right, Y = world Z).</summary>
    public static Vector2[] ConvexHull2(List<Vector2> points)
    {
        var p = points.Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToArray();
        if (p.Length < 3) return p;
        var h = new Vector2[p.Length * 2];
        int k = 0;
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        for (int i = 0; i < p.Length; i++)
        {
            while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        for (int i = p.Length - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        return h[..(k - 1)];
    }
}

/// <summary>The vertices (in Kenshi's Y-up building space) of the collision files <c>interior mask</c> parts name, read once each; safe from several threads.</summary>
public sealed class InteriorCollisions(GameInstall install)
{
    readonly ConcurrentDictionary<string, Vector3[]?> files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every triangulated vertex of the file's shapes, or null when the path is empty or the file is missing or unreadable.</summary>
    public Vector3[]? Vertices(string recordPath) =>
        string.IsNullOrWhiteSpace(recordPath) ? null : files.GetOrAdd(recordPath, Load);

    Vector3[]? Load(string recordPath)
    {
        if (CollisionPaths.Resolve(install, recordPath) is not { } path) return null;
        CollisionFile file;
        try { file = CollisionFile.ReadFile(path); }
        catch (Exception e) when (e is InvalidDataException or IOException or System.Xml.XmlException or FormatException) { return null; }
        var all = new List<Vector3>();
        foreach (var s in file.Shapes)
        {
            var v = new List<Vector3>();
            CollisionTriangulator.Triangulate(s, v, []);
            all.AddRange(v.Select(CollisionTriangulator.ToWorldAxes));
        }
        return all.Count == 0 ? null : [.. all];
    }
}
