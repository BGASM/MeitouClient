using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.FirstAid;
using Meitou.Simulation.Items;
using SimWorld = Meitou.Simulation.World;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Simulation;

/// <summary>First aid and resting in play: the formulas against the doc, a medic walking up and treating, resting, the medic job, XP on the move and determinism.</summary>
public class FirstAidTests
{
    // ------------------------------------------------------------------ the formulas (docs/game/character-stats.md "First aid, medkits and robot repair")

    [Fact]
    public void The_treatment_rate_and_the_kit_drain_follow_the_documented_formulas()
    {
        var c = BaseGame;   // medic speed mult 3, robot medic speed mult 0.33, medkit drain 1 / 99 = 2 / 0.1 after the loader's scaling
        // lerp(1, 15, min(skill, quality) x 0.01) x medic speed mult: skill 50 with a quality 40 kit is limited by the kit (t = 0.4).
        Assert.Equal((1 + 14 * 0.4f) * 3, MedicalState.TreatmentRate(c, 50, 40, false), 4);
        Assert.Equal((1 + 14 * 0.4f) * 3, MedicalState.TreatmentRate(c, 90, 40, false), 4);     // a better medic gains nothing from a poor kit
        Assert.Equal((1 + 14 * 0.4f) * 3, MedicalState.TreatmentRate(c, 40, 100, false), 4);    // nor a better kit from a poor medic
        Assert.Equal(3f, MedicalState.TreatmentRate(c, 0, 100, false), 4);                      // t = 0: 1 x 3
        Assert.Equal(45f, MedicalState.TreatmentRate(c, 100, 100, false), 4);                   // t = 1: 15 x 3
        Assert.Equal(45f * 0.33f, MedicalState.TreatmentRate(c, 100, 100, true), 4);            // a robot kit is slower
        // lerp(medkit drain 99, medkit drain 1, (100 - skill) / 100) x dt: an expert uses a kit 20 times slower than a beginner.
        Assert.Equal(1.05f * 2, MedicalState.KitDrain(c, 50, 2, false), 4);
        Assert.Equal(2f, MedicalState.KitDrain(c, 0, 1, false), 4);
        Assert.Equal(0.1f, MedicalState.KitDrain(c, 100, 1, false), 4);
        Assert.Equal(20f, MedicalState.KitDrain(c, 0, 1, false) / MedicalState.KitDrain(c, 100, 1, false), 3);
        Assert.Equal(2 * 0.33f, MedicalState.KitDrain(c, 0, 1, true), 4);
    }

    [Fact]
    public void Bandaging_stops_at_the_part_maximum_and_each_kit_type_treats_its_own_parts()
    {
        var m = Healthy();
        m.Parts[RArm].Flesh = 20;                       // 80 to bandage
        m.Parts[HeadIx].Flesh = 95;                     // 5
        var ctx = Context();
        Assert.Equal(85, m.BandageRoom(Human, false), 3);
        Assert.True(m.NeedsAid(Human, false));
        Assert.False(m.NeedsAid(Human, true), "an ordinary body has no robotic parts");
        Assert.Equal(0, m.Treat(1, 100, 100, true, ctx));    // a robot kit does nothing to flesh
        float bandaged = m.Treat(1, 100, 100, false, ctx);   // 45 per part for one unit of dt
        Assert.Equal(45 + 5, bandaged, 3);
        Assert.Equal(45, m.Parts[RArm].Bandage, 3);
        Assert.Equal(5, m.Parts[HeadIx].Bandage, 3);         // capped: h + b = M
        m.Treat(1, 100, 100, false, ctx);
        Assert.Equal(80, m.Parts[RArm].Bandage, 3);
        Assert.False(m.NeedsAid(Human, false), "nothing is left to bandage");
        // A robot is treated by robot kits only.
        var robot = new RaceData { StringId = "robot-t", Name = "Robot", IsRobot = true, StatMultipliers = Human.StatMultipliers, Anatomy = Human.Anatomy };
        var machine = MedicalState.Create(robot, 50);
        machine.Parts[RArm].Flesh = 20;
        Assert.False(machine.NeedsAid(robot, false));
        Assert.True(machine.NeedsAid(robot, true));
        Assert.True(machine.Treat(1, 100, 100, true, Context(robot)) > 0);
    }

