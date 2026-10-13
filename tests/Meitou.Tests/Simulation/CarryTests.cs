using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using SimWorld = Meitou.Simulation.World;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Simulation;

/// <summary>Carrying downed characters, rescue and looting (docs/game/carry-loot.md, docs/simulation.md "Carrying, rescue and looting").</summary>
public class CarryTests
{
    /// <summary>The fixture's Greenlander, carriable as every base race but the Leviathan is.</summary>
    static readonly RaceData Person = new() { StringId = "17-c", Name = "Greenlander", StatMultipliers = Human.StatMultipliers, Anatomy = Human.Anatomy, Carriable = true };
    static readonly RaceData Leviathan = new() { StringId = "lev-c", Name = "Leviathan", StatMultipliers = Human.StatMultipliers, Anatomy = Human.Anatomy, Carriable = false };

    /// <summary>Hand-made characters on open ground with the game's movement, body and carry systems (in the game's order).</summary>
    sealed class Rig : IDisposable
    {
        public SimWorld World { get; }
        public CarrySystem Carry { get; }
        readonly OpenGroundWalkability walk = new(SyntheticTown.Ground);

        public Rig(int threads = 1, int minPartition = 16, FactionRelations? relations = null, ulong seed = 5, CarrySettings? settings = null)
        {
            Carry = new CarrySystem(BaseGame, BodyOptions.Default, 1, settings) { Relations = relations };
            var systems = new List<ITickSystem> { new PlayerSystem(), new MovementSystem(new PathService(walk, true)), new BodySystem(BaseGame, BodyOptions.Default), Carry };
            World = new SimWorld(new WorldSettings { Seed = seed, Threads = threads, PublishSnapshots = false, MinPartitionSize = minPartition }, walk, systems);
        }

        public CharacterId Add(float x, float z, bool player = true, int squad = 0, int faction = 0, float strength = 50, RaceData? race = null, CharacterTask task = CharacterTask.Idle, float yaw = 0, bool leader = false)
        {
            var stats = new CharacterStats();
            stats[StatsEnumerated.Strength] = strength;
            var cold = new CharacterCold
            {
                Name = "C" + World.Characters.HighWater, Race = race ?? Person, Stats = stats, Medical = MedicalState.Create(race ?? Person, strength), Inventory = new Inventory(),
                IsPlayer = player, SquadId = squad, Faction = faction,
            };
            var hot = new CharacterHot { Position = new Vector3(x, 300, z), MaxSpeed = 40, WalkSpeed = 15, Task = (byte)task, Yaw = yaw };
            var id = World.Characters.Spawn(hot, cold, 0);
            var sq = World.Squads.Find(squad);
            if (sq is null) World.Squads.Add(sq = new Squad { Id = squad, TemplateId = "t", Faction = faction, HomeCentre = new Vector2(x, z), HomeRadius = 100 });
            sq.Members.Add(id);
            if (leader || sq.Leader.IsNone) sq.Leader = id;
            return id;
        }

        public CharacterCold Cold(CharacterId id) => World.Characters.Cold(id.Slot)!;
        public MedicalState Body(CharacterId id) => Cold(id).Medical!;
        public ref readonly CharacterHot Hot(CharacterId id) => ref World.Characters.Previous[id.Slot];
        public CharacterTask Task(CharacterId id) => (CharacterTask)Hot(id).Task;
        public void Say(SimCommand c) => World.Commands.Enqueue(c);

        /// <summary>Orders a pick-up and runs until the lift is over; returns the ticks it took.</summary>
        public int PickUp(CharacterId carrier, CharacterId body, int max = 1500)
        {
            Say(new PickUpOrder([carrier], body) { Tick = World.Tick });
            for (int t = 1; t <= max; t++)
            {
                World.RunTick();
                if (Cold(carrier).Carrying == body && Task(carrier) != CharacterTask.PickUp) return t;
            }
            throw new Xunit.Sdk.XunitException("the pick-up did not finish");
        }

