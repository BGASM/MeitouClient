using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>Crimes, witnesses and bounties (docs/simulation.md "Crimes and bounties", docs/game/economy.md section 11).</summary>
public class CrimeTests
{
    // Factions: 0 the city (offers bounties; its police book crimes), 1 bandits (lawless), 2 the player (Nameless), 3 farmers (allied with the city both ways, book nothing),
    // 4 wildlife (not real), 5 rivals (hostile to the city both ways).
    const int City = 0, Bandits = 1, Player = 2, Farmers = 3, Wildlife = 4, Rivals = 5;

    static FactionRelations Relations()
    {
        FactionData F(string id, int def = 0, (string, int)[]? rel = null, bool offers = false, FundamentalType type = FundamentalType.Civilian, bool notReal = false) => new()
        {
            Id = id, Name = id, DefaultRelation = def, OffersBounties = offers, FundamentalType = type, NotReal = notReal,
            Relations = (rel ?? []).ToDictionary(r => r.Item1, r => r.Item2),
        };
        var table = FactionRelations.Build([
            F("city", 0, [("farmers", 60), ("rivals", -80)], offers: true),
            F("bandits", 0, type: FundamentalType.Bandit),
            F("Nameless", 10),
            F("farmers", 0, [("city", 60)]),
            F("wildlife", -100, notReal: true),
            F("rivals", 0, [("city", -80)]),
        ]);
        table.PlayerFaction = Player;
        return table;
    }

    // ------------------------------------------------------------------ the tables

    [Theory]
    [InlineData(CrimeCode.Enslaving, 100)]
    [InlineData(CrimeCode.Trespassing, 100)]
    [InlineData(CrimeCode.Burglary, 1000)]
    [InlineData(CrimeCode.Fencing, 1000)]
    [InlineData(CrimeCode.Theft, 2500)]
    [InlineData(CrimeCode.EscapingPrison, 2500)]
    [InlineData(CrimeCode.UniformTheft, 2500)]
    [InlineData(CrimeCode.Murder, 5000)]
    [InlineData(CrimeCode.Terrorism, 5000)]
    [InlineData(CrimeCode.Smuggling, 5000)]
    [InlineData(CrimeCode.Terrorism2, 5000)]
    [InlineData(CrimeCode.Kidnapping, 5000)]
    [InlineData(CrimeCode.Treason, 50000)]
    [InlineData(CrimeCode.Assault, 500)]
    [InlineData(CrimeCode.Looting, 500)]
    [InlineData(CrimeCode.CropTheft, 500)]
    [InlineData((CrimeCode)17, 500)]
    public void The_crime_table_is_the_games(CrimeCode code, int amount) => Assert.Equal(amount, CrimeTable.Amount(code));

    [Fact]
    public void A_bounty_lasts_four_hundredths_of_an_hour_per_cat_and_never_from_ten_thousand()
    {
        Assert.Equal(20, CrimeTable.LifetimeHours(500), 6);
        Assert.Equal(200, CrimeTable.LifetimeHours(5000), 6);
        Assert.Equal(399.96, CrimeTable.LifetimeHours(9999), 6);
        Assert.True(double.IsPositiveInfinity(CrimeTable.LifetimeHours(10000)));
        Assert.Equal("Murder, Assault", CrimeTable.Names((1 << 4) | (1 << 5)));
    }

    [Fact]
    public void Factions_book_bounties_by_type_or_flag_and_bandits_and_unreal_ones_are_lawless()
    {
        var f = new CrimeFactions(Relations());
        Assert.True(f.Books(City));
        Assert.False(f.Books(Farmers));
        Assert.True(new FactionData { Id = "p", Name = "p", FundamentalType = FundamentalType.LawEnforcement }.BooksBounties);
        Assert.True(f.Lawless(Bandits));
        Assert.True(f.Lawless(Wildlife));
        Assert.False(f.Lawless(City));
    }

    // ------------------------------------------------------------------ worlds

