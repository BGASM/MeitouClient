using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

/// <summary>The Meitou shadow schedule's spreading of far cascade redraws over frames (pure arithmetic, no GPU).</summary>
public class ShadowScheduleTests
{
    static bool[] Pick(bool[] want, int[] waited, int budget = 1, int maxWait = 3, bool[]? must = null)
    {
        var draw = new bool[want.Length];
        ShadowSchedule.Pick(want, must ?? new bool[want.Length], waited, budget, maxWait, draw);
        return draw;
    }

    [Fact]
    public void A_redraw_waiting_the_cap_is_forced_beyond_the_budget()
    {
        var waited = new[] { 0, 0, 3, 3 };
        Assert.Equal([false, false, true, true], Pick([false, true, true, true], waited, maxWait: 3));
        Assert.Equal([0, 1, 0, 0], waited);
    }

    [Fact]
    public void A_steady_demand_below_the_budget_never_waits_more_than_a_frame_or_two()
    {
        // Each far cascade asks every 4th, 16th and 32nd frame, uncovered redraws now and then: simulate and bound the wait.
        var waited = new int[4];
        var owed = new bool[4];
        int worst = 0;
        for (int frame = 0; frame < 2000; frame++)
        {
            var want = new bool[4];
            for (int i = 0; i < 4; i++) want[i] = owed[i] || frame % new[] { 1, 4, 16, 32 }[i] == i * 5 % new[] { 1, 4, 16, 32 }[i] || frame % (7 + i) == 0;
            var draw = Pick(want, waited);
            for (int i = 0; i < 4; i++) { owed[i] = want[i] && !draw[i]; worst = Math.Max(worst, waited[i]); }
        }
        Assert.True(worst <= 3);
    }

    [Fact]
    public void With_five_cascades_the_second_is_never_deferred_and_does_not_use_the_budget()
    {
        Assert.Equal(1, ShadowSchedule.NearCascades(4));
        Assert.Equal(2, ShadowSchedule.NearCascades(5));
        var waited = new int[5];
        var draw = new bool[5];
        // Cascades 1 and 2 want a redraw: 1 is near (not counted), so the budget of one still lets 2 go.
        ShadowSchedule.Pick([true, true, true, false, false], new bool[5], waited, 1, 3, draw, ShadowSchedule.NearCascades(5));
        Assert.Equal([true, true, true, false, false], draw);
        // Two far ones: one waits.
        ShadowSchedule.Pick([false, true, false, true, true], new bool[5], waited, 1, 3, draw, ShadowSchedule.NearCascades(5));
        Assert.Equal([false, true, false, true, false], draw);
        Assert.Equal([0, 0, 0, 0, 1], waited);
    }

    [Fact]
    public void The_cadences_and_phases_keep_four_cascades_as_they_were_and_spread_five()
    {
        Assert.Equal([1, 4, 16, 32], Enumerable.Range(0, 4).Select(i => ShadowPass.Cadence(i, 4)));
        Assert.Equal([0, 1, 10, 15], ShadowPass.Phases(4));
        Assert.Equal([1, 2, 4, 16, 32], Enumerable.Range(0, 5).Select(i => ShadowPass.Cadence(i, 5)));
        var phases = ShadowPass.Phases(5);
        // No two cascades past the first come due on the same frame (over a 32-frame cycle).
        for (int f = 0; f < 64; f++)
            Assert.True(Enumerable.Range(1, 4).Count(i => f % ShadowPass.Cadence(i, 5) == phases[i]) <= 1, $"frame {f}");
    }
}
