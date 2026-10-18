using System.Numerics;
using Meitou.Navigation;
using Meitou.Rendering;

namespace Meitou.Game;

/// <summary>
/// The navmesh debug view (Meitou only, Shift+F11; docs/game/pathfinding.md "Meitou debug view"): the zones the walkers use now, drawn by <see cref="NavmeshOverlayRenderer"/>.
/// The geometry is built on a worker whenever the navigation world changes while the view is on.
/// </summary>
sealed partial class GameHost
{
    /// <summary>How far above the mesh the view is drawn, so the ground does not hide it.</summary>
    const float NavOverlayLift = 3;

    NavWorld? navOverlayWorld;
    Task<(Vector3 Origin, float[] Fill, float[] Edges)>? navOverlayBuild;

    void ToggleNavOverlay()
    {
        if (nav is null || gpu.NavmeshOverlay is not { } overlay) { Console.WriteLine("navmesh   no navmesh in this scene"); return; }
        overlay.Visible = !overlay.Visible;
        Console.WriteLine(overlay.Visible ? "navmesh   shown (Shift+F11 hides it)" : "navmesh   hidden");
    }

    /// <summary>Once a frame: a finished build goes to the renderer; a changed world starts the next.</summary>
    void UpdateNavOverlay()
    {
        if (nav is null || gpu.NavmeshOverlay is not { Visible: true } overlay) return;
        if (navOverlayBuild is { IsCompleted: true } done)
        {
            if (done.IsCompletedSuccessfully) { var (origin, fill, edges) = done.Result; overlay.SetMesh(origin, fill, edges); }
            navOverlayBuild = null;
        }
        var world = nav.Walkability.World;
        if (navOverlayBuild is null && !ReferenceEquals(world, navOverlayWorld))
        {
            navOverlayWorld = world;
            navOverlayBuild = Task.Run(() => BuildNavOverlay(world));
        }
    }

    /// <summary>
    /// The view's triangles and lines: walkable polygons cyan (each a little lighter or darker, so neighbours tell apart), door polygons blue, polygons the seed pass
    /// removed red; edges where the mesh ends yellow, the rest faint.
    /// </summary>
    static (Vector3, float[], float[]) BuildNavOverlay(NavWorld world)
    {
        var fill = new List<float>();
        var edges = new List<float>();
        Vector3? origin = null;
        for (int z = 0; z < world.ZoneCount; z++)
        {
            var m = world.Mesh(z);
            if (m.Vertices.Length == 0) continue;
            var o = origin ??= new Vector3(MathF.Round(m.Vertices[0].X), MathF.Round(m.Vertices[0].Y), MathF.Round(m.Vertices[0].Z));
            for (int p = 0; p < m.PolygonCount; p++)
            {
                var poly = m.Polygons[p];
                if (poly.Length < 3) continue;
                float shade = 0.75f + 0.25f * ((p * 7919 % 5) / 4f);
                Vector4 colour = !m.Kept[p] ? new(0.95f, 0.2f, 0.15f, 0.22f)
                    : m.DoorOf is { } doors && doors[p] >= 0 ? new(0.3f, 0.45f, 1f, 0.35f)
                    : new(0.1f * shade, 0.85f * shade, 0.95f * shade, 0.2f);
                for (int k = 1; k + 1 < poly.Length; k++)
                {
                    Add(fill, m.Vertices[poly[0]] - o, colour);
                    Add(fill, m.Vertices[poly[k]] - o, colour);
                    Add(fill, m.Vertices[poly[k + 1]] - o, colour);
                }
                var links = m.LinksOf(p);
                for (int k = 0; k < poly.Length; k++)
                {
                    int across = m.Neighbours[p][k];
                    if (across >= 0 && across < p) continue;   // drawn from the other side
                    bool linked = false;
                    for (int l = 0; l + 1 < links.Length; l += 2) linked |= links[l] == k;
                    bool end = across < 0 && !linked && world.LinksOf(new NavRef(z, p)).ToArray().All(n => n.Edge != k);
                    var edge = end ? new Vector4(1f, 0.85f, 0.2f, 0.9f) : new Vector4(0.1f, 0.9f, 1f, 0.35f);
                    Add(edges, m.Vertices[poly[k]] - o, edge);
                    Add(edges, m.Vertices[poly[(k + 1) % poly.Length]] - o, edge);
                }
            }
        }
        return (origin ?? Vector3.Zero, [.. fill], [.. edges]);

        static void Add(List<float> to, Vector3 p, Vector4 c) => to.AddRange([p.X, p.Y + NavOverlayLift, p.Z, c.X, c.Y, c.Z, c.W]);
    }
}
