namespace Meitou.Simulation;

/// <summary>
/// The player in the world (docs/game/ui-input.md section 7, economy.md): the faction that is theirs, their money and the characters
/// selected. Selection is world state, changed by <see cref="SelectCommand"/>, so a scripted game of selects and orders replays the
/// same. There is no player until a new game starts (<see cref="Exists"/>).
/// </summary>
public sealed class PlayerState
{
    /// <summary>The player's faction, a position in the world's faction list (-1 before a game starts).</summary>
    public int Faction { get; set; } = -1;
    public bool Exists => Faction >= 0;
    /// <summary>Money in cats (NEW_GAME_STARTOFF <c>money</c>; how it is spent is the economy stage).</summary>
    public long Money { get; set; }
    /// <summary>The selected characters, in the order they were selected.</summary>
    public List<CharacterId> Selection { get; } = [];
    /// <summary>The player's squad (the one the start created).</summary>
    public int Squad { get; set; } = -1;
    /// <summary>The new game's character editor is open: the start's characters may still be remade, race and gender too (<see cref="Editing.EditCharacter"/>). Not saved.</summary>
    public bool InCreation { get; set; }
    /// <summary>The start's <c>force race</c> RACE ids the creation is limited to (empty: every playable race).</summary>
    public IReadOnlyList<string> CreationRaces { get; set; } = [];
    /// <summary>The character of another faction the HUD shows (<see cref="InspectCommand"/>), or <see cref="CharacterId.None"/>. Display only: not hashed and not saved.</summary>
    public CharacterId Inspected { get; set; } = CharacterId.None;
    /// <summary>
    /// What the player knows of each placed town (<see cref="PopulationData.Sites"/> order): discovered, explored, recently discovered (docs/game/ui-screens.md "Map").
    /// Replaced, never changed in place, so a snapshot may hold it. Not hashed: it follows from positions and nothing in the simulation reads it.
    /// </summary>
    public TownKnowledge[] Towns { get; private set; } = [];

    /// <summary>Adds <paramref name="flags"/> to a town's knowledge; true when that changed it.</summary>
    public bool Know(int site, TownKnowledge flags)
    {
        if (site < 0) return false;
        if (site < Towns.Length && (Towns[site] & flags) == flags) return false;
        var next = new TownKnowledge[Math.Max(Towns.Length, site + 1)];
        Towns.CopyTo(next, 0);
        next[site] |= flags;
        Towns = next;
        return true;
    }

    /// <summary>The camps and nests known (<see cref="CampView"/>), by wild site. Replaced, never changed in place.</summary>
    public IReadOnlyList<CampView> Camps { get; private set; } = [];

    /// <summary>Adds <paramref name="flags"/> to a camp's knowledge (adding the camp when new) and fills in its position when it has none; true when that changed it.</summary>
    public bool KnowCamp(CampView camp, TownKnowledge flags)
    {
        int i = -1;
        for (int k = 0; k < Camps.Count; k++) if (Camps[k].Site == camp.Site) { i = k; break; }
        var old = i >= 0 ? Camps[i] : null;
        var next = (old ?? camp) with { Known = (old?.Known ?? 0) | flags, Position = old?.Position ?? camp.Position };
        if (next == old) return false;
        var list = Camps.ToList();
        if (i >= 0) list[i] = next; else list.Add(next);
        Camps = list;
        return true;
    }

    internal void SeeDiscoveries()
    {
        if (Towns.Any(t => (t & TownKnowledge.Recent) != 0)) Towns = [.. Towns.Select(t => t & ~TownKnowledge.Recent)];
        if (Camps.Any(c => (c.Known & TownKnowledge.Recent) != 0)) Camps = [.. Camps.Select(c => c with { Known = c.Known & ~TownKnowledge.Recent })];
    }

    /// <summary>Drops characters that are gone (serial phases, after the commit).</summary>
    internal void Prune(CharacterTable table)
    {
        Selection.RemoveAll(c => !table.TryResolveNext(c, out _));
        if (!Inspected.IsNone && !table.TryResolveNext(Inspected, out _)) Inspected = CharacterId.None;
    }

    internal void Hash(ref StateHasher h)
    {
        h.Add(Faction);
        h.Add(Money);
        h.Add(Squad);
        h.Add(Selection.Count);
        foreach (var c in Selection)
        {
            h.Add(c);
        }
    }
}

