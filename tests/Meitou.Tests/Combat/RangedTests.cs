using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using static Meitou.Tests.Combat.CombatFixtures;
using static Meitou.Tests.Combat.DuelFixture;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Combat;

/// <summary>Crossbows: the formulas of docs/game/combat.md "Ranged and turrets" and the ranged controller, bolts and switching in headless worlds.</summary>
public class RangedTests(ITestOutputHelper output)
{
    /// <summary>CROSSBOW "Ranger" (66169-Newwworld.mod) as dumped from the base game; its ammunition stands in as "bolts".</summary>
    internal static CrossbowData Ranger { get; } = new()
    {
        StringId = "66169-Newwworld.mod", Name = "Ranger",
        PierceDamageMin0 = 20, PierceDamageMin1 = 30, PierceDamageMax0 = 30, PierceDamageMax1 = 45, Range0 = 600, Range1 = 800,
        ReloadTimeMin0 = 3, ReloadTimeMin1 = 2, ReloadTimeMax0 = 4.5f, ReloadTimeMax1 = 4, AccuracyDeviation0 = 5, AccuracyDeviation1 = 3,
        ShotSpeed0 = 400, ShotSpeed1 = 500, BleedMult = 0.6f, NumShots = 1, AccuracyPerfectSkill = 60, AmmoId = "bolts",
    };

    /// <summary>CROSSBOW "Spring Bat": its slowest reload gets slower with quality (reload time max 6 / 8).</summary>
    static CrossbowData SpringBat { get; } = new()
    {
        StringId = "spring-bat", Name = "Spring Bat", PierceDamageMin0 = 30, PierceDamageMin1 = 50, PierceDamageMax0 = 40, PierceDamageMax1 = 80, Range0 = 400, Range1 = 550,
        ReloadTimeMin0 = 4.5f, ReloadTimeMin1 = 3.5f, ReloadTimeMax0 = 6, ReloadTimeMax1 = 8, AccuracyDeviation0 = 8, AccuracyDeviation1 = 4, ShotSpeed0 = 500, ShotSpeed1 = 600,
    };

    // ------------------------------------------------------------------ formulas

    [Fact]
    public void Crossbow_stats_lerp_by_quality_with_the_bow_damage_constants_before_the_lerp()
    {
        var q0 = Ranger.At(0, Constants);
        var q1 = Ranger.At(1, Constants);
        // pierceMin = trunc(lerp(q, 1.1 x min0, 1.3 x min1) x G).
        Assert.Equal(22, q0.PierceMin);   // 1.1 x 20
        Assert.Equal(33, q0.PierceMax);   // 1.1 x 30
        Assert.Equal(39, q1.PierceMin);   // 1.3 x 30
        Assert.Equal(58, q1.PierceMax);   // trunc(1.3 x 45 = 58.5)
        var half = Ranger.At(0.5f, Constants, 2);
        Assert.Equal((int)((22 + 39) / 2f * 2), half.PierceMin);
        Assert.Equal(700, half.Range);
        Assert.Equal(2.5f, half.ReloadMin);
        Assert.Equal(4.25f, half.ReloadMax);
        Assert.Equal(4, half.AccuracyDeviation);
        Assert.Equal(450, half.ShotSpeed);
        // The plain lerp without the constants is kept.
        Assert.Equal(20, Ranger.At(0).PierceMin);
        // A shot speed of 0 or less becomes 1.
        Assert.Equal(1, (Ranger with { ShotSpeed0 = 0, ShotSpeed1 = 0 }).At(0, Constants).ShotSpeed);
    }

