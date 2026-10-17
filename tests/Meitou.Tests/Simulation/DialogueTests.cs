using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Dialogue;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using static Meitou.Tests.Gameplay.EconomyTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Player conversations (docs/simulation.md "Dialogue", docs/game/dialogue.md) on an invented database.</summary>
public class DialogueTests
{
    static FcsReference Ref(string id, int v0 = 0) => new(id, v0, 0, 0);

    /// <summary>
    /// A villager with two event-1 dialogues: "rich" (needs the player's money &gt;= 500, so it scores 1) greets with the GREETING word swap and offers to sell for c.100, which locks it;
    /// "plain" (score 0) says hello.
    /// </summary>
    static PopulationData Data() => PopulationData.Create(Db([
        Rec("p-d", FcsRecordType.FACTION, r => r.Name = "Nameless"),
        Rec("f-d", FcsRecordType.FACTION, r => r.Name = "Villagers"),
        Rec("npc-d", FcsRecordType.CHARACTER, r => { r.Name = "Villager"; r.References["dialogue"] = [Ref("rich-d"), Ref("plain-d")]; }),
        Rec("money-d", FcsRecordType.DIALOG_ACTION, r => r.Ints["condition name"] = DialogueConditions.PlayerMoney),
        Rec("pay-d", FcsRecordType.DIALOG_ACTION, r => r.Ints["action name"] = DialogueActions.TakeMoney),
        Rec("rich-d", FcsRecordType.DIALOGUE, r => { r.References["conditions"] = [Ref("money-d", 500)]; r.References["lines"] = [Ref("rich-l")]; }),
        Rec("rich-l", FcsRecordType.DIALOGUE_LINE, r => { r.Strings["text0"] = "/GREETING/, rich /YOURNAME/."; r.References["lines"] = [Ref("buy-l"), Ref("bye-l")]; }),
        Rec("buy-l", FcsRecordType.DIALOGUE_LINE, r =>
        {
            r.Ints["speaker"] = (int)Talker.Target;
            r.Strings["text0"] = "Here, c./COST/.";
            r.References["effects"] = [Ref("pay-d", 100)];
            r.References["locks"] = [Ref("rich-d")];
        }),
        Rec("bye-l", FcsRecordType.DIALOGUE_LINE, r => { r.Ints["speaker"] = (int)Talker.Target; r.Strings["text0"] = "Bye."; }),
        Rec("plain-d", FcsRecordType.DIALOGUE, r => r.References["lines"] = [Ref("plain-l")]),
        Rec("plain-l", FcsRecordType.DIALOGUE_LINE, r => r.Strings["text0"] = "Hello."),
        Rec("greet-d", FcsRecordType.WORD_SWAPS, r => { r.Name = "GREETING"; r.References["lines"] = [Ref("greet-l")]; }),
        Rec("greet-l", FcsRecordType.DIALOGUE_LINE, r => r.Strings["text0"] = "well met"),
    ]), []);

    sealed class Village : IDisposable
    {
        public PopulationData Data { get; } = DialogueTests.Data();
        public SimWorld World { get; }
        public DialogueSystem Dialogue { get; }
        public CharacterId Hero { get; }
        public CharacterId Villager { get; }

        public Village(int threads = 1, float apart = 20)
        {
            Dialogue = new DialogueSystem(Data);
            var ground = new OpenGroundWalkability((_, _) => 150);
            World = new SimWorld(new WorldSettings { Seed = 5, Threads = threads, PublishSnapshots = false }, ground, [new MovementSystem(new PathService(ground, true)), Dialogue]);
            World.Player.Faction = Data.PlayerFaction;
            Hero = Spawn("hero", Vector2.Zero, Data.PlayerFaction, true, "");
            Villager = Spawn("Villager", new Vector2(apart, 0), Data.FactionIndex("f-d"), false, "npc-d");
        }

        public void Dispose() => World.Dispose();