/// <summary>Applies the player's selection and stop commands (step 1 of the tick). Move orders are the movement system's.</summary>
public sealed class PlayerSystem(PopulationData? data = null) : ITickSystem
{
    readonly CharacterAssembly? assembly = data is null ? null : new(data);
    internal PopulationData? Data => data;
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        var table = world.Characters;
        var player = world.Player;
        foreach (var command in commands)
        {
            switch (command)
            {
                case SelectCommand select:
                    var selectable = select.Characters.Where(id =>
                        table.TryResolveNext(id, out int slot) && table.Cold(slot)!.IsPlayer).ToArray();
                    if (selectable.Length == 0) break;
                    if (!select.Additive) player.Selection.Clear();
                    foreach (var id in selectable)
                        if (!player.Selection.Contains(id)) player.Selection.Add(id);
                    break;
                case SeenDiscoveriesCommand:
                    player.SeeDiscoveries();
                    break;
                case InspectCommand inspect:
                    if (!inspect.Target.IsNone) player.Selection.Clear();
                    player.Inspected = inspect.Target;
                    break;
                case StopCommand stop:
                    foreach (var id in stop.Characters.Count > 0 ? stop.Characters : player.Selection.ToArray())
                    {
                        if (!table.TryResolveNext(id, out int slot) || !table.Cold(slot)!.IsPlayer) continue;
                        MovementSystem.Stop(table, slot);
                    }
                    break;
                case Items.ReadMapItem read:
                    ReadMap(world, read);
                    break;
                case Items.MoveInventoryItem move:
                    if (table.TryResolveNext(move.Character, out int owner) && table.Cold(owner) is { IsPlayer: true, Inventory: { } inventory } cold)
                    {
                        if (!Items.InventoryCommands.Move(inventory, move.Item, move.Section, move.X, move.Y))
                            world.Post("Cannot move that item: the slot is occupied, incompatible or the inventory changed.");
                        else if (Items.InventoryCommands.IsEquipmentSection(move.Item.Section) || Items.InventoryCommands.IsEquipmentSection(move.Section))
                            assembly?.RefreshEquipment(cold);
                    }
                    break;
                case OrdersCommand or RunSpeedCommand:
                    Orders.Apply(world, command);
                    break;
                case Editing.EditCharacter edit:
                    if (data is not null) Editing.CharacterEditing.Apply(world, data, edit);
                    break;
                case Editing.FinishCreation:
                    player.InCreation = false;
                    break;
                case Items.ArrangeInventory arrange:
                    if (table.TryResolveNext(arrange.Character, out int character) && table.Cold(character) is { IsPlayer: true, Inventory: { } carried } &&
                        !Items.InventoryCommands.Arrange(carried)) world.Post("Cannot arrange inventory: the items do not fit.");
                    break;
            }
        }
    }

    public void SlowWorld(World world) => world.Player.Prune(world.Characters);

    /// <summary>A map item of a player character's inventory: its towns become discovered and recently discovered, and it is used up; with nothing new it stays.</summary>
    void ReadMap(World world, Items.ReadMapItem read)
    {
        var table = world.Characters;
        if (data is null || !table.TryResolveNext(read.Character, out int owner) || table.Cold(owner) is not { IsPlayer: true, Inventory: { } inventory }) return;
        var t = read.Item;
        var item = inventory.All().ElementAtOrDefault(t.Index);
        if (item is null || item.Record != t.Record || item.Section != t.Section || item.X != t.X || item.Y != t.Y || item.Type != Meitou.Data.Fcs.FcsRecordType.MAP_ITEM
            || data.Db.Find(item.Record) is not { } record) return;
        var sites = Items.MapItems.Reveals(data, record, world.Player.Towns, world.Seed);
        if (sites.Count == 0) { world.Post("You already know all these locations, no new information can be learned."); return; }
        foreach (int site in sites) world.Player.Know(site, TownKnowledge.Discovered | TownKnowledge.Recent);
        inventory.RemoveOne(item);
        world.Post($"{sites.Count} location(s) added to the map.");
    }
}

/// <summary>What the player knows of a town (the GAMESTATE_TOWN bools <c>discovered</c>, <c>explored</c>, <c>recently discovered</c>; docs/game/ui-screens.md "Map").</summary>
[Flags]
public enum TownKnowledge : byte
{
    None = 0,
    /// <summary>On the map.</summary>
    Discovered = 1,
    /// <summary>Visited: the map names it and shows its faction's colour.</summary>
    Explored = 2,
    /// <summary>Found but the map not yet opened since: the map marks it as new.</summary>
    Recent = 4,
}

/// <summary>
/// A camp or nest the player knows (a wild nest, docs/game/ui-screens.md "Map"): its wild site, TOWN record, name, owner faction, whether that faction is <c>not real</c>
/// (the map calls it a nest, else a camp), where it is (null until it has loaded once) and what is known.
/// </summary>
public sealed record CampView(int Site, string TownId, string Name, string FactionId, bool Wildlife, System.Numerics.Vector3? Position, TownKnowledge Known);
