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
}
