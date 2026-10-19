using Meitou.Data.Fcs;

namespace Meitou.Data.Gameplay.Construction;

/// <summary>A RESEARCH record (docs/game/buildings-production.md "Research").</summary>
public sealed record ResearchData
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public int Level { get; init; }
    /// <summary><c>time</c>: the base hours before <c>research rate</c> and the level growth.</summary>
    public float Time { get; init; }
    public bool IsLevelUpgrade { get; init; }
    public bool BlueprintOnly { get; init; }
    /// <summary><c>requirements</c>: RESEARCH ids that must be finished, other records (buildings) that must be unlocked.</summary>
    public IReadOnlyList<string> Requirements { get; init; } = [];
    /// <summary><c>cost</c>: items and counts (v0) paid when it is queued.</summary>
    public IReadOnlyList<(string Item, int Count)> Cost { get; init; } = [];
    public IReadOnlyList<string> EnableBuildings { get; init; } = [];

    /// <summary>
    /// The research hours (<b>Observed</b>, <c>FUN_14082ed10</c>): <c>time x research rate x level increase^(level - 1) / option</c>, the power by repeated multiplication (as the exe).
    /// </summary>
    public float Hours(GameConstants constants, float option = 1)
    {
        float hours = Time * constants.ResearchRate / option;
        for (int i = 1; i < Level; i++) hours *= constants.ResearchLevelIncreaseRate;
        return hours;
    }

    public static ResearchData From(GameRecord r) => new()
    {
        Id = r.StringId,
        Name = r.Name,
        Category = r.GetString("category"),
        Description = r.GetString("description"),
        Level = r.GetInt("level"),
        Time = r.GetInt("time"),
        IsLevelUpgrade = r.GetBool("is level upgrade"),
        BlueprintOnly = r.GetBool("blueprint only"),
        Requirements = [.. r.GetReferences("requirements").Select(l => l.TargetStringId)],
        Cost = [.. r.GetReferences("cost").Where(l => l.Values.Value0 > 0).Select(l => (l.TargetStringId, l.Values.Value0))],
        EnableBuildings = [.. r.GetReferences("enable buildings").Select(l => l.TargetStringId)],
    };

    /// <summary>Every RESEARCH record, in the database's order.</summary>
    public static List<ResearchData> LoadAll(GameDatabase db) => [.. db.OfType(FcsRecordType.RESEARCH).Select(From)];
}
