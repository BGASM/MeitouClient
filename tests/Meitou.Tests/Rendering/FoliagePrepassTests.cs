using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

/// <summary>The foliage depth prepass (<see cref="FoliagePrepass"/>): its texts must keep the pass and the colour pass that tests depth EQUAL in agreement.</summary>
public class FoliagePrepassTests
{
    [Fact]
    public void The_vertex_stage_is_invariant_so_the_prepass_and_the_equal_colour_pass_rasterise_the_same_depth()
    {
        Assert.Contains("invariant gl_Position;", FoliageShaders.MeshVertex());
    }

    [Fact]
    public void The_prepass_program_has_the_dither_and_the_cut_out_and_the_solid_one_nothing()
    {
        string full = FoliageShaders.MeshPrepassFragment();
        Assert.True(full.Split("discard").Length - 1 >= 2, "the fade's and the cut-out's discard");
        Assert.Contains("foliageDither() >= vFade", full);
        Assert.DoesNotContain("discard", FoliageShaders.MeshPrepassFragment(solid: true));
        Assert.Contains("#version 450", FoliageShaders.MeshPrepassNative());
    }

    [Fact]
    public void The_dither_is_the_colour_programs()
    {
        const string dither = "fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))))";
        Assert.Contains(dither, FoliageShaders.MeshPrepassFragment());
        Assert.Contains(dither, FoliageShaders.MeshFragment());
    }
}