        public void Dispose() => World.Dispose();
    }

    static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    // ------------------------------------------------------------------ carrying

    [Fact]
    public void A_pick_up_walks_lifts_at_the_documented_clip_point_and_adds_the_carry_person_weight()
    {
        using var rig = new Rig();
        var carrier = rig.Add(1000, 2000, strength: 5);
        var body = rig.Add(1100, 2000, player: false, squad: 1, faction: 1);
        rig.Body(body).KnockOut(10000);
        rig.World.RunTicks(2);
        float unladen = rig.Hot(carrier).MaxSpeed;
        rig.Say(new PickUpOrder([carrier], body) { Tick = rig.World.Tick });
        long liftStart = -1, attached = -1;
        for (int t = 0; t < 1500 && rig.Task(carrier) == CharacterTask.PickUp || t == 0; t++)
        {
            rig.World.RunTick();
            if (liftStart < 0 && rig.Cold(carrier).LiftTime >= 0) liftStart = rig.World.Tick;
            if (attached < 0 && rig.Cold(carrier).Carrying == body) attached = rig.World.Tick;
        }
        Assert.True(liftStart > 0 && attached > 0, "it walked up and lifted");
        Assert.True(Flat(rig.Hot(carrier).Position, new Vector3(1000, 0, 2000)) > 70, "it walked to the body");
        // The clip is 1 s here: the body is attached once more than 0.4 of it has played (12 or 13 ticks of 1/30 s, by float rounding), the lift ends at 0.95.
        Assert.InRange(attached - liftStart, 11, 13);
        Assert.Equal(CharacterTask.Idle, rig.Task(carrier));
        Assert.Equal(carrier, rig.Cold(body).CarriedBy);
        rig.World.RunTicks(2);
        Assert.Equal(rig.Hot(carrier).Position, rig.Hot(body).Position);
        // The load is carry person weight (30 in the base game) on top of an empty inventory: strength 5 carries 1.2 x 5 + 15 = 21 freely, so the factor drops.
        var c = rig.Cold(carrier);
        float factor = Encumbrance.Factor(BaseGame, 0, true, 5, c.Medical!.StatMultiplier(StatsEnumerated.Strength));
        Assert.Equal((21 - 0.05f * 30) / (0.95f * 30), Encumbrance.Factor(BaseGame, 0, true, 5), 4);
        Assert.True(factor < 1);
        Assert.Equal(Speed.Run(Person, c.Stats!, c.Medical!, factor), rig.Hot(carrier).MaxSpeed, 3);
        Assert.True(rig.Hot(carrier).MaxSpeed < unladen);
    }

    [Fact]
    public void Only_the_down_of_a_carriable_race_are_lifted_and_a_carrier_needs_its_left_arm()
    {
        using var rig = new Rig();
        var carrier = rig.Add(1000, 2000);
        var awake = rig.Add(1010, 2000, player: false, squad: 1, faction: 1);
        var leviathan = rig.Add(1000, 2010, player: false, squad: 2, faction: 1, race: Leviathan);
        rig.Body(leviathan).KnockOut(10000);
        var body = rig.Add(990, 2000, player: false, squad: 3, faction: 1);
        rig.Body(body).KnockOut(10000);
        rig.World.RunTicks(2);
        rig.Say(new PickUpOrder([carrier], awake) { Tick = rig.World.Tick });
        rig.Say(new PickUpOrder([carrier], leviathan) { Tick = rig.World.Tick + 1 });
        rig.World.RunTicks(3);
        Assert.Equal(CharacterTask.Idle, rig.Task(carrier));
        // A limp left arm (the right does not matter): no lift.
        rig.Body(carrier).Parts[LArm].Flesh = -20;
        Assert.False(CarrySystem.LeftArmUsable(rig.Body(carrier)));
        rig.Body(carrier).Parts[RArm].Flesh = -20;
        rig.Body(carrier).Parts[LArm].Flesh = 100;
        Assert.True(CarrySystem.LeftArmUsable(rig.Body(carrier)));
        rig.Body(carrier).Parts[LArm].Limb = LimbState.Stump;
        Assert.False(CarrySystem.LeftArmUsable(rig.Body(carrier)));
        rig.Say(new PickUpOrder([carrier], body) { Tick = rig.World.Tick });
        rig.World.RunTicks(3);
        Assert.Equal(CharacterTask.Idle, rig.Task(carrier));
        Assert.True(rig.Cold(carrier).Carrying.IsNone);
    }

