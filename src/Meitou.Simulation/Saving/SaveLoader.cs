using System.Numerics;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Saving;

/// <summary>Settings of <see cref="SaveLoader.Load"/>.</summary>
public sealed class SaveLoadOptions
{
    /// <summary>Put the save's relation table into <c>PopulationData.Relations</c> (it is one table shared by the data; a loaded game replaces its start values).</summary>
    public bool ApplyRelations { get; init; } = true;

    /// <summary>
    /// Make the save's other platoons (not the player's) roaming <see cref="Platoon"/> stand-ins in <see cref="World.Platoons"/>, at their saved positions, when they belong to a placed
    /// town. Their characters are not made: the population system makes the members again from the template when a zone near them is active.
    /// </summary>
    public bool NpcPlatoons { get; init; } = true;

    /// <summary>The look of a spawned character, rolled by this source from the character's saved serial (the saved CHARACTER_APPEARANCE is not read into a drawable look yet). Null: no look.</summary>
    public IAppearanceSource? Appearances { get; init; }
}

/// <summary>
/// Loads a <see cref="SaveGame"/> into a <see cref="World"/> (docs/simulation.md "Saves"): the clock, the factions' state and relations, the player's money and
/// platoons with their characters (positions, stats, medical state), the camera's selection, and the other platoons as roaming stand-ins. Everything else a save
/// holds (towns, war state, weather, research, zone buildings, items, appearance sliders) stays in <see cref="LoadedSave.Source"/> for <see cref="SaveCapture"/> to carry on.
/// Call it on a world that has not run a tick yet and holds no characters.
/// </summary>
public static class SaveLoader
{
    public static LoadedSave Load(World world, SaveGame save, PopulationData data, SaveLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(data);
        options ??= new SaveLoadOptions();
        if (world.Tick != 0 || world.Characters.Count != 0) throw new InvalidOperationException("A save is loaded into a world that has not started and has no characters.");

        var camera = save.Camera;
        var loaded = new LoadedSave { Source = save, Clock = new SaveClock(camera.Day, camera.Hour, camera.Minute) };
        // The CAMERA's mods list names the data files the save was made with (without extension; gamedata.base as "base", docs/formats/save.md).
        var running = new HashSet<string>(data.Db.Files.Select(f => f == "gamedata.base" ? "base" : Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);
        foreach (var mod in camera.Record.References.GetValueOrDefault("mods") ?? [])
            if (!running.Contains(mod.TargetStringId)) loaded.MissingMods.Add(mod.TargetStringId);
        if (loaded.MissingMods.Count > 0) loaded.Notes.Add($"Made with data files this game does not load: {string.Join(", ", loaded.MissingMods)}.");
        LoadFactions(save, data, options, loaded);
        loaded.RelationBaseline = Snapshot(data.Relations);

        // The player.
        var playerFaction = save.PlayerFaction;
        int playerIndex = playerFaction is not null && data.FactionIndex(playerFaction.Id) is var pi and >= 0 ? pi : data.PlayerFaction;
        world.Player.Faction = playerIndex;
        world.Player.Money = camera.PlayerMoney;
        LoadTownKnowledge(save, data, world.Player);

        // Building locks and doors are taken from the zones when their town registers; held characters name their bed or cage by building instance.
        var buildingStates = HeldSave.BuildingStates(save);
        world.Fixtures.Saved = buildingStates;
        var instanceOf = new Dictionary<Meitou.Data.Fcs.FcsRecord, string>(ReferenceEqualityComparer.Instance);
        foreach (var (instance, record) in buildingStates) instanceOf[record] = instance;
        var handles = save.BuildHandles();

        // The research the player has finished, and the buildings the player made (docs/simulation.md "Buildings").
        ResearchSave.Load(save.Research, world.Research, data.Db);
        if (playerFaction is not null) BuildingSave.Read(save, world, data.Db, playerFaction.Id, playerIndex);

        var races = new Dictionary<string, RaceData?>(StringComparer.Ordinal);
        var parts = new Dictionary<string, BodyPartTemplate?>(StringComparer.Ordinal);
        var bySource = new Dictionary<SaveCharacter, CharacterId>();
        var assembly = new CharacterAssembly(data);
        foreach (var platoon in save.Platoons)
        {
            bool mine = playerFaction is not null && platoon.FactionId == playerFaction.Id;
            if (mine && platoon.IsLoaded)
                LoadPlayerPlatoon(world, platoon, data, options, loaded, playerIndex, races, parts, bySource, assembly, handles, instanceOf);
            else if (!mine && options.NpcPlatoons)
                LoadRoamingPlatoon(world, platoon, data, loaded);
        }
        if (loaded.PlayerPlatoons.Count > 0) world.Player.Squad = loaded.PlayerPlatoons[0].Squad.Id;

        // The selection.
        var selected = new List<Hand> { camera.SelectedCharacter };
        for (int k = 0; camera.Record.Ints.ContainsKey($"selected_characters{k}I"); k++) selected.Add(Hand.Read(camera.Record, $"selected_characters{k}"));
        foreach (var h in selected)
            if (!h.IsNull && handles.Character(h) is { } c && bySource.TryGetValue(c, out var id) && !world.Player.Selection.Contains(id))
                world.Player.Selection.Add(id);
        return loaded;
    }

    internal static float[] Snapshot(FactionRelations relations)
    {
        int n = relations.Count;
        var all = new float[n * n];
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++) all[a * n + b] = relations.Get(a, b);
        return all;
    }