    [Fact]
    public void Reload_time_runs_from_max_at_no_skill_to_min_at_full_skill()
    {
        var s = Ranger.At(0, Constants);
        // The doc's example: Ranger at quality 0, crossbows 10 and dexterity 10 (s = 0.10): 4.35 s; at s = 1: 3 s.
        float blend = RangedFormulas.SkillBlend(10, 10);
        Assert.Equal(0.1f, blend, 5);
        Assert.Equal(4.35f, RangedFormulas.ReloadTime(blend, s.ReloadMin, s.ReloadMax), 4);
        Assert.Equal(3f, RangedFormulas.ReloadTime(1, s.ReloadMin, s.ReloadMax), 4);
        Assert.Equal(4.5f, RangedFormulas.ReloadTime(0, s.ReloadMin, s.ReloadMax), 4);
        Assert.Equal(3f, RangedFormulas.ReloadTime(1.5f, s.ReloadMin, s.ReloadMax), 4);   // clamped
        // Spring Bat at full quality: min 3.5, max 8.
        var bat = SpringBat.At(1, Constants);
        Assert.Equal(8f, RangedFormulas.ReloadTime(0, bat.ReloadMin, bat.ReloadMax), 4);
        Assert.Equal(3.5f, RangedFormulas.ReloadTime(1, bat.ReloadMin, bat.ReloadMax), 4);
    }

    [Fact]
    public void Deviation_cone_narrows_with_skill_and_perception_to_nothing_at_200()
    {
        Assert.Equal(5f, RangedFormulas.Deviation(5, 0, 0), 5);
        Assert.Equal(2.5f, RangedFormulas.Deviation(5, 50, 50), 5);
        Assert.Equal(0f, RangedFormulas.Deviation(5, 100, 100), 5);
        Assert.Equal(0f, RangedFormulas.Deviation(5, 150, 120), 5);   // floored at 0
        // The tilt is the angle asked for, whatever the spin.
        var dir = Vector3.Normalize(new Vector3(1, 0.2f, -0.5f));
        foreach (float spin in new[] { 0f, 0.25f, 0.6f, 0.99f })
        {
            var d = RangedFormulas.Deviate(dir, 3, spin);
            Assert.Equal(1f, d.Length(), 4);
            Assert.Equal(3f, MathF.Acos(Math.Clamp(Vector3.Dot(d, dir), -1, 1)) * 180 / MathF.PI, 2);
        }
        Assert.Equal(dir, RangedFormulas.Deviate(dir, 0, 0.3f));
    }

    [Fact]
    public void Height_factor_and_bolt_numbers_follow_the_doc()
    {
        // Level (dy = 0): f = -20/280, so 1 - 0.4 f = 1.0286 (the quirk: not 1).
        Assert.Equal(1 + 0.4f * 20 / 280, RangedFormulas.HeightFactor(100, 100), 5);
        Assert.Equal(1 - 0.5f * 20 / 280 * 0 + 0.5f * (30 - 20) / 280f, RangedFormulas.HeightFactor(130, 100), 5);
        Assert.Equal(1.5f, RangedFormulas.HeightFactor(400, 100), 5);    // 30 m above: capped
        Assert.Equal(1.5f, RangedFormulas.HeightFactor(2000, 100), 5);
        Assert.Equal(0.6f, RangedFormulas.HeightFactor(100, 400), 5);    // 30 m below
        Assert.Equal(1f, RangedFormulas.HeightFactor(300, 0), 5);        // the guard for a target at height 0
        // In range: d^2 < h x range^2.
        Assert.True(RangedFormulas.InRange(599 * 599, 600, 1));
        Assert.False(RangedFormulas.InRange(601 * 601, 600, 1));
        Assert.True(RangedFormulas.InRange(650 * 650, 600, 1.2f));
        var stats = Ranger.At(0, Constants);
        var (damage, range, speed) = RangedFormulas.Bolt(stats, 1.5f, 0.5f);
        Assert.Equal((22 + 0.5f * 11) * 1.5f, damage, 4);
        Assert.Equal(900, range, 3);
        Assert.Equal(400 * (0.8f * 1.5f + 0.2f), speed, 3);
        Assert.Equal(0.75f, RangedFormulas.FriendlyAvoidance(50), 5);
        Assert.Equal(1f, RangedFormulas.FriendlyAvoidance(90), 5);
    }

