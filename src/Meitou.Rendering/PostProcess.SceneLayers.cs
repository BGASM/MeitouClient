using System.Numerics;

using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

/// <summary>
/// The merged scene layers (docs/render-post.md "Merged scene layers"): with <see cref="MergeLayers"/> the light shafts' apply, the fog volumes and the
/// SSAO's factor go onto the scene colour in one full-screen pass (<see cref="PostProcessShaders.SceneLayers"/>) instead of three, each reading the
/// depth and the scene colour again; with <see cref="FogHalf"/> the fog volumes are computed at half the render size and upsampled by depth there.
/// <see cref="RunLightShafts"/> and <see cref="RunFogVolumes"/> then leave their layer pending, <see cref="ApplySsao"/> draws them all.
/// </summary>
public sealed unsafe partial class PostProcess
{
    /// <summary>Meitou: one pass for the shafts, the fog volumes and the occlusion (default on; <c>MEITOU_LAYER_MERGE=0</c>, <c>--ab layer-merge</c>).</summary>
    public bool MergeLayers { get; set; } = Environment.GetEnvironmentVariable("MEITOU_LAYER_MERGE") != "0";
    /// <summary>Meitou: with <see cref="MergeLayers"/>, the fog volumes at half the render size (<c>MEITOU_FOG_HALF=0</c>, <c>--ab fog-half</c>).</summary>
    public bool FogHalf { get; set; } = Environment.GetEnvironmentVariable("MEITOU_FOG_HALF") != "0";

    /// <summary>The ray and distance rebuild of the merged passes (<c>PostProcessShaders.SceneDistance</c>), the light shafts' camera and grid, and the
    /// fog volumes' shadow from the shafts' first atlas.</summary>
    class LayerPass : FullscreenProgram
    {
        public readonly SamplerSlot NearDepth, FarDepth, ShaftInjected;
        public readonly UniformHandle NearPlanes, FarPlanes, WaterY, HasFar, FullSize, Grid, Range, Eye, Right, Up, Back, Tan, ShaftFog;
        public LayerPass(GpuContext gpu, string fragment, string name) : base(gpu, fragment, name)
        {
            (NearDepth, FarDepth, ShaftInjected) = (P.Sampler("uNearDepth"), P.Sampler("uFarDepth"), P.Sampler("uShaftInjected"));
            (NearPlanes, FarPlanes, WaterY, HasFar, FullSize) = (P.Uniform("uNearPlanes"), P.Uniform("uFarPlanes"), P.Uniform("uWaterY"), P.Uniform("uHasFar"), P.Uniform("uFullSize"));
            (Grid, Range, Eye, Tan, ShaftFog) = (P.Uniform("uShaftGrid"), P.Uniform("uShaftRange"), P.Uniform("uShaftEye"), P.Uniform("uShaftTan"), P.Uniform("uShaftFog"));
            (Right, Up, Back) = (P.Uniform("uShaftRight"), P.Uniform("uShaftUp"), P.Uniform("uShaftBack"));
        }
    }

    sealed class SceneLayersPass : LayerPass
    {
        public readonly SamplerSlot Light, Scatter, FogHalfTexture, Ao;
        public readonly UniformHandle Atlas, Params, Air, Layers;
        public SceneLayersPass(GpuContext gpu) : base(gpu, PostProcessShaders.SceneLayers, "post scene layers")
        {
            (Light, Scatter, FogHalfTexture, Ao) = (P.Sampler("uShaftLight"), P.Sampler("uShaftScatter"), P.Sampler("uFogHalf"), P.Sampler("uAo"));
            (Atlas, Params, Air, Layers) = (P.Uniform("uShaftAtlas"), P.Uniform("uShaftParams"), P.Uniform("uShaftAir"), P.Uniform("uLayers"));
        }
    }

    LayerPass? fogHalfPass;
    SceneLayersPass? sceneLayersPass;
    Target2D? fogHalf;

    // What is pending for the merged pass this frame: the shafts' apply (its grid and parameters), the fog volumes (1 here, 2 from the half-size pass) and
    // the shafts' grid the fog's shadow reads.
    bool shaftsPending;
    (Vector4 Air, Vector4 Params) shaftArgs;
    int fogPending;
    (Vector4 Grid, Vector4 Range, Vector2 Tan, float Darkening) fogShadow;

