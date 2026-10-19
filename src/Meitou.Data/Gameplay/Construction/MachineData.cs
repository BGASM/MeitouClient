using Meitou.Data.Fcs;

namespace Meitou.Data.Gameplay.Construction;

/// <summary>An input of a production recipe (a <c>consumes</c> entry): the ITEM, how many items its section holds (v0) and the level it loses per unit of work (v1 / 100).</summary>
public readonly record struct RecipeInput(string Item, int Capacity, float Rate);

/// <summary>The output of a production recipe (the first <c>produces</c> entry): the ITEM and how many items the output holds (v0).</summary>
public readonly record struct RecipeOutput(string Item, int Capacity);

/// <summary>
/// What a BUILDING does once built (docs/game/buildings-production.md "Production", "Farming", "Research"): its functionality's work and the recipe, the farm data and the storage.
/// Null from <see cref="From"/> for a building that neither works nor stores.
/// </summary>
public sealed record MachineData
{
    /// <summary>The game day at speed 1 in seconds, G (<b>Verified</b>, <c>FUN_14066cd50</c>): 24 x 1200/11.</summary>
    public const float DaySeconds = 24 * 1200f / 11f;

    public const int MineFunction = 1, ResearchFunction = 3;
    /// <summary><c>MiningResource</c> WATER and GROUND (<b>Verified</b>, editor enum).</summary>
    public const int WaterResource = 5, GroundResource = 6;

    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>The functionality's <c>function</c> (BuildingFunction); -1 without a functionality.</summary>
    public int Function { get; init; } = -1;
    /// <summary><c>stat used</c> (a <c>StatsEnumerated</c> value); 0 for none.</summary>
    public int Stat { get; init; }
    /// <summary>How many characters may work it at once: the BUILDING's <c>max operators</c>, else the functionality's (at least 1).</summary>
    public int MaxOperators { get; init; } = 1;
    /// <summary>
    /// The work per second at skill factor 1, before P: BUILDING <c>output rate</c> x functionality <c>production mult</c>, / (2G) when <c>output per day</c> (<b>Observed</b>, loader <c>FUN_14029e7b0</c>).
    /// For a research bench it is the bench's <c>output rate</c>.
    /// </summary>
    public float Rate { get; init; }
    /// <summary><c>use range</c>: how near an operator works.</summary>
    public float UseRange { get; init; } = 20;
    /// <summary><c>world resource mining</c> (MiningResource); 0 none.</summary>
    public int Resource { get; init; }
    /// <summary>The functionality's <c>tech level</c> (a research bench's level).</summary>
    public int TechLevel { get; init; }
    /// <summary>The <c>animation</c> clip operators play (default <c>stonemine</c>, <b>Observed</b>: <c>Task_OperateMachine</c>).</summary>
    public string Animation { get; init; } = "stonemine";
    public IReadOnlyList<RecipeInput> Inputs { get; init; } = [];
    public RecipeOutput? Output { get; init; }
    public FarmData? Farm { get; init; }
    /// <summary><c>has inventory</c>: a storage grid of <see cref="StorageWidth"/> x <see cref="StorageHeight"/>.</summary>
    public bool HasInventory { get; init; }
    public int StorageWidth { get; init; } = 18;
    public int StorageHeight { get; init; } = 18;
    /// <summary><c>power output</c> (negative consumes). Power is not modelled: every building runs as if powered (engine gap).</summary>
    public float PowerOutput { get; init; }

    public bool IsResearch => Function == ResearchFunction;
    public bool IsProducer => Function == MineFunction && Farm is null && Output is not null;
    /// <summary>A building characters can operate.</summary>
    public bool Operable => IsResearch || Farm is not null || IsProducer;