    [Fact]
    public void The_body_is_dropped_when_the_carrier_is_knocked_out_or_loses_its_left_arm()
    {
        using var rig = new Rig();
        var a = rig.Add(1000, 2000);
        var b = rig.Add(1400, 2000, squad: 2);
        var c = rig.Add(1800, 2000, squad: 3);
        var bodies = new[] { rig.Add(1010, 2000, false, 4, 1), rig.Add(1410, 2000, false, 5, 1), rig.Add(1810, 2000, false, 6, 1) };
        foreach (var body in bodies) rig.Body(body).KnockOut(10000);
        rig.World.RunTick();
        rig.PickUp(a, bodies[0]);
        rig.PickUp(b, bodies[1]);
        rig.PickUp(c, bodies[2]);
        rig.Body(a).KnockOut(100);                       // knocked out
        rig.Body(b).Parts[LArm].Limb = LimbState.Stump;   // the left arm cut off
        rig.Body(c).Parts[LArm].Flesh = -30;              // the left arm limp
        rig.World.RunTick();
        foreach (var (carrier, body) in new[] { (a, bodies[0]), (b, bodies[1]), (c, bodies[2]) })
        {
            Assert.True(rig.Cold(carrier).Carrying.IsNone);
            Assert.True(rig.Cold(body).CarriedBy.IsNone);
            Assert.True(Flat(rig.Hot(carrier).Position, rig.Hot(body).Position) < 0.01f, "dropped where the carrier stands");
        }
    }

    [Fact]
    public void Putting_down_lets_go_late_in_the_reversed_clip_and_knocks_out_a_non_friendly_body_again()
    {
        using var rig = new Rig();
        var carrier = rig.Add(1000, 2000);
        var enemy = rig.Add(1010, 2000, player: false, squad: 1, faction: 1);
        var friend = rig.Add(1400, 2000);
        var friendCarrier = rig.Add(1390, 2000);
        rig.Body(enemy).KnockOut(5);
        rig.Body(friend).KnockOut(5);
        rig.World.RunTick();
        rig.PickUp(carrier, enemy);
        rig.PickUp(friendCarrier, friend);
        rig.Say(new PutDownOrder([carrier, friendCarrier]) { Tick = rig.World.Tick });
        int ticks = 0;
        while (!rig.Cold(carrier).Carrying.IsNone && ticks < 100) { rig.World.RunTick(); ticks++; }
        // Released below 0.2 of the 1 s clip played backwards: after about 0.8 s.
        Assert.InRange(ticks, 24, 26);
        Assert.Equal(CharacterTask.Idle, rig.Task(carrier));
        Assert.Equal(CharacterTask.Idle, rig.Task(friendCarrier));
        Assert.True(rig.Cold(friendCarrier).Carrying.IsNone);
        float beside = Flat(rig.Hot(carrier).Position, rig.Hot(enemy).Position);
        Assert.InRange(beside, 2.9f, 3.1f);
        // Low assassination: a factor of about 0.1, so the KO is max(3, (2 x 20 + terms) x f x 4), well above what was left of the 5 minutes.
        var cold = rig.Cold(enemy);
        var ctx = new MedicalContext(BaseGame, BodyOptions.Default, Person) { Toughness = 0, Strength = 50 };
        Assert.Equal(0.1f, MedicalState.PutDownKoFactor(0), 5);
        Assert.Equal(0.4f, MedicalState.PutDownKoFactor(100), 5);
        float factor = MedicalState.PutDownKoFactor(rig.Cold(carrier).Stats![StatsEnumerated.Assassination]);   // a fresh character has assassination 1: 0.103
        float expected = cold.Medical!.PutDownKoMinutes(ctx, factor);
        Assert.True(expected >= 16);
        Assert.Equal(expected, cold.Medical.KoTimer, 0.05f);
        Assert.True(rig.Body(friend).KoTimer < 5, "a friend is not knocked out again");
    }

