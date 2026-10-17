using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using static Meitou.Tests.Combat.DuelFixture;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Combat;

/// <summary>Rules of the fight loop beyond the formulas: reach, staggering, orders, the animation stance, and the downed.</summary>
public class FightRulesTests
{
    [Fact]
    public void A_knocked_out_fighter_cannot_react_and_the_fight_ends()
    {
        using var world = NewWorld(6, 1, out var combat);
        var (a, b) = Pair(world, new Combatant { Attack = 100, WeaponSkill = 100, Dexterity = 100, Strength = 100, Quality = 1 }, new Combatant { Defence = 0, Toughness = 1, Attack = 1, WeaponSkill = 1 });
        RunToEnd(world, a, b);
        Assert.True(Down(world, b));
        Assert.False(Down(world, a));
        int blows = combat.Log.Count;
        world.RunTicks(120);
        Assert.Equal(blows, combat.Log.Count);                       // no more blows at the downed
        Assert.False(combat.StateOf(a).Fighting);
        Assert.False(combat.StateOf(b).Fighting);
        // An order against the downed does nothing.
        world.Commands.Enqueue(new AttackOrder([a], b) { Tick = world.Tick });
        world.RunTicks(60);
        Assert.False(world.Characters.Cold(a.Slot)!.InCombat);
    }

    [Fact]
    public void Movement_and_combat_systems_side_by_side_do_not_fight_over_the_position()
    {
        // The movement system comes first (as the integration note says); a fighter ordered to attack from afar closes in by itself, and the two systems agree on where it stands.
        var walk = new OpenGroundWalkability((_, _) => 150);
        var combat = new CombatSystem(KatanaTechniques(), Constants, BaseGame, BodyOptions.Default, Lengths, new CombatOptions { RecordLog = true });
        using var world = new SimWorld(new WorldSettings { Seed = 12, Threads = 4, PublishSnapshots = false, MinPartitionSize = 1 }, walk, [new MovementSystem(new PathService(walk, true)), combat]);
        var (a, b) = Pair(world, new Combatant(), new Combatant(), distance: 120);
        world.RunTicks(150);
        var pa = world.Characters.Previous[a.Slot].Position;
        var pb = world.Characters.Previous[b.Slot].Position;
        float gap = Vector3.Distance(pa, pb);
        Assert.InRange(gap, 4, 30);
        Assert.Equal(150f, pa.Y);
        Assert.NotEmpty(combat.Log);
        Assert.Equal((byte)CharacterTask.Attack, world.Characters.Previous[a.Slot].Task);
    }
}
