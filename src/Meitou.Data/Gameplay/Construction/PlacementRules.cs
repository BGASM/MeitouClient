using System.Numerics;

namespace Meitou.Data.Gameplay.Construction;

/// <summary>Why a ghost may not be placed: one value per message of docs/game/buildings-production.md "Placement", plus the data checks of the engine.</summary>
public enum PlacementFailure : byte
{
    None,
    TooCloseToTown, NotOwnedBuilding, NoResources, CropWillNotGrow, InsideIncomplete, MustBeOutside, MustBeInside, NeedsSnapNode,
    TooCloseToBuilding, NoValidUsageNodes, CharactersInTheWay, BlocksUseNodes, InvalidGround, InvalidFloor, NotLevel,
    /// <summary>Engine: the record is not a BUILDING or not research-unlocked.</summary>
    NotAvailable,
}

/// <summary>A placed town as the placement rules see it.</summary>
public readonly record struct PlacementTown(TownData Town, Vector2 Position);

/// <summary>
/// The deterministic half of the placement checks (docs/simulation.md "Buildings"): pure functions of the data, run by the host for the ghost and again by the
/// simulation on <c>PlaceBuildings</c>. The geometric half lives in the host.
/// </summary>
public static class PlacementRules
{
    /// <summary>The town block of docs/game/buildings-production.md "Placement" (Towns); player towns never block.</summary>
    public static PlacementFailure TownBlock(BuildingBuildData building, Vector2 at, IReadOnlyList<PlacementTown> towns, string playerFactionId) =>
        throw new NotImplementedException("simulation track");

    /// <summary>The text the original shows for a failure ("" for <see cref="PlacementFailure.None"/>).</summary>
    public static string Message(PlacementFailure failure) => throw new NotImplementedException("simulation track");
}