    [Fact]
    public void A_friend_who_comes_round_is_set_down_and_a_captive_is_knocked_out_again_on_the_shoulder()
    {
        using var rig = new Rig();
        var carrier = rig.Add(1000, 2000);
        var friend = rig.Add(1010, 2000);
        var captor = rig.Add(1400, 2000);
        var captive = rig.Add(1410, 2000, player: false, squad: 1, faction: 1);
        rig.Body(friend).KnockOut(2);
        rig.Body(captive).KnockOut(2);
        rig.World.RunTick();
        rig.PickUp(carrier, friend);
        rig.PickUp(captor, captive);
        for (int t = 0; t < 400 && !rig.Cold(carrier).Carrying.IsNone; t++) rig.World.RunTick();
        Assert.True(rig.Cold(carrier).Carrying.IsNone, "the friend was set down when it came round");
        Assert.False(rig.Body(friend).Unconscious);
        Assert.Equal(captive, rig.Cold(captor).Carrying);
        Assert.True(rig.Body(captive).Unconscious);
    }

    [Fact]
    public void A_carrier_in_walk_mode_runs_at_the_run_cap_instead()
    {
        // An NPC squad whose leader is out: a wanderer of the squad picks it up and goes back to wandering, in run mode while it carries.
        using var rig = new Rig();
        var leader = rig.Add(1000, 2000, player: false, squad: 1, faction: 1, leader: true);
        var mate = rig.Add(1100, 2000, player: false, squad: 1, faction: 1, task: CharacterTask.Wander);
        var walker = rig.Add(3000, 2000, player: false, squad: 2, faction: 1, task: CharacterTask.Wander);
        rig.Body(leader).KnockOut(10000);
        float carrying = 0, walking = 0;
        for (int t = 0; t < 3000; t++)
        {
            rig.World.RunTick();
            if (rig.Cold(mate).Carrying == leader && rig.Task(mate) == CharacterTask.Wander) carrying = MathF.Max(carrying, rig.Hot(mate).Velocity.Length());
            walking = MathF.Max(walking, rig.Hot(walker).Velocity.Length());
        }
        Assert.Equal(leader, rig.Cold(mate).Carrying);
        Assert.True(walking > 0 && walking <= rig.Hot(walker).WalkSpeed + 0.01f, $"a wanderer walks ({walking})");
        Assert.True(carrying > rig.Hot(mate).WalkSpeed + 1, $"a carrying wanderer runs ({carrying})");
        Assert.True(carrying <= SpeedMode.RunCap + 0.01f);
    }

    // ------------------------------------------------------------------ rescue

    [Fact]
    public void The_rescue_job_picks_the_nearest_knocked_out_member_of_its_faction_that_no_one_else_lifts()
    {
        using var rig = new Rig();
        var rescuer = rig.Add(1000, 2000);
        var second = rig.Add(1000, 2040);
        var stranger = rig.Add(1030, 2000, player: false, squad: 1, faction: 1);   // nearest, but another faction
        var dead = rig.Add(1040, 2000, squad: 2);                                 // a corpse is not rescued
        var near = rig.Add(1100, 2000, squad: 3);
        var far = rig.Add(1300, 2000, squad: 4);
        var awake = rig.Add(1060, 2000, squad: 5);
        foreach (var down in new[] { stranger, near, far }) rig.Body(down).KnockOut(10000);
        rig.Body(dead).Parts[HeadIx].Flesh = -1000;
        rig.World.RunTick();
        Assert.True(rig.Body(dead).Dead);
        rig.Say(new RescueJobCommand([rescuer, second], true) { Tick = rig.World.Tick });
        for (int t = 0; t < 2000 && (rig.Cold(rescuer).Carrying.IsNone || rig.Cold(second).Carrying.IsNone); t++) rig.World.RunTick();
        Assert.Equal(near, rig.Cold(rescuer).Carrying);
        Assert.Equal(far, rig.Cold(second).Carrying);
        Assert.True(rig.Cold(stranger).CarriedBy.IsNone && rig.Cold(dead).CarriedBy.IsNone && rig.Cold(awake).CarriedBy.IsNone);
        Assert.True(rig.Cold(rescuer).RescueJob);
    }

