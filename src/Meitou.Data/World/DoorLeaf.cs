using System.Numerics;
using Meitou.Data.Fcs;

namespace Meitou.Data.World;

/// <summary>
/// How a door leaf moves (docs/game/locks.md "Door motion"): doors slide, they do not swing. <see cref="Speed"/> is open amount per second; the leaf moves
/// <see cref="Distance"/> units along its local <see cref="Axis"/> (0 X, 1 Y, 2 Z), first stepping <see cref="ComesOut"/> units out of the wall when that is not 0.
/// </summary>
/// <param name="Axis">BUILDING <c>door axis</c>.</param>
/// <param name="Distance">BUILDING <c>door move dist</c> (signed).</param>
/// <param name="Speed">BUILDING <c>door move speed</c>.</param>
/// <param name="ComesOut">BUILDING <c>door comes out</c> (signed).</param>
public readonly record struct DoorLeaf(int Axis, float Distance, float Speed, float ComesOut)
{
    /// <summary>The navmesh counts a door as open from this open amount on (docs/game/locks.md "Doors", <b>Verified</b>).</summary>
    public const float NavOpen = 0.6f;

    /// <summary>The fields of a door's BUILDING record (the fcs.def defaults when absent: axis 0, 30 units, 1 per second, no step out).</summary>
    public static DoorLeaf Of(GameRecord door) =>
        new(Math.Clamp(door.GetInt("door axis"), 0, 2), door.GetFloat("door move dist", 30), door.GetFloat("door move speed", 1), door.GetFloat("door comes out"));

    /// <summary>
    /// The leaf's displacement at open amount <paramref name="doa"/> in its part's local frame (<b>Verified</b> formula, kenshi_x64 <c>FUN_140298d10</c>): without a step out,
    /// <c>slide x Distance x doa</c>; with one, the leaf first moves out along <c>out</c> until <c>t = |ComesOut| / (0.25 |Distance| + |ComesOut|)</c>, then slides:
    /// <c>slide x Distance x (doa - t) / (1 - t) + out x ComesOut</c>. The out axis is the part's thinnest other axis, pointing away from the building's origin
    /// (<b>Observed</b>: the original takes the thinnest axis of the door's collision box; the sign test's reference point is not decoded), from the part's local bounds.
    /// </summary>
    public Vector3 LocalOffset(float doa, Vector3 boundsMin, Vector3 boundsMax)
    {
        var slide = Unit(Axis);
        if (ComesOut == 0) return slide * (Distance * doa);
        float t = MathF.Abs(ComesOut) / (0.25f * MathF.Abs(Distance) + MathF.Abs(ComesOut));
        var size = boundsMax - boundsMin;
        int o = -1;
        for (int k = 0; k < 3; k++)
            if (k != Axis && (o < 0 || Component(size, k) < Component(size, o))) o = k;
        float centre = Component(boundsMin + boundsMax, o);
        var outward = Unit(o) * (centre < 0 ? -1 : 1);
        return doa < t ? outward * (ComesOut * doa / t) : slide * (Distance * (doa - t) / (1 - t)) + outward * ComesOut;
    }

    static Vector3 Unit(int axis) => axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
    static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
