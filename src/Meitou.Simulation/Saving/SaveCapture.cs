using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Saving;

/// <summary>Settings of <see cref="SaveCapture.Capture"/>.</summary>
public sealed class SaveCaptureOptions
{
    /// <summary>The data files in load order, for the CAMERA <c>mods</c> list.</summary>
    public IReadOnlyList<string> DataFiles { get; init; } = LoadOrder.BaseFiles;

    /// <summary>The placed towns of the level data, in order. A new save (no loaded one to carry on) writes a default state for each; without them it has no town states.</summary>
    public IReadOnlyList<TownPlacement>? Placements { get; init; }

    /// <summary>Supplies the stats and medical state of a character when it has none from a loaded save (the host's body systems). Null results fall back to the saved ones, then to defaults.</summary>
    public Func<CharacterId, (CharacterStats? Stats, MedicalState? Medical)>? Bodies { get; init; }

    /// <summary>Seeds the random serials of new handles.</summary>
    public ulong Seed { get; init; }

    /// <summary>
    /// The level data: the zone files of the visited zones a loaded save has none of are made from it (<see cref="ZoneSave"/>), and a new save's town states take their level state's
    /// handle. Null: no zone files are made and new towns get new handles.
    /// </summary>
    public WorldLevelData? Levels { get; init; }
}

/// <summary>
/// Turns a <see cref="World"/> into a <see cref="SaveGame"/> (docs/simulation.md "Saves"). With the <see cref="LoadedSave"/> the world was loaded from, the save it came
/// from is copied and the parts the world models are written over it: clock, money, the factions' relations and prosperity, the player's platoons and their characters (position,
/// facing, stats, medical state, new recruits, removals), the selection, the save's NPC platoons (stand-in positions; the characters of those that were in the world) and the
/// population's own town and roaming squads in the world as new platoons; everything else is carried through unchanged. Without one a new save
/// is made from the game data: all factions with their relations, the player's platoons and the population's squads, a default state per placed town, and empty weather, research and decals.
/// Either way every visited zone gets a zone file (<see cref="ZoneSave"/>).
/// </summary>
public static partial class SaveCapture
{
    /// <summary>
    /// The save a capture starts from: a copy of the loaded save, or a new one made from the data (all factions with the data's relations, a default state per placed town). It holds
    /// nothing of the world, and costs tens of milliseconds, so a host makes it ahead on a worker and hands it to <see cref="Capture"/> (which changes it: one use). Reads
    /// <paramref name="data"/>'s relations and the loaded save, neither of which a running world changes.
    /// </summary>
    public static SaveBuilder PrepareBase(PopulationData data, LoadedSave? loaded = null, SaveCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new SaveCaptureOptions();
        if (loaded is not null) return new SaveBuilder(loaded.Source.Clone(), options.Seed);
        var builder = new SaveBuilder(options.Seed);
        AddFreshFactions(builder, data);
        AddFreshTowns(builder, data, options);
        builder.AddEmptyWorldState();
        return builder;
    }