    [Fact]
    public void A_bolt_hits_a_body_cylinder_and_misses_beside_it()
    {
        var feet = new Vector3(100, 0, 0);
        Assert.True(CombatSystem.HitBody(new Vector3(0, 13, 0), new Vector3(200, 0, 0), feet, 4, 18, out float t));
        Assert.Equal(96f / 200, t, 4);
        Assert.False(CombatSystem.HitBody(new Vector3(0, 13, 5), new Vector3(200, 0, 0), feet, 4, 18, out _));
        Assert.False(CombatSystem.HitBody(new Vector3(0, 25, 0), new Vector3(200, 0, 0), feet, 4, 18, out _));   // over the head
        // Coming down onto the head from above.
        Assert.True(CombatSystem.HitBody(new Vector3(100, 30, 0), new Vector3(0, -20, 0), feet, 4, 18, out t));
        Assert.Equal(12f / 20, t, 4);
    }

    [Fact]
    public void Ammunition_is_used_a_charge_at_a_time_from_the_main_inventory_then_the_backpack()
    {
        var inv = new Inventory();
        var bag = new ItemInstance { Record = "pack", Type = FcsRecordType.CONTAINER, Section = "backpack_attach", Contents = new Inventory(6, 6) };
        inv.Items.Add(bag);
        bag.Contents!.Items.Add(new ItemInstance { Record = "bolts", Section = "backpack_content", Charges = 2, MaxCharges = 30, Stackable = true });
        inv.Items.Add(new ItemInstance { Record = "bolts", Section = "main", Charges = 1, MaxCharges = 30, Quantity = 2, Stackable = true });
        Assert.Equal(1 + 30 + 2, Ammunition.Count(inv, "bolts"));
        Assert.True(Ammunition.UseOne(inv, "bolts"));          // the main stack's unit is empty: one unit less, refilled
        Assert.Equal(1, inv.Items.Find(i => i.Record == "bolts")!.Quantity);
        Assert.Equal(30, inv.Items.Find(i => i.Record == "bolts")!.Charges);
        for (int k = 0; k < 30; k++) Assert.True(Ammunition.UseOne(inv, "bolts"));
        Assert.DoesNotContain(inv.Items, i => i.Record == "bolts");
        Assert.True(Ammunition.UseOne(inv, "bolts"));          // now from the backpack
        Assert.True(Ammunition.UseOne(inv, "bolts"));
        Assert.False(Ammunition.UseOne(inv, "bolts"));
        Assert.Equal(0, Ammunition.Count(inv, "bolts"));
    }

    // ------------------------------------------------------------------ worlds

    /// <summary>A crossbowman: the given skills, a Ranger at quality 0 (or none) and <paramref name="bolts"/> bolts in one item, plus a katana for melee.</summary>
    internal static CharacterCold Shooter(float crossbows, float perception, int bolts, int faction = 0, CrossbowData? bow = null, float friendlyFire = 0)
    {
        var cold = new Combatant { Name = "shooter" }.MakeCold();
        cold.Stats![StatsEnumerated.Crossbows] = crossbows;
        cold.Stats[StatsEnumerated.Perception] = perception;
        cold.Stats[StatsEnumerated.FriendlyFire] = friendlyFire;
        var x = bow ?? Ranger;
        cold.Fighter = new Fighter(cold.Fighter!.Weapon) { Crossbow = CrossbowInstance.Create(0, x, Constants, 1) };
        cold.Faction = faction;
        cold.Inventory = new Inventory();
        if (bolts > 0) cold.Inventory.Items.Add(new ItemInstance { Record = x.AmmoId, Section = "main", Charges = bolts, MaxCharges = 30, Stackable = true });
        return cold;
    }

    static CharacterId Put(SimWorld world, CharacterCold cold, Vector3 at) => world.Characters.Spawn(new CharacterHot { Position = at, Health = 100 }, cold, 0);

    static CharacterCold Dummy(int faction, float toughness = 50)
    {
        var cold = new Combatant { Name = "target", Toughness = toughness }.MakeCold();
        cold.Faction = faction;
        return cold;
    }

