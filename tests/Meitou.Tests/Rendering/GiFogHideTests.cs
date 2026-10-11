using System.Numerics;
using Meitou.Rendering;
using Meitou.Rendering.Gi;
using Meitou.Tests.World;

namespace Meitou.Tests.Rendering;

/// <summary>The probes the fog hides (docs/render-gi.md "Probes in the fog"): the reach box of a probe column, the layers of it that are hidden, and the frame's probe list.</summary>
public class GiFogHideTests
{
    const float S = 512, SY = 384;

    [Fact]
    public void The_reach_box_holds_every_point_that_can_read_the_probes_of_the_column()
    {
        // giCascade reads a point q = p + offset (offset at most 0.3 of the smaller spacing) from the probes of the cells around q: a probe at column x
        // is read from p within one spacing across. Layer l is read from base + (l - 0.5) sy to base + (l + 1.5) sy.
        const float x = 10240, z = -20480, baseY = 100;
        for (int layers = 1; layers <= 7; layers++)
        {
            var (min, max) = GiFogHide.ReachBox(x, z, baseY, S, SY, layers, -1000, 0);
            float offset = 0.3f * MathF.Min(S, SY);
            Assert.True(min.X <= x - S - offset && max.X >= x + S + offset);
            Assert.True(min.Z <= z - S - offset && max.Z >= z + S + offset);
            Assert.True(max.Y >= baseY + (layers - 1 + 1.5f) * SY + offset, $"top for {layers} layers");
            Assert.True(min.Y <= -1000);   // the lowest layer is read from below, as far as the block goes
        }
        // Wider with the slack.
        var (a, _) = GiFogHide.ReachBox(x, z, baseY, S, SY, 1, -1000, 0);
        var (b, _) = GiFogHide.ReachBox(x, z, baseY, S, SY, 1, -1000, 64);
        Assert.Equal(a.X - 64, b.X);
    }

    static FogVolumes Frame(Vector3 eye)
    {
        var volumes = new FogVolumes([FogCullTests.Block()]) { SolveInline = true };
        volumes.Update(eye, Vector3.UnitX, 50 * MathF.PI / 180, 16f / 9, 50000, 0.5f, on: true);
        return volumes;
    }

    [Fact]
    public void Columns_far_inside_the_block_hide_their_low_layers_and_near_ones_nothing()
    {
        var fog = Frame(new Vector3(0, 150, 0));
        float radius = fog.CullRadius;
        Assert.True(radius > 4400, $"radius {radius}");
        int Hidden(float x, float z, float baseY = 0) => GiFogHide.HiddenLayers(fog.Covers, x, z, baseY, S, SY, 8, fog.CullFloor, 64);
        Assert.Equal(0, Hidden(0, 0));
        Assert.Equal(0, Hidden(radius * 0.5f, 0));
        // Just past the hide distance plus the column's reach: not yet; well past it: yes.
        Assert.Equal(0, Hidden(radius + 0.3f * SY, 0));
        int far = Hidden(20000, 0);
        // The block's ceiling is 3000: the layers whose reach stays below it. Layer l reaches base + (l + 1.5) sy + 0.3 sy: 6 layers is 3000 - 115 at most.
        Assert.True(far is >= 5 and <= 7, $"hidden layers {far}");
        Assert.True(far < 8);   // the top layer is never counted
        // A column on a hill is hidden in fewer layers, never more.
        Assert.True(Hidden(20000, 0, 800) <= far);
        // Over the block's wall: not hidden at all.
        Assert.Equal(0, Hidden(49800, 0));
        Assert.Equal(0, Hidden(60000, 0));
    }

    [Fact]
    public void What_the_reach_box_hides_is_inside_the_block_and_past_the_hide_distance()
    {
        var fog = Frame(new Vector3(0, 150, 0));
        var rng = new Random(3);
        int hiddenColumns = 0;
        for (int i = 0; i < 400; i++)
        {
            float x = rng.Next(-25000, 25000), z = rng.Next(-25000, 25000), baseY = rng.Next(-200, 600);
            int layers = GiFogHide.HiddenLayers(fog.Covers, x, z, baseY, S, SY, 8, fog.CullFloor, 64);
            if (layers == 0) continue;
            hiddenColumns++;
            // Every point a probe of those layers could light (the box's corners and centre) is covered by the cull's own test.
            var (min, max) = GiFogHide.ReachBox(x, z, baseY, S, SY, layers, fog.CullFloor, 0);
            Assert.True(fog.Covers(min, max));
            Assert.True(Vector3.Distance(new Vector3(x, baseY, z), new Vector3(0, 150, 0)) >= fog.CullRadius);
        }
        Assert.True(hiddenColumns > 100, $"{hiddenColumns} columns");
    }

    [Fact]
    public void Nothing_is_hidden_without_a_fog_cull()
    {
        var volumes = new FogVolumes([FogCullTests.Block()]) { CullEnabled = false, SolveInline = true };
        volumes.Update(new Vector3(0, 150, 0), Vector3.UnitX, 50 * MathF.PI / 180, 16f / 9, 50000, 0.5f, on: true);
        Assert.False(volumes.CullActive);
        Assert.Equal(0, GiFogHide.HiddenLayers(volumes.Covers, 20000, 0, 0, S, SY, 8, volumes.CullFloor, 64));
    }

