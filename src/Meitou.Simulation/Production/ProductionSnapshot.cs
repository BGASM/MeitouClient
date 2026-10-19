using Meitou.Simulation.Items;

namespace Meitou.Simulation.Production;

/// <summary>A recipe slot as the host shows it: the item, how many are held of how many, and the level (input) or progress (output) 0..1.</summary>
public readonly record struct MachineSlotView(string Item, string Name, int Count, int Capacity, float Fill);

/// <summary>A farm as the host shows it (docs/simulation.md "Production").</summary>
public readonly record struct FarmView(float Growth, float Death, int Harvested, int Plants, FarmStatus Status, float Fertility, float CropMatch, float Ripeness)
{
    /// <summary>Each plant's growth (a copy; -10 harvested): what its crop mesh is drawn at.</summary>
    public IReadOnlyList<float> PlantGrowth { get; init; } = [];
}

/// <summary>A machine of a complete player building after a tick (docs/simulation.md "Production"); <see cref="Site"/> is the building's instance id.</summary>
public sealed record MachineSnapshot(string Site, string Record, IReadOnlyList<MachineSlotView> Inputs, MachineSlotView? Output, InventoryView? Storage, FarmView? Farm, byte Status, int Operators, int MaxOperators)
{
    /// <summary>The research bench's tech level (0 for other machines).</summary>
    public int TechLevel { get; init; }
    /// <summary>Characters can work it (a well, farm or research bench).</summary>
    public bool Operable { get; init; }

    internal static MachineSnapshot Of(Machine m, ItemFactory items)
    {
        string Name(string record) => items.Info(record)?.Name ?? record;
        var inputs = m.Inputs.Select(i => new MachineSlotView(i.Recipe.Item, Name(i.Recipe.Item), i.Count, i.Recipe.Capacity, Math.Clamp(i.Level, 0, 1))).ToArray();
        MachineSlotView? output = m.Output is { } o ? new MachineSlotView(o.Recipe.Item, Name(o.Recipe.Item), o.Count, o.Recipe.Capacity, Math.Clamp(o.Progress, 0, 1)) : null;
        InventoryView? storage = m.Storage is { } s ? new InventoryView(s.Width, s.Height, 0, 0,
            Array.AsReadOnly(s.Items.Select((item, index) => new InventoryItemView(index, item.Record, item.Name, "main", item.X, item.Y, item.Width, item.Height, item.Quantity, item.Weight, item.Quality)
                { Material = item.MaterialId, Charge = item.ChargeFraction }).ToArray())) : null;
        FarmView? farm = m.Farm is { } f && m.Data.Farm is { } fd
            ? new FarmView(f.Growth, f.Death, f.Harvested, f.Plants.Length, f.Status, m.Yield, m.CropMatch, ProductionSystem.Ripeness(f)) { PlantGrowth = [.. f.Plants] } : null;
        return new MachineSnapshot(m.Site, m.Data.Id, inputs, output, storage, farm, m.Status, m.Operators, m.Data.MaxOperators) { TechLevel = m.Data.IsResearch ? m.Data.TechLevel : 0, Operable = m.Data.Operable };
    }
}

/// <summary>A research the player may queue, with its cost and whether the benches can pay it and the bench level allows it.</summary>
public readonly record struct ResearchOption(string Id, string Name, string Category, int Level, float Hours, bool Affordable, bool BenchTooLow, float Progress)
{
    public string Description { get; init; } = "";
    /// <summary>The items paid at queue time (empty once paid).</summary>
    public IReadOnlyList<(string Name, int Count)> Cost { get; init; } = [];
}

/// <summary>The research state as the host shows it (docs/simulation.md "Production", Research).</summary>
public sealed record ResearchSnapshot(int TechLevel, int BenchLevel, IReadOnlyList<(string Id, string Name, float Progress)> Queue, float Rate, IReadOnlyList<ResearchOption> Available)
{
    public static ResearchSnapshot Empty { get; } = new(1, 0, [], 0, []);
}
