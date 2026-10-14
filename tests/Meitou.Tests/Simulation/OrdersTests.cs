using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>The orders panel's toggles: block, passive, hold, sneak and the move speed (docs/simulation.md "Orders").</summary>
public class OrdersTests
{
    /// <summary>A player character at the origin and a bandit <paramref name="distance"/> away that attacks it at tick 0; <paramref name="orders"/> set on the player first.</summary>
    static (SimWorld World, CombatSystem Combat, CharacterId Player, CharacterId Bandit) Attacked(CharacterOrders orders, float distance = 30)
    {
        var combat = new CombatSystem(KatanaTechniques(), Constants, BaseGame, BodyOptions.Default, DuelFixture.Lengths, new CombatOptions { SelfApproach = false, TickMedical = false });
        var walk = new OpenGroundWalkability((_, _) => 150);
        var world = new SimWorld(new WorldSettings { Seed = 3, PublishSnapshots = false }, walk,
            [new PlayerSystem(), new PursuitSystem(combat), new MovementSystem(new PathService(walk, true)), new BodySystem(BaseGame), combat, new RetaliationSystem(combat)]);
        CharacterId Make(string name, Vector3 at, bool player)
        {
            var cold = new Combatant { Name = name }.MakeCold();
            cold.IsPlayer = player;
            return world.Characters.Spawn(new CharacterHot { Position = at, Health = 100, MaxSpeed = 60, WalkSpeed = 15, Mode = SpeedMode.Free }, cold, 0);
        }
        var player = Make("player", new Vector3(1000, 150, 1000), true);
        var bandit = Make("bandit", new Vector3(1000 + distance, 150, 1000), false);
        if (orders != CharacterOrders.None) world.Commands.Enqueue(new OrdersCommand([player], orders, true) { Tick = 0 });
        world.Commands.Enqueue(new AttackOrder([bandit], player) { Tick = 0 });
        return (world, combat, player, bandit);
    }

    [Fact]
    public void Block_fights_back_without_attacking_and_passive_does_not_fight_back()
    {
        var (open, openCombat, p0, _) = Attacked(CharacterOrders.None);
        var (block, blockCombat, p1, _) = Attacked(CharacterOrders.Block);
        var (passive, passiveCombat, p2, _) = Attacked(CharacterOrders.Passive);
        using (open) using (block) using (passive)
        {
            open.RunTicks(600); block.RunTicks(600); passive.RunTicks(600);
            Assert.True(openCombat.StateOf(p0).Thrown > 0, "without orders the attacked character swings back");
            Assert.True(blockCombat.StateOf(p1).TargetSlot >= 0 || block.Characters.Cold(p1.Slot)!.Medical!.Incapacitated, "a blocker still faces its attacker");
            Assert.Equal(0, blockCombat.StateOf(p1).Thrown);
            Assert.NotEqual((byte)CharacterTask.Attack, passive.Characters.Next[p2.Slot].Task);
            Assert.Equal(0, passiveCombat.StateOf(p2).Thrown);
        }
    }

    [Fact]
    public void Hold_waits_for_the_attacker_to_come_within_reach_and_never_walks_after_it()
    {
        var (open, _, p0, _) = Attacked(CharacterOrders.None, distance: 300);
        var (hold, _, p1, b1) = Attacked(CharacterOrders.Hold, distance: 300);
        using (open) using (hold)
        {
            var start = hold.Characters.Next[p1.Slot].Position;
            open.RunTicks(20); hold.RunTicks(20);
            Assert.Equal((byte)CharacterTask.Attack, open.Characters.Next[p0.Slot].Task);
            Assert.NotEqual((byte)CharacterTask.Attack, hold.Characters.Next[p1.Slot].Task);
            int ticks = 20;
            while (ticks < 3000 && hold.Characters.Next[p1.Slot].Task != (byte)CharacterTask.Attack) { hold.RunTick(); ticks++; }
            Assert.Equal((byte)CharacterTask.Attack, hold.Characters.Next[p1.Slot].Task);
            Assert.True(Orders.InHoldReach(hold.Characters.Next[p1.Slot].Position, hold.Characters.Next[b1.Slot].Position), "it answered only once the bandit was near");
            Assert.True(Vector3.Distance(start, hold.Characters.Next[p1.Slot].Position) < 5, "it did not leave its spot");
        }
    }

