using System.Numerics;
using Meitou.Data.Fcs;

namespace Meitou.Data.World;

/// <summary>What a spot inside a building is for: a bed, or one of the invisible <c>is node</c> markers of an interior layout, by its record's name.</summary>
public enum HomeSpotKind : byte
{
    Bed = 0,
    /// <summary>A node whose name holds "shop" ("Node Shopkeeper").</summary>
    Shopkeeper = 1,
    /// <summary>A node whose name holds "guard".</summary>
    Guard = 2,
    /// <summary>A node whose name holds "bar".</summary>
    Bar = 3,
    /// <summary>A node whose name holds "throne".</summary>
    Throne = 4,
    /// <summary>Any other node.</summary>
    General = 5,
}

/// <summary>A spot inside a building in world space; <paramref name="Yaw"/> as the simulation measures it (0 along +Z, turning towards +X).</summary>
public readonly record struct HomeSpot(HomeSpotKind Kind, Vector3 Position, float Yaw)
{
    /// <summary>A node's BUILDING <c>node type</c> (NodeType: 0 NODE_SHOPKEEPER, 1 NODE_GENERAL, 2 NODE_GUARD_POST, 3 NODE_TURRET, 4 NODE_USE, 5 NODE_LIGHT); -1 for a bed.</summary>
    public int NodeType { get; init; } = -1;
}

/// <summary>
/// A placed building of a town that squads can live in, with the beds and nodes of its interior layout (docs/formats/zones.md "Building layouts") and the shop and
/// bar flags of its exterior layout. Position is the building's origin (absolute Y).
/// </summary>
public sealed record TownBuilding(string InstanceId, string BuildingId, string Name, Vector3 Position, float Yaw, bool IsShop, bool IsBar, IReadOnlyList<HomeSpot> Spots)
{
    public IEnumerable<HomeSpot> Of(HomeSpotKind kind) => Spots.Where(s => s.Kind == kind);
    public int Beds => Spots.Count(s => s.Kind == HomeSpotKind.Bed);
    /// <summary>The placement state's <c>owner faction ID</c> (a FACTION string id); null when the state names none (the town's faction owns it).</summary>
    public string? OwnerFactionId { get; init; }
    /// <summary>The cages and lockable containers of its interior layout (docs/game/locks.md "Data"), in layout order.</summary>
    public IReadOnlyList<TownFixture> Fixtures { get; init; } = [];
    /// <summary>The zone whose building list places it (instance ids repeat between zones).</summary>
    public ZoneCoordinate Zone { get; init; }
    /// <summary>
    /// Whether residents can be housed in it (docs/game/factions-squads-towns.md "Town residents"): a BUILDING with <c>interior</c> parts that does not
    /// <c>links together</c> (a wall), or an <c>is wall gate</c> gate.
    /// </summary>
    public bool CanHouse { get; init; }
    /// <summary><c>is wall gate</c>: a town gate, housing only resident entries that name it in <c>building</c>.</summary>
    public bool IsGate { get; init; }
    /// <summary>The level data's <c>residents template</c> of its state (a SQUAD_TEMPLATE id; empty when none).</summary>
    public string ResidentsTemplate { get; init; } = "";
    /// <summary>The state's <c>interior layout name</c> (the one its <see cref="Spots"/> come from); empty when none.</summary>
    public string InteriorLayout { get; init; } = "";
    /// <summary>The names of the interior and exterior layouts its record can take (its own and those of <c>shares interiors with</c>), case ignored.</summary>
    public IReadOnlySet<string> InteriorLayouts { get; init; } = EmptyNames;
    public IReadOnlySet<string> ExteriorLayouts { get; init; } = EmptyNames;
    /// <summary>
    /// The slot (<c>I</c>) and serial (<c>S</c>) of the <c>handle</c> its level-data state carries (-1 / 0 when it has none). A save writes a base building's state with this
    /// same handle (docs/formats/save.md "Object handles"), so a platoon can name a building whose zone was never saved.
    /// </summary>
    public int HandleSlot { get; init; } = -1;
    public int HandleSerial { get; init; }

    static readonly IReadOnlySet<string> EmptyNames = new HashSet<string>();
}

/// <summary>
/// A lockable object of a building's interior layout: a cage or pole (BUILDING_FUNCTIONALITY <c>function</c> 8) or a BUILDING with <c>has lock</c>. <paramref name="Id"/> is
/// the parent's instance id, "/", and the item's index in the layout (an <b>engine</b> name: the save's ids of generated interiors are not mapped).
/// </summary>
public sealed record TownFixture(string Id, string RecordId, bool IsCage, Vector3 Position, float Yaw);