    sealed class Town : IDisposable
    {
        public SimWorld World { get; }
        public CombatSystem Combat { get; }
        public AggroSystem Aggro { get; }
        public CarrySystem Carry { get; }
        public CrimeSystem Crime { get; }
        public FactionRelations Relations { get; }

        public Town(ulong seed = 7, int threads = 1, int minPartition = 1)
        {
            Relations = CrimeTests.Relations();
            Combat = new CombatSystem(KatanaTechniques(), Constants, BaseGame, BodyOptions.Default, DuelFixture.Lengths, new CombatOptions { SelfApproach = false, TickMedical = false, RecordLog = true })
            {
                Relations = Relations,
            };
            Aggro = new AggroSystem(Relations, new AggroSettings { RecordLog = true });
            Carry = new CarrySystem(BaseGame, BodyOptions.Default, 1) { Relations = Relations };
            Crime = new CrimeSystem(Relations, new CrimeSettings { RecordLog = true });
            Combat.Crimes = Crime;
            Carry.Crimes = Crime;
            Aggro.Crimes = Crime;
            var walk = new OpenGroundWalkability((_, _) => 150);
            World = new SimWorld(new WorldSettings { Seed = seed, Threads = threads, PublishSnapshots = false, MinPartitionSize = minPartition }, walk,
                [new PlayerSystem(), new PursuitSystem(Combat), new MovementSystem(new PathService(walk, true)), new BodySystem(BaseGame), Carry, Combat,
                 new RetaliationSystem(Combat), Aggro, Crime]);
        }

        public void Dispose() => World.Dispose();

