using System.Numerics;
using Meitou.Rendering.Impostors;

namespace Meitou.Tests.Rendering;

/// <summary>The billboards' depth prepass and tight quad (<see cref="ImpostorPrepass"/>, <see cref="ImpostorQuad"/>): the texts must keep the prepass and the colour pass that tests depth EQUAL in agreement.</summary>
public class ImpostorPrepassTests
{
    [Fact]
    public void The_vertex_stage_is_invariant_so_the_prepass_and_the_equal_colour_pass_rasterise_the_same_depth()
    {
        Assert.Contains("invariant gl_Position;", ImpostorShaders.Vertex);
    }

    [Fact]
    public void The_prepass_decides_with_the_colour_programs_fade_pick_and_cut_out_and_shades_nothing()
    {
        string prepass = ImpostorShaders.PrepassFragment, colour = ImpostorShaders.Fragment;
        // The same statements: the fade, the sample and the cut-out.
        string[] shared =
        [
            "if (fract(vFadeKey + smoothstep(3.0, 12.0, pixels) * dither) >= vFade) discard;",
            "vObjectEye, vObjectPoint - vObjectEye, vCellA, vCellB, vCellC, vWeights, pick);",
            "else if (s.coverage < 0.5) discard;",
        ];
        foreach (string statement in shared)
        {
            Assert.Contains(statement, prepass);
            Assert.Contains(statement, colour);
        }
        Assert.DoesNotContain("kenshiLight", prepass);
        Assert.DoesNotContain("fragColour", prepass);
        Assert.Contains("kenshiLight", colour);
    }

    [Fact]
    public void The_colour_after_the_prepass_has_no_discard_but_the_same_lighting()
    {
        string after = ImpostorShaders.FragmentAfterPrepass, colour = ImpostorShaders.Fragment;
        Assert.DoesNotContain("discard", after.Substring(after.IndexOf("void main()", StringComparison.Ordinal)));
        Assert.Contains("colour = kenshiLight(albedo, n, v, gloss, world);", after);
        Assert.Contains("colour = atmoApply(colour, uEye, world);", after);
        Assert.Contains("#version 450", ImpostorShaders.FragmentAfterPrepassNative());
        Assert.Contains("#version 450", ImpostorShaders.PrepassFragmentNative());
        Assert.Contains("discard", colour);
    }

    [Fact]
    public void A_quad_without_the_box_is_the_spheres_silhouette()
    {
        Assert.Equal(0u, ImpostorQuad.Pack(new Vector3(1, 1, 1), 0));
        Assert.Equal(0u, ImpostorQuad.Pack(new Vector3(2, 1, 1), 1));   // a box wider than the sphere: no tight quad
    }

    [Theory]
    [InlineData(100f, 60f, 10f, 112f)]
    [InlineData(1f, 1f, 1f, 1.7f)]
    [InlineData(0f, 0f, 0f, 1f)]
    public void The_packed_half_extents_never_shrink_the_box(float x, float y, float z, float radius)
    {
        uint word = ImpostorQuad.Pack(new Vector3(x, y, z), radius);
        Assert.Equal(1u, word >> 30);
        float[] half = [x, y, z];
        for (int axis = 0; axis < 3; axis++)
        {
            float unpacked = ((word >> (10 * axis)) & 1023u) * (radius / 1023f);
            Assert.True(unpacked >= half[axis] - 1e-4f, $"axis {axis}: {unpacked} < {half[axis]}");
            Assert.True(unpacked <= half[axis] + radius / 1023f + 1e-4f, $"axis {axis}: {unpacked} is more than a step over {half[axis]}");
        }
    }
}
