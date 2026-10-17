using System.Numerics;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation;

/// <summary>
/// Town squads kept as their characters (docs/simulation.md "Population"): when a town unloads, each squad it made becomes a pinned <see cref="Platoon"/> holding a
/// <see cref="StoredSquad"/>, and when the town loads again the platoon is made of those same characters (name, look, body, kit, bounties, place), never rolled anew.
/// </summary>
public sealed partial class PopulationSystem
{
    /// <summary>
    /// Takes the squad's characters out of the world into <paramref name="stored"/> (serial phase): each living member's cold state, cleared of what referred to the
    /// world, with its place; the dead are left out. Returns where the leader (else the first member) stood.
    /// </summary>
    static Vector2 Store(World world, Squad squad, StoredSquad stored)
    {
        var table = world.Characters;
        var at = new Vector2(squad.Position.X, squad.Position.Z);
        bool placed = false;
        stored.Members.Clear();
        foreach (var id in squad.Members)
        {
            if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { } cold) continue;
            var hot = table.Previous[slot];
            if (cold.DiedTick < 0 && hot.Alive)
            {
                Detach(cold);
                bool leads = id == squad.Leader;
                stored.Members.Add(new StoredMember(cold, hot.Position, hot.Yaw, leads));
                if (leads || !placed) at = new Vector2(hot.Position.X, hot.Position.Z);
                placed |= leads;
            }
            table.Remove(id);
        }
        return at;
    }

    /// <summary>Clears what a character's cold state holds only while it is in the world: paths, AI, carrying, treatment, combat and stealth state, a pending crime, jobs.</summary>
    static void Detach(CharacterCold c)
    {
        c.Path = [];
        c.PathExact = int.MaxValue;
        c.PathRequest = 0;
        c.PathApplied = 0;
        c.OrderQueue.Clear();
        c.GoalHeight = float.NaN;
        c.Animation = null;
        c.DrawnWeapon = default;
        c.ModeOverride = null;
        c.SpeedOverride = 0;
        c.InCombat = false;
        c.Fleeing = false;
        c.Engaged = false;
        c.Watchers = null;
        c.StealthExposure = default;
        c.StealthExposureTick = long.MinValue;
        c.TreatTarget = CharacterId.None;
        c.TreatSplint = false;
        c.MedicJob = false;
        c.Carrying = CharacterId.None;
        c.CarriedBy = CharacterId.None;
        c.CarryTarget = CharacterId.None;
        c.LiftTime = -1;
        c.RescueJob = false;
        c.Pending = null;
        c.Ai = null;
        c.PickingLock = null;
        c.BuildSite = null;
        c.BuildBar = 0;
        c.SquadId = -1;
    }

    /// <summary>
    /// The stored characters into the world as the squad of <paramref name="p"/> (serial phase), each where it stood, the leader wandering round the town and the rest
    /// following it; the stored list is emptied (the characters are the world's again). Null when no one is left.
    /// </summary>
    Squad? SpawnStored(World world, Platoon p, StoredSquad stored)
    {
        if (stored.Members.Count == 0) return null;
        var site = data.Sites[p.Origin];
        float radius = site.Town.SizeRadius * site.Town.TownRadiusMult;
        var table = world.Characters;
        var squad = new Squad
        {
            Id = world.Squads.NextId(),
            TemplateId = p.TemplateId,
            Name = stored.Name,
            Faction = p.Faction,
            Town = p.Origin,
            PlatoonId = p.Id,
            Pinned = true,
            Position = new Vector3(p.Position.X, world.Walkability.GroundHeight(p.Position.X, p.Position.Y), p.Position.Y),
            HomeCentre = new Vector2(site.Position.X, site.Position.Z),
            HomeRadius = Math.Max(radius * 0.8f, 40),
            HomeBuilding = stored.HomeBuilding,
            Shop = stored.Shop,
        };
        bool hasLeader = stored.Members.Any(m => m.Leader);
        foreach (var m in stored.Members)
        {
            var cold = m.Cold;
            bool leads = m.Leader || !hasLeader && squad.Members.Count == 0;
            cold.SquadId = squad.Id;
            var hot = new CharacterHot
            {
                Position = m.Position,
                Yaw = m.Yaw,
                Health = 100,
                Task = (byte)(leads ? CharacterTask.Wander : CharacterTask.Follow),
                Mode = SpeedMode.Walk,
                MaxSpeed = cold.Race is { } race && cold.Stats is { } stats && cold.Medical is { } medical ? Speed.Run(race, stats, medical, 1) : CharacterAssembly.StandInMaxSpeed,
                WalkSpeed = cold.Race?.WalkSpeed ?? 15,
                TaskTime = 0.5f + Rng.Float(Rng.Hash(world.Seed, (ulong)squad.Id << 8 | (uint)squad.Members.Count, RngPurpose.Think)) * 3,
            };
            var id = table.Spawn(hot, cold, world.Tick);
            squad.Members.Add(id);
            if (leads && squad.Leader.IsNone) squad.Leader = id;
        }
        stored.Members.Clear();
        if (squad.Leader.IsNone) squad.Leader = squad.Members[0];
        var lead = table.Next[squad.Leader.Slot].Position;
        foreach (var id in squad.Members)
        {
            var at = table.Next[id.Slot].Position;
            table.Cold(id.Slot)!.FormationOffset = new Vector2(at.X - lead.X, at.Z - lead.Z);
        }
        world.Squads.Add(squad);
        // A resident in its bed takes it again.
        world.Fixtures.Bind(world);
        return squad;
    }
}