    [Fact]
    public void Sneaking_is_slower_and_harder_to_see_and_hear_by_the_originals_numbers()
    {
        Assert.Equal(23, Orders.SneakSpeed(60, 0), 3);          // a clumsy sneaker creeps at 12 + 11
        Assert.Equal(60, Orders.SneakSpeed(60, 0.95f), 3);      // from 0.9 on, no cap
        Assert.Equal(15, Orders.SneakSpeed(15, 0), 3);          // never faster than it was going
        Assert.Equal(11, Orders.SneakSpeed(5, 0), 3);           // floored at 11
        Assert.Equal(0.75f, Orders.SneakView(1, 0), 4);
        Assert.Equal(0.6f * 0.5f * 0.4f, Orders.SneakView(0.4f, 1), 4);
        Assert.Equal(450, Orders.SneakNoise(0), 3);
        Assert.Equal(80, Orders.SneakNoise(1), 3);
        Assert.Equal(0.1f * 0.5f * 0.75f, Orders.SneakHearChance(100, 200, 0.5f), 5);
        // Straight ahead in daylight (R = 1500): 1000 away is seen standing up and sneaking; 1200 only standing up (the sneaker's reach is 0.75 R).
        var eye = new Vector3(0, 0, 0);
        Assert.True(AggroSystem.Visibility(eye, 0, new Vector3(0, 0, 1200), 1500) > 0);
        Assert.True(AggroSystem.SeenSneaking(eye, 0, new Vector3(0, 0, 1000), 1500, 0));
        Assert.False(AggroSystem.SeenSneaking(eye, 0, new Vector3(0, 0, 1200), 1500, 0));
    }

    [Fact]
    public void Run_speed_cycles_and_sets_the_mode_of_move_orders()
    {
        var walk = new OpenGroundWalkability((_, _) => 150);
        using var world = new SimWorld(new WorldSettings { Seed = 1, PublishSnapshots = false }, walk, [new PlayerSystem(), new MovementSystem(new PathService(walk, true))]);
        var cold = new CharacterCold { Name = "p", IsPlayer = true };
        var id = world.Characters.Spawn(new CharacterHot { Position = new Vector3(1000, 150, 1000), Health = 100, MaxSpeed = 60, WalkSpeed = 15 }, cold, 0);
        Assert.Equal(SpeedMode.Free, cold.RunSpeed);
        world.Commands.Enqueue(new RunSpeedCommand([id], 1) { Tick = 0 });
        world.RunTick();
        Assert.Equal(SpeedMode.Walk, cold.RunSpeed);
        world.Commands.Enqueue(new MoveOrder([id], new Vector3(1200, 150, 1000)) { Tick = world.Tick });
        world.RunTick();
        Assert.Equal(SpeedMode.Walk, world.Characters.Next[id.Slot].Mode);
        world.Commands.Enqueue(new RunSpeedCommand([id], -1) { Tick = world.Tick });
        world.RunTick();
        Assert.Equal(SpeedMode.Free, cold.RunSpeed);
    }

    [Fact]
    public void Orders_go_to_the_originals_state_keys_and_back()
    {
        var cold = new CharacterCold { Orders = CharacterOrders.Block | CharacterOrders.Sneak | CharacterOrders.Hold, RunSpeed = SpeedMode.Run };
        var record = new FcsRecord { Type = (int)FcsRecordType.GAMESTATE_CHARACTER };
        Orders.Write(record, cold);
        Assert.True(record.Bools["defensive mode"]);
        Assert.True(record.Bools["stealth"]);
        Assert.True(record.Bools["passive mode"]);
        Assert.False(record.Bools["non combat mode"]);
        Assert.Equal(1, record.Ints["speed mode"]);
        var back = new CharacterCold();
        Orders.Read(record, back);
        Assert.Equal(cold.Orders, back.Orders);
        Assert.Equal(SpeedMode.Run, back.RunSpeed);
    }
}