    [Fact]
    public void Resting_doubles_the_blood_recovery_and_slows_hunger_to_the_bed_rate()
    {
        float Blood(bool resting)
        {
            var m = Healthy();
            m.Blood = 50;
            var ctx = new MedicalContext(BaseGame, BodyOptions.Default, Human) { Toughness = 50, Strength = 50, Seed = 1, CharacterKey = 1, Resting = resting };
            m.Tick(1, ctx);
            return m.Blood - 50;
        }
        Assert.Equal(BaseGame.RestingHealRateMult, Blood(true) / Blood(false), 3);
        float Hunger(bool resting)
        {
            var m = Healthy();
            var ctx = new MedicalContext(BaseGame, BodyOptions.Default, Human) { Toughness = 50, Strength = 50, Seed = 1, CharacterKey = 1, Resting = resting };
            m.Tick(1, ctx);
            return MedicalState.MaxHunger - m.Hunger;
        }
        Assert.Equal(BaseGame.BedHungerRate, Hunger(true) / Hunger(false), 3);   // unencumbered: the active factor is 1
    }

    // ------------------------------------------------------------------ in the world

    const float KitQuality = 40, KitCharges = 400;

    static GameDatabase KitDatabase()
    {
        FcsRecord Kit(string id, string name, int function, float quality, float charges)
        {
            var r = new FcsRecord { StringId = id, Flags = 0x10, Name = name, RecordType = FcsRecordType.ITEM };
            r.Ints["item function"] = function;
            r.Floats["quality"] = quality;
            r.Floats["charges"] = charges;
            r.Floats["weight kg"] = 2;
            return r;
        }
        var file = new FcsFile();
        file.Records.AddRange([Kit("72-t", "Test kit", 1, KitQuality, KitCharges), Kit("73-t", "Test robot kit", 12, 50, 500)]);
        var db = new GameDatabase();
        db.Apply(file, "test.mod");
        return db;
    }

    /// <summary>A small world of hand-made characters on open ground, with the movement, first aid and body systems (and combat when asked).</summary>
    sealed class Rig : IDisposable
    {
        public SimWorld World { get; }
        public ItemFactory Items { get; }
        readonly OpenGroundWalkability walk = new(SyntheticTown.Ground);
        public List<CharacterId> Ids { get; } = [];

        public Rig(int threads = 1, int minPartition = 16, FirstAidSettings? settings = null, bool fighters = false, ulong seed = 5)
        {
            Items = new ItemFactory(KitDatabase(), BaseGame);
            var movement = new MovementSystem(new PathService(walk, true));
            var systems = new List<ITickSystem> { new PlayerSystem(), movement, new FirstAidSystem(BaseGame, BodyOptions.Default, Items, settings), new BodySystem(BaseGame, BodyOptions.Default) };
            World = new SimWorld(new WorldSettings { Seed = seed, Threads = threads, PublishSnapshots = false, MinPartitionSize = minPartition }, walk, systems);
            this.fighters = fighters;
        }

        readonly bool fighters;

        /// <summary>Adds a character at a spot; <paramref name="kits"/> are record ids put in its inventory. All join squad <paramref name="squad"/> (created when new).</summary>
        public CharacterId Add(float x, float z, bool player = true, int squad = 0, float medic = 50, params string[] kits)
        {
            var inventory = new Inventory();
            foreach (var k in kits) inventory.Add(Items.Create(k)!);
            var stats = new CharacterStats();
            stats[StatsEnumerated.Medic] = medic;
            stats[StatsEnumerated.Strength] = 50;
            var cold = new CharacterCold
            {
                Name = "C" + Ids.Count, Race = Human, Stats = stats, Medical = MedicalState.Create(Human, 50), Inventory = inventory, IsPlayer = player,
                SquadId = squad, Fighter = fighters ? new Fighter(null) : null,
            };
            var hot = new CharacterHot { Position = new Vector3(x, 300, z), MaxSpeed = 40, WalkSpeed = 15, Task = (byte)CharacterTask.Idle };
            var id = World.Characters.Spawn(hot, cold, 0);
            Ids.Add(id);
            var sq = World.Squads.Find(squad);
            if (sq is null) World.Squads.Add(sq = new Squad { Id = squad, TemplateId = "t" });
            sq.Members.Add(id);
            if (player) World.Player.Selection.Add(id);
            return id;
        }