    [Fact]
    public void An_npc_squad_picks_up_its_knocked_out_leader_and_the_squad_follows_the_carrier()
    {
        using var rig = new Rig();
        var leader = rig.Add(1000, 2000, player: false, squad: 1, faction: 1, leader: true);
        var near = rig.Add(1050, 2000, player: false, squad: 1, faction: 1, task: CharacterTask.Follow);
        var far = rig.Add(1200, 2000, player: false, squad: 1, faction: 1, task: CharacterTask.Follow);
        rig.Body(leader).KnockOut(10000);
        for (int t = 0; t < 600 && rig.Cold(leader).CarriedBy.IsNone; t++) rig.World.RunTick();
        Assert.Equal(near, rig.Cold(leader).CarriedBy);
        rig.World.RunTicks(60);
        Assert.Equal(CharacterTask.Follow, rig.Task(near));
        Assert.True(rig.Hot(near).Velocity.Length() < 1, "the carrier does not follow the leader it carries");
        Assert.True(rig.Cold(far).Carrying.IsNone);
    }

    // ------------------------------------------------------------------ looting

    static ItemInstance Item(string record, int w, int h, int quantity = 1, bool stack = false) =>
        new() { Record = record, Name = record, Width = w, Height = h, Quantity = quantity, StackLimit = stack ? 99 : 1, UnitWeight = 1 };

    [Fact]
    public void Take_all_moves_what_fits_and_leaves_the_rest_and_the_worn_gear()
    {
        var looter = new Inventory();
        looter.Add(Item("bolts", 1, 1, 5, stack: true));        // at (0, 0)
        looter.Add(Item("filler", 8, 3));                       // rows 1 to 3 of the 8 x 6 main grid: row 0 from x 1 and rows 4 and 5 are left
        var victim = new Inventory();
        var pack = new ItemInstance { Record = "pack", Section = "backpack_attach", Contents = new Inventory(4, 4), Width = 2, Height = 2 };
        victim.Items.Add(pack);
        victim.Items.Add(new ItemInstance { Record = "katana", Section = "hip", Width = 1, Height = 4 });
        victim.Add(Item("crate", 4, 2));                        // fits
        victim.Add(Item("bolts", 1, 1, 7, stack: true));        // joins the looter's stack
        victim.Add(Item("crate", 4, 2));                        // fits
        victim.Items.Add(Item("long", 1, 5));                   // too tall for what is left (its cell in the victim does not matter here)
        pack.Contents!.Add(Item("food", 1, 1));                 // in the backpack: taken too
        var (taken, left) = CarrySystem.TakeAll(looter, victim);
        Assert.Equal(4, taken);
        Assert.Equal(1, left);
        Assert.Equal(12, looter.Items.Single(i => i.Record == "bolts").Quantity);
        Assert.Equal(2, looter.Items.Count(i => i.Record == "crate"));
        Assert.Contains(looter.Items, i => i.Record == "food");
        Assert.Equal(["pack", "katana", "long"], victim.Items.Select(i => i.Record));
        Assert.Empty(pack.Contents.Items);
        // No two items of the looter overlap.
        var main = looter.Items.Where(i => i.Section == "main").ToList();
        for (int a = 0; a < main.Count; a++)
            for (int b = a + 1; b < main.Count; b++)
                Assert.False(main[a].X < main[b].X + main[b].Width && main[b].X < main[a].X + main[a].Width && main[a].Y < main[b].Y + main[b].Height && main[b].Y < main[a].Y + main[a].Height);
    }

