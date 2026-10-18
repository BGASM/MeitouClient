using System.Numerics;

namespace Meitou.Simulation.Items;

/// <summary>An item lying in the world, as the host draws it: <see cref="Yaw"/> about Y, the mesh laid flat (docs/simulation.md "Ground items").</summary>
public readonly record struct GroundItemSnapshot(int Id, string Record, Vector3 Position, float Yaw);

/// <summary>
/// Loose items lying in the world (docs/simulation.md "Ground items"): so far only the severed limbs combat drops. Oldest first; beyond <see cref="Limit"/> the oldest go.
/// Not saved yet (the zones' <c>1-itemlist</c>, docs/formats/save.md), and nothing picks them up. Hashed by <see cref="World.StateHash"/> only while it holds one.
/// </summary>
public sealed class GroundItems
{
    /// <summary>How many lie in the world at most (an engine choice; how long the original keeps them is <b>Unknown</b>).</summary>
    public const int Limit = 64;

    readonly List<(int Id, ItemInstance Item, Vector3 Position, float Yaw)> items = [];
    int nextId = 1;

    public int Count => items.Count;
    /// <summary>Bumped when an item is dropped or goes: the host diffs <see cref="Published"/> by id when it changes.</summary>
    public long Revision { get; private set; }

    public void Drop(ItemInstance item, Vector3 position, float yaw)
    {
        items.Add((nextId++, item, position, yaw));
        if (items.Count > Limit) items.RemoveAt(0);
        Revision++;
    }

    /// <summary>The items as published, made again only when <see cref="Revision"/> changed.</summary>
    internal IReadOnlyList<GroundItemSnapshot> Published()
    {
        if (publishedAt != Revision) (published, publishedAt) = ([.. items.Select(i => new GroundItemSnapshot(i.Id, i.Item.Record, i.Position, i.Yaw))], Revision);
        return published;
    }
    IReadOnlyList<GroundItemSnapshot> published = [];
    long publishedAt;

    public void Hash(ref StateHasher h)
    {
        h.Add(items.Count);
        foreach (var (id, item, position, yaw) in items)
        {
            h.Add(id);
            h.Add(Rng.StableHash(item.Record));
            h.Add(position);
            h.Add(yaw);
        }
    }
}
