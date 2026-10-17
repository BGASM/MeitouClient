using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;

namespace Meitou.Navigation;

/// <summary>
/// Where a building's doors are: the centre of door i's collision (the shapes the navmesh paints as the door, docs/game/pathfinding.md) in the building's frame, scaled by the
/// building's <c>scale</c>, not rotated or moved. Read once per (BUILDING, door) from the data; safe from any thread.
/// </summary>
public sealed class NavDoorGeometry(GameInstall install, GameDatabase db)
{
    readonly CollisionCache collision = new(install);
    readonly ConcurrentDictionary<(string Building, int Door), Vector3?> centres = new();

    public Vector3? Centre(string buildingRecordId, int doorIndex) => centres.GetOrAdd((buildingRecordId, doorIndex), Find);

    Vector3? Find((string Building, int Door) key)
    {
        if (db.Find(key.Building) is not { Type: FcsRecordType.BUILDING } building) return null;
        float scale = building.GetFloat("scale", 1);
        var node = Matrix4x4.CreateScale(scale <= 0 ? 1 : scale);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var mesh in WorldObjectLayout.Building(db, building, "", Vector3.Zero, Quaternion.Identity))
        {
            if (mesh.DoorIndex != key.Door || collision.Get(mesh.Source.GetPath("xml collision")) is not { } prepared) continue;
            foreach (var shape in prepared.Shapes)
                foreach (var v in shape.Vertices)
                {
                    var p = Vector3.Transform(v, node);
                    lo = Vector3.Min(lo, p);
                    hi = Vector3.Max(hi, p);
                }
        }
        return lo.X <= hi.X ? (lo + hi) / 2 : null;
    }
}