    // Factions: 0 townsfolk (neutral), 1 the player (Nameless).
    static FactionRelations TwoFactions()
    {
        var table = FactionRelations.Build([new FactionData { Id = "townsfolk", Name = "townsfolk", DefaultRelation = 10 }, new FactionData { Id = "Nameless", Name = "Nameless", DefaultRelation = 10 }]);
        table.PlayerFaction = 1;
        return table;
    }

    /// <summary>A player looter, a knocked-out townsman with three items, and a townsman 300 units away facing the looter or away from it. Returns the relation after the loot.</summary>
    static (float Relation, LootEntry Loot) LootScene(bool facing)
    {
        var relations = TwoFactions();
        using var rig = new Rig(relations: relations);
        var looter = rig.Add(1000, 2000, faction: 1);
        var victim = rig.Add(1100, 2000, player: false, squad: 1, faction: 0);
        // The witness stands at x 1400 and looks along -X (yaw -90 degrees) at the scene, or along +X away from it.
        rig.Add(1400, 2000, player: false, squad: 2, faction: 0, yaw: facing ? -MathF.PI / 2 : MathF.PI / 2);
        foreach (var r in new[] { "a", "b", "c" }) rig.Cold(victim).Inventory!.Add(Item(r, 1, 1));
        rig.Body(victim).KnockOut(10000);
        rig.World.RunTick();
        float before = relations.Get(0, 1);
        rig.Say(new LootOrder([looter], victim) { Tick = rig.World.Tick });
        for (int t = 0; t < 1000 && rig.Carry.Loots.Count == 0; t++) rig.World.RunTick();
        Assert.Equal(10, before);
        Assert.Equal(3, rig.Cold(looter).Inventory!.Items.Count);
        Assert.Empty(rig.Cold(victim).Inventory!.Items);
        Assert.Equal(CharacterTask.Idle, rig.Task(looter));
        return (relations.Get(0, 1), rig.Carry.Loots.Single());
    }

    [Fact]
    public void Looting_seen_by_a_member_of_the_victims_faction_is_a_theft_and_unseen_it_is_not()
    {
        var (seen, loot) = LootScene(facing: true);
        Assert.True(loot.Witnessed);
        Assert.Equal(3, loot.Taken);
        Assert.Equal(10 - 10, seen);   // relation event Stealing: -10 x 1
        var (unseen, quiet) = LootScene(facing: false);
        Assert.False(quiet.Witnessed);
        Assert.Equal(10, unseen);
    }

    [Fact]
    public void The_lower_body_ignores_the_carrying_flags_and_the_upper_body_needs_them()
    {
        var lower = new AnimationDefinition { Id = "l", Name = "walk lower", Clip = "walk lower", Area = AnimationArea.Lower, MoveSpeed = 14, CombatMode = 2, StealthMode = 2, WeaponLeft = 2, WeaponRight = 2 };
        var upper = lower with { Name = "walk upper", Area = AnimationArea.Upper };
        var carryUpper = upper with { Name = "walk carrying left", CarryingLeft = true };
        var carrying = new AnimationStance { CarryingLeft = true };
        Assert.True(lower.FitsExceptLegs(carrying));
        Assert.False(upper.FitsExceptLegs(carrying));
        Assert.True(carryUpper.FitsExceptLegs(carrying));
        Assert.False(carryUpper.FitsExceptLegs(new AnimationStance()));
    }

    // ------------------------------------------------------------------ determinism

