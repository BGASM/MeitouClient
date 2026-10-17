using System.Numerics;
using Meitou.Rendering;

namespace Meitou.Tests.Rendering;

/// <summary>The Meitou water's CPU side (docs/render-water.md): the breakers on the game clock, the polar grid and the foam bake.</summary>
public class MeitouWaterTests
{
    [Fact]
    public void Reflection_fog_shader_compiles_for_plain_and_multisampled_depth()
    {
        var compiler = new Meitou.Rendering.Gpu.Shaders.GlslProgramCompiler(new Meitou.Rendering.Gpu.Shaders.ShaderCompileOptions { UseDiskCache = false, UseMemoryCache = false, RemapClipDepth = true });
        foreach (bool ms in new[] { false, true })
        {
            var p = compiler.Compile(PostProcessShaders.Vertex, ReflectionPass.FogFragment(ms));
            Assert.NotEmpty(p.FragmentSpirv);
        }
    }

    [Fact]
    public void Polar_grid_covers_the_disc_with_valid_triangles()
    {
        foreach (int segments in new[] { 128, 256 })
        {
            var (positions, indices) = WaterRenderer.PolarGrid(segments);
            int vertices = positions.Length / 2;
            Assert.Equal(0, indices.Length % 3);
            Assert.All(indices, i => Assert.InRange(i, 0u, (uint)vertices - 1));
            float outer = 0, detail = 0;
            for (int i = 0; i < vertices; i++)
            {
                float r = new Vector2(positions[i * 2], positions[i * 2 + 1]).Length();
                if (r > 1e5f) outer = r; else detail = MathF.Max(detail, r);
            }
            Assert.Equal(WaterRenderer.GridDetailRadius, detail, 1);
            Assert.True(outer > 1e5f, "the outer ring is marked for the extent");
            // Counter-clockwise seen from above (+Y): with x right and z down the screen that is a negative cross product in (x, z).
            for (int t = 0; t < indices.Length; t += 3)
            {
                Vector2 P(uint i) => new(positions[i * 2], positions[i * 2 + 1]);
                Vector2 a = P(indices[t]), b = P(indices[t + 1]), c = P(indices[t + 2]);
                float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                Assert.True(cross < 0, $"triangle {t / 3} is wound the other way or degenerate");
            }
        }
    }
}
