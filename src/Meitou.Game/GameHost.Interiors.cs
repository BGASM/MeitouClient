namespace Meitou.Game;

/// <summary>The interior view (docs/formats/zones.md, "Interior view"): buildings with a player character inside show their interior and hide their upper floors.</summary>
sealed partial class GameHost
{
    /// <summary>
    /// The viewed floor, 0..3 (the game's global floor: 0 at the start, stepped by the HUD's floor arrows and PageUp / PageDown, which are not wired here yet).
    /// Every building in the interior view hides its parts and objects above it.
    /// </summary>
    internal int ViewedFloor
    {
        get => viewedFloor;
        set => viewedFloor = Math.Clamp(value, 0, 3);
    }

    int viewedFloor;
    readonly Dictionary<string, int> interiorView = new(StringComparer.Ordinal);

    /// <summary>
    /// Every frame: each non-destroyed building that holds a player character (its indoor hull contains the character) is put in the interior view at the viewed
    /// floor; every other building shows its outside. Game: the player interface's per-frame interior driver over the player's characters.
    /// </summary>
    void UpdateInteriorView()
    {
        if (gpu.Objects is not { } objects) return;
        interiorView.Clear();
        foreach (var c in session.CurrentSnapshot.Characters)
            if (c.IsPlayer && objects.InteriorAt(DrawnPosition(c), InsideMargin) is { } building)
                interiorView[building.PlacementId] = viewedFloor;
        objects.InteriorView = interiorView;
    }

    /// <summary>How far (units) outside the indoor hull a character still counts as inside (engine choice: the feet stand on the floor at the hull's bottom).</summary>
    const float InsideMargin = 2;
}