    /// <summary>
    /// A scripted game: player characters pick up, carry, put down and loot; one has the rescue job; an NPC squad rescues its leader; a carrier is knocked out mid-carry.
    /// Cut into one-slot partitions.
    /// </summary>
    internal static List<ulong> Scripted(int threads) => Scripted(threads, out _);

    /// <summary>The scripted scenario; <paramref name="happened"/> lists what the script did (to show it does something).</summary>
    internal static List<ulong> Scripted(int threads, out string happened)
    {
        var relations = TwoFactions();
        using var rig = new Rig(threads, minPartition: 1, relations: relations, seed: 21);
        var p1 = rig.Add(1000, 2000, faction: 1, strength: 20);
        var p2 = rig.Add(1050, 2000, faction: 1);
        var p3 = rig.Add(1000, 2100, faction: 1);
        var down1 = rig.Add(1200, 2050, player: false, squad: 1, faction: 0);
        var down2 = rig.Add(1150, 1900, squad: 2, faction: 1);
        var down3 = rig.Add(900, 2200, squad: 3, faction: 1);
        var leader = rig.Add(1600, 2000, player: false, squad: 4, faction: 0, leader: true);
        rig.Add(1650, 2000, player: false, squad: 4, faction: 0, task: CharacterTask.Wander);
        rig.Add(1700, 2050, player: false, squad: 4, faction: 0, task: CharacterTask.Follow, yaw: -MathF.PI / 2);
        foreach (var r in new[] { "a", "b", "c", "d" }) rig.Cold(down1).Inventory!.Add(Item(r, 2, 2));
        rig.Body(down1).KnockOut(10000);
        rig.Body(down2).KnockOut(3);
        rig.Body(down3).KnockOut(10000);
        rig.Body(leader).KnockOut(40);
        void Say(SimCommand c) => rig.Say(c);
        Say(new PickUpOrder([p1], down1) { Tick = 3 });
        Say(new RescueJobCommand([p3]) { Tick = 5 });
        Say(new LootOrder([p2], down1) { Tick = 10 });
        Say(new MoveOrder([p1], new Vector3(1300, 0, 2300)) { Tick = 200 });
        Say(new PutDownOrder([p1]) { Tick = 500 });
        Say(new LootOrder([p2], down1) { Tick = 600 });
        Say(new PickUpOrder([p2], down3) { Tick = 720 });   // the body p3 dropped when it went down
        var hashes = new List<ulong>();
        int ran = 0;
        var carried = new SortedSet<string>(StringComparer.Ordinal);
        foreach (int target in new[] { 4, 60, 150, 400, 700, 1000, 1500 })
        {
            for (; ran < target; ran++)
            {
                if (ran == 700) rig.Body(p3).KnockOut(5);   // a carrier goes down mid-carry
                rig.World.RunTick();
                foreach (var body in new[] { down1, down2, down3, leader })
                    if (!rig.Cold(body).CarriedBy.IsNone) carried.Add($"{body}<-{rig.Cold(body).CarriedBy}");
            }
            hashes.Add(rig.World.StateHash());
        }
        happened = $"carried {string.Join(", ", carried)}; loots {string.Join(", ", rig.Carry.Loots.Select(l => $"{l.Taken}/{l.Left}{(l.Witnessed ? " seen" : "")}"))}; relation {relations.Get(0, 1)}";
        return hashes;
    }

    [Fact]
    public void A_scripted_game_of_carrying_rescue_and_looting_hashes_the_same_at_1_4_and_16_threads()
    {
        var one = Scripted(1, out var happened);
        // The script lifts (p1 a townsman, p3 by its rescue job, p2 the body p3 dropped, an NPC its leader), loots in sight of the townsfolk (relation 10 to 0) and loots an emptied body.
        Assert.Equal("carried 3.0<-0.0, 5.0<-1.0, 5.0<-2.0, 6.0<-7.0; loots 4/0 seen, 0/0; relation 0", happened);
        Assert.Equal(one, Scripted(4));
        Assert.Equal(one, Scripted(16));
        Assert.Equal(one, Scripted(1));
    }
}