/// <summary>
/// The buildings of each placed town, for the AI's homes, beds and nodes (docs/simulation.md "AI", Homes). A placed building belongs to the town its state's
/// <c>town</c> handle names (at any distance: Squin's reach 4.5 x its radius), else the nearest one (<see cref="BuildingTowns"/>) when it stands within 1.5 x that
/// town's radius (<b>engine choice</b>: the nearest-town fallback would otherwise hand every lone building in the world to some town). Buildings the level data marks destroyed are kept (the game rebuilds a non-player building on import: <c>FUN_140552b70</c> keeps <c>destroyed</c> only for the player's; the Hub's ruins house its squatters); node markers and resource buildings
/// (<c>is foliage</c>) are left out. Beds are interior-layout objects with a BED or SKELETON_BED functionality (docs/game/buildings-production.md, BuildingFunction 6
/// and 25, found by the type of the referenced records rather than a field name); nodes are its <c>is node</c> objects, their <c>node type</c> kept and their kind read
/// from it (0 shopkeeper, 2 guard post), else from the record name (<see cref="NodeKind(string)"/>).
/// </summary>
public static class TownBuildings
{
    public const int BedFunction = 6, SkeletonBedFunction = 25;
    /// <summary>BUILDING_FUNCTIONALITY <c>function</c> 8, BF_CAGE: cages, the prisoner pole, the peeler (docs/game/locks.md "Data").</summary>
    public const int CageFunction = 8;