    public static MachineData? From(GameDatabase db, GameRecord building)
    {
        var function = building.GetReferences("functionality") is [var link, ..] ? db.Find(link.TargetStringId) : null;
        bool stores = building.GetBool("has inventory");
        if (function is null && !stores) return null;
        int fn = function?.GetInt("function", -1) ?? -1;
        var farm = building.GetReferences("farm data") is [var farmLink, ..] && db.Find(farmLink.TargetStringId) is { Type: FcsRecordType.FARM_DATA } fd ? FarmData.From(fd) : null;
        float rate = building.GetFloat("output rate", 1);
        if (fn != ResearchFunction && function is not null)
        {
            rate *= function.GetFloat("production mult", 1);
            if (function.GetBool("output per day")) rate /= 2 * DaySeconds;
        }
        var inputs = new List<RecipeInput>();
        RecipeOutput? output = null;
        if (function is not null)
        {
            foreach (var c in function.GetReferences("consumes"))
                if (db.Find(c.TargetStringId) is not null) inputs.Add(new RecipeInput(c.TargetStringId, c.Values.Value0, farm is not null ? 1 : c.Values.Value1 * 0.01f));
            if (function.GetReferences("produces") is [var p, ..] && db.Find(p.TargetStringId) is not null) output = new RecipeOutput(p.TargetStringId, p.Values.Value0);
        }
        int maxOperators = building.GetInt("max operators", -1);
        if (maxOperators <= 0) maxOperators = Math.Max(1, function?.GetInt("max operators", 1) ?? 1);
        return new MachineData
        {
            Id = building.StringId,
            Name = building.Name,
            Function = fn,
            Stat = function?.GetInt("stat used") ?? 0,
            MaxOperators = maxOperators,
            Rate = rate,
            UseRange = function?.GetFloat("use range", 20) ?? 20,
            Resource = function?.GetInt("world resource mining") ?? 0,
            TechLevel = function?.GetInt("tech level") ?? 0,
            Animation = function?.GetReferences("animation") is [var anim, ..] && db.Find(anim.TargetStringId) is { } clip ? clip.Name : "stonemine",
            Inputs = inputs,
            Output = output,
            Farm = farm,
            HasInventory = stores,
            StorageWidth = Math.Max(1, building.GetInt("storage size width", 18)),
            StorageHeight = Math.Max(1, building.GetInt("storage size height", 18)),
            PowerOutput = building.GetInt("power output"),
        };
    }
}

/// <summary>A FARM_DATA record (docs/game/buildings-production.md "Farming"). Times in game hours, rates per game day as in the data.</summary>
public sealed record FarmData
{
    public required string Id { get; init; }
    /// <summary><c>amount</c>: the number of plants.</summary>
    public int Plants { get; init; } = 1;
    public float GrowthTime { get; init; } = 1;
    public float HarvestTime { get; init; } = 1;
    public float DeathTime { get; init; } = 1;
    public float DroughtDeathTime { get; init; }
    public float DroughtMultiplier { get; init; }
    public float DeathThreshold { get; init; } = 0.4f;
    public float ConsumptionRate { get; init; }
    public float HarvestRate { get; init; }
    public float ClearRate { get; init; }
    public float OutputPerPlant { get; init; } = 1;
    public float FertilityEffect { get; init; }
    public float MinimumFertility { get; init; }
    /// <summary>Crop weights for the biome's <c>arid</c>, <c>green</c> and <c>swamp</c> coverages.</summary>
    public float Arid { get; init; }
    public float Green { get; init; }
    public float Swamp { get; init; }
    /// <summary>An indoor farm (<c>fertility effect</c> 0): fertility and biome do not matter (<b>Observed</b>, ctor <c>FUN_1400e8420</c>).</summary>
    public bool Indoor => FertilityEffect == 0;

    public static FarmData From(GameRecord r) => new()
    {
        Id = r.StringId,
        Plants = Math.Max(1, r.GetInt("amount", 1)),
        GrowthTime = r.GetFloat("growth time", 1),
        HarvestTime = r.GetFloat("harvest time", 1) is > 0 and var h ? h : 1e-6f,
        DeathTime = r.GetFloat("death time", 1),
        DroughtDeathTime = r.GetFloat("drought death time"),
        DroughtMultiplier = r.GetFloat("drought multiplier"),
        DeathThreshold = r.GetFloat("death threshold", 0.4f),
        ConsumptionRate = r.GetFloat("consumption rate"),
        HarvestRate = r.GetFloat("harvest rate"),
        ClearRate = r.GetFloat("clear rate"),
        OutputPerPlant = r.GetFloat("output per plant", 1),
        FertilityEffect = r.GetFloat("fertility effect"),
        MinimumFertility = r.GetFloat("minimum fertility"),
        Arid = r.GetFloat("arid"),
        Green = r.GetFloat("green"),
        Swamp = r.GetFloat("swamp"),
    };

