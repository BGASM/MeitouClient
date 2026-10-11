using System.Numerics;
using Meitou.Data.World;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Rendering.Gpu.Core;

namespace Meitou.Tests.Vulkan;

/// <summary>The post-processing chain's native targets (docs/renderer-native.md 8, phase 8 stage 2): named allocations, a resize, the exposure readback.</summary>
[Collection("StageClock")]
public unsafe class PostProcessNativeTests
{
    [Theory]
    [Slow]
    [InlineData(false)]
    [InlineData(true)]
    public void Shafts_keep_sky_at_the_horizon_when_the_water_hit_is_past_the_far_clip(bool merged)
    {
        VulkanDevice? d;
        try { d = VulkanDevice.Create(new VulkanDeviceOptions { Validation = true }); }
        catch (Exception e) when (e is VulkanException or DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { d = null; }
        using var device = d;
        Assert.SkipWhen(device is null, "No Vulkan 1.3 device");
        using (var ctx = new GpuContext(device!))
        {
            using var sky = new SkyRenderer(ctx) { HazeStrength = 1, CloudCoverage = 0 };
            sky.Prepare(new SkyClock(54, 5, 23).SunDirection(22), 1000, 50000);
            var options = PostOptions.Create("meitou");
            options.Upscale.Kind = UpscalerKind.Off;
            options.ShaftDebug = true;
            options.ShaftAir = 0;
            options.Ssao = false;
            (options.ToneMap, options.Grade, options.Fxaa) = (ToneMapOperator.Clamp, false, false);
            using var post = new PostProcess(ctx, options) { MergeLayers = merged, WaterHeight = 100 };
            const int w = 96, h = 64;
            using var target = Texture.Create(ctx, new TextureDesc(Silk.NET.Vulkan.Format.R8G8B8A8Unorm, w, h,
                Use: TextureUse.ColourTarget | TextureUse.TransferSrc | TextureUse.Sampled, Name: "shaft horizon test"));
            post.Target = target;
            post.Begin(w, h);
            post.SetCamera(new Vector3(0, 1000, 0), Matrix4x4.Identity, 1, w / (float)h);
            post.BeginFarSlice(1000, 10000);
            post.BeginNearSlice(1, 1000);
            var t = post.SceneTargets;
            var cmd = ctx.BeginNative("shaft horizon empty sky");
            cmd.BeginRendering(new RenderingDesc(
                t.Colour with { Load = Silk.NET.Vulkan.AttachmentLoadOp.Clear, Clear = new Silk.NET.Vulkan.ClearValue(new Silk.NET.Vulkan.ClearColorValue(0, 0, 0, 1)) },
                t.Depth with { Load = Silk.NET.Vulkan.AttachmentLoadOp.Clear, Clear = new Silk.NET.Vulkan.ClearValue(depthStencil: new Silk.NET.Vulkan.ClearDepthStencilValue(1, 0)) }, w, h));
            cmd.EndRendering();
            ctx.EndNative(cmd);
            post.SetNearSlice(1, 1000, 1, w / (float)h);
            post.RunLightShafts(0.8f, 50000, 0);
            post.End();
            ctx.Finish();
            var pixels = ctx.ReadBack(target, 4);
            // Debug blue is the surface haze colour; sky is exactly zero. The two rays beside the
            // horizon hit the analytic water much farther than 10000, so both must remain sky.
            foreach (int y in new[] { h / 2 - 1, h / 2 })
                Assert.Equal(0, pixels[(y * w + w / 2) * 4 + 2]);
            // The ray 5.5 pixels below the horizon hits water around 9700 units away: inside
            // the clip, but beyond the fade's 9000-unit end. It must also use the sky correction.
            foreach (int y in new[] { h / 2 - 6, h / 2 + 5 })
                Assert.Equal(0, pixels[(y * w + w / 2) * 4 + 2]);
            // Steeper downward rays still hit visible water and receive its haze correction.
            Assert.Contains(Enumerable.Range(0, h).Select(y => pixels[(y * w + w / 2) * 4 + 2]), b => b > 0);
        }
        Assert.Equal(0, device!.ValidationErrors);
    }

    [Theory]
    [Slow]
    [InlineData(UpscalerKind.Off)]
    [InlineData(UpscalerKind.Taa)]
    public void The_chain_owns_named_native_targets_survives_a_resize_and_reads_its_exposure_back(UpscalerKind upscaler)
    {
        VulkanDevice? d;
        try { d = VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, SyncValidation = true }); }
        catch (Exception e) when (e is VulkanException or DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { d = null; }
        using var device = d;
        Assert.SkipWhen(device is null, "No Vulkan 1.3 device");
        using (var ctx = new GpuContext(device!))
        {
            var options = PostOptions.Create("meitou");
            options.Upscale.Kind = upscaler;
            options.Upscale.Scale = 1;   // render at the display size (the Meitou default with an upscaler is 0.83)
            (options.ToneMap, options.Grade) = (ToneMapOperator.Clamp, false);   // the plain clip, so the grey below is exact
            using var post = new PostProcess(ctx, options) { AutoExposure = (0.01f, 10f), InstantAdaptation = true };
            foreach (var (w, h) in new[] { (64, 36), (80, 48) })
            {
                // The caller's RGBA8 target, as the viewer's offscreen framebuffer.
                using var target = Texture.Create(ctx, new TextureDesc(Silk.NET.Vulkan.Format.R8G8B8A8Unorm, w, h,
                    Use: TextureUse.ColourTarget | TextureUse.TransferSrc | TextureUse.Sampled, Name: "test target"));
                post.Target = target;

                for (int frame = 0; frame < 2; frame++)
                {
                    post.Begin(w, h);
                    post.SetCamera(Vector3.Zero, Matrix4x4.Identity, 1, w / (float)h);
                    post.BeginFarSlice(1000, 10000);
                    post.BeginNearSlice(1, 1000);
                    // A grey scene: luminance 0.5 everywhere, so the measured mean and the adapted value are 0.5.
                    var t = post.SceneTargets;
                    var cmd = ctx.BeginNative("test scene clear");
                    cmd.BeginRendering(new RenderingDesc(
                        t.Colour with { Load = Silk.NET.Vulkan.AttachmentLoadOp.Clear, Clear = new Silk.NET.Vulkan.ClearValue(new Silk.NET.Vulkan.ClearColorValue(0.5f, 0.5f, 0.5f, 1)) },
                        t.Depth with { Load = Silk.NET.Vulkan.AttachmentLoadOp.Clear, Clear = new Silk.NET.Vulkan.ClearValue(depthStencil: new Silk.NET.Vulkan.ClearDepthStencilValue(1f, 0)) },
                        t.Width, t.Height));
                    cmd.EndRendering();
                    ctx.EndNative(cmd);
                    post.SetNearSlice(1, 1000, 1, w / (float)h);
                    post.End();
                    ctx.Finish();
                }
                Assert.Equal((w, h), (post.RenderWidth, post.RenderHeight));

                var names = device!.Allocator.Breakdown().Select(o => o.Name).ToList();
                foreach (var name in new[] { "post scene colour", "post scene depth", "post ldr", "post ssao", "post luminance", "post exposure adapted" })
                    Assert.Contains(name, names);
                if (upscaler != UpscalerKind.Off)
                    foreach (var name in new[] { "post far slice depth", "post motion", "post upscale depth", "post reactive", "post taa history" })
                        Assert.Contains(name, names);

                var (adapted, mean) = post.ReadExposure();
                Assert.Equal(0.5f, mean, 0.01f);
                Assert.Equal(0.5f, adapted, 0.01f);

                // The composite reached the caller's target: 0.5 × 0.55 / 0.5, clipped, with the dither's ±1.
                var texels = ctx.ReadBack(target, 4);
                Assert.Equal(w * h * 4, texels.Length);
                for (int i = 0; i < texels.Length; i += 4) Assert.InRange(texels[i], 138, 143);
            }
        }
        Assert.True(device!.ValidationErrors == 0, "Validation errors:\n" + string.Join("\n", device.ValidationLog));
    }
}
