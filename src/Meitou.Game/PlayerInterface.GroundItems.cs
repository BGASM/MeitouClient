using System.Numerics;

namespace Meitou.Game;

/// <summary>Loose items under the pointer (docs/simulation.md "Ground items"): a right click on one sends the nearest selected character to pick it up (pointer <c>pick_item</c>).</summary>
sealed partial class PlayerInterface
{
    /// <summary>How near the pointer an item's projected middle must be, in pixels (an engine choice: the items have no pick mesh).</summary>
    const float GroundItemPickPixels = 24;

    /// <summary>The ground item whose middle (half a unit above where it lies) projects nearest the pixel within <see cref="GroundItemPickPixels"/>, if any.</summary>
    int? PickGroundItem(Vector2 pixel)
    {
        int? best = null;
        float bestDistance = GroundItemPickPixels;
        foreach (var g in session.CurrentSnapshot.GroundItems)
        {
            if (Project(g.Position + new Vector3(0, 0.5f, 0)) is not { } at) continue;
            float d = Vector2.Distance(at, pixel);
            if (d < bestDistance) (best, bestDistance) = (g.Id, d);
        }
        return best;
    }

    /// <summary>A right click on a ground item: the pick-up order (true), with a selection.</summary>
    bool GroundItemClick(Vector2 p)
    {
        if (SelectedIds().Count == 0 || PickGroundItem(p) is not { } item) return false;
        Say(new Meitou.Simulation.Items.PickUpItemOrder([], item) { Tick = session.World.Tick });
        return true;
    }
}