    [Fact]
    public void A_skilled_shooter_reloads_and_hits_a_still_target_at_range_using_one_bolt_a_shot()
    {
        using var world = NewWorld(5, 1, out var combat);
        var shooter = Put(world, Shooter(100, 100, 3), new Vector3(1000, 150, 1000));
        var target = Put(world, Dummy(1, toughness: 100), new Vector3(1300, 150, 1000));
        world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
        int ticks = 0;
        while (ticks < 900 && !combat.Log.Any(e => e.Ranged)) { world.RunTick(); ticks++; }
        var hit = Assert.Single(combat.Log, e => e.Ranged);
        output.WriteLine($"hit after {ticks} ticks: part {hit.Part} pierce {hit.Pierce:0.0} KO {hit.Knockout}");
        Assert.True(hit.Intended);
        Assert.Equal(target.Slot, hit.Defender);
        Assert.True(hit.Pierce > 0);
        Assert.Equal(0f, hit.Cut);
        // Reload (3 s for s = 1) + steady aim (0.4 s) + flight (300 units at about 397 units a second): about 4.2 s.
        Assert.InRange(ticks, 3.4 * 30, 5 * 30);
        Assert.Equal(1, combat.ProjectilesFired);
        Assert.Equal(2, Ammunition.Count(world.Characters.Cold(shooter.Slot)!.Inventory, "bolts"));
        var s = combat.StateOf(shooter);
        Assert.True(s.Ranged);
        Assert.Equal(1, s.Landed);
        Assert.Equal(1, combat.StateOf(target).Taken);
        // XP: aiming and reloading trained the crossbow skill and perception.
        Assert.True(world.Characters.Cold(shooter.Slot)!.Stats![StatsEnumerated.Crossbows] > 99.9f);
    }

    [Fact]
    public void A_shooter_plays_the_reload_then_the_aim_clip_by_the_controllers_timing()
    {
        var combat = new CombatSystem(KatanaTechniques(), Constants, Bodies.BodyFixtures.BaseGame, BodyOptions.Default, Lengths, new CombatOptions { RecordLog = true });
        AnimationDefinition Ranged(string id, string name, string clip) => new() { Id = id, Name = name, Clip = clip, Area = AnimationArea.All, Category = AnimationLibrary.RangedCategory, WeaponRight = 1, CombatMode = 2, StealthMode = 2, Kinds = WeaponKinds.All };
        var library = new AnimationLibrary([
            new AnimationDefinition { Id = "idle", Name = "idle", Clip = "idle", Area = AnimationArea.All, Idle = true, IdleChance = 1, IdleTimeMin = 5, IdleTimeMax = 5, WeaponLeft = 2, WeaponRight = 2, CombatMode = 2, StealthMode = 2 },
            Ranged("aim", AnimationLibrary.RangedAimRecord, "aimshoot"),
            Ranged("reload", AnimationLibrary.RangedReloadRecord, "reload 1"),
        ]);
        var animation = new AnimationSystem(library, new AnimationLengths().Add("aimshoot", 2).Add("reload 1", 3).Add("idle", 4), 4, combat);
        using var world = new SimWorld(new WorldSettings { Seed = 4, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 150), [combat, animation]);
        var shooter = Put(world, Shooter(100, 100, 3), new Vector3(1000, 150, 1000));
        var target = Put(world, Dummy(1, toughness: 100), new Vector3(1300, 150, 1000));
        world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
        string Playing() => world.Characters.Cold(shooter.Slot)!.Animation is { } a
            ? string.Join(",", Enumerable.Range(0, a.Count).Where(k => a.Layers[k].Target > 0).Select(k => library.Definitions[a.Layers[k].Definition].Name)) : "";
        world.RunTicks(30);
        Assert.Equal(AnimationLibrary.RangedReloadRecord, Playing());
        while (combat.ProjectilesFired == 0 && world.Tick < 300) world.RunTick();
        Assert.Equal(AnimationLibrary.RangedAimRecord, Playing());
    }

