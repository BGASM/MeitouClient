using System.Numerics;

namespace Meitou.Simulation;

/// <summary>Where a roaming squad is in its life (docs/game/game-loop.md "Factions and squads": a platoon is active or unloaded).</summary>
public enum PlatoonState : byte
{
    /// <summary>Only a stand-in moving towards its target: no characters exist (the original's <c>UnloadedPlatoon</c>).</summary>
    Unloaded,
    /// <summary>Its characters are being made (on the build thread); it keeps its place meanwhile.</summary>
    Activating,
    /// <summary>Its characters exist in the world (the original's <c>ActivePlatoon</c>).</summary>
    Loaded,
}

/// <summary>
/// A roaming squad that outlives the loading of its zone: it belongs to a town's roaming pool (<see cref="Origin"/>), travels from
/// town to town, and is only drawn and simulated in detail while a zone near it is active. <see cref="Key"/> and
/// <see cref="TemplateId"/> reproduce the squad's members whenever it is loaded again (the plan is a pure function of them and
/// the world seed). Changed only in serial phases.
/// </summary>
public sealed class Platoon
{
    public required int Id { get; init; }
    /// <summary>The town (a position in the population's site list) whose pool it counts against; -1 for a save's wild platoon.</summary>
    public required int Origin { get; init; }
    public required string TemplateId { get; init; }
    public int Faction { get; init; } = -1;
    public required ulong Key { get; init; }
    /// <summary>Members when made: what the pool counts.</summary>
    public int Size { get; init; }
    public Vector2 Position { get; set; }
    /// <summary>The town it is travelling to (-1: none chosen, it waits).</summary>
    public int Target { get; set; } = -1;
    /// <summary>It is on its way back to <see cref="Origin"/> (else out to another town).</summary>
    public bool GoingHome { get; set; }
    /// <summary>Seconds still to wait at the town it reached.</summary>
    public float WaitLeft { get; set; }
    /// <summary>The purse while the squad is not loaded (<see cref="Squad.Money"/> while it is; save key <c>money</c>).</summary>
    public long Money { get; set; }
    public PlatoonState State { get; set; }
    /// <summary>The squad object while loaded (-1 otherwise).</summary>
    public int SquadId { get; set; } = -1;
    /// <summary>The tick at which a loaded platoon far from every active zone is turned into a stand-in (-1: not counting down).</summary>
    public long UnloadAtTick { get; set; } = -1;
    /// <summary>Seconds before a loaded platoon's leader is sent on again when it has no go-to (a blocked path must not be retried every look).</summary>
    public float OrderCooldown { get; set; }
    /// <summary>The AI package it runs (an AI_PACKAGE id, saved as <c>currentPackage</c>; null: none) and the off-screen job that package gives (<see cref="Ai.PlatoonJobs"/>).</summary>
    public string? Package { get; set; }
    public Ai.PlatoonJob Job { get; set; }
    /// <summary>A short-range patrol's next point (null: none; it goes to <see cref="Target"/> instead).</summary>
    public Vector2? Point { get; set; }
    /// <summary>The save's platoon this stand-in is (null: one the population made): its characters are made from the save, not from <see cref="Key"/>.</summary>
    public Saving.SavedPlatoon? Saved { get; init; }
    /// <summary>A town's squad the population made, kept as its characters since its town unloaded (null: none): made of them again, never rolled anew.</summary>
    public StoredSquad? Stored { get; init; }
    /// <summary>A platoon of its town's own (residents, bar squads, slaves; saved or stored): it never travels, its squad is run by the AI like the town's, and it is not in the roaming pool.</summary>
    public bool Pinned => Saved?.Pinned == true || Stored is not null;
    /// <summary>A save's wild platoon (a nest's or a homeless one, <see cref="Origin"/> -1): it never travels and counts against its wild site, not a town's pool.</summary>
    public bool Wild => Saved?.Wild is not null;
}

/// <summary>
/// A town's squad the population made, kept while it is not in the world (docs/simulation.md "Population"): each member's own <see cref="CharacterCold"/> (name, look, body,
/// kit, bounties) with where it stood, and the squad's home, shop and AI package. Its platoon is made of them again; a save writes them as the town's platoon.
/// </summary>
public sealed class StoredSquad
{
    public required string Name { get; init; }
    public Meitou.Data.World.TownBuilding? HomeBuilding { get; init; }
    public Trading.Shop? Shop { get; set; }
    /// <summary>The AI package it ran (saved as <c>currentPackage</c>; null: none).</summary>
    public string? Package { get; set; }
    /// <summary>The characters while the platoon is not in the world (empty while it is).</summary>
    public List<StoredMember> Members { get; } = [];
}

/// <summary>A character of a <see cref="StoredSquad"/>: its cold state, cleared of what only meant something in the world, and its place.</summary>
public sealed record StoredMember(CharacterCold Cold, Vector3 Position, float Yaw, bool Leader);

/// <summary>The roaming squads of a world, by id. Ids are never reused. Part of the state hash.</summary>
public sealed class PlatoonRegistry
{
    readonly SortedDictionary<int, Platoon> platoons = [];
    int nextId;

    public int Count => platoons.Count;
    public IEnumerable<Platoon> All => platoons.Values;
    public Platoon? Find(int id) => platoons.GetValueOrDefault(id);
    public int NextId() => nextId++;
    public void Add(Platoon p) => platoons[p.Id] = p;
    public bool Remove(int id) => platoons.Remove(id);

    internal void Hash(ref StateHasher h)
    {
        h.Add(platoons.Count);
        h.Add(nextId);
        foreach (var p in platoons.Values)
        {
            h.Add(p.Id);
            h.Add(p.Origin);
            h.Add(p.Faction);
            h.Add(p.Key);
            h.Add(p.Size);
            h.Add(p.Money);
            h.Add(p.Position);
            h.Add(p.Target);
            h.Add(p.GoingHome);
            h.Add(p.WaitLeft);
            h.Add((int)p.State);
            h.Add(p.SquadId);
            h.Add(p.UnloadAtTick);
            h.Add(p.OrderCooldown);
            if (p.Job != Ai.PlatoonJob.None || p.Point is not null)
            {
                // Only for platoons whose package gives an off-screen job, so worlds without AI packages hash as before.
                h.Add((int)p.Job);
                h.Add(p.Point.HasValue);
                h.Add(p.Point ?? default);
            }
            if (p.Stored is { } stored)
            {
                // Only for stored squads, so worlds without them hash as before.
                h.Add(stored.Members.Count);
                foreach (var m in stored.Members) h.Add(m.Position);
            }
            if (p.Saved is { } saved)
            {
                // Only for a save's platoons, so worlds without them hash as before.
                h.Add(saved.Pinned);
                h.Add(saved.Touched);
            }
        }
    }
}