    /// <summary><paramref name="prepared"/> is a <see cref="PrepareBase"/> made with the same <paramref name="loaded"/> (null: made here, which is the slow part).</summary>
    public static SaveGame Capture(World world, PopulationData data, SaveClock clock, LoadedSave? loaded = null, SaveCaptureOptions? options = null, SaveBuilder? prepared = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        options ??= new SaveCaptureOptions();

        var builder = prepared ?? PrepareBase(data, loaded, options);
        var game = builder.Game;
        var camera = game.Camera;

        camera.Day = clock.Day;
        camera.Hour = clock.Hour;
        camera.Minute = clock.Minute;
        camera.PlayerMoney = (int)Math.Clamp(world.Player.Money, int.MinValue, int.MaxValue);
        camera.Version = SaveGame.GameVersion;
        camera.SetMods(options.DataFiles);
        if (data.PlayerFaction >= 0) camera.PlayerFactionName = data.Factions[data.PlayerFaction].Name;
        camera.Record.Strings[SaveLoader.SeedKey] = world.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

        UpdateFactions(game, data, loaded);
        WriteTownKnowledge(builder, data, world.Player);
        // The zone files of the zones visited since (before the locks, the player's buildings and the home handles, which write into them).
        ZoneSave.WriteVisited(game, world, data, options.Levels);
        var buildings = HeldSave.BuildingStates(game);
        WriteLocks(world, buildings);
        ResearchSave.Write(game.Research, world.Research);
        if (data.PlayerFaction >= 0) BuildingSave.Write(game, world, data.Factions[data.PlayerFaction].Id, options.Seed);
        var races = new Dictionary<string, RaceData?>(StringComparer.Ordinal);
        var written = WritePlayerSquads(builder, world, data, loaded, options, clock, buildings, races);
        WriteSelection(game, world, written);
        // The NPC platoons: the save's as they are now, and the population's own squads in the world as new platoons (docs/simulation.md "Saves").
        if (loaded is not null) WriteRoaming(game, world, loaded);
        WriteSavedNpcs(builder, world, data, options, clock, buildings, races);
        WriteFreshNpcs(builder, world, data, options, clock, buildings, races);
        // The wild: the nests made so far in the town list the original's way, and the wild squads in the world as new platoons of their nest or sector.
        if (world.Systems.OfType<PopulationSystem>().FirstOrDefault() is { } population)
        {
            var nests = WriteNests(builder, world, data, population, options.Seed);
            WriteWildNpcs(builder, world, data, population, nests, options, clock, buildings, races);
        }

        game.UpdateDerived();
        return game;
    }

    // ------------------------------------------------------------------ factions and towns

    static void AddFreshFactions(SaveBuilder builder, PopulationData data)
    {
        for (int a = 0; a < data.Factions.Count; a++)
        {
            var f = data.Factions[a];
            bool player = a == data.PlayerFaction;
            var table = new List<SaveRelation>();
            if (!player)
                for (int b = 0; b < data.Factions.Count; b++)
                    table.Add(new SaveRelation(data.Factions[b].Id, data.Relations.Get(a, b), 0, 0));
            builder.AddFaction(f.Id, f.Name, player, 1000, table);
        }
    }

    static void AddFreshTowns(SaveBuilder builder, PopulationData data, SaveCaptureOptions options)
    {
        if (options.Placements is null) return;
        foreach (var p in options.Placements)
        {
            if (data.Db.Find(p.TownId) is not { Type: FcsRecordType.TOWN } record) continue;
            var town = TownData.From(record);
            // The level state's handle, as the original keeps it (docs/formats/save.md "Object handles"): the buildings' and platoons' town handles name it.
            Hand? hand = p.StateId is not null && options.Levels?.Level.Find(p.StateId) is { } level && level.Ints.ContainsKey("handI")
                ? new Hand(level.GetInt("handTYPE"), level.GetInt("handC"), level.GetInt("handCS"), level.GetInt("handI"), level.GetInt("handS")) : null;
            builder.AddTown("Town state " + record.Name, p.InstanceId, town.Faction ?? "", isNest: false, WorldLayout.ZoneOf(p.Position.X, p.Position.Z), hand: hand);
        }
    }

    /// <summary>
    /// The instance of a camp's town state in Meitou saves written before nests were saved the original's way (wild site and TOWN record): still read, and still updated
    /// in a save that has one, never written new (camps are now their nest's state, <see cref="WriteNests"/>).
    /// </summary>
    internal const string CampInstancePrefix = "meitou-nest:";