    /// <summary>The fertility factor F = <c>1 - (1 - f) x fertility effect</c>, 0 when f is at or below <c>minimum fertility</c> (<b>Observed</b>, <c>FUN_1400e4320</c>).</summary>
    public float FertilityFactor(float fertility) => fertility <= MinimumFertility ? 0 : 1 - (1 - fertility) * FertilityEffect;
}

/// <summary>
/// The resources of the ground at a position (docs/game/buildings-production.md "Resources"): the BIOME_GROUP of the areas map names an ENVIRONMENT_RESOURCES record whose
/// <c>water</c> / <c>farming</c> fields give the WATER and GROUND values by height, and whose coverages weigh the crops.
/// </summary>
public sealed class ResourceField
{
    readonly BiomeMap? map;
    readonly GameDatabase db;
    readonly Dictionary<int, GameRecord?> resourcesOf = [];

    public ResourceField(GameDatabase db, BiomeMap? map)
    {
        this.db = db;
        this.map = map;
    }

    GameRecord? Group(float x, float z)
    {
        if (map is null) return null;
        var (cx, cz) = World.WeatherAreas.CellOf(x, z);
        int g = map.GroupOf(cx, cz);
        return g >= 0 ? db.Find(map.Groups[g].Id) : null;
    }

    GameRecord? Resources(float x, float z)
    {
        if (map is null) return null;
        var (cx, cz) = World.WeatherAreas.CellOf(x, z);
        int g = map.GroupOf(cx, cz);
        if (resourcesOf.TryGetValue(g, out var cached)) return cached;
        var record = g >= 0 && db.Find(map.Groups[g].Id) is { } group && group.GetReferences("resources") is [var link, ..] ? db.Find(link.TargetStringId) : null;
        resourcesOf[g] = record;
        return record;
    }

    /// <summary>
    /// The WATER or GROUND value at a position (<b>Observed</b>, <c>FUN_140a12ff0</c>): band x <c>&lt;p&gt; mult</c> + <c>&lt;p&gt; min</c>, the band 1 between the altitude min and max and
    /// ramping to 0 over the fade beyond them. 0 without a resources record.
    /// </summary>
    public float Value(int resource, float x, float y, float z)
    {
        if (Resources(x, z) is not { } r) return 0;
        string p = resource == MachineData.WaterResource ? "water" : resource == MachineData.GroundResource ? "farming" : "";
        if (p.Length == 0) return 0;
        float min = r.GetInt(p + " altitude min"), max = r.GetInt(p + " altitude max"), fade = r.GetInt(p + " altitude fade");
        float band;
        if (y >= min && y <= max) band = 1;
        else if (fade <= 0) band = 0;
        else if (y < min) band = Math.Clamp((y - (min - fade)) / fade, 0, 1);
        else band = Math.Clamp(((max + fade) - y) / fade, 0, 1);
        return band * r.GetFloat(p + " mult") + r.GetFloat(p + " min");
    }

    /// <summary>The farm's crop match C = <c>arid</c> x the arid coverage + <c>green</c> x green + <c>swamp</c> x swamp (<b>Observed</b>, <c>FUN_1400dfe10</c>); 1 indoors.</summary>
    public float CropMatch(FarmData farm, float x, float z)
    {
        if (farm.Indoor) return 1;
        if (Resources(x, z) is not { } r) return 0;
        return farm.Arid * r.GetFloat("arid") + farm.Green * r.GetFloat("green") + farm.Swamp * r.GetFloat("swamp");
    }

    /// <summary>The biome group's <c>farm water usage</c> (1 without a group).</summary>
    public float WaterUsage(float x, float z) => Group(x, z) is { } g && g.Floats.ContainsKey("farm water usage") ? g.GetFloat("farm water usage") : 1;
}
