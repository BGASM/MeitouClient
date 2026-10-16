using System.Numerics;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Game;

/// <summary>
/// The movement marker (docs/game/ui-input.md "Movement marker"): the original's <c>position.mesh</c> at eight times its size, put down where a right
/// click on the ground sends the selection and faded out over one second of game time. Drawn flat in the first pass's colour over the picture,
/// with no depth test, as the selection rings are.
/// </summary>
sealed partial class PlayerInterface
{
    const float MarkerScale = 8;
    static readonly Vector3 MarkerColour = new(0.612f, 0.694f, 0.529f);

    // The mesh's triangles (three corners each, already scaled); empty when the mesh is not found.
    Vector3[] markerTriangles = [];
    Vector3 markerAt;
    float markerLife;
    long markerFrame;

    /// <summary>Reads <c>position.mesh</c> through <paramref name="find"/>; without it there is no marker.</summary>
    public void LoadMoveMarker(Func<string, string?> find)
    {
        if (find("position.mesh") is not { } path) return;
        try
        {
            var mesh = OgreMeshReader.ReadFile(path);
            var corners = new List<Vector3>();
            foreach (var sub in mesh.SubMeshes)
            {
                if (sub.Operation != OgreOperationType.TriangleList || (sub.VertexData ?? mesh.SharedVertexData)?.ReadPositions() is not { } positions) continue;
                foreach (uint i in sub.Indices.Indices) corners.Add(positions[i] * MarkerScale);
            }
            markerTriangles = [.. corners];
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"interface movement marker skipped: {e.Message}");
        }
    }

    /// <summary>Shows the marker at <paramref name="at"/> at full strength (each move order given on the ground).</summary>
    void PlaceMarker(Vector3 at) => (markerAt, markerLife) = (at, 1);

    /// <summary>The marker's front faces at its current strength, which then drops by this frame's game time (0.01 a frame at speed 0, as the original's scaled frame time).</summary>
    void DrawMarker(DebugOverlay o)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float frame = markerFrame == 0 ? 0 : (float)System.Diagnostics.Stopwatch.GetElapsedTime(markerFrame, now).TotalSeconds;
        markerFrame = now;
        if (markerLife <= 0 || markerTriangles.Length == 0) return;
        var colour = new Vector4(MarkerColour, markerLife);
        for (int i = 0; i + 2 < markerTriangles.Length; i += 3)
        {
            if (Project(markerAt + markerTriangles[i]) is not { } a || Project(markerAt + markerTriangles[i + 1]) is not { } b ||
                Project(markerAt + markerTriangles[i + 2]) is not { } c) continue;
            // Front faces only (counter-clockwise in the mesh, clockwise once the screen's y points down), and none under half a pixel.
            float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (area > -1) continue;
            o.Triangle(a, b, c, colour);
        }
        double speed = session.Simulation.Speed;
        markerLife -= speed > 0 ? frame * (float)speed : 0.01f;
    }
}