    [Fact]
    public void An_unskilled_shooter_misses_most_shots_at_long_range()
    {
        using var world = NewWorld(15, 1, out var combat);
        // Skill and perception 0: the full 5 degree cone, which at 500 units is up to 44 units off a body 4 units in radius.
        var shooter = Put(world, Shooter(0, 0, 10), new Vector3(1000, 150, 1000));
        var target = Put(world, Dummy(1, toughness: 100), new Vector3(1500, 150, 1000));
        world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
        while (world.Tick < 3000 && (combat.ProjectilesFired < 10 || combat.ProjectilesInFlight > 0)) world.RunTick();
        int hits = combat.Log.Count(e => e.Ranged);
        output.WriteLine($"fired {combat.ProjectilesFired}, hits {hits}, tick {world.Tick}");
        Assert.Equal(10, combat.ProjectilesFired);
        Assert.InRange(hits, 0, 4);
    }

    [Fact]
    public void A_shooter_running_dry_draws_its_melee_weapon_and_closes_in()
    {
        using var world = NewWorld(6, 1, out var combat);
        var shooter = Put(world, Shooter(60, 60, 2), new Vector3(1000, 150, 1000));
        var target = Put(world, Dummy(1, toughness: 100), new Vector3(1200, 150, 1000));
        world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
        bool sawRanged = false;
        for (int k = 0; k < 2400 && combat.StateOf(shooter).Thrown == 0; k++)
        {
            world.RunTick();
            sawRanged |= combat.StateOf(shooter).Ranged;
        }
        var s = combat.StateOf(shooter);
        output.WriteLine($"fired {combat.ProjectilesFired}, ranged hits {combat.Log.Count(e => e.Ranged)}, thrown {s.Thrown}, tick {world.Tick}");
        Assert.True(sawRanged);
        Assert.Equal(2, combat.ProjectilesFired);
        Assert.Equal(0, Ammunition.Count(world.Characters.Cold(shooter.Slot)!.Inventory, "bolts"));
        Assert.True(s.Dry);
        Assert.False(s.Ranged);
        Assert.True(s.Thrown > 0, "the shooter fought on in melee");
        Assert.Equal(HandHold.OfCategory((int)WeaponCategory.Katanas).Weapon, world.Characters.Cold(shooter.Slot)!.DrawnWeapon);
    }

    [Fact]
    public void A_crossbowman_with_a_melee_attacker_on_it_fights_in_melee()
    {
        using var world = NewWorld(7, 1, out var combat);
        var shooter = Put(world, Shooter(60, 60, 10), new Vector3(1000, 150, 1000));
        var foe = Put(world, Dummy(1), new Vector3(1012, 150, 1000));
        world.Commands.Enqueue(new AttackOrder([shooter], foe) { Tick = 0 });
        world.Commands.Enqueue(new AttackOrder([foe], shooter) { Tick = 0 });
        world.RunTicks(30);
        Assert.False(combat.StateOf(shooter).Ranged);
        Assert.Equal(0, combat.ProjectilesFired);
    }

    [Fact]
    public void A_friend_in_the_line_of_fire_holds_the_shot_and_an_enemy_in_it_takes_the_bolt()
    {
        // A friend (the shooter's faction) halfway: no shot.
        using (var world = NewWorld(8, 1, out var combat))
        {
            var shooter = Put(world, Shooter(100, 100, 5), new Vector3(1000, 150, 1000));
            Put(world, Dummy(0), new Vector3(1150, 150, 1000));
            var target = Put(world, Dummy(1), new Vector3(1300, 150, 1000));
            world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
            world.RunTicks(300);
            Assert.True(combat.StateOf(shooter).Loaded > 0, "loaded and waiting");
            Assert.Equal(0, combat.ProjectilesFired);
        }
        // Another enemy (a third faction) halfway: the bolt is fired and hits it instead of the target.
        using (var world = NewWorld(8, 1, out var combat))
        {
            var shooter = Put(world, Shooter(100, 100, 5), new Vector3(1000, 150, 1000));
            var other = Put(world, Dummy(2, toughness: 100), new Vector3(1150, 150, 1000));
            var target = Put(world, Dummy(1), new Vector3(1300, 150, 1000));
            world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
            for (int k = 0; k < 600 && !combat.Log.Any(e => e.Ranged); k++) world.RunTick();
            var hit = Assert.Single(combat.Log, e => e.Ranged);
            Assert.Equal(other.Slot, hit.Defender);
            Assert.False(hit.Intended);
        }
    }

