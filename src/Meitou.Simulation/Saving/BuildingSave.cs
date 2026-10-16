using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Construction;
using Meitou.Data.Save;
using Meitou.Simulation.Construction;

namespace Meitou.Simulation.Saving;

/// <summary>
/// The player's buildings and construction sites in a save (docs/formats/zones.md "Placement keys", docs/formats/save.md). Each is an instance of its zone file's <c>0-buildinglist</c>, id
/// <c>&lt;n&gt;-zone&lt;X&gt;.&lt;Y&gt;-INGAME</c>, with a GAMESTATE_BUILDING state <c>&lt;id&gt;-S23</c> in the keys of the original's writer (<c>FUN_1405547f0</c>, <b>Verified</b> by code; no player-built building
/// is in the sample saves, so a real file with one has not been compared): <c>name</c>, <c>owner faction ID</c>, <c>floornum</c>, <c>destroyed</c>, <c>flags</c>, <c>is complete</c>, <c>pause</c>,
/// <c>construction progress</c>, <c>&lt;i&gt;construction mats</c> (delivered of entry i), <c>world Y pos</c> (absolute) and the handles <c>handle</c>, <c>isInsideBuilding</c>, <c>town</c>, <c>residents</c>.
/// The instance's position is the absolute one (<b>Observed</b>: in the samples all 11405 game-made `-zoneX.Y-INGAME` buildings have an instance Y equal to <c>world Y pos</c>; the placements of the level data differ by a height above the terrain). Meitou's own key <c>meitou parent</c> (the parent's
/// instance id) carries a parent the original's handle cannot name, and wins on load.
/// </summary>
public static class BuildingSave
{
    public const string MeitouParent = "meitou parent";
    /// <summary>The container serial of every zone container (<b>Verified</b>, 1496 of 1496 handles).</summary>
    const int ContainerSerial = 11111;
    /// <summary>The <c>version</c> key of a building state (<b>Observed</b> 270 in every sample state).</summary>
    const int StateVersion = 270;
    const string StateSuffix = "-S23";   // the hex of GAMESTATE_BUILDING (35)