        public CharacterCold Cold(CharacterId id) => World.Characters.Cold(id.Slot)!;
        public MedicalState Body(CharacterId id) => Cold(id).Medical!;
        public CharacterTask Task(CharacterId id) => (CharacterTask)World.Characters.Previous[id.Slot].Task;
        public Vector3 Where(CharacterId id) => World.Characters.Previous[id.Slot].Position;
        public void Say(SimCommand c) => World.Commands.Enqueue(c);
        public ItemInstance Kit(CharacterId id) => Cold(id).Inventory!.All().First(i => i.ItemFunction is 1 or 12);

        /// <summary>Wounds a part so first aid has something to do: its flesh is set to <paramref name="fraction"/> of the maximum.</summary>
        public void Hurt(CharacterId id, int part, float fraction) => Body(id).Parts[part].Flesh = Body(id).Parts[part].MaxHp * fraction;

        public void Dispose() => World.Dispose();
    }

    static float Bandage(MedicalState m) => m.Parts.Sum(p => p.Bandage);

    [Fact]
    public void A_medic_walks_to_the_patient_and_bandages_it_with_the_kit_at_the_documented_rate()
    {
        using var rig = new Rig(settings: new FirstAidSettings { AutoNpcs = false });
        var medic = rig.Add(1000, 2000, medic: 50, kits: "72-t");
        var patient = rig.Add(1130, 2000, squad: 1);
        rig.Hurt(patient, RLeg, 0.3f);                 // 70 to bandage
        rig.Hurt(patient, LArm, 0.6f);                 // 40
        rig.World.RunTicks(2);
        float startX = rig.Where(medic).X;
        rig.Say(new TreatOrder([medic], patient) { Tick = rig.World.Tick });
        // The medic walks (the task is Treat), the patient is untouched until it arrives.
        rig.World.RunTicks(10);
        Assert.Equal(CharacterTask.Treat, rig.Task(medic));
        Assert.Equal(0, Bandage(rig.Body(patient)));
        long arrived = -1;
        float before = 0;
        for (int t = 0; t < 1500 && arrived < 0; t++)
        {
            before = Bandage(rig.Body(patient));
            rig.World.RunTick();
            if (Bandage(rig.Body(patient)) > 0) arrived = rig.World.Tick;
        }
        Assert.True(arrived > 0, "the medic reached the patient");
        Assert.True(rig.Where(medic).X > startX + 50);
        // One tick of treatment: skill 50 (injury multiplier 1), kit quality 40: lerp(1, 15, 0.4) x 3 per second, a tick is 1/30 s.
        float expected = MedicalState.TreatmentRate(BaseGame, 50, KitQuality, false) / 30;
        Assert.Equal(2 * expected, Bandage(rig.Body(patient)) - before, 1e-3);   // both wounded parts take the rate
        var kit = rig.Kit(medic);
        float charges = kit.Charges;
        float medicBefore = rig.Cold(medic).Stats![StatsEnumerated.Medic];
        // Runs on until nothing is left to bandage.
        for (int t = 0; t < 3000 && rig.Task(medic) == CharacterTask.Treat; t++) rig.World.RunTick();
        Assert.Equal(CharacterTask.Idle, rig.Task(medic));
        Assert.False(rig.Body(patient).NeedsAid(Human, false));
        foreach (var p in rig.Body(patient).Parts) Assert.True(p.Flesh + p.Bandage >= p.MaxHp - 0.01f, $"{p.Template.Name} is bandaged up to its maximum");
        Assert.True(kit.Charges < charges && kit.Charges > 0, "the kit was used, not used up");
        Assert.True(rig.Cold(medic).Stats![StatsEnumerated.Medic] > medicBefore, "treating trains the medic skill");
    }