        /// <summary>An idle NPC alone in its squad, facing <paramref name="face"/> (null: along +Z).</summary>
        public CharacterId Npc(int faction, Vector2 at, Vector2? face = null, FundamentalType type = FundamentalType.Civilian, string name = "npc")
        {
            var cold = new Combatant { Name = name }.MakeCold();
            cold.Faction = faction;
            cold.NpcClass = type;
            cold.Inventory = new Inventory();
            var squad = new Squad { Id = World.Squads.NextId(), TemplateId = "t", Faction = faction, HomeCentre = at, HomeRadius = 20 };
            World.Squads.Add(squad);
            cold.SquadId = squad.Id;
            float yaw = face is { } f ? MathF.Atan2(f.X - at.X, f.Y - at.Y) : 0;
            var id = World.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 150, at.Y), Health = 100, MaxSpeed = 60, WalkSpeed = 15, Yaw = yaw, Task = (byte)CharacterTask.Idle }, cold, 0);
            squad.Members.Add(id);
            squad.Leader = id;
            return id;
        }

        public CharacterId PlayerCharacter(Vector2 at, string name = "hero")
        {
            var cold = new Combatant { Name = name }.MakeCold();
            cold.Faction = Player;
            cold.IsPlayer = true;
            cold.NpcClass = FundamentalType.Adventurer;
            cold.Inventory = new Inventory();
            var squad = World.Squads.Find(1000) ?? AddSquad();
            cold.SquadId = squad.Id;
            var id = World.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 150, at.Y), Health = 100, MaxSpeed = 60, WalkSpeed = 15 }, cold, 0);
            squad.Members.Add(id);
            if (squad.Leader.IsNone) squad.Leader = id;
            return id;

            Squad AddSquad()
            {
                var s = new Squad { Id = 1000, TemplateId = "p", Faction = Player };
                World.Squads.Add(s);
                return s;
            }
        }

        public CharacterCold Cold(CharacterId id) => World.Characters.Cold(id.Slot)!;
        public int Bounty(CharacterId id, int faction) => Cold(id).Bounties?.AmountFor(faction) ?? 0;
        public void Say(SimCommand c) => World.Commands.Enqueue(c);
        public bool Fighting(CharacterId id) => Cold(id).InCombat;
        public IEnumerable<CrimeEvent> Events(CrimeEventKind kind) => Crime.Log.Where(e => e.Kind == kind);
    }

    static readonly Vector2 O = new(2000, 2000);

    /// <summary>A Greenlander that can be carried (the fixture's is not marked <c>carriable</c>).</summary>
    static readonly Meitou.Data.Gameplay.Bodies.RaceData Person = new() { StringId = "17-c", Name = "Greenlander", StatMultipliers = Human.StatMultipliers, Anatomy = Human.Anatomy, Carriable = true };

    // ------------------------------------------------------------------ witnesses

    [Fact]
    public void An_assault_seen_by_the_police_of_a_faction_that_offers_bounties_is_booked_with_it()
    {
        using var t = new Town();
        var me = t.PlayerCharacter(O);
        var victim = t.Npc(City, O + new Vector2(0, 20), name: "victim");
        t.Npc(City, O + new Vector2(0, 250), face: O, type: FundamentalType.LawEnforcement, name: "officer");
        t.Say(new AttackOrder([me], victim) { Tick = t.World.Tick });
        t.World.RunTicks(40);
        Assert.Equal(500, t.Bounty(me, City));
        var entry = t.Cold(me).Bounties!.Of(City)!;
        Assert.Equal(1 << (int)CrimeCode.Assault, entry.Crimes);
        Assert.Equal("city", entry.FactionName);
        Assert.Contains(t.World.TakeMessages(), m => m.Contains("wanted by city for Assault: bounty c.500"));
        // Booked once: more sweeps while the same crime is pending add nothing.
        t.World.RunTicks(60);
        Assert.Single(t.Events(CrimeEventKind.Booked));
    }

    [Fact]
    public void An_ordinary_witness_only_sees_and_nobody_books_an_unseen_crime()
    {
        // A civilian of the city sees the assault: it is witnessed (the timer goes up), but only police book bounties.
        using (var t = new Town())
        {
            var me = t.PlayerCharacter(O);
            var victim = t.Npc(City, O + new Vector2(0, 20));
            var civilian = t.Npc(City, O + new Vector2(0, 250), face: O);
            t.Say(new AttackOrder([me], victim) { Tick = t.World.Tick });
            t.World.RunTicks(40);
            Assert.Equal(0, t.Bounty(me, City));
            Assert.Contains(t.Events(CrimeEventKind.Witnessed), e => e.Witness == civilian || e.Witness == victim);
            Assert.Contains(t.World.TakeMessages(), m => m.Contains("has been caught committing Assault"));
        }
        // Police looking the other way, out of sight: a loot of a knocked-out townsman is pending and then forgotten, with no bounty.
        using (var t = new Town())
        {
            var me = t.PlayerCharacter(O);
            var down = t.Npc(City, O + new Vector2(0, 20));
            t.Cold(down).Inventory!.Add(new ItemInstance { Record = "bread", Name = "bread" });
            t.Cold(down).Medical!.KnockOut(100000);
            t.Npc(City, O + new Vector2(0, 250), face: O + new Vector2(0, 600), type: FundamentalType.LawEnforcement);
            t.World.RunTicks(2);
            t.Say(new LootOrder([me], down) { Tick = t.World.Tick });
            t.World.RunTicks(30);
            Assert.Equal(CrimeCode.Looting, t.Cold(me).Pending?.Code);
            t.World.RunTicks(150);
            Assert.Null(t.Cold(me).Pending);
            Assert.Null(t.Cold(me).Bounties);
            Assert.Empty(t.Events(CrimeEventKind.Witnessed));
        }
    }

    [Fact]
    public void Looting_a_bandit_or_a_wanted_man_is_no_crime_and_looting_a_townsman_before_the_police_is()
    {
        using var t = new Town();
        var me = t.PlayerCharacter(O);
        var bandit = t.Npc(City, O + new Vector2(0, 20), type: FundamentalType.Bandit);   // a bandit by class (the permission test)
        var outlaw = t.Npc(Bandits, O + new Vector2(10, 20));                              // of a lawless faction (the setter refuses)
        var townsman = t.Npc(City, O + new Vector2(-10, 20));
        foreach (var v in new[] { bandit, outlaw, townsman })
        {
            t.Cold(v).Inventory!.Add(new ItemInstance { Record = "bread", Name = "bread" });
            t.Cold(v).Medical!.KnockOut(100000);
        }
        t.Npc(City, O + new Vector2(0, 250), face: O, type: FundamentalType.LawEnforcement);
        t.World.RunTicks(2);
        t.Say(new LootOrder([me], bandit) { Tick = t.World.Tick });
        t.World.RunTicks(60);
        t.Say(new LootOrder([me], outlaw) { Tick = t.World.Tick });
        t.World.RunTicks(60);
        Assert.Null(t.Cold(me).Pending);
        Assert.Equal(0, t.Bounty(me, City));
        t.Say(new LootOrder([me], townsman) { Tick = t.World.Tick });
        t.World.RunTicks(60);
        Assert.Equal(500, t.Bounty(me, City));
        Assert.Equal(1 << (int)CrimeCode.Looting, t.Cold(me).Bounties!.Of(City)!.Crimes);
    }

    [Fact]
    public void Hitting_a_policeman_books_an_assault_at_once_even_unseen()
    {
        using var t = new Town();
        var me = t.PlayerCharacter(O);
        var officer = t.Npc(City, O + new Vector2(0, 20), face: O + new Vector2(0, 600), type: FundamentalType.LawEnforcement);
        t.Say(new AttackOrder([me], officer) { Tick = t.World.Tick });
        int ticks = 0;
        while (ticks++ < 900 && !t.Combat.Log.Any(e => e.Attacker == me.Slot && e.Outcome == BlowOutcome.Hit)) t.World.RunTick();
        Assert.True(t.Combat.Log.Any(e => e.Attacker == me.Slot && e.Outcome == BlowOutcome.Hit), "a blow landed");
        Assert.Equal(500, t.Bounty(me, City));
        Assert.Contains(t.Events(CrimeEventKind.Booked), e => e.Witness == officer);
    }

    [Fact]
    public void Carrying_a_townsman_past_people_who_care_is_a_kidnapping()
    {
        using var t = new Town();
        var me = t.PlayerCharacter(O);
        var body = t.Npc(Farmers, O + new Vector2(0, 15));
        t.Cold(body).Race = Person;
        t.Cold(body).Medical!.KnockOut(100000);
        t.World.RunTicks(2);
        t.Say(new PickUpOrder([me], body) { Tick = t.World.Tick });
        int ticks = 0;
        while (ticks++ < 300 && t.Cold(me).Carrying.IsNone) t.World.RunTick();
        Assert.Equal(body, t.Cold(me).Carrying);
        t.World.RunTicks(30);
        Assert.Null(t.Cold(me).Pending);   // nobody about
        // A city policeman (the city is allied with the farmers, so it cares) comes into view: a kidnapping, booked at the full 5000.
        t.Npc(City, O + new Vector2(0, 250), face: O, type: FundamentalType.LawEnforcement);
        t.World.RunTicks(31);
        Assert.Equal(5000, t.Bounty(me, City));
        Assert.Equal(1 << (int)CrimeCode.Kidnapping, t.Cold(me).Bounties!.Of(City)!.Crimes);
    }

    // ------------------------------------------------------------------ consequences

    [Fact]
    public void The_police_of_the_faction_that_wants_a_character_attack_it_and_nobody_else_does()
    {
        CharacterId Run(int policeFaction, FundamentalType type, bool wanted, out Town town)
        {
            var t = new Town();
            var me = t.PlayerCharacter(O);
            if (wanted)
            {
                var e = (t.Cold(me).Bounties = new BountyLedger()).Get(City, "city");
                e.Amount = 2500;
                e.Crimes = 1 << (int)CrimeCode.Theft;
            }
            var looker = t.Npc(policeFaction, O + new Vector2(0, 150), face: O, type: type);
            t.World.RunTicks(60);
            town = t;
            return looker;
        }
        var officer = Run(City, FundamentalType.LawEnforcement, true, out var a);
        using (a)
        {
            Assert.Contains(a.Aggro.Log, e => e.Attacker == officer && a.World.Characters.Cold(e.Target.Slot)!.IsPlayer);
            Assert.True(a.Fighting(officer));
        }
        // A city civilian, the farmers' police and an officer with nothing on the character leave it alone.
        foreach (var (faction, type, wanted) in new[] { (City, FundamentalType.Civilian, true), (Farmers, FundamentalType.LawEnforcement, true), (City, FundamentalType.LawEnforcement, false) })
        {
            var looker = Run(faction, type, wanted, out var b);
            using (b)
            {
                Assert.Empty(b.Aggro.Log);
                Assert.False(b.Fighting(looker));
            }
        }
    }

    [Fact]
    public void Military_count_as_police_only_in_their_own_towns_resident_squads()
    {
        using var t = new Town();
        var soldier = t.Npc(City, O, type: FundamentalType.Military);
        Assert.False(t.Crime.IsPoliceLike(t.World, t.Cold(soldier)));
        var resident = new Squad { Id = t.World.Squads.NextId(), TemplateId = "g", Faction = City, Town = 3 };
        t.World.Squads.Add(resident);
        t.Cold(soldier).SquadId = resident.Id;
        Assert.True(t.Crime.IsPoliceLike(t.World, t.Cold(soldier)));
        var me = t.PlayerCharacter(O + new Vector2(50, 0));
        t.Cold(me).NpcClass = FundamentalType.LawEnforcement;
        Assert.False(t.Crime.IsPoliceLike(t.World, t.Cold(me)));   // the player's characters never are
    }

    [Fact]
    public void A_bounty_expires_by_its_amount_and_a_notorious_one_never()
    {
        using var t = new Town();
        var me = t.PlayerCharacter(O);
        double hoursPerTick = t.World.TickSeconds * 11.0 / 1200.0;
        var ledger = t.Cold(me).Bounties = new BountyLedger();
        var small = ledger.Get(City, "city");
        small.Amount = 500;                                                // 20 hours
        small.RaisedTick = -(long)(19.9 / hoursPerTick);
        var big = ledger.Get(Farmers, "farmers");
        big.Amount = 10000;                                                // notorious
        big.RaisedTick = -(long)(10000 / hoursPerTick);
        t.World.RunTicks(16);
        Assert.Equal(500, t.Bounty(me, City));
        t.World.RunTicks((int)(0.2 / hoursPerTick));
        Assert.Equal(0, t.Bounty(me, City));
        Assert.Equal(10000, t.Bounty(me, Farmers));
        Assert.Contains(t.World.TakeMessages(), m => m.Contains("no longer wanted by city"));
        Assert.Single(t.Events(CrimeEventKind.Expired));
    }

    // ------------------------------------------------------------------ saves

    [Fact]
    public void A_ledger_goes_into_the_save_keys_and_back()
    {
        var relations = Relations();
        var ledger = new BountyLedger();
        var e = ledger.Get(City, "city");
        e.Amount = 5500;
        e.Crimes = (1 << 4) | (1 << 5);
        e.RaisedTick = 3000;
        var f = ledger.Get(Farmers, "farmers");
        f.Amount = 100;
        f.Claimed = true;
        f.RaisedTick = 0;
        var state = new FcsRecord();
        var clock = new SaveClock(5, 9, 0);   // game hour 129
        float dt = 1f / 30;
        BountySave.Write(state, ledger, relations, clock, 9000, dt);
        Assert.Equal("city", state.Strings["bountyfac0"]);
        Assert.Equal(5500, state.Ints["amount0"]);
        Assert.Equal(e.Crimes, state.Ints["crimes0"]);
        Assert.Equal(0, state.Ints["claim0"]);
        Assert.Equal(1, state.Ints["claim1"]);
        // 6000 ticks are 6000 x 11 / 36000 = 1.83 hours: raised at hour 127.17, written truncated.
        Assert.Equal(127, state.Ints["bountyexp0"]);
        var back = BountySave.Read(state, relations, clock, 0, dt)!;
        Assert.Equal([City, Farmers], back.Entries.Select(x => x.Faction));
        Assert.Equal(5500, back.Of(City)!.Amount);
        Assert.True(back.Of(Farmers)!.Claimed);
        // The hour is truncated in the file: the raise comes back within an hour of where it was (in ticks before the load).
        Assert.InRange(back.Of(City)!.RaisedTick, -6000 - 3273, -6000);
        // Writing again replaces the entries; an empty ledger removes them.
        BountySave.Write(state, null, relations, clock, 0, dt);
        Assert.False(state.Strings.ContainsKey("bountyfac0"));
        Assert.False(state.Ints.ContainsKey("amount1"));
    }

    // ------------------------------------------------------------------ determinism

    /// <summary>
    /// A market: city police and townsfolk, farmers, a rival squad, and two player characters. At tick 30 the first assaults a townsman in view of the police (booked), the police go after
    /// it; at tick 300 the second loots a knocked-out farmer next to a policeman; hashes at five checkpoints.
    /// </summary>
    internal static List<ulong> Scripted(int threads, out (int Booked, int Witnessed, int Fights) seen, int minPartition = 1)
    {
        using var t = new Town(13, threads, minPartition);
        var me = t.PlayerCharacter(O);
        var other = t.PlayerCharacter(O + new Vector2(2500, 0), "second");
        var townsman = t.Npc(City, O + new Vector2(0, 25));
        t.Npc(City, O + new Vector2(30, 200), face: O, type: FundamentalType.LawEnforcement);
        t.Npc(City, O + new Vector2(-30, 220), face: O, type: FundamentalType.LawEnforcement);
        t.Npc(City, O + new Vector2(-200, 0), face: O);
        var farmer = t.Npc(Farmers, O + new Vector2(2500, 20));
        t.Cold(farmer).Inventory!.Add(new ItemInstance { Record = "bread", Name = "bread", Quantity = 2 });
        t.Cold(farmer).Medical!.KnockOut(1000000);
        t.Npc(City, O + new Vector2(2520, 220), face: O + new Vector2(2500, 0), type: FundamentalType.LawEnforcement);
        t.Npc(Rivals, O + new Vector2(-1200, 1200), face: O + new Vector2(-1200, 0));
        var hashes = new List<ulong>();
        foreach (int target in new[] { 1, 30, 120, 300, 400, 900 })
        {
            t.World.RunTicks(target - (int)t.World.Tick);
            if (target == 30) t.Say(new AttackOrder([me], townsman) { Tick = t.World.Tick });
            if (target == 300) t.Say(new LootOrder([other], farmer) { Tick = t.World.Tick });
            hashes.Add(t.World.StateHash());
        }
        seen = (t.Events(CrimeEventKind.Booked).Count(), t.Events(CrimeEventKind.Witnessed).Count(), t.Aggro.Log.Count);
        return hashes;
    }

    internal static List<ulong> Scripted(int threads) => Scripted(threads, out _);

    [Fact]
    public void The_crime_scenario_hashes_the_same_at_1_4_and_16_threads()
    {
        var one = Scripted(1, out var seen);
        // It covers what it is for: crimes booked (the assault and the loot), witnessed, and the police starting fights over a bounty.
        Assert.True(seen.Booked >= 2 && seen.Witnessed >= 2 && seen.Fights > 0, $"{seen}");
        Assert.Equal(one, Scripted(4));
        Assert.Equal(one, Scripted(16));
        Assert.Equal(one, Scripted(4, out _, minPartition: 16));
        Assert.NotEqual(one[^1], one[^2]);
    }
}

