using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Combat;
using static Meitou.Tests.Combat.CombatFixtures;

namespace Meitou.Tests.Combat;

/// <summary>The defender's reaction and the outcome rule (docs/game/combat.md "Technique choice, block and dodge", "Hit outcome").</summary>
public class ReactionTests
{
    [Fact]
    public void A_block_needs_the_right_direction_and_more_than_half_the_animation()
    {
        var block = Block("block left", 2);
        Assert.Equal(BlowOutcome.Blocked, HitOutcomes.Decide(block, 0.8f, 2));
        Assert.Equal(BlowOutcome.Blocked, HitOutcomes.Decide(block, 0.51f, 2));
        Assert.Equal(BlowOutcome.Hit, HitOutcomes.Decide(block, 0.5f, 2));       // "past half": exactly half is not
        Assert.Equal(BlowOutcome.Hit, HitOutcomes.Decide(block, 0.3f, 2));
        Assert.Equal(BlowOutcome.Hit, HitOutcomes.Decide(block, 0.9f, 3));       // the wrong direction
        Assert.Equal(BlowOutcome.Hit, HitOutcomes.Decide(null, 1, 2));           // no reaction
    }

    [Fact]
    public void Attack_choice_follows_weapon_distance_skill_and_the_static_rows()
    {
        var all = new[]
        {
            Cut("static only") with { AttackDistance = -999, AttackDistanceMinVsStatic = 99 },
            Cut("moving only") with { AttackDistance = 0, AttackDistanceMinVsStatic = -10 },
            Cut("both") with { AttackDistance = 10, AttackDistanceMinVsStatic = 10 },
            Cut("disabled") with { Disabled = true },
            Cut("expert") with { MinSkill = 70 },
            Block("block", 1),
        };
        string Pick(float gap, bool moving, float skill = 50, WeaponKinds kind = WeaponKinds.Katana, float reach = 10.5f)
        {
            var seen = new HashSet<string>();
            for (float r = 0; r < 1; r += 0.05f)
            {
                int i = TechniqueChooser.ChooseAttack(all, kind, 0, false, skill, gap, moving, reach, r);
                seen.Add(i < 0 ? "-" : all[i].Name);
            }
            return string.Join(",", seen.Order());
        }
        Assert.Equal("both,static only", Pick(5, false));
        Assert.Equal("-", Pick(11, false));                  // beyond the weapon's reach (10.5), whatever the technique allows
        Assert.Equal("both,moving only", Pick(0, true));
        Assert.Equal("both", Pick(8, true));
        Assert.Equal("both,expert,static only", Pick(5, false, skill: 80));
        Assert.Equal("-", Pick(5, false, kind: WeaponKinds.Blunt));
    }
}