    /// <summary>Moves one character somewhere else at a given tick (a test's hand).</summary>
    sealed class Teleport(CharacterId who, Vector3 to) : ITickSystem
    {
        public long At { get; set; } = long.MaxValue;

        public void Move(SimWorld world, Partition part)
        {
            if (world.Tick != At || who.Slot < part.Start || who.Slot >= part.End) return;
            world.Characters.Next[who.Slot].Position = to;
        }
    }

    [Fact]
    public void A_bolt_that_misses_flies_its_range_then_drops_and_sticks_in_a_slope()
    {
        // Flat ground, then a slope rising beyond x = 1650 (a spent bolt meeting flat ground at a few degrees grazes off and is dropped).
        var combat = new CombatSystem(KatanaTechniques(), Constants, Bodies.BodyFixtures.BaseGame, BodyOptions.Default, Lengths, new CombatOptions { RecordLog = true });
        using var world = new SimWorld(new WorldSettings { Seed = 9, PublishSnapshots = false }, new OpenGroundWalkability((x, _) => x > 1650 ? 150 + (x - 1650) * 0.5f : 150), [combat]);
        var shooter = Put(world, Shooter(100, 100, 1), new Vector3(1000, 150, 1000));
        var target = Put(world, Dummy(1), new Vector3(1500, 150, 1000));
        var hand = new Teleport(target, new Vector3(1500, 150, 1600));
        world.AddSystem(hand);
        world.Commands.Enqueue(new AttackOrder([shooter], target) { Tick = 0 });
        while (combat.ProjectilesFired == 0 && world.Tick < 600) world.RunTick();
        Assert.Equal(1, combat.ProjectilesFired);
        // The target steps away before the bolt arrives.
        hand.At = world.Tick;
        world.RunTicks(600);
        var p = Assert.Single(combat.PublishProjectiles());
        output.WriteLine($"stuck at {p.Position}");
        Assert.True(p.Stuck);
        Assert.True(p.Position.X > 1000 + 600 * 0.9f, $"flew its range first ({p.Position})");
        Assert.True(p.Position.X > 1650);
        Assert.Equal(150 + (p.Position.X - 1650) * 0.5f, p.Position.Y, 1);
        Assert.Equal(0, combat.ProjectilesInFlight);
        Assert.DoesNotContain(combat.Log, e => e.Ranged);
    }

    // ------------------------------------------------------------------ determinism

    /// <summary>Six squads of three: crossbowmen of faction 0 against katana fighters of faction 1, 250 units apart, cut into one-slot partitions.</summary>
    static SimWorld Skirmish(ulong seed, int threads, out CombatSystem combat)
    {
        var world = NewWorld(seed, threads, out combat, minPartition: 1);
        for (int g = 0; g < 6; g++)
        {
            float x = 600 + g * 900, z = 1000;
            var shooters = new List<CharacterId>();
            var fighters = new List<CharacterId>();
            for (int k = 0; k < 3; k++)
            {
                float skill = 20 + 15 * ((g + k) % 5);
                shooters.Add(Put(world, Shooter(skill, skill, 2 + (g + k) % 3, 0, friendlyFire: skill), new Vector3(x + k * 12, 150, z + k * 8)));
                fighters.Add(Put(world, Dummy(1), new Vector3(x + 250 + k * 10, 150, z + 20 * k)));
            }
            for (int k = 0; k < 3; k++)
            {
                world.Commands.Enqueue(new AttackOrder([shooters[k]], fighters[(k + g) % 3]) { Tick = 0 });
                world.Commands.Enqueue(new AttackOrder([fighters[k]], shooters[(k + 1) % 3]) { Tick = 0 });
            }
        }
        return world;
    }

