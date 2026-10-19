using Meitou.Data.Gameplay.Construction;
using Meitou.Simulation.Items;

namespace Meitou.Simulation.Production;

/// <summary>A recipe input of a machine (docs/game/buildings-production.md "Production", Recipes): the items waiting in its section and the level drawn from them.</summary>
public sealed class InputSlot(RecipeInput recipe)
{
    public RecipeInput Recipe { get; } = recipe;
    /// <summary>Items held in the section (no quality: inputs and crops have none, engine choice).</summary>
    public int Count { get; set; }
    /// <summary>The level taken from items, one item = 1 (the save's <c>consumption amount-&lt;k&gt;</c>).</summary>
    public float Level { get; set; }
}

/// <summary>The output of a machine: the items made so far (at most <see cref="RecipeOutput.Capacity"/>) and the progress to the next (the save's <c>production amount</c>).</summary>
public sealed class OutputSlot(RecipeOutput recipe)
{
    public RecipeOutput Recipe { get; } = recipe;
    public int Count { get; set; }
    public float Progress { get; set; }
    public bool Full => Count >= Recipe.Capacity;
}

/// <summary>What a farm reports (<b>Observed</b>, FarmBuilding <c>+0x468</c>).</summary>
public enum FarmStatus : byte { NoWater = 0, OutputFull = 1, Harvest = 2, Growing = 3 }

/// <summary>A farm's crop (docs/game/buildings-production.md "Farming"). Growth 0..1 grows, 1..2 ripe, 2 and up rots; <see cref="Death"/> 1 loses the crop.</summary>
public sealed class FarmState(int plants)
{
    /// <summary>Each plant's growth; a harvested plant is -10.</summary>
    public float[] Plants { get; } = new float[plants];
    /// <summary>The farm's growth (<c>plant growth</c>): the lowest growing plant while below 1, then ripening up to 2.</summary>
    public float Growth { get; set; }
    /// <summary><c>plant death</c>.</summary>
    public float Death { get; set; }
    /// <summary><c>plant start</c>: the lowest growth at replanting.</summary>
    public float Start { get; set; }
    /// <summary><c>plants harvested</c>: also the plant worked next.</summary>
    public int Harvested { get; set; }
    public float HarvestProgress { get; set; }
    public float ClearProgress { get; set; }
    public FarmStatus Status { get; set; } = FarmStatus.Growing;
    /// <summary>Plants replanted so far, the roll counter of the replanting draws.</summary>
    public int Plantings { get; set; }
    /// <summary>Plant rolls made so far, the roll counter of the harvest draws.</summary>
    public int Rolls { get; set; }
}

/// <summary>
/// A complete player building that works or stores (docs/simulation.md "Production"): its recipe slots, storage grid and farm. Made by <see cref="ProductionSystem"/> when the site completes
/// (or loads complete) and dropped with the site; serial phases only.
/// </summary>
public sealed class Machine
{
    public required string Site { get; init; }
    public required MachineData Data { get; init; }
    /// <summary>Where the building stands (its items drop here when it goes).</summary>
    public System.Numerics.Vector3 Position { get; init; }
    public InputSlot[] Inputs { get; init; } = [];
    public OutputSlot? Output { get; init; }
    /// <summary>The storage grid of a building with <c>has inventory</c> (a research bench's books).</summary>
    public Inventory? Storage { get; init; }
    public FarmState? Farm { get; init; }
    /// <summary>The resource yield (<c>resource mult</c>): 1 without a resource, else the ground's value (0 becomes 0.5); for a farm its fertility factor.</summary>
    public float Yield { get; set; } = 1;
    /// <summary>A farm's crop match with the biome (C).</summary>
    public float CropMatch { get; set; } = 1;
    /// <summary>A farm's water draw per second: <c>consumption rate</c> / G x the biome's <c>farm water usage</c>.</summary>
    public float WaterRate { get; set; }
    /// <summary>Production status: 2 working, 1 output full, 0 idle (<b>Observed</b>, <c>+0x468</c>).</summary>
    public byte Status { get; set; }
    /// <summary>Characters working it this tick (derived; not saved, not hashed).</summary>
    public int Operators { get; set; }

    internal void Hash(ref StateHasher h)
    {
        h.Add(Rng.StableHash(Site));
        h.Add(Yield);
        h.Add(Status);
        foreach (var i in Inputs) { h.Add(i.Count); h.Add(i.Level); }
        if (Output is { } o) { h.Add(o.Count); h.Add(o.Progress); }
        if (Storage is { } s) { h.Add(s.Items.Count); foreach (var item in s.Items) { h.Add(Rng.StableHash(item.Record)); h.Add(item.Quantity); h.Add(item.X); h.Add(item.Y); } }
        if (Farm is { } f)
        {
            h.Add(f.Growth); h.Add(f.Death); h.Add(f.Start); h.Add(f.Harvested); h.Add(f.HarvestProgress); h.Add(f.ClearProgress); h.Add((byte)f.Status);
            foreach (var p in f.Plants) h.Add(p);
        }
    }
}

/// <summary>The machines of the player's buildings, by site id, in the order they were made. Hashed only while it holds one.</summary>
public sealed class MachineRegistry
{
    readonly List<Machine> all = [];
    readonly Dictionary<string, Machine> bySite = new(StringComparer.Ordinal);
    IReadOnlyList<MachineSnapshot>? published;

    public int Count => all.Count;
    public IReadOnlyList<Machine> All => all;
    public Machine? Find(string site) => bySite.GetValueOrDefault(site);
    /// <summary>Bumped on every change the host shows (items, progress, status).</summary>
    public long Version { get; private set; }

    public void Touch()
    {
        Version++;
        published = null;
    }

    internal void Add(Machine machine)
    {
        all.Add(machine);
        bySite[machine.Site] = machine;
        Touch();
    }

    internal bool Remove(string site)
    {
        if (!bySite.Remove(site, out var m)) return false;
        all.Remove(m);
        Touch();
        return true;
    }

    internal void Clear()
    {
        all.Clear();
        bySite.Clear();
        Touch();
    }

    internal void Hash(ref StateHasher h)
    {
        h.Add(0x4D616368);
        h.Add(all.Count);
        foreach (var m in all) m.Hash(ref h);
    }

    internal IReadOnlyList<MachineSnapshot> Published(ItemFactory items) => published ??= [.. all.Select(m => MachineSnapshot.Of(m, items))];
}