    /// <summary>Writes what the player knows of each placed town into its state, by instance id. Flags are only added: the world never forgets a town.</summary>
    static void WriteTownKnowledge(SaveBuilder builder, PopulationData data, PlayerState player)
    {
        var game = builder.Game;
        var known = player.Towns;
        var byInstance = new Dictionary<string, TownKnowledge>(StringComparer.Ordinal);
        for (int i = 0; i < known.Length && i < data.Sites.Count; i++)
            if (known[i] != 0 && data.Sites[i].InstanceId.Length > 0) byInstance[data.Sites[i].InstanceId] = known[i];
        foreach (var camp in player.Camps)
            byInstance[CampInstancePrefix + camp.Site.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + camp.TownId] = camp.Known;
        foreach (var town in game.Towns)
        {
            if (!byInstance.TryGetValue(town.Instance, out var flags)) continue;
            if ((flags & TownKnowledge.Discovered) != 0) town.Record.Bools["discovered"] = true;
            if ((flags & TownKnowledge.Explored) != 0) town.Record.Bools["explored"] = true;
            town.Record.Bools["recently discovered"] = (flags & TownKnowledge.Recent) != 0;
        }
    }

    static void UpdateFactions(SaveGame game, PopulationData data, LoadedSave? loaded)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < data.Factions.Count; i++) index[data.Factions[i].Id] = i;
        foreach (var f in game.Factions)
        {
            if (loaded?.FactionOf(f.Id) is { } state)
            {
                f.Prosperity = state.Prosperity;
                f.PlatoonCounter = Math.Max(f.PlatoonCounter, state.PlatoonCounter);
            }
            if (loaded is null || !index.TryGetValue(f.Id, out int a) || f.IsPlayer || loaded.RelationBaseline.Length != index.Count * index.Count) continue;
            var table = f.Relations;
            var updated = new List<SaveRelation>(table.Count);
            bool changed = false;
            foreach (var r in table)
            {
                // A value the world changed since loading is written; one it did not touch stays as the save had it.
                float value = r.Relation;
                if (index.TryGetValue(r.FactionId, out int b) && data.Relations.Get(a, b) != loaded.RelationBaseline[a * index.Count + b])
                    value = data.Relations.Get(a, b);
                changed |= value != r.Relation;
                updated.Add(r with { Relation = value });
            }
            if (changed) f.SetRelations(updated);
        }
    }

    // ------------------------------------------------------------------ the player's platoons

    static Dictionary<CharacterId, SaveCharacter> WritePlayerSquads(SaveBuilder builder, World world, PopulationData data, LoadedSave? loaded, SaveCaptureOptions options, SaveClock clock,
        IReadOnlyDictionary<string, FcsRecord> buildings, Dictionary<string, RaceData?> races)
    {
        var game = builder.Game;
        var table = world.Characters;
        var written = new Dictionary<CharacterId, SaveCharacter>();
        string playerFactionId = data.PlayerFaction >= 0 ? data.Factions[data.PlayerFaction].Id : "204-gamedata.base";
        string playerFactionName = data.PlayerFaction >= 0 ? data.Factions[data.PlayerFaction].Name : "Nameless";
        var playerState = game.Factions.FirstOrDefault(f => f.Id == playerFactionId);
        int squads = 0, members = 0;

        // Platoons of the loaded save whose characters the world held: those not alive any more are removed.
        var spawned = loaded?.SpawnedSlots ?? [];
        var seen = new HashSet<(string Platoon, int Slot)>();

        foreach (var squad in world.Squads.All)
        {
            if (squad.PlatoonId >= 0) continue;   // a roaming or saved NPC platoon's squad (WriteSavedNpcs, WriteFreshNpcs)
            // A member of a loaded platoon is written whoever owns it (a prisoner or slave kept in the squad is not the player's but is in the file); only the
            // resident squads the population system made, which have no link, are left out by the player test.
            var alive = squad.Members.Where(m => table.IsAlive(m) && (table.Cold(m.Slot)!.IsPlayer || table.Cold(m.Slot)!.Save is not null)).ToList();
            if (alive.Count == 0) continue;
            squads++;

            string? name = alive.Select(m => table.Cold(m.Slot)!.Save?.PlatoonName).FirstOrDefault(n => n is not null);
            var platoon = name is null ? null : game.Platoons.FirstOrDefault(p => p.Name == name);
            if (platoon is null)
            {
                var at = table.Previous[alive[0].Slot].Position;
                platoon = builder.AddPlatoon(FreshName(game, playerState, playerFactionName), playerFactionName, playerFactionId, squad.TemplateId, at);
            }
            else
            {
                platoon.Position = table.Previous[alive[0].Slot].Position;
            }

            foreach (var id in alive)
            {
                members++;
                var cold = table.Cold(id.Slot)!;
                SaveCharacter? sc = null;
                if (cold.Save is { } link && link.PlatoonName == platoon.Name)
                {
                    sc = platoon.Characters.FirstOrDefault(c => c.Handle.I == link.Slot);
                    seen.Add((link.PlatoonName, link.Slot));
                }
                written[id] = WriteCharacter(builder, world, data, options, clock, buildings, races, platoon, sc, id, playerFactionId, squad.Leader == id, orders: true);
            }
            // The platoon's `imprisoned` (meaning Observed): set while a member is in a cage, cleared when the save had it and none is.
            bool caged = alive.Any(m => table.Cold(m.Slot)!.IsImprisoned);
            if (caged || platoon.Record.Bools.ContainsKey("imprisoned")) platoon.Record.Bools["imprisoned"] = caged;
            SetCharCount(platoon);
        }

        // Characters of a loaded platoon that the world had and now has no more (killed, left the squad): gone from the file.
        foreach (var (platoonName, slot) in spawned)
        {
            if (seen.Contains((platoonName, slot))) continue;
            var platoon = game.Platoons.FirstOrDefault(p => p.Name == platoonName);
            var sc = platoon?.Characters.FirstOrDefault(c => c.Handle.I == slot);
            if (platoon is not null && sc is not null) builder.RemoveCharacter(platoon, sc);
        }

        game.Camera.Squads = squads;
        game.Camera.Members = members;
        return written;
    }

    /// <summary>The next free <c>&lt;faction name&gt;_&lt;n&gt;</c> platoon name of a faction, counting its platoon counter on.</summary>
    static string FreshName(SaveGame game, SaveFaction? faction, string factionName)
    {
        int n = faction?.PlatoonCounter ?? 0;
        while (game.Platoons.Any(p => p.Name == $"{factionName}_{n}")) n++;
        if (faction is not null) faction.PlatoonCounter = n + 1;
        return $"{factionName}_{n}";
    }

    static void SetCharCount(SavePlatoon platoon)
    {
        platoon.CharCount = platoon.Characters.Count;
        if (platoon.Collection is { } c) c.Ints["char count"] = platoon.Characters.Count;
        platoon.Record.Ints["char count"] = platoon.Characters.Count;
    }

    /// <summary>
    /// One character as it is in the world into its platoon file: its records (<paramref name="sc"/>), or new ones when null. Place and facing, name, leader flag,
    /// bounties, crime marks, the player's orders (<paramref name="orders"/>, the player's squads), bed or cage; stats and medical state (from the host, else the
    /// loaded ones, else defaults for a new one); a new one's look; what it carries.
    /// </summary>
    static SaveCharacter WriteCharacter(SaveBuilder builder, World world, PopulationData data, SaveCaptureOptions options, SaveClock clock, IReadOnlyDictionary<string, FcsRecord> buildings,
        Dictionary<string, RaceData?> races, SavePlatoon platoon, SaveCharacter? sc, CharacterId id, string ownerFactionId, bool isLeader, bool orders)
    {
        var table = world.Characters;
        var cold = table.Cold(id.Slot)!;
        ref readonly var hot = ref table.Previous[id.Slot];
        bool isNew = sc is null;
        sc ??= builder.AddCharacter(platoon, cold.RecordId, cold.Name, ownerFactionId, hot.Position, SaveRotation.OfYaw(hot.Yaw), isLeader: isLeader, squadMemberType: cold.Role);
        // The handle's own serial is "random" in the original, and the loader rolls a character's look from it: a character with a rolled look gets the look's seed
        // as its serial, so loading a save without its items (one written before inventories were) rolls the same face, hair and clothes again.
        if (isNew && cold.Appearance?.Loadout is { } loadout) sc.Handle = sc.Handle with { S = loadout.Seed };

        // Where and which way.
        sc.Position = hot.Position;
        var link = cold.Save;
        sc.Rotation = link is not null && MathF.Abs(SaveRotation.Difference(hot.Yaw, link.Yaw)) < 1e-4f ? link.Rotation : SaveRotation.OfYaw(hot.Yaw);
        if (sc.GameState is { } g)
        {
            g.Strings["name"] = cold.Name;
            g.Bools["is leader"] = isLeader;
            BountySave.Write(g, cold.Bounties, data.Relations, clock, world.Tick, world.TickSeconds);
            CrimeSave.Write(g, cold);
            if (orders) Orders.Write(g, cold);
            HeldSave.Write(g, cold, link, world.Fixtures, buildings);
        }

        // Body: stats and medical state, from the host, else the loaded ones, else (for a new character) defaults.
        var (stats, medical) = options.Bodies?.Invoke(id) ?? (null, null);
        stats ??= cold.Stats ?? link?.Stats;
        medical ??= cold.Medical ?? link?.Medical;
        WriteBody(sc, cold.RecordId, stats, medical, isNew, data, races);
        if (isNew || cold.LookEdited) WriteAppearance(sc, cold, data.Db, races, replace: !isNew);
        // What it carries and wears, as it is now; items read from a save keep their records' other keys.
        if (cold.Inventory is { } inventory) builder.SetInventory(platoon, sc, SavedKit.FromInventory(inventory));
        return sc;
    }

    static void WriteBody(SaveCharacter sc, string recordId, CharacterStats? stats, MedicalState? medical, bool isNew, PopulationData data, Dictionary<string, RaceData?> races)
    {
        if (stats is null && isNew) stats = new CharacterStats();
        if (stats is not null && sc.Stats is { } sr)
        {
            stats.WriteSave(sr.Floats);
            SaveBuilder.FillLegacyStats(sr);
        }
        if (medical is null && isNew && RaceOf(recordId, data.Db, races) is { } race)
            medical = MedicalState.Create(race, stats?.Strength ?? 50);
        if (medical is not null && sc.Medical is { } mr)
            medical.WriteSave(mr.Floats, mr.Bools, mr.Strings);
    }

    // ------------------------------------------------------------------ NPC platoons

    /// <summary>
    /// The save's NPC platoons (docs/simulation.md "Saves"): a stand-in that travelled gets its position and package; one whose characters were in the world gets
    /// them as they are (in the world now) or were (when it left): place, facing, body, kit, bounties; a character whose body is gone leaves the file.
    /// </summary>
    static void WriteSavedNpcs(SaveBuilder builder, World world, PopulationData data, SaveCaptureOptions options, SaveClock clock, IReadOnlyDictionary<string, FcsRecord> buildings,
        Dictionary<string, RaceData?> races)
    {
        if (world.Saved is not { } saved) return;
        var game = builder.Game;
        var table = world.Characters;
        var byName = new Dictionary<string, SavePlatoon>(StringComparer.Ordinal);
        foreach (var x in game.Platoons) byName.TryAdd(x.Name, x);
        foreach (var sp in saved.Platoons)
        {
            if (!byName.TryGetValue(sp.Name, out var platoon)) continue;
            if (!sp.Touched) continue;   // a stand-in that travelled is written by WriteRoaming
            var p = sp.PlatoonId >= 0 ? world.Platoons.Find(sp.PlatoonId) : null;
            var squad = p is { State: PlatoonState.Loaded } ? world.Squads.Find(p.SquadId) : null;
            if (squad is not null && table.TryResolveNext(squad.Leader, out int leaderSlot)) platoon.Position = table.Previous[leaderSlot].Position;
            if (p is not null) platoon.Money = (int)Math.Clamp(squad?.Money ?? p.Money, int.MinValue, int.MaxValue);
            foreach (var m in sp.Members)
            {
                var sc = platoon.Characters.FirstOrDefault(c => c.Handle.I == m.Slot);
                if (sc is null) continue;
                if (m.Gone)
                {
                    builder.RemoveCharacter(platoon, sc);
                    continue;
                }
                if (!m.Live.IsNone && table.IsAlive(m.Live))
                    WriteCharacter(builder, world, data, options, clock, buildings, races, platoon, sc, m.Live, sc.OwnerFactionId, squad?.Leader == m.Live, orders: false);
                else if (m.Position is { } at)
                {
                    // As it was when its platoon left the world.
                    sc.Position = at;
                    if (MathF.Abs(SaveRotation.Difference(m.Yaw, SaveRotation.YawOf(sc.Rotation))) >= 1e-4f) sc.Rotation = SaveRotation.OfYaw(m.Yaw);
                    WriteBody(sc, sc.RecordId, m.Stats, m.Medical, false, data, races);
                    if (sc.GameState is { } g) BountySave.Write(g, m.Bounties, data.Relations, clock, world.Tick, world.TickSeconds);
                    if (m.Inventory is { } inventory) builder.SetInventory(platoon, sc, SavedKit.FromInventory(inventory));
                }
            }
            SetCharCount(platoon);
        }
    }

    /// <summary>
    /// The NPC squads the population made (a town's residents and bar squads, and roaming squads while they are in the world) as new platoons, so a save keeps them
    /// where they are: a new PLATOON with its <c>basetown</c>, <c>is resident</c> for a town's residents and the AI package running; its characters as new ones
    /// (look, kit, body). Wild squads and roaming stand-ins (no characters) are not written: the population makes them again.
    /// </summary>
    static void WriteFreshNpcs(SaveBuilder builder, World world, PopulationData data, SaveCaptureOptions options, SaveClock clock, IReadOnlyDictionary<string, FcsRecord> buildings,
        Dictionary<string, RaceData?> races)
    {
        var game = builder.Game;
        var table = world.Characters;
        var factions = new Dictionary<string, SaveFaction>(StringComparer.Ordinal);
        foreach (var f in game.Factions) factions.TryAdd(f.Id, f);
        var townHands = new Dictionary<string, Hand>(StringComparer.Ordinal);
        foreach (var t in game.Towns)
            if (t.Instance.Length > 0 && !t.Handle.IsNull) townHands.TryAdd(t.Instance, t.Handle);
        foreach (var squad in world.Squads.All)
        {
            if (squad.Town < 0 || squad.Town >= data.Sites.Count || squad.Faction < 0 || squad.Faction >= data.Factions.Count || squad.Faction == data.PlayerFaction) continue;
            Platoon? roamer = null;
            if (squad.PlatoonId >= 0)
            {
                roamer = world.Platoons.Find(squad.PlatoonId);
                if (roamer is null || roamer.Saved is not null) continue;   // a save's platoon (WriteSavedNpcs)
            }
            var alive = squad.Members.Where(m => table.IsAlive(m) && table.Cold(m.Slot) is { IsPlayer: false, DiedTick: < 0, Save: null }).ToList();
            if (alive.Count == 0) continue;
            var faction = data.Factions[squad.Faction];
            factions.TryGetValue(faction.Id, out var state);
            var site = data.Sites[squad.Town];
            var leader = alive.Contains(squad.Leader) ? squad.Leader : alive[0];
            var platoon = builder.AddPlatoon(FreshName(game, state, faction.Name), faction.Name, faction.Id, squad.TemplateId, table.Previous[leader.Slot].Position,
                (int)Math.Clamp(squad.Money, int.MinValue, int.MaxValue));
            var record = platoon.Record;
            record.Strings["basetown"] = site.Town.Id;
            int siteFaction = data.SiteFactions.Count > squad.Town ? data.SiteFactions[squad.Town] : -1;
            // `sqt` is the town pool it counts against: 1 residents (and bar squads), 2 roaming (docs/formats/save.md "NPC platoons").
            record.Ints["sqt"] = roamer is null ? 1 : 2;
            // A squad with a home is a resident (every platoon of the samples with a `homebuilding` has `is resident`, and only those): a default resident too.
            record.Bools["is resident"] = roamer is null && (squad.HomeBuilding is not null
                || PopulationSystem.ResidentEntries(site.Town, siteFaction >= 0 ? data.Factions[siteFaction] : null).Any(e => e.Id == squad.TemplateId));
            if ((roamer?.Package ?? squad.Ai?.Current?.Id) is { } package) record.Strings["currentPackage"] = package;
            // Its town and home building by handle, as the original names them (docs/formats/save.md "NPC platoons").
            if (townHands.TryGetValue(site.InstanceId, out var townHand)) townHand.Write(record, "hometown");
            if (squad.HomeBuilding is { } home && HomeHandle(game, home) is { } homeHand) homeHand.Write(record, "homebuilding");
            WriteMapArea(record, data, table.Previous[leader.Slot].Position);
            foreach (var id in alive)
            {
                int f = table.Cold(id.Slot)!.Faction;
                string owner = f >= 0 && f < data.Factions.Count ? data.Factions[f].Id : faction.Id;
                WriteCharacter(builder, world, data, options, clock, buildings, races, platoon, null, id, owner, id == leader, orders: false);
            }
            SetCharCount(platoon);
        }
    }

    /// <summary>
    /// The handle a platoon's <c>homebuilding</c> names a town building by: its state's <c>handle</c> when the save's zone file holds the building, else the handle of its
    /// level-data state, which the original gives the building when it loads a zone that has no saved state of it (docs/formats/save.md "Object handles"). Null when it has neither.
    /// </summary>
    static Hand? HomeHandle(SaveGame game, TownBuilding b)
    {
        if (game.Zones.TryGetValue(b.Zone, out var zone) && zone.BuildingList is { } list)
        {
            var entry = list.Instances.FirstOrDefault(i => i.Id == b.InstanceId);
            if (entry is not null && zone.File.Records.FirstOrDefault(r => r.RecordType == FcsRecordType.GAMESTATE_BUILDING && entry.States.Contains(r.StringId)) is { } state
                && Hand.Read(state, "handle") is { IsNull: false } saved)
                return saved;
        }
        return b.HandleSlot >= 0 ? new Hand((int)FcsRecordType.BUILDING, SaveZone.ContainerIdOf(b.Zone), ZoneContainerSerial, b.HandleSlot, b.HandleSerial) : null;
    }

    /// <summary>The <c>CS</c> of every zone container (docs/formats/save.md "Object handles").</summary>
    const int ZoneContainerSerial = 11111;

    /// <summary>The registered buildings' locks and doors, and the homes a lock-up round locked, into their zones' existing state records (a building the save has no record of, or a new save without zones, is left out).</summary>
    static void WriteLocks(World world, IReadOnlyDictionary<string, FcsRecord> buildings)
    {
        var locks = world.Systems.OfType<Locks.LockSystem>().FirstOrDefault();
        foreach (var b in world.Fixtures.Buildings)
        {
            if (!buildings.TryGetValue(b, out var state)) continue;
            world.Fixtures.WriteBuilding(b, state);
            if (locks is null) continue;
            if (locks.IsHouseLocked(world, b)) state.Bools[Locks.LockSystem.HouseLockedKey] = true;
            else state.Bools.Remove(Locks.LockSystem.HouseLockedKey);
        }
    }

    static RaceData? RaceOf(string recordId, GameDatabase db, Dictionary<string, RaceData?> cache)
    {
        if (db.Find(recordId) is not { } record || CharacterAssembly.RaceId(record) is not { } id) return null;
        return CharacterAssembly.RaceDataOf(db, id, cache);
    }

    /// <summary>
    /// The CHARACTER_APPEARANCE of a new character: the generated one when the character has a look (its sliders, head, hair, skin tone), else only the race and the
    /// editor flags. With <paramref name="replace"/> (a loaded character edited in the character editor) the record's keys become the look's exactly: the editor starts
    /// from a copy of the saved record, so the original's key set and order are kept. The loader reads the record back into the look (<see cref="SavedKit"/>).
    /// </summary>
    static void WriteAppearance(SaveCharacter sc, CharacterCold cold, GameDatabase db, Dictionary<string, RaceData?> races, bool replace = false)
    {
        var target = sc.Appearance;
        if (target is null) return;
        if (cold.Appearance?.Loadout?.Appearance.Record is { } source)
        {
            if (ReferenceEquals(source, target)) return;
            if (replace)
            {
                target.Bools.Clear(); target.Floats.Clear(); target.Ints.Clear(); target.Vector3s.Clear(); target.Strings.Clear(); target.References.Clear();
            }
            foreach (var (k, v) in source.Bools) target.Bools[k] = v;
            foreach (var (k, v) in source.Floats) target.Floats[k] = v;
            foreach (var (k, v) in source.Ints) target.Ints[k] = v;
            foreach (var (k, v) in source.Vector3s) target.Vector3s[k] = v;
            foreach (var (k, v) in source.Strings) target.Strings[k] = v;
            foreach (var (k, v) in source.References) target.References[k] = [.. v];
            return;
        }
        target.Bools["sex female"] = false;
        target.Bools["in editor"] = false;
        target.Ints["body version"] = 2;
        target.Ints["Age"] = 20;
        target.Vector3s["Skin Tone"] = new Vector3(0.5f, 0.5f, 0.5f);
        foreach (var k in new[] { "head", "idle stance", "beard", "hair style" }) target.Strings[k] = "";
        if (db.Find(cold.RecordId) is { } record)
            foreach (var r in record.GetReferences("race"))
                if (r.Values.Value0 > 0) { target.References["race"] = [new FcsReference(r.TargetStringId, 0, 0, 0)]; break; }
    }

    static void WriteSelection(SaveGame game, World world, Dictionary<CharacterId, SaveCharacter> written)
    {
        var camera = game.Camera;
        var record = camera.Record;
        for (int k = 0; record.Ints.ContainsKey($"selected_characters{k}I"); k++)
            foreach (var part in new[] { "TYPE", "C", "CS", "I", "S" })
                record.Ints.Remove($"selected_characters{k}{part}");
        int n = 0;
        foreach (var id in world.Player.Selection)
        {
            if (!written.TryGetValue(id, out var sc) || sc.Handle.IsNull) continue;
            if (n == 0) camera.SelectedCharacter = sc.Handle;
            sc.Handle.Write(record, $"selected_characters{n}");
            n++;
        }
        if (n == 0) camera.SelectedCharacter = Hand.Null;
    }

    /// <summary>Puts the roaming platoons' positions and AI packages (<c>currentPackage</c>, docs/formats/save.md PLATOON) into their PLATOON records.</summary>
    static void WriteRoaming(SaveGame game, World world, LoadedSave loaded)
    {
        foreach (var (id, name) in loaded.RoamingPlatoons)
        {
            if (world.Platoons.Find(id) is not { } p) continue;
            var platoon = game.Platoons.FirstOrDefault(x => x.Name == name);
            if (platoon is null) continue;
            var at = platoon.Position;
            platoon.Position = new Vector3(p.Position.X, at.Y, p.Position.Y);
            if (p.Package is { } package) platoon.Record.Strings["currentPackage"] = package;
        }
    }
}