    [Fact]
    public void The_kit_drains_by_the_documented_amount_and_is_gone_when_used_up()
    {
        using var rig = new Rig(settings: new FirstAidSettings { AutoNpcs = false });
        var medic = rig.Add(1000, 2000, medic: 50, kits: "72-t");
        var patient = rig.Add(1005, 2000, squad: 1);
        rig.Hurt(patient, RLeg, 0.5f);
        rig.World.RunTicks(2);
        var kit = rig.Kit(medic);
        rig.Say(new TreatOrder([medic], patient) { Tick = rig.World.Tick });
        rig.World.RunTicks(1);    // the order is applied, the effect runs in the same tick
        float drained = KitCharges - kit.Charges;
        Assert.Equal(MedicalState.KitDrain(BaseGame, 50, 1f / 30, false), drained, 4);
        // A nearly empty kit runs out mid-treatment: it is removed and the treatment ends.
        kit.Charges = 0.01f;
        rig.World.RunTicks(5);
        Assert.DoesNotContain(rig.Cold(medic).Inventory!.All(), i => i.ItemFunction == 1);
        Assert.Equal(CharacterTask.Idle, rig.Task(medic));
        Assert.True(rig.Body(patient).NeedsAid(Human, false), "the patient still needs a kit");
    }

    [Fact]
    public void A_medic_without_a_kit_uses_a_squad_mates_and_a_character_can_treat_itself()
    {
        using var rig = new Rig(settings: new FirstAidSettings { AutoNpcs = false });
        var medic = rig.Add(1000, 2000, medic: 50);
        var mate = rig.Add(1010, 2000, kits: "72-t");
        rig.Hurt(medic, RArm, 0.2f);
        rig.World.RunTicks(2);
        rig.Say(new TreatOrder([medic], medic) { Tick = rig.World.Tick });   // itself, with the kit a mate carries
        rig.World.RunTicks(3);
        for (int t = 0; t < 3000 && rig.Task(medic) == CharacterTask.Treat; t++) rig.World.RunTick();
        Assert.False(rig.Body(medic).NeedsAid(Human, false));
        Assert.True(rig.Kit(mate).Charges < KitCharges, "the mate's kit paid for it");
        // Out of reach of every kit, nothing happens.
        var alone = rig.Add(5000, 5000, squad: 2, medic: 50);
        rig.Hurt(alone, RArm, 0.2f);
        rig.Say(new TreatOrder([alone], alone) { Tick = rig.World.Tick });
        rig.World.RunTicks(30);
        Assert.Equal(CharacterTask.Idle, rig.Task(alone));
        Assert.True(rig.Body(alone).NeedsAid(Human, false));
    }

    [Fact]
    public void A_rest_order_sits_the_character_down_until_a_move_order_replaces_it()
    {
        using var rig = new Rig();
        var resting = rig.Add(1000, 2000);
        var twin = rig.Add(1050, 2000);
        foreach (var id in new[] { resting, twin }) rig.Body(id).Blood = 5;
        rig.World.RunTicks(2);
        rig.Say(new RestOrder([resting]) { Tick = rig.World.Tick });
        rig.World.RunTicks(3000);
        Assert.Equal(CharacterTask.Rest, rig.Task(resting));
        Assert.Equal(CharacterTask.Idle, rig.Task(twin));
        float gainResting = rig.Body(resting).Blood - 5, gainTwin = rig.Body(twin).Blood - 5;
        Assert.True(gainTwin > 0);
        Assert.InRange(gainResting / gainTwin, BaseGame.RestingHealRateMult * 0.98f, BaseGame.RestingHealRateMult * 1.02f);   // resting doubles the blood recovery (float noise at small steps)
        Assert.True(rig.Body(resting).Hunger > rig.Body(twin).Hunger, "resting uses the bed hunger rate");
        // Any move order ends it.
        rig.Say(new MoveOrder([resting], new Vector3(1200, 0, 2000)) { Tick = rig.World.Tick });
        rig.World.RunTicks(10);
        Assert.Equal(CharacterTask.GoTo, rig.Task(resting));
        // So does stop.
        rig.Say(new RestOrder([resting]) { Tick = rig.World.Tick });
        rig.World.RunTicks(2);
        Assert.Equal(CharacterTask.Rest, rig.Task(resting));
        rig.Say(new StopCommand([resting]) { Tick = rig.World.Tick });
        rig.World.RunTicks(2);
        Assert.Equal(CharacterTask.Idle, rig.Task(resting));
    }