    /// <summary>The towns' <c>discovered</c>, <c>explored</c> and <c>recently discovered</c> bools, matched to the placed towns by instance id.</summary>
    static void LoadTownKnowledge(SaveGame save, PopulationData data, PlayerState player)
    {
        var sites = data.Sites.Where(s => s.InstanceId.Length > 0).ToDictionary(s => s.InstanceId, s => s.Index, StringComparer.Ordinal);
        foreach (var town in save.Towns)
        {
            var flags = (town.Discovered ? TownKnowledge.Discovered : 0) | (town.Record.Bools.GetValueOrDefault("explored") ? TownKnowledge.Explored : 0)
                | (town.Record.Bools.GetValueOrDefault("recently discovered") ? TownKnowledge.Recent : 0);
            if (flags == 0) continue;
            if (sites.TryGetValue(town.Instance, out int site)) player.Know(site, flags);
            // A camp Meitou saved: its wild site and TOWN record; the position comes when the nest loads.
            else if (town.IsNest && town.Instance.StartsWith(SaveCapture.CampInstancePrefix, StringComparison.Ordinal)
                && town.Instance[SaveCapture.CampInstancePrefix.Length..].Split(':', 2) is [var index, var townId]
                && int.TryParse(index, System.Globalization.CultureInfo.InvariantCulture, out int wild) && data.Db.Find(townId) is { } record)
            {
                var nest = TownData.From(record);
                int faction = nest.Faction is { } id ? data.FactionIndex(id) : -1;
                player.KnowCamp(new CampView(wild, townId, nest.Name, nest.Faction ?? "", faction >= 0 && data.Factions[faction].NotReal, null, flags), flags);
            }
        }
    }

    static void LoadFactions(SaveGame save, PopulationData data, SaveLoadOptions options, LoadedSave loaded)
    {
        foreach (var f in save.Factions)
        {
            var state = new FactionState { Id = f.Id, Name = f.Name, Prosperity = f.Prosperity, PlatoonCounter = f.PlatoonCounter, IsPlayer = f.IsPlayer };
            foreach (var r in f.Relations) state.Relations[r.FactionId] = r;
            loaded.Factions.Add(state);
            int a = data.FactionIndex(f.Id);
            if (!options.ApplyRelations || a < 0) continue;
            foreach (var r in f.Relations)
                if (data.FactionIndex(r.FactionId) is var b and >= 0) data.Relations.Set(a, b, r.Relation);
        }
        foreach (var f in save.Factions)
            if (data.FactionIndex(f.Id) < 0) loaded.Notes.Add($"Faction {f.Id} ({f.Name}) is not in the game data; its state is kept in the save only.");
    }

