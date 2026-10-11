using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

/// <summary>The rules of the fog shading rate (<c>fog-vrs</c>, docs/render-post.md "Fog shading rate"): the codes of the rate attachment, the opacity of the air over a surface and the fragment size it gives.</summary>
public class FogShadingRateTests
{
    static readonly (float TwoByTwo, float FourByFour) Defaults = (FogShadingRate.DefaultTwoByTwo, FogShadingRate.DefaultFourByFour);

    [Fact]
    public void The_haze_and_the_volumes_multiply_what_shows_of_the_surface()
    {
        Assert.Equal(0f, FogShadingRate.Opacity(0f, 1f), 1e-6f);          // clear air
        Assert.Equal(1f, FogShadingRate.Opacity(1f, 1f), 1e-6f);          // the haze alone hides it
        Assert.Equal(1f, FogShadingRate.Opacity(0f, 0f), 1e-6f);          // a volume alone hides it
        Assert.Equal(0.75f, FogShadingRate.Opacity(0.5f, 0.5f), 1e-6f);   // half of half shows
        Assert.Equal(0f, FogShadingRate.Opacity(-1f, 2f), 1e-6f);         // out of range values are clamped
    }

    [Fact]
    public void A_more_opaque_surface_never_gets_a_finer_rate()
    {
        int last = 1;
        for (float o = 0; o <= 1f; o += 0.01f)
        {
            int size = FogShadingRate.Size(o, 4, Defaults);
            Assert.True(size >= last, $"opacity {o}");
            last = size;
        }
    }
}