    static bool IsPlayerBuilt(string instanceId, FcsRecord? state, string playerFactionId) =>
        BuildingRegistry.TryParseId(instanceId, out _, out _) && state is not null && state.Strings.GetValueOrDefault("owner faction ID") == playerFactionId;

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// Reserves the ids of every <c>-zone&lt;X&gt;.&lt;Y&gt;-INGAME</c> building of the save (generated interiors share the pattern) and reads the player faction's into the world's registry, in zone and list
    /// order. Returns how many sites it made.
    /// </summary>
    public static int Read(SaveGame save, World world, GameDatabase db, string playerFactionId, int playerIndex)
    {
        var reg = world.Buildings;
        foreach (var zone in save.Zones.Values)
            if (zone.BuildingList is { } list)
                foreach (var i in list.Instances) reg.Reserve(i.Id);
        var states = HeldSave.BuildingStates(save);
        var idOf = new Dictionary<FcsRecord, string>(ReferenceEqualityComparer.Instance);
        foreach (var (id, record) in states) idOf[record] = id;
        var handles = save.BuildHandles();
        int made = 0;
        foreach (var zone in save.Zones.Values)
        {
            if (zone.BuildingList is not { } list) continue;
            foreach (var instance in list.Instances)
            {
                if (instance.IsRemoved || !states.TryGetValue(instance.Id, out var state) || !IsPlayerBuilt(instance.Id, state, playerFactionId)) continue;
                if (db.Find(instance.Target) is not { Type: FcsRecordType.BUILDING } record) continue;
                var data = BuildingBuildData.From(record);
                var materials = new MaterialSlot[data.Materials.Count];
                for (int k = 0; k < materials.Length; k++)
                    materials[k] = new MaterialSlot(data.Materials[k].Item, data.Materials[k].Required) { Delivered = state.Floats.GetValueOrDefault($"{k}construction mats") };
                float target = data.Target;
                bool complete = state.Bools.GetValueOrDefault("is complete", true);   // absent means complete
                string? parent = state.Strings.GetValueOrDefault(MeitouParent) is { Length: > 0 } own ? own
                    : state.Bools.GetValueOrDefault("is inside building") && handles.Building(Hand.Read(state, "isInsideBuilding")) is { } building ? idOf.GetValueOrDefault(building) : null;
                float y = state.Floats.TryGetValue("world Y pos", out float absolute) ? absolute : instance.Position.Y;
                reg.Add(new BuildSite
                {
                    Id = instance.Id,
                    Record = data.Id,
                    Name = data.Name,
                    Position = new Vector3(instance.Position.X, y, instance.Position.Z),
                    Rotation = instance.Rotation,
                    Floor = state.Ints.GetValueOrDefault("floornum"),
                    Parent = parent,
                    Owner = playerIndex,
                    Materials = materials,
                    Target = target,
                    Rate = data.Rate,
                    Progress = complete ? target : MathF.Min(state.Floats.GetValueOrDefault("construction progress"), target),
                    Complete = complete,
                    Paused = state.Bools.GetValueOrDefault("pause"),
                    Destroyed = state.Bools.GetValueOrDefault("destroyed"),
                });
                made++;
            }
        }
        return made;
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Writes the registry into the save: every site's instance and state in its zone file (a zone without a file gets one), and removes the player-built entries the world no longer has (a
    /// dismantled building). Everything else in the zone files is left as it was.
    /// </summary>
    public static void Write(SaveGame game, World world, string playerFactionId, ulong seed = 0)
    {
        var reg = world.Buildings;
        var live = new HashSet<string>(reg.All.Select(s => s.Id), StringComparer.Ordinal);
        foreach (var zone in game.Zones.Values)
        {
            if (zone.BuildingList is not { } list) continue;
            var byId = new Dictionary<string, FcsRecord>(StringComparer.Ordinal);
            foreach (var r in zone.File.Records) byId[r.StringId] = r;
            foreach (var instance in list.Instances.ToList())
            {
                if (live.Contains(instance.Id)) continue;
                var state = instance.States.Count > 0 ? byId.GetValueOrDefault(instance.States[0]) : null;
                if (!IsPlayerBuilt(instance.Id, state, playerFactionId)) continue;
                list.Instances.Remove(instance);
                zone.File.Records.Remove(state!);
                if (Hand.Read(state!, "handle") is { IsNull: false } h && zone.File.SlotLists.Count > 0)
                    zone.File.SlotLists[0] = [.. zone.File.SlotLists[0].Where(slot => slot != h.I)];
            }
        }
        if (reg.Count == 0) return;
        foreach (var site in reg.All) WriteSite(game, site, playerFactionId, seed);

        // Parents last: they may be sites written above.
        var states = HeldSave.BuildingStates(game);
        foreach (var site in reg.All)
        {
            if (!states.TryGetValue(site.Id, out var state)) continue;
            if (site.Parent is { } parent)
            {
                state.Strings[MeitouParent] = parent;
                state.Bools["is inside building"] = true;
                (states.TryGetValue(parent, out var p) && Hand.IsComplete(p, "handle") ? Hand.Read(p, "handle") : Hand.Null).Write(state, "isInsideBuilding");
            }
            else
            {
                state.Strings.Remove(MeitouParent);
                state.Bools["is inside building"] = false;
                Hand.Null.Write(state, "isInsideBuilding");
            }
        }
    }

    static void WriteSite(SaveGame game, BuildSite site, string playerFactionId, ulong seed)
    {
        var coordinate = site.Zone;
        if (!game.Zones.TryGetValue(coordinate, out var zone)) game.Zones[coordinate] = zone = new SaveZone(coordinate, new SaveFile());
        var file = zone.File;
        var list = zone.BuildingList;
        if (list is null)
        {
            list = new FcsRecord { Type = (int)FcsRecordType.INSTANCE_COLLECTION, Id = NextNumber(file), Name = "0", StringId = Meitou.Data.World.WorldLevelData.BuildingListId };
            file.Records.Add(list);
        }
        string stateId = site.Id + StateSuffix;
        var state = file.Records.FirstOrDefault(r => r.StringId == stateId && r.RecordType == FcsRecordType.GAMESTATE_BUILDING);
        if (state is null)
        {
            state = new FcsRecord { Type = (int)FcsRecordType.GAMESTATE_BUILDING, Id = NextNumber(file), Name = "0", StringId = stateId };
            file.Records.Add(state);
        }
        var at = site.Position;
        var instance = list.Instances.FirstOrDefault(i => i.Id == site.Id);
        if (instance is null)
        {
            instance = new FcsInstance { Id = site.Id };
            list.Instances.Add(instance);
        }
        instance.Target = site.Record;
        instance.Position = at;
        instance.Rotation = site.Rotation;
        instance.States.Clear();
        instance.States.Add(stateId);

        // The slot: kept while the state has one in this zone, else the next free one of the zone's slot list.
        if (file.SlotLists.Count == 0) file.SlotLists.Add([]);
        var hand = Hand.Read(state, "handle");
        if (hand.IsNull || hand.C != zone.ContainerId)
        {
            int slot = Math.Max(file.SlotLists[0].DefaultIfEmpty(0).Max(), file.Records.Where(r => r.RecordType == FcsRecordType.GAMESTATE_BUILDING)
                .Select(r => Hand.Read(r, "handle")).Where(h => !h.IsNull && h.C == zone.ContainerId).Select(h => h.I).DefaultIfEmpty(0).Max()) + 1;
            hand = new Hand((int)FcsRecordType.BUILDING, zone.ContainerId, ContainerSerial, slot, (int)Rng.Mix(Rng.StableHash(site.Id) ^ seed));
        }
        if (!file.SlotLists[0].Contains(hand.I)) file.SlotLists[0] = [.. file.SlotLists[0].Append(hand.I).Order()];

        state.Floats["world Y pos"] = site.Position.Y;
        state.Floats["player furniture Y offset"] = 0;
        state.Floats["construction progress"] = site.Progress;
        for (int i = 0; i < site.Materials.Length; i++) state.Floats[$"{i}construction mats"] = site.Materials[i].Delivered;
        state.Ints["version"] = StateVersion;
        state.Ints["flags"] = site.Parent is null ? 1 : 0;   // bit 0: is outside
        state.Ints["floornum"] = site.Floor;
        state.Ints["serialised interior"] = 1;
        hand.Write(state, "handle");
        Hand.Null.Write(state, "town");
        Hand.Null.Write(state, "residents");
        state.Bools["foliage"] = false;
        state.Bools["pause"] = site.Paused;
        state.Bools["is public day"] = true;
        state.Bools["is complete"] = site.Complete;
        state.Bools["is inside building"] = site.Parent is not null;
        state.Bools["destroyed"] = site.Destroyed;
        state.Strings["squad template"] = "";
        state.Strings["owner faction ID"] = playerFactionId;
        state.Strings["residents template"] = "";
        state.Strings["name"] = site.Name;
    }

    /// <summary>One more than the highest numeric id of the file's records.</summary>
    static int NextNumber(SaveFile file) => file.Records.Count == 0 ? Math.Max(file.Data.NextId, 0) + 1 : Math.Max(file.Data.NextId, file.Records.Max(r => r.Id)) + 1;
}