    [Fact]
    public void The_list_without_hiding_is_the_shaders_old_enumeration()
    {
        var into = new uint[GiProbes.Probes];
        var hidden = new byte[GiProbes.Cascades * GiProbes.Columns * GiProbes.Columns];
        Array.Fill(hidden, (byte)7);   // ignored at period 0
        foreach (int phases in new[] { 1, 2, 4, 8 })
            for (int phase = 0; phase < phases; phase++)
            {
                int n = GiFogHide.List(into, phases, phase, 5, 0, hidden, 3, 4, 5, 6);
                Assert.Equal(GiProbes.Probes / phases, n);
                for (int gid = 0; gid < n; gid++) Assert.Equal((uint)(gid * phases + phase), into[gid]);
            }
    }

    [Fact]
    public void A_hidden_probe_is_listed_once_in_each_period_of_its_rounds_and_the_others_every_time()
    {
        const int phases = 4, phase = 1, period = 16;
        const int offX0 = 5, offZ0 = 9, offX1 = 30, offZ1 = 2;
        var hidden = new byte[GiProbes.Cascades * GiProbes.Columns * GiProbes.Columns];
        // Cascade 1's columns in the window's far half (wx >= 16) hide 5 layers; cascade 0 hides nothing.
        for (int wz = 0; wz < GiProbes.Columns; wz++)
            for (int wx = 16; wx < GiProbes.Columns; wx++)
                hidden[(1 * GiProbes.Columns + (offZ1 + wz) % GiProbes.Columns) * GiProbes.Columns + (offX1 + wx) % GiProbes.Columns] = 5;
        var into = new uint[GiProbes.Probes];
        var seen = new Dictionary<uint, int>();
        int plain = 0;
        for (uint round = 0; round < period; round++)
        {
            int n = GiFogHide.List(into, phases, phase, round, period, hidden, offX0, offZ0, offX1, offZ1);
            var set = new HashSet<uint>();
            for (int i = 0; i < n; i++)
            {
                uint probe = into[i] & ~GiFogHide.SlowBit;
                Assert.True(set.Add(probe));   // never twice in one list
                int layer = (int)(probe % GiProbes.Layers), rest = (int)(probe / GiProbes.Layers), wx = rest % GiProbes.Columns, c = rest / (GiProbes.Columns * GiProbes.Columns);
                bool isHidden = c == 1 && wx >= 16 && layer < 5;
                Assert.Equal(isHidden, (into[i] & GiFogHide.SlowBit) != 0);
                if (isHidden) seen[probe] = seen.GetValueOrDefault(probe) + 1; else plain++;
            }
        }
        Assert.Equal(GiProbes.Probes / phases - seen.Count, plain / period);   // every visible probe in every round
        Assert.NotEmpty(seen);
        Assert.All(seen.Values, count => Assert.Equal(1, count));   // each hidden probe exactly once in the 16 rounds
    }
    [Fact]
    public void The_far_cascade_is_listed_once_in_each_far_period_of_its_rounds_and_a_hidden_probe_keeps_its_own_rule()
    {
        const int phases = 4, phase = 2, period = 16, farPeriod = 2;
        var hidden = new byte[GiProbes.Cascades * GiProbes.Columns * GiProbes.Columns];
        // Cascade 1's first toroidal row of columns (offsets 0): the lowest 3 layers hidden.
        for (int colX = 0; colX < GiProbes.Columns; colX++) hidden[(1 * GiProbes.Columns + 0) * GiProbes.Columns + colX] = 3;
        var into = new uint[GiProbes.Probes];
        var far = new Dictionary<uint, int>();
        var slow = new Dictionary<uint, int>();
        int near = 0;
        for (uint round = 0; round < period; round++)
        {
            int n = GiFogHide.List(into, phases, phase, round, period, farPeriod, hidden, [0, 0, 0, 0]);
            for (int i = 0; i < n; i++)
            {
                bool isSlow = (into[i] & GiFogHide.SlowBit) != 0, isFar = (into[i] & GiFogHide.FarBit) != 0;
                uint probe = into[i] & ~(GiFogHide.SlowBit | GiFogHide.FarBit);
                int c = (int)(probe / GiProbes.Layers) / (GiProbes.Columns * GiProbes.Columns);
                Assert.False(isSlow && isFar);
                if (isSlow) slow[probe] = slow.GetValueOrDefault(probe) + 1;
                else if (isFar) { Assert.Equal(1, c); far[probe] = far.GetValueOrDefault(probe) + 1; }
                else { Assert.Equal(0, c); near++; }
            }
        }
        Assert.Equal(GiProbes.Probes / GiProbes.Cascades / phases, near / period);   // cascade 0: every probe in every round
        Assert.All(slow.Values, count => Assert.Equal(1, count));   // hidden: once in 16 rounds
        Assert.All(far.Values, count => Assert.Equal(period / farPeriod, count));   // far: every second round
        Assert.Equal(GiProbes.Probes / GiProbes.Cascades / phases - slow.Count, far.Count);
    }
}