    [Fact]
    public void Npcs_with_a_kit_treat_a_downed_squad_mate_and_the_players_only_with_the_medic_job()
    {
        foreach (bool player in new[] { false, true })
        {
            using var rig = new Rig();
            var medic = rig.Add(1000, 2000, player: player, medic: 50, kits: "72-t");
            var patient = rig.Add(1100, 2000, player: player);
            rig.Hurt(patient, RLeg, 0.2f);
            rig.World.RunTicks(2);
            rig.World.RunTicks(60);
            if (player)
            {
                Assert.Equal(CharacterTask.Idle, rig.Task(medic));   // the original's auto first aid is the medic job; off by default
                Assert.Equal(0, Bandage(rig.Body(patient)));
                rig.Say(new MedicJobCommand([medic]) { Tick = rig.World.Tick });
            }
            for (int t = 0; t < 3000 && (t < 20 || rig.Task(medic) != CharacterTask.Idle); t++) rig.World.RunTick();
            Assert.False(rig.Body(patient).NeedsAid(Human, false), player ? "the medic job treats" : "an NPC treats");
            Assert.Equal(CharacterTask.Idle, rig.Task(medic));
        }
    }

    [Fact]
    public void A_fight_in_the_squad_keeps_the_npc_medics_from_treating()
    {
        using var rig = new Rig(fighters: true);
        var medic = rig.Add(1000, 2000, player: false, kits: "72-t");
        var fighter = rig.Add(1030, 2000, player: false);
        var patient = rig.Add(1060, 2000, player: false);
        rig.Hurt(patient, RLeg, 0.2f);
        rig.Cold(fighter).InCombat = true;
        rig.World.RunTicks(120);
        Assert.Equal(0, Bandage(rig.Body(patient)));
        rig.Cold(fighter).InCombat = false;
        rig.World.RunTicks(400);
        Assert.True(Bandage(rig.Body(patient)) > 0);
    }

    // ------------------------------------------------------------------ XP and encumbrance on the move

    [Fact]
    public void Running_trains_athletics_and_a_heavy_load_trains_strength_and_reaches_the_fighter()
    {
        using var rig = new Rig(fighters: true);
        var runner = rig.Add(1000, 2000);
        var loaded = rig.Add(1000, 2200, squad: 2);
        rig.Cold(loaded).Inventory!.Items.Add(new ItemInstance { Record = "x", UnitWeight = 400, Section = "main" });
        rig.World.RunTicks(2);
        float athletics = rig.Cold(runner).Stats!.Athletics, strengthLoaded = rig.Cold(loaded).Stats![StatsEnumerated.Strength], strengthRunner = rig.Cold(runner).Stats![StatsEnumerated.Strength];
        rig.Say(new MoveOrder([runner], new Vector3(1600, 0, 2000)) { Tick = rig.World.Tick });
        rig.Say(new MoveOrder([loaded], new Vector3(1600, 0, 2200)) { Tick = rig.World.Tick });
        rig.World.RunTicks(300);
        Assert.True(rig.Cold(runner).Stats!.Athletics > athletics, "running trains athletics");
        Assert.Equal(strengthRunner, rig.Cold(runner).Stats![StatsEnumerated.Strength]);   // an unburdened runner gains no strength
        Assert.True(rig.Cold(loaded).Stats![StatsEnumerated.Strength] > strengthLoaded, "carrying a heavy load trains strength");
        Assert.True(rig.Cold(loaded).Fighter!.EncumbranceFactor < 1, "the fighter sees the encumbrance");
        Assert.Equal(1, rig.Cold(runner).Fighter!.EncumbranceFactor);
    }

    // ------------------------------------------------------------------ determinism