    static void LoadPlayerPlatoon(World world, SavePlatoon platoon, PopulationData data, SaveLoadOptions options, LoadedSave loaded,
        int playerIndex, Dictionary<string, RaceData?> races, Dictionary<string, BodyPartTemplate?> parts, Dictionary<SaveCharacter, CharacterId> bySource, CharacterAssembly assembly,
        SaveHandles handles, IReadOnlyDictionary<Meitou.Data.Fcs.FcsRecord, string> instanceOf)
    {
        var squad = new Squad
        {
            Id = world.Squads.NextId(),
            TemplateId = platoon.SquadTemplate,
            Name = platoon.Name,
            Faction = playerIndex,
            Position = platoon.Position,
            HomeCentre = new Vector2(platoon.Position.X, platoon.Position.Z),
            HomeRadius = 100,
        };
        var table = world.Characters;
        foreach (var sc in platoon.Characters)
        {
            var record = data.Db.Find(sc.RecordId);
            if (record is null)
            {
                loaded.Notes.Add($"{platoon.Name}: character {sc.Name} is made from {sc.RecordId}, which the game data does not have; left out.");
                continue;
            }
            RaceData? race = RaceOf(sc, record, data.Db, races);
            CharacterStats? stats = sc.Stats is null ? null : CharacterStats.ReadSave(sc.Stats.Floats);
            MedicalState? medical = null;
            if (race is not null && sc.Medical is not null)
                medical = MedicalState.ReadSave(race, sc.Medical.Floats, sc.Medical.Bools, sc.Medical.Strings, id => PartOf(id, data.Db, parts));
            if (medical is { Dead: true })
            {
                loaded.Notes.Add($"{platoon.Name}: {sc.Name} is dead in the save; not made, and kept as it is when saving.");
                continue;
            }

            float yaw = SaveRotation.YawOf(sc.Rotation);
            float athletics = stats?.Athletics ?? 20;
            float speed = race is null ? 45 : medical is not null && stats is not null ? Speed.Run(race, stats, medical, 1) : Speed.RunUnhurt(race, athletics);
            var hot = new CharacterHot
            {
                Position = sc.Position,
                Yaw = yaw,
                Health = 100,
                Task = (byte)CharacterTask.Idle,
                Mode = SpeedMode.Free,
                MaxSpeed = speed,
                WalkSpeed = race?.WalkSpeed ?? 15,
            };
            string ownerId = sc.OwnerFactionId;
            int faction = ownerId.Length > 0 && data.FactionIndex(ownerId) is var fi and >= 0 ? fi : playerIndex;
            var handle = sc.Handle;
            // The kit: a character whose INVENTORY_STATE holds items gets them, and its look is its saved appearance wearing them. One with none (a Meitou save from before
            // inventories were written) keeps the old rule: the look rolled from the handle's serial and the inventory a new character of the look would have.
            var savedItems = sc.Inventory is { } invState && platoon.File is { } platoonFile ? SaveInventory.Read(platoonFile, invState) : [];
            CharacterAppearance? look = null;
            if (savedItems.Count > 0 && options.Appearances is { } source && SavedKit.LoadoutOf(sc.Appearance, savedItems, data.Db, handle.S & 0x7FFFFFFF) is { } kit)
                look = source.Create(sc.RecordId, kit);
            look ??= options.Appearances?.Create(sc.RecordId, ownerId.Length > 0 ? ownerId : null, handle.S & 0x7FFFFFFF);
            var cold = new CharacterCold
            {
                Name = sc.Name.Length > 0 ? sc.Name : record.Name,
                Faction = faction,
                Appearance = look,
                RecordId = sc.RecordId,
                SquadId = squad.Id,
                Role = sc.SquadMemberType,
                IsPlayer = faction == playerIndex,
                Orders = faction == playerIndex ? Orders.PlayerDefaults : CharacterOrders.None,
                FootprintRadius = race?.PathfindFootprintRadius ?? 0,
                Save = new SavedCharacterLink
                {
                    Source = sc,
                    PlatoonName = platoon.Name,
                    Slot = handle.I,
                    Rotation = sc.Rotation,
                    Yaw = yaw,
                    Stats = stats,
                    Medical = medical,
                },
            };
            // Bounties: the ledger keys of the character's state record (docs/simulation.md "Crimes and bounties").
            cold.Bounties = BountySave.Read(sc.GameState, data.Relations, loaded.Clock, world.Tick, world.TickSeconds);
            CrimeSave.Read(sc.GameState, cold);
            if (cold.IsPlayer && sc.GameState is { } orders) Orders.Read(orders, cold);
            // In a bed or a cage, and the sentence (docs/simulation.md "Cages, beds and locks"); the fixture binds when its town registers.
            HeldSave.Read(sc.GameState, cold, handles, instanceOf);
            cold.Save.InSomething = cold.InSomething;
            cold.Save.InWhat = cold.InWhat;
            // The body: the saved stats and medical state go to the body systems, the rest of the assembly (inventory, fighter) is made as for a new character.
            if (race is not null && stats is not null && medical is not null)
                assembly.AttachSaved(cold, race, stats, medical, record, savedItems.Count > 0 ? SavedKit.ToInventory(savedItems, data.Items, loaded.Notes) : null);
            else if (race is not null)
                loaded.Notes.Add($"{platoon.Name}: {cold.Name} has no stats or medical state that could be read; it has no body in the world.");
            var id = table.Spawn(hot, cold, world.Tick);
            squad.Members.Add(id);
            bySource[sc] = id;
            loaded.Characters.Add(id);
            loaded.SpawnedSlots.Add((platoon.Name, handle.I));
            if (sc.IsLeader && squad.Leader.IsNone) squad.Leader = id;
        }
        if (squad.Leader.IsNone && squad.Members.Count > 0) squad.Leader = squad.Members[0];
        world.Squads.Add(squad);
        loaded.PlayerPlatoons.Add(new LoadedPlatoon(platoon, squad));
    }

