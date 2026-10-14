using System.Numerics;
using Meitou.Rendering.Gpu;
using Silk.NET.Vulkan;

namespace Meitou.Rendering.Characters;

/// <summary>A portrait the host wants this frame: the character by its draw-list key, the atlas cell to draw it in, and the number that turns its camera.</summary>
public readonly record struct PortraitRequest(long Key, int Cell, uint Serial);

/// <summary>
/// The squad portraits (docs/character-renderer.md "Portraits"): each requested character drawn into its cell of one atlas, from the camera the original
/// places by the race's <c>portrait distance</c> and <c>portrait offset</c> round the head bone, lit by the shader's plain model (no fog, sky or shadows).
/// </summary>
internal sealed unsafe partial class CharacterRenderer
{
    /// <summary>A cell's size in pixels and the atlas's cells across and down (the original's atlas: 128 px cells).</summary>
    public const int PortraitSize = 128, PortraitColumns = 16, PortraitRows = 2;
    /// <summary>The vertical field of view of the portrait camera.</summary>
    const float PortraitFieldOfView = 25 * MathF.PI / 180;

    /// <summary>The portraits to draw this frame (the host fills it before <see cref="Update"/>, which also poses these characters when they are out of range).</summary>
    public List<PortraitRequest> PortraitRequests { get; } = [];
    readonly HashSet<long> portraitKeys = [];
    readonly Dictionary<long, int> liveByKey = [];
    int portraitLive = -1;
    Texture? portraitColour, portraitDepth;
    SampledTexture? portraitSampled;

    /// <summary>The atlas (sRGB, transparent where no character is), made on first use. Render thread.</summary>
    public (Texture Texture, SampledTexture Sampled) PortraitAtlas
    {
        get
        {
            if (portraitColour is null)
            {
                int w = PortraitSize * PortraitColumns, h = PortraitSize * PortraitRows;
                portraitColour = Texture.Create(Gpu, new TextureDesc(Format.R8G8B8A8Srgb, w, h, Use: TextureUse.Sampled | TextureUse.ColourTarget, Name: "portraits"));
                portraitDepth = Texture.Create(Gpu, new TextureDesc(Format.D32Sfloat, w, h, Use: TextureUse.DepthTarget, Name: "portraits depth"));
                portraitSampled = new SampledTexture(Gpu.Samplers.Get(SamplerDesc.FromGl(TextureMinFilter.Linear, TextureMagFilter.Linear,
                    TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, false, DepthFunction.Lequal, false, 1, false, 0)), portraitColour.View(), portraitColour.Image);
                cleared = false;
            }
            return (portraitColour, portraitSampled!.Value);
        }
    }

    bool cleared;

    /// <summary>The keys <see cref="Update"/> poses even out of range.</summary>
    void GatherPortraitKeys()
    {
        portraitKeys.Clear();
        foreach (var r in PortraitRequests) portraitKeys.Add(r.Key);
    }

    /// <summary>
    /// Draws the requested portraits into their cells (after <see cref="Update"/>, outside any pass). A character not posed this frame (not in the list) keeps
    /// what its cell last showed.
    /// </summary>
    public void DrawPortraits()
    {
        if (PortraitRequests.Count == 0) return;
        var (atlas, _) = PortraitAtlas;
        liveByKey.Clear();
        for (int i = 0; i < liveCount; i++) liveByKey[live[i].Key] = i;
        var cmd = Gpu.BeginNative("portraits");
        var target = PassTargets.Of(atlas, portraitDepth);
        cmd.BeginRendering(new RenderingDesc(target.Colour, target.Depth with { Load = AttachmentLoadOp.Clear, Clear = new ClearValue(depthStencil: new ClearDepthStencilValue(1f, 0)) },
            target.Width, target.Height));
        Gpu.BeginHostPass(cmd, target, DrawState.Scene(target.Formats));
        if (!cleared) { Gpu.Clear(cmd, true, default, false, 1, new Rect2D(default, new Extent2D((uint)target.Width, (uint)target.Height))); cleared = true; }
        foreach (var request in PortraitRequests)
        {
            if (request.Cell < 0 || request.Cell >= PortraitColumns * PortraitRows || !liveByKey.TryGetValue(request.Key, out int index)) continue;
            ref var l = ref live[index];
            if (!l.State.HasHead && l.Asset.HeadBone >= 0) continue;   // not posed yet
            int x = request.Cell % PortraitColumns * PortraitSize, y = request.Cell / PortraitColumns * PortraitSize;
            var rect = new Rect2D(new Offset2D(x, y), new Extent2D(PortraitSize, PortraitSize));
            Gpu.Clear(cmd, true, default, true, 1, rect);
            Gpu.SetPassViewport(new Viewport(x, y, PortraitSize, PortraitSize, 0, 1), rect);
            var (eye, look) = PortraitCamera(in l, request.Serial);
            var view = Matrix4x4.CreateLookAt(eye, look, Vector3.UnitY);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(PortraitFieldOfView, 1, 0.5f, 400);
            // The key light from the camera's side, above and a little to the left.
            var toEye = Vector3.Normalize(eye - look);
            var left = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toEye));
            var light = Vector3.Normalize(toEye + 0.7f * Vector3.UnitY + 0.4f * left);
            portraitLive = index;
            try { DrawView(new ViewConstants { ViewProjection = view * projection, Eye = eye, LightDir = light }, eye, []); }
            finally { portraitLive = -1; }
        }
        Gpu.EndHostPass(cmd);
        cmd.EndRendering();
        Gpu.EndNative(cmd);
    }

    /// <summary>
    /// The original's portrait camera (docs/character-renderer.md "Portraits"): it looks at the head bone moved by the race's <c>portrait offset</c> (in the model's
    /// axes, times the character's size) from <c>portrait distance</c> along the model's front, turned about the vertical by up to 45 degrees either way and about
    /// the side axis by a few degrees, both by the character's serial.
    /// </summary>
    static (Vector3 Eye, Vector3 Target) PortraitCamera(in Live l, uint serial)
    {
        var race = l.Asset.Appearance.Race;
        float scale = l.World.ScaleLength();
        var head = l.State.HasHead ? l.State.Head : new Vector3(0, l.Asset.Height * 0.85f, 0);
        var offset = new Vector3(race.GetFloat("portrait offset x"), race.GetFloat("portrait offset y"), race.GetFloat("portrait offset z"));
        var target = Vector3.Transform(head, l.World) + Vector3.TransformNormal(offset, l.World);
        float yaw = (serial % 500 / 1000f - 0.25f) * MathF.PI, pitch = (serial % 252 / 1000f - 0.083f) * MathF.PI;
        var local = Vector3.Transform(new Vector3(0, 0, race.GetFloat("portrait distance", 10)), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
        var direction = Vector3.TransformNormal(local, l.World) / Math.Max(scale, 1e-3f);
        return (target + direction, target);
    }

    void DisposePortraits()
    {
        portraitColour?.Dispose();
        portraitDepth?.Dispose();
        (portraitColour, portraitDepth, portraitSampled) = (null, null, null);
    }
}