    /// <summary>The buildings by town placement id (<see cref="TownPlacement.InstanceId"/>), each list in placement order.</summary>
    public static Dictionary<string, List<TownBuilding>> Build(GameDatabase db, WorldLevelData levels, BuildingLayouts? layouts, Func<double, double, float> terrainHeight)
    {
        var towns = new BuildingTowns(db, levels);
        var radius = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var t in levels.Towns())
            if (db.Find(t.TownId) is { Type: FcsRecordType.TOWN } record && Gameplay.TownData.From(record) is var town)
                radius[t.InstanceId] = MathF.Max(town.SizeRadius * town.TownRadiusMult, 1);
        var beds = new Dictionary<string, bool>(StringComparer.Ordinal);
        var functions = new Dictionary<string, GameRecord?>(StringComparer.Ordinal);
        var names = new Dictionary<(string, bool), IReadOnlySet<string>>();
        List<TownFixture>? fixtures = null;
        var result = new Dictionary<string, List<TownBuilding>>(StringComparer.Ordinal);
        foreach (var b in levels.Buildings())
        {
            if (BuildingPlacements.Resolve(b, db, levels, terrainHeight) is not { } resolved) continue;
            var record = resolved.Record;
            if (record.GetBool("is node") || record.GetBool("is foliage")) continue;
            var state = b.StateId is not null && levels.Zones.TryGetValue(b.Zone, out var zoneDb) ? zoneDb.Find(b.StateId) : null;
            var linked = towns.TownByHandle(state);
            if ((linked ?? towns.NearestTown(resolved.Position)) is not { } town || !radius.TryGetValue(town.InstanceId, out float r)) continue;
            if (linked is null && Vector2.Distance(new Vector2(town.Position.X, town.Position.Z), new Vector2(resolved.Position.X, resolved.Position.Z)) > r * 1.5f) continue;
            var spots = new List<HomeSpot>();
            bool shop = false, bar = false;
            if (layouts is not null && state is not null)
            {
                if (layouts.Find(record, state.GetString("exterior layout name")) is { } exterior)
                {
                    shop = exterior.GetInt("is shop") != 0;
                    bar = exterior.GetInt("is bar") != 0;
                }
                if (layouts.Find(record, state.GetString("interior layout name"), exterior: false) is { } interior)
                {
                    int index = 0;
                    foreach (var item in layouts.Items(interior))
                    {
                        var (at, rotation) = BuildingLayouts.Place(resolved.Position, b.Rotation, item);
                        bool node = item.Building.GetBool("is node");
                        HomeSpotKind? kind = node ? NodeKind(item.Building) : IsBed(db, item.Building, beds) ? HomeSpotKind.Bed : null;
                        if (kind is { } k) spots.Add(new HomeSpot(k, at, YawOf(rotation)) { NodeType = node ? item.Building.GetInt("node type") : -1 });
                        else if (FunctionOf(db, item.Building, functions)?.GetInt("function") == CageFunction || item.Building.GetBool("has lock"))
                            (fixtures ??= []).Add(new TownFixture($"{b.InstanceId}/{index}", item.Building.StringId,
                                FunctionOf(db, item.Building, functions)?.GetInt("function") == CageFunction, at, YawOf(rotation)));
                        index++;
                    }
                }
            }
            if (!result.TryGetValue(town.InstanceId, out var list)) result[town.InstanceId] = list = [];
            string? owner = state?.GetString("owner faction ID") is { Length: > 0 } o ? o : null;
            list.Add(new TownBuilding(b.InstanceId, record.StringId, record.Name, resolved.Position, YawOf(b.Rotation), shop, bar, spots)
            {
                OwnerFactionId = owner, Fixtures = fixtures ?? [], Zone = b.Zone,
                CanHouse = record.GetBool("is wall gate") || record.GetReferences("interior").Count > 0 && !record.GetBool("links together"),
                IsGate = record.GetBool("is wall gate"),
                ResidentsTemplate = state?.GetString("residents template") ?? "",
                InteriorLayout = state?.GetString("interior layout name") ?? "",
                InteriorLayouts = LayoutNames(layouts, record, exterior: false, names),
                ExteriorLayouts = LayoutNames(layouts, record, exterior: true, names),
                HandleSlot = state is not null && state.Ints.ContainsKey("handleI") && state.GetInt("handleTYPE") == (int)FcsRecordType.BUILDING ? state.GetInt("handleI") : -1,
                HandleSerial = state?.GetInt("handleS") ?? 0,
            });
            fixtures = null;
        }
        return result;
    }

    /// <summary>The kind of a node: by its <c>node type</c> (0 shopkeeper, 2 guard post), else by its record's name.</summary>
    public static HomeSpotKind NodeKind(GameRecord node) => node.GetInt("node type") switch
    {
        0 => HomeSpotKind.Shopkeeper,
        2 => HomeSpotKind.Guard,
        _ => NodeKind(node.Name),
    };

    /// <summary>The layout names a building record can take (case ignored), cached by record and side; empty without layouts.</summary>
    static IReadOnlySet<string> LayoutNames(BuildingLayouts? layouts, GameRecord record, bool exterior, Dictionary<(string, bool), IReadOnlySet<string>> cache)
    {
        if (layouts is null) return new HashSet<string>();
        if (!cache.TryGetValue((record.StringId, exterior), out var set))
            cache[(record.StringId, exterior)] = set = new HashSet<string>(layouts.Names(record, exterior), StringComparer.OrdinalIgnoreCase);
        return set;
    }

    /// <summary>The kind of a node from its record's name (case ignored).</summary>
    public static HomeSpotKind NodeKind(string name) =>
        name.Contains("shop", StringComparison.OrdinalIgnoreCase) ? HomeSpotKind.Shopkeeper
        : name.Contains("guard", StringComparison.OrdinalIgnoreCase) ? HomeSpotKind.Guard
        : name.Contains("throne", StringComparison.OrdinalIgnoreCase) ? HomeSpotKind.Throne
        : name.Contains("bar", StringComparison.OrdinalIgnoreCase) ? HomeSpotKind.Bar
        : HomeSpotKind.General;

    /// <summary>A BUILDING with a BUILDING_FUNCTIONALITY record of function BED or SKELETON_BED in any of its reference lists (cached by id).</summary>
    public static bool IsBed(GameDatabase db, GameRecord building, Dictionary<string, bool> cache)
    {
        if (cache.TryGetValue(building.StringId, out bool known)) return known;
        bool bed = false;
        foreach (var list in building.ReferenceLists)
            foreach (var r in building.GetReferences(list))
                if (db.Find(r.TargetStringId) is { Type: FcsRecordType.BUILDING_FUNCTIONALITY } f && f.GetInt("function") is BedFunction or SkeletonBedFunction)
                    bed = true;
        cache[building.StringId] = bed;
        return bed;
    }

    /// <summary>
    /// The first BUILDING_FUNCTIONALITY record in any reference list of <paramref name="building"/> (its <c>function</c>, <c>restrains movement</c>, <c>use range</c>), cached by id; null
    /// for a building without one.
    /// </summary>
    public static GameRecord? FunctionOf(GameDatabase db, GameRecord building, Dictionary<string, GameRecord?> cache)
    {
        if (cache.TryGetValue(building.StringId, out var known)) return known;
        GameRecord? found = null;
        foreach (var list in building.ReferenceLists)
        {
            foreach (var r in building.GetReferences(list))
                if (db.Find(r.TargetStringId) is { Type: FcsRecordType.BUILDING_FUNCTIONALITY } f) { found = f; break; }
            if (found is not null) break;
        }
        cache[building.StringId] = found;
        return found;
    }

    /// <summary>The simulation's yaw (0 along +Z, turning towards +X) of a rotation.</summary>
    public static float YawOf(Quaternion rotation)
    {
        var q = rotation.LengthSquared() < 1e-12f ? Quaternion.Identity : Quaternion.Normalize(rotation);
        var forward = Vector3.Transform(Vector3.UnitZ, q);
        return MathF.Atan2(forward.X, forward.Z);
    }
}