    static void LoadRoamingPlatoon(World world, SavePlatoon platoon, PopulationData data, LoadedSave loaded)
    {
        string baseTown = platoon.Record.Strings.GetValueOrDefault("basetown", "");
        int origin = -1;
        for (int i = 0; i < data.Sites.Count && baseTown.Length > 0; i++)
            if (data.Sites[i].Town.Id == baseTown) { origin = i; break; }
        if (origin < 0 || platoon.SquadTemplate.Length == 0)
        {
            loaded.Notes.Add($"{platoon.Name}: no placed town for its base town '{baseTown}' or no squad template; its platoon stays in the save only.");
            return;
        }
        // Its AI package: the saved currentPackage when the template lists it, else the slot rule's; the package's unloaded func is its off-screen job.
        string? saved = platoon.Record.Strings.TryGetValue("currentPackage", out var current) && current.Length > 0 ? current : null;   // a string: the AI_PACKAGE id (docs/formats/save.md)
        var (package, job) = data.Db.Find(platoon.SquadTemplate) is { } templateRecord && SquadFactory.IsTemplate(templateRecord)
            ? Ai.PlatoonJobs.For(data.Ai, Meitou.Data.Gameplay.SquadTemplate.From(templateRecord), saved) : (null, Ai.PlatoonJob.None);
        // Town-bound platoons are the town's own: its residents, bar squads, slaves and farm animals, which the population makes again when the town loads.
        // A stand-in for them as well would double the town, so they stay in the save only (docs/simulation.md "Saves").
        int siteFaction = data.SiteFactions.Count > origin ? data.SiteFactions[origin] : -1;
        if (!Travels(platoon, job, data.Sites[origin].Town, siteFaction >= 0 ? data.Factions[siteFaction] : null))
        {
            loaded.TownBoundPlatoons++;
            return;
        }
        var p = new Platoon
        {
            Id = world.Platoons.NextId(),
            Origin = origin,
            TemplateId = platoon.SquadTemplate,
            Faction = data.FactionIndex(platoon.FactionId),
            Key = Rng.Mix(Rng.StableHash(platoon.Name)),
            Size = Math.Max(platoon.CharCount, platoon.Characters.Count),
            Position = new Vector2(platoon.Position.X, platoon.Position.Z),
            State = PlatoonState.Unloaded,
            Package = package?.Id, Job = job,
        };
        world.Platoons.Add(p);
        loaded.RoamingPlatoons[p.Id] = platoon.Name;
    }

    /// <summary>
    /// Whether a saved NPC platoon travels and so gets a roaming stand-in (<b>engine rule</b>, from the five saves of docs/formats/save.md: residents are marked <c>is resident</c>, and
    /// the bar squads, slaves and farm animals of a town have a package whose unloaded func keeps them home, 4, or 0): not a resident, and an off-screen job that moves it
    /// (unloaded func 2, 5 or 6) and not a bar squad of the town or its faction (which the population makes with the town), or a template of its base town's <c>roaming squads</c>.
    /// </summary>
    internal static bool Travels(SavePlatoon platoon, Ai.PlatoonJob job, TownData baseTown, FactionData? townFaction)
    {
        string template = platoon.SquadTemplate;
        if (platoon.Record.Bools.GetValueOrDefault("is resident")) return false;
        if (baseTown.RoamingSquads.Any(r => r.Id == template)) return true;
        if (baseTown.BarSquads.Any(r => r.Id == template) || townFaction?.BarSquads.Any(r => r.Id == template) == true) return false;
        return job is Ai.PlatoonJob.PatrolShortRange or Ai.PlatoonJob.TravelTarget or Ai.PlatoonJob.TravelTargetFast;
    }

    static RaceData? RaceOf(SaveCharacter sc, GameRecord record, GameDatabase db, Dictionary<string, RaceData?> cache)
    {
        string? id = sc.Appearance is { } a && a.References.TryGetValue("race", out var list) && list.Count > 0 ? list[0].TargetStringId : null;
        id ??= CharacterAssembly.RaceId(record);
        return id is null ? null : CharacterAssembly.RaceDataOf(db, id, cache);
    }

    static BodyPartTemplate? PartOf(string id, GameDatabase db, Dictionary<string, BodyPartTemplate?> cache)
    {
        if (cache.TryGetValue(id, out var part)) return part;
        part = db.Find(id) is { } rec ? BodyPartTemplate.From(rec) : null;
        cache[id] = part;
        return part;
    }
}