    internal static List<ulong> Hashes(int threads, ulong seed = 13)
    {
        using var world = Skirmish(seed, threads, out var combat);
        var hashes = new List<ulong>();
        int ran = 0;
        foreach (int target in new[] { 30, 150, 300, 600, 1200 })
        {
            world.RunTicks(target - ran);
            ran = target;
            hashes.Add(world.StateHash());
        }
        return hashes;
    }

    [Fact]
    public void A_skirmish_with_crossbows_is_the_same_at_any_thread_count()
    {
        var one = Hashes(1);
        var four = Hashes(4);
        var sixteen = Hashes(16);
        Assert.Equal(one, four);
        Assert.Equal(one, sixteen);
        Assert.Equal(one, Hashes(1));
        Assert.NotEqual(one, Hashes(1, 14));
        using var world = Skirmish(13, 4, out var combat);
        world.RunTicks(1200);
        output.WriteLine($"fired {combat.ProjectilesFired}, ranged hits {combat.Log.Count(e => e.Ranged)} (to a friend or another: {combat.Log.Count(e => e.Ranged && !e.Intended)}), melee blows {combat.Log.Count(e => !e.Ranged)}");
        Assert.True(combat.ProjectilesFired > 10);
        Assert.Contains(combat.Log, e => e.Ranged);
        Assert.Contains(combat.Log, e => !e.Ranged);
    }
}

/// <summary>The crossbow facts of docs/game/combat.md "Ranged and turrets" against the install's records (base load order), and a real crossbowman shooting. Skipped without the game.</summary>
[Slow]
public class RangedInstallTests(ITestOutputHelper output)
{
    static Meitou.Data.GameDatabase Db()
    {
        Assert.SkipWhen(InstallData.Install is null, "Kenshi install not found");
        return InstallData.BaseGame!;
    }

    [Fact]
    public void Every_crossbow_names_its_ammunition_and_the_ranged_clips_load_apart_from_the_normal_ones()
    {
        var db = Db();
        var combat = CombatDatabase.From(db);
        var items = new ItemFactory(db, GameConstants.FromDatabase(db));
        Assert.Equal(7, combat.Crossbows.Count);
        foreach (var x in combat.Crossbows)
        {
            var bolts = items.Create(x.AmmoId);
            Assert.NotNull(bolts);
            output.WriteLine($"{x.Name}: ammo {bolts!.Name} charges {bolts.Charges}, reload anim {db.Find(x.ReloadAnimId)?.Name ?? "-"}, shots {x.NumShots}, perfect {x.AccuracyPerfectSkill}");
            Assert.StartsWith("Bolts", bolts.Name);
            Assert.InRange(bolts.Charges, 10, 40);
            Assert.Equal(1, x.NumShots);
        }
        var ranger = combat.Crossbows.Single(x => x.Name == "Ranger");
        Assert.Equal(new CrossbowStats(22, 33, 600, 3, 4.5f, 5, 400), ranger.At(0, combat.Constants));
        Assert.Equal("Bolts [Regulars]", items.Info(ranger.AmmoId)!.Name);
        Assert.Equal(30, items.Info(ranger.AmmoId)!.Charges);
        Assert.Equal("reload 3 phase", db.Find(combat.Crossbows.Single(x => x.Name == "Eagle's Cross").ReloadAnimId)!.Name);

        var library = AnimationLibrary.FromDatabase(db);
        int aim = library.IndexOf(AnimationLibrary.RangedAimRecord);
        Assert.True(aim >= 0);
        Assert.Equal(AnimationLibrary.RangedCategory, library.Definitions[aim].Category);
        Assert.Equal("aimshoot", library.Definitions[aim].Clip);
        Assert.True(library.IndexOf(AnimationLibrary.RangedReloadRecord) >= 0);
        // The ranged records come after every other (no index of the normal set moves), followed only by the carried body's (category 3), and neither is chosen as an idle.
        int firstRanged = library.Definitions.ToList().FindIndex(d => d.Category != 0);
        int firstCarried = library.Definitions.ToList().FindIndex(d => d.Category == AnimationLibrary.CarriedCategory);
        Assert.True(firstCarried < 0 || firstCarried > firstRanged);
        Assert.All(library.Definitions.Skip(firstRanged).Take((firstCarried < 0 ? library.Definitions.Count : firstCarried) - firstRanged), d => Assert.Equal(AnimationLibrary.RangedCategory, d.Category));
        if (firstCarried >= 0) Assert.All(library.Definitions.Skip(firstCarried), d => Assert.Equal(AnimationLibrary.CarriedCategory, d.Category));
        foreach (var stance in new[] { new AnimationStance(), new AnimationStance { Combat = true, Right = HandHold.OfCategory((int)WeaponCategory.Bow) } })
        {
            Assert.DoesNotContain(library.Idles(stance), x => x.Index >= firstRanged);
            Assert.DoesNotContain(library.CombatIdles(stance), x => x.Index >= firstRanged);
        }
    }