    internal static List<ulong> Scripted(int threads) => Scripted(threads, out _);

    /// <summary>The scripted scenario; <paramref name="bandaged"/> is the bandage the players and the NPCs had put on at the end (to show the script does something).</summary>
    internal static List<ulong> Scripted(int threads, out (float Players, float Npcs) bandaged)
    {
        using var rig = new Rig(threads, minPartition: 1, seed: 12);
        var a = rig.Add(1000, 2000, player: true, medic: 60, kits: "72-t");
        var b = rig.Add(1060, 2000, player: true);
        var c = rig.Add(1100, 2060, player: true);
        var d = rig.Add(1500, 2000, player: false, squad: 1, medic: 30, kits: "72-t");
        var e = rig.Add(1530, 2000, player: false, squad: 1);
        var f = rig.Add(1560, 2030, player: false, squad: 1);
        rig.Hurt(b, RLeg, 0.3f);
        rig.Hurt(c, LArm, 0.5f);
        rig.Hurt(e, RArm, 0.1f);
        rig.Hurt(f, RLeg, 0.6f);
        void Say(SimCommand cmd) => rig.Say(cmd);
        Say(new TreatOrder([a], b) { Tick = 5 });
        Say(new RestOrder([c]) { Tick = 8 });
        Say(new MedicJobCommand([a]) { Tick = 200 });
        Say(new MoveOrder([c], new Vector3(1300, 0, 2200)) { Tick = 260 });
        Say(new TreatOrder([a], a) { Tick = 400 });
        var hashes = new List<ulong>();
        int ran = 0;
        foreach (int target in new[] { 4, 60, 150, 400, 900, 1500 })
        {
            rig.World.RunTicks(target - ran);
            ran = target;
            hashes.Add(rig.World.StateHash());
        }
        bandaged = (Bandage(rig.Body(b)) + Bandage(rig.Body(c)) + Bandage(rig.Body(a)), Bandage(rig.Body(e)) + Bandage(rig.Body(f)));
        return hashes;
    }

    [Fact]
    public void A_scripted_game_of_first_aid_and_rest_hashes_the_same_at_1_4_and_16_threads()
    {
        var one = Scripted(1, out var done);
        Assert.True(done.Players > 100 && done.Npcs > 100, $"the script treats: {done}");
        Assert.Equal(one, Scripted(4));
        Assert.Equal(one, Scripted(16));
        Assert.Equal(one, Scripted(1));
    }
}

/// <summary>The kits of the installed game: the three first aid kits and the robot repair kit read as the doc says, and a CHARACTER that lists one is given it by the usual loadout rules.</summary>
[Slow]
public class FirstAidInstallTests
{
    [Fact]
    public void The_base_game_kits_have_the_documented_quality_charges_and_function()
    {
        Assert.SkipWhen(InstallData.BaseGame is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var items = new ItemFactory(db, GameConstants.FromDatabase(db));
        foreach (var (id, function, quality, charges) in new[] { ("209-gamedata.base", 1, 5f, 150f), ("515-gamedata.base", 1, 40f, 400f), ("1359-gamedata.base", 1, 100f, 1000f), ("18020-gamedata.base", 12, 50f, 500f) })
        {
            var info = items.Info(id)!;
            Assert.Equal(function, info.Function);
            Assert.Equal(quality, info.Quality);
            var kit = items.Create(id)!;
            Assert.Equal(charges, kit.Charges);
            Assert.Equal(function, kit.ItemFunction);
        }
    }

    [Fact]
    public void A_character_that_lists_a_kit_carries_it()
    {
        Assert.SkipWhen(InstallData.BaseGame is null, "Kenshi install not found");
        var db = InstallData.BaseGame!;
        var items = new ItemFactory(db, GameConstants.FromDatabase(db));
        var citizen = db.OfType(FcsRecordType.CHARACTER).First(r => r.Name == "Citizen" && r.GetReferences("inventory").Any(i => i.TargetStringId == "515-gamedata.base"));
        var inventory = items.Build(citizen, null);
        Assert.Contains(inventory.All(), i => i.Record == "515-gamedata.base" && i.ItemFunction == 1);
    }
}