        CharacterId Spawn(string name, Vector2 at, int faction, bool player, string record)
        {
            var squad = new Squad { Id = World.Squads.NextId(), TemplateId = "t", Faction = faction, HomeCentre = at, HomeRadius = 10 };
            World.Squads.Add(squad);
            var cold = new Meitou.Tests.Combat.Combatant { Name = name }.MakeCold();
            cold.Faction = faction;
            cold.IsPlayer = player;
            cold.RecordId = record;
            cold.Inventory = new Inventory();
            cold.SquadId = squad.Id;
            var id = World.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 150, at.Y), Health = 100, MaxSpeed = 60, WalkSpeed = 15, Task = (byte)CharacterTask.Idle }, cold, 0);
            squad.Members.Add(id);
            squad.Leader = id;
            return id;
        }

        public void Say(SimCommand c) => World.Commands.Enqueue(c with { Tick = World.Tick });
        public ConversationView? Talk => Dialogue.PublishConversation();
    }

    [Fact]
    public void The_best_scoring_passing_dialogue_is_said_with_its_word_swaps_and_its_effects_lock_it()
    {
        var data = Data();
        Assert.Equal(1, data.Dialogue.Node("rich-d")!.ListScore);   // one condition
        Assert.Equal(0, data.Dialogue.Node("plain-d")!.ListScore);

        using var v = new Village();
        v.World.Player.Money = 100;   // too poor for "rich": the condition fails, "plain" is said
        v.Say(new TalkOrder(v.Hero, v.Villager) { Tick = 0 });
        v.World.RunTicks(2);
        Assert.Equal(("Hello.", "[Leave]"), (v.Talk!.Text, v.Talk.Choices.Single()));
        v.Say(new DialogueChoice(0) { Tick = 0 });
        v.World.RunTicks(1);
        Assert.Null(v.Talk);

        v.World.Player.Money = 1000;
        v.Say(new TalkOrder(v.Hero, v.Villager) { Tick = 0 });
        v.World.RunTicks(2);
        Assert.Equal("Well met, rich hero.", v.Talk!.Text);
        Assert.Equal(["Here, c.100.", "Bye."], v.Talk.Choices);
        v.Say(new DialogueChoice(0) { Tick = 0 });
        v.World.RunTicks(1);
        Assert.Null(v.Talk);   // nothing follows the reply
        Assert.Equal(900, v.World.Player.Money);
        var cold = v.World.Characters.Cold(v.Villager.Slot)!;
        Assert.True(cold.DialogueLocks!["rich-d"]);

        v.Say(new TalkOrder(v.Hero, v.Villager) { Tick = 0 });   // locked: back to "plain"
        v.World.RunTicks(2);
        Assert.Equal("Hello.", v.Talk!.Text);

        // The lock is saved as the original's lok0 pair.
        var state = new FcsRecord { StringId = "s", RecordType = FcsRecordType.GAMESTATE_CHARACTER };
        DialogueSave.Write(state, cold);
        Assert.Equal(("rich-d", true), (state.Strings["lok0"], state.Bools["lok0"]));
        var loaded = new Meitou.Tests.Combat.Combatant { Name = "x" }.MakeCold();
        DialogueSave.Read(state, loaded);
        Assert.True(loaded.DialogueLocks!["rich-d"]);
    }

    /// <summary>The hero walks 300 up to the villager and talks twice (rich, buying; then plain), pinned in <see cref="GoldenHashTests"/>.</summary>
    internal static List<ulong> Scripted(int threads)
    {
        using var v = new Village(threads, apart: 300);
        v.World.Player.Money = 1000;
        var hashes = new List<ulong>();
        string said = "";
        foreach (int target in new[] { 1, 60, 300, 310, 320, 400 })
        {
            v.World.RunTicks(target - (int)v.World.Tick);
            if (target == 1) v.Say(new TalkOrder(v.Hero, v.Villager) { Tick = 0 });
            if (target is 300 or 320) said += v.Talk?.Text + "|";
            if (target == 300) v.Say(new DialogueChoice(0) { Tick = 0 });
            if (target == 310) v.Say(new TalkOrder(v.Hero, v.Villager) { Tick = 0 });
            hashes.Add(v.World.StateHash());
        }
        Assert.Equal("Well met, rich hero.|Hello.|", said);   // it covers what it is for: an approach, a choice with effects, the lock
        return hashes;
    }
}

/// <summary>Talking to the Hub's barman over the installed game's data. Skipped without the game.</summary>
[Slow]
public class DialogueInstallTests
{
    [Fact]
    public void The_hub_barman_offers_his_goods()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var install = InstallData.Install!;
        var db = InstallData.BaseGame!;
        var levels = WorldLevelData.Load(install, includeInteriors: true);
        using var map = TerrainHeightmap.Open(install);
        var buildings = TownBuildings.Build(db, levels, new BuildingLayouts(db, levels.Interiors), (x, z) => map.HeightAt((float)x, (float)z));
        var data = PopulationData.Create(db, levels.Towns(), buildings: buildings);
        var hub = data.Sites.Single(s => s.Town.Name == "The Hub");
        var dialogue = new DialogueSystem(data);
        var walk = new OpenGroundWalkability((_, _) => 300);
        using var world = new SimWorld(new WorldSettings { Seed = 1, PublishSnapshots = false }, walk,
            [new PopulationSystem(data, new PopulationSettings { CheckEveryTicks = 1 }), new MovementSystem(new PathService(walk, true)), dialogue]);
        world.Player.Faction = data.PlayerFaction;
        world.Commands.Enqueue(new FocusCommand(hub.Position) { Tick = 0 });
        world.RunTicks(5);

        var table = world.Characters;
        int barman = Enumerable.Range(0, table.HighWater).First(i => table.Next[i].Alive && table.Cold(i) is { Name: "Barman" });
        var at = table.Next[barman].Position;
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "p", Faction = data.PlayerFaction };
        world.Squads.Add(squad);
        var cold = new Meitou.Tests.Combat.Combatant { Name = "hero" }.MakeCold();
        cold.Faction = data.PlayerFaction;
        cold.IsPlayer = true;
        cold.Inventory = new Inventory();
        cold.SquadId = squad.Id;
        var hero = table.Spawn(new CharacterHot { Position = at + new Vector3(20, 0, 0), Health = 100, MaxSpeed = 60, WalkSpeed = 15 }, cold, 0);
        squad.Members.Add(hero);
        squad.Leader = hero;

        Assert.True(world.CanTalk(table.IdOf(barman)));
        world.Commands.Enqueue(new TalkOrder(hero, table.IdOf(barman)) { Tick = world.Tick });
        world.RunTicks(2);
        var talk = dialogue.PublishConversation();
        Assert.NotNull(talk);
        Assert.Equal("Barman", talk.SpeakerName);
        Assert.Contains("Show me your goods.", talk.Choices);
    }
}