    [Fact]
    public void A_real_crossbowman_shoots_a_ranger_bolt_at_a_katana_fighter()
    {
        var db = Db();
        var combatData = CombatDatabase.From(db);
        var constants = GameConstants.FromDatabase(db);
        var race = RaceData.From(db.OfType(FcsRecordType.RACE).First(r => r.Name == "Greenlander"), db);
        var combat = new CombatSystem(combatData.Techniques, combatData.Constants, constants, BodyOptions.Default, new AnimationLengths(), new CombatOptions { RecordLog = true });
        using var world = new SimWorld(new WorldSettings { Seed = 3, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 150), [combat]);
        var items = new ItemFactory(db, constants);
        var ranger = combatData.Crossbows.Single(x => x.Name == "Ranger");
        CharacterCold Make(bool shooter, int faction)
        {
            var stats = new CharacterStats();
            foreach (var s in new[] { StatsEnumerated.MeleeAttack, StatsEnumerated.MeleeDefence, StatsEnumerated.Katanas, StatsEnumerated.Dexterity, StatsEnumerated.Strength, StatsEnumerated.Toughness, StatsEnumerated.Crossbows, StatsEnumerated.Perception })
                stats[s] = 60;
            var fighter = new Fighter(WeaponInstance.Create(0.5f, combatData.Weapon("476-gamedata.base")!, null, null, combatData.Constants))
            {
                Crossbow = shooter ? CrossbowInstance.Create(0, ranger, combatData.Constants, 1) : null,
            };
            var inv = new Inventory();
            if (shooter) inv.Add(items.Create(ranger.AmmoId)!);
            return new CharacterCold { Name = shooter ? "shooter" : "fighter", Faction = faction, Stats = stats, Race = race, Medical = MedicalState.Create(race, stats.Strength), Fighter = fighter, Inventory = inv };
        }
        var a = world.Characters.Spawn(new CharacterHot { Position = new(1000, 150, 1000), Health = 100 }, Make(true, 0), 0);
        var b = world.Characters.Spawn(new CharacterHot { Position = new(1200, 150, 1000), Health = 100 }, Make(false, 1), 0);
        world.Commands.Enqueue(new AttackOrder([a], b) { Tick = 0 });
        world.Commands.Enqueue(new AttackOrder([b], a) { Tick = 0 });
        world.RunTicks(1800);
        output.WriteLine($"fired {combat.ProjectilesFired}, bolt hits {combat.Log.Count(e => e.Ranged)}, melee blows {combat.Log.Count(e => !e.Ranged)}, bolts left {Ammunition.Count(world.Characters.Cold(a.Slot)!.Inventory, ranger.AmmoId)}");
        foreach (var e in combat.Log.Where(e => e.Ranged)) output.WriteLine($"  t{e.Tick} part {e.Part} pierce {e.Pierce:0.0} stun {e.Stun:0.0}");
        Assert.True(combat.ProjectilesFired >= 1);
        Assert.Equal(30 - combat.ProjectilesFired, Ammunition.Count(world.Characters.Cold(a.Slot)!.Inventory, ranger.AmmoId));
    }
}