    /// <summary>The uniforms both merged passes share; the grid and range those of this frame's shafts (unused while neither layer reads them).</summary>
    void SetLayerCommon(LayerPass p, (Vector4 Grid, Vector4 Range, Vector2 Tan, float Darkening) shaft)
    {
        Bind(p.P, p.NearDepth, sceneDepth);
        Bind(p.P, p.FarDepth, farSliceDrawn ? farDepth : null);
        p.P.Set(p.NearPlanes, nearPlanes.X, nearPlanes.Y);
        p.P.Set(p.FarPlanes, farPlanes.X, farPlanes.Y);
        p.P.Set(p.WaterY, WaterHeight ?? float.MinValue);
        p.P.Set(p.HasFar, farSliceDrawn ? 1 : 0);
        p.P.Set(p.FullSize, width, height);
        float tanY = MathF.Tan(fovNow * 0.5f);
        var r = viewRotation;
        p.P.Set(p.Tan, tanY * aspectNow, tanY);
        p.P.Set(p.Right, r.M11, r.M21, r.M31);
        p.P.Set(p.Up, r.M12, r.M22, r.M32);
        p.P.Set(p.Back, r.M13, r.M23, r.M33);
        p.P.Set(p.Eye, eyeNow);
        p.P.Set(p.Grid, shaft.Grid);
        p.P.Set(p.Range, shaft.Range);
        Bind(p.P, p.ShaftInjected, shaft.Darkening > 0 ? shaftInjected : null);
        p.P.Set(p.ShaftFog, shaft.Darkening, shaftInjected?.Width ?? 1, shaftInjected?.Height ?? 1, 0);
    }

    /// <summary>The fog volumes for the merged pass: at half size into their own target now, or left to the merged pass (full size).</summary>
    void PrepareFogLayer()
    {
        fogShadow = shaftFrame;
        if (!FogHalf) { fogPending = 1; return; }
        int hw = Math.Max((width + 1) / 2, 1), hh = Math.Max((height + 1) / 2, 1);
        if (fogHalf is null || fogHalf.Width != hw || fogHalf.Height != hh)
        {
            fogHalf?.Texture.Dispose();   // released after the frames in flight
            using var batch = Gpu.Uploads.Begin();
            fogHalf = Make(batch, hw, hh, InternalFormat.Rgba16f, TextureMinFilter.Nearest, "post fog half");
        }
        var f = fogHalfPass ??= new LayerPass(Gpu, PostProcessShaders.FogVolumesHalf, "post fog half");
        SetLayerCommon(f, fogShadow);
        f.P.ApplyGlobals();   // the atmosphere's and the fog volumes' uniforms, through the frame globals
        Draw(f.P, fogHalf);
        fogPending = 2;
    }

    /// <summary>The pending layers and, with <paramref name="occlusion"/>, the SSAO (run here) in one pass over the scene colour. Nothing when no layer is pending.</summary>
    void ApplyLayers(bool occlusion)
    {
        if (occlusion) RunSsao();
        if (!shaftsPending && fogPending == 0 && !occlusion) return;
        var p = sceneLayersPass ??= new SceneLayersPass(Gpu);
        // The fog's shadow and the shafts' layer read the same grid when both ran; the fog's copy was taken before RunFogVolumes reset the frame's.
        var grid = fogPending > 0 ? fogShadow : shaftFrame;
        SetLayerCommon(p, grid);
        Bind(p.P, p.Light, shaftsPending ? shaftLight : null);
        Bind(p.P, p.Scatter, shaftsPending ? shaftScatter : null);
        p.P.Set(p.Atlas, shaftInjected?.Width ?? 1, shaftInjected?.Height ?? 1);
        p.P.Set(p.Params, shaftArgs.Params);
        p.P.Set(p.Air, shaftArgs.Air);
        Bind(p.P, p.FogHalfTexture, fogPending == 2 ? fogHalf : null);
        Bind(p.P, p.Ao, occlusion ? aoB : null);
        p.P.Set(p.Layers, shaftsPending ? 1 : 0, fogPending, occlusion ? 1 : 0, 0);
        p.P.ApplyGlobals();
        Draw(p.P, sceneColour!.Attachment, sceneColour.Format, width, height, width, height, FogState);
        Stamp("layers");
        CloseSegment();
        shaftsPending = false;
        fogPending = 0;
        if (occlusion) aoApplied = true;
    }

    void DisposeSceneLayers()
    {
        fogHalfPass?.P.Dispose();
        sceneLayersPass?.P.Dispose();
        fogHalf?.Texture.Dispose();
    }
}
