using Meitou.Data;
using Meitou.Game;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Rendering.Gpu.Core;

namespace Meitou.Tests.Vulkan;

[Collection("StageClock")]
public class TitleTerrainTests
{
    [Fact, Slow]
    public void ReturningFromTheDummyTerrainRestoresTheGameplayHeightTextures()
    {
        VulkanDevice? native;
        try { native = VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, SyncValidation = true }); }
        catch (Exception e) when (e is VulkanException or DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { native = null; }
        using var device = native;
        Assert.SkipWhen(device is null, "No Vulkan 1.3 device");
        using (var context = new GpuContext(device!))
        using (var scene = TitleComposition.CreateTerrain(new(100, 200, 300), new GameDatabase(), Meitou.Data.World.SkyClock.Fallback))
        using (var world = new TerrainRenderer(context, scene.Coarse, scene.CoarseSize, scene.Window))
        {
            var worldImage = context.Globals.Texture("uHeightFine")!().Image;
            using var title = new TerrainRenderer(context, scene.Coarse, scene.CoarseSize, scene.Window);
            var titleImage = context.Globals.Texture("uHeightFine")!().Image;
            Assert.NotEqual(worldImage, titleImage);
            world.Activate();
            Assert.Equal(worldImage, context.Globals.Texture("uHeightFine")!().Image);
            int version = context.Globals.Version;
            world.Activate();
            Assert.Equal(version, context.Globals.Version);
            title.Activate();
            Assert.Equal(titleImage, context.Globals.Texture("uHeightFine")!().Image);
            context.Finish();
        }
        Assert.True(device!.ValidationErrors == 0, string.Join("\n", device.ValidationLog));
    }
}