/// <summary>The crime rules over the installed game's data: who books bounties, who is police in the towns, and the police going after a wanted player character. Skipped without the game.</summary>
[Slow]
public class CrimeInstallTests(ITestOutputHelper output)
{
    [Fact]
    public void Town_police_of_the_factions_that_book_bounties_go_after_a_wanted_character()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var data = PopulationData.Create(db, InstallData.Levels!.Towns(), new GeneratedAppearances(db, InstallData.Install!.Root));
        // The factions that book (the loader's +0x40: law enforcement, or offers bounties).
        var books = data.Factions.Where(f => f.BooksBounties).Select(f => f.Name).OrderBy(n => n).ToList();
        output.WriteLine("book bounties: " + string.Join(", ", books));
        Assert.Equal(["Mercenary Police", "Police", "Republic of Tertius", "Shek Kingdom", "The Holy Nation", "United Cities"], books);

        var start = NewGameStart.Find(NewGameStart.LoadAll(data.Db), NewGameStart.DefaultName)!;
        string record = "";
        foreach (var link in start.Squad)
        {
            if (data.Db.Find(link.Id) is not { } r) continue;
            if (!SquadFactory.IsTemplate(r)) { record = link.Id; break; }
            if (SquadFactory.Plan(data.Db, SquadTemplate.From(r), 1, 0, 0, IWorldStates.None).Members.FirstOrDefault(m => !m.IsAnimal) is { } member) { record = member.RecordId; break; }
        }
        int found = 0;
        foreach (string town in new[] { "Admag", "Stack", "Heng", "Blister Hill", "Bad Teeth" })
        {
            if (data.Sites.FirstOrDefault(s => s.Town.Name == town) is not { } site) continue;
            var walk = new OpenGroundWalkability((_, _) => 300);
            var systems = StandardSystems.Build(data, walk, new StandardSystemOptions
            {
                Parts = StandardParts.Population | StandardParts.Player | StandardParts.Movement | StandardParts.Pursuit | StandardParts.Body | StandardParts.Combat
                    | StandardParts.Retaliation | StandardParts.Aggro | StandardParts.Crime,
                SynchronousPaths = true,
                Population = new PopulationSettings { CheckEveryTicks = 1 },
                Combat = new CombatOptions { SelfApproach = false, TickMedical = false, RecordLog = true },
                Aggro = new AggroSettings { RecordLog = true },
            });
            using var world = new SimWorld(new WorldSettings { Seed = 1, PublishSnapshots = false }, walk, systems.Systems);
            world.Commands.Enqueue(new FocusCommand(site.Position) { Tick = 0 });
            world.RunTicks(60);
            var table = world.Characters;
            var police = Enumerable.Range(0, table.HighWater).Where(s => table.Previous[s].Alive && systems.Crime!.IsPoliceLike(world, table.Cold(s)!)).ToList();
            var wantedBy = police.Select(s => table.Cold(s)!.Faction).Where(f => data.Factions[f].BooksBounties).Distinct().ToList();
            output.WriteLine($"{town}: {police.Count} police-like ({string.Join(", ", police.Select(s => $"{table.Cold(s)!.Name} {table.Cold(s)!.NpcClass}").Distinct().Take(6))})");
            if (wantedBy.Count == 0) continue;
            found++;
            // A wanted player character right beside the first such policeman, in front of it.
            int officer = police.First(s => table.Cold(s)!.Faction == wantedBy[0]);
            var at = table.Previous[officer].Position;
            float yaw = table.Previous[officer].Yaw;
            var spot = new Vector2(at.X + MathF.Sin(yaw) * 60, at.Z + MathF.Cos(yaw) * 60);
            var me = systems.Population!.SpawnSandbox(world, record, spot, player: true);
            var entry = (table.Cold(me.Slot)!.Bounties = new BountyLedger()).Get(wantedBy[0], data.Factions[wantedBy[0]].Name);
            entry.Amount = 5000;
            entry.RaisedTick = world.Tick;
            world.RunTicks(60);
            var attackers = systems.Aggro!.Log.Where(e => e.Target == me).ToList();
            output.WriteLine($"  wanted by {data.Factions[wantedBy[0]].Name}: attacked by {attackers.Count} ({string.Join(", ", attackers.Select(e => table.Cold(e.Attacker.Slot)?.Name))})");
            Assert.NotEmpty(attackers);
            Assert.Contains(attackers, e => e.AttackerFaction == wantedBy[0]);
        }
        Assert.True(found > 0, "some town has police of a faction that books bounties");
    }
}
