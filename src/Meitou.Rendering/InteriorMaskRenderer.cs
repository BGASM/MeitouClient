using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Ogre;
using Meitou.Data.World;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

/// <summary>
/// The interior cut (docs/render-terrain.md "Interior cut"): the <c>interior mask</c> meshes of the buildings near the eye drawn into a depth range of the
/// main view, nearest front face (R) and farthest back face (A) as view depth + 1, which the terrain reads to leave out its fragments inside a building
/// (the game's <c>InteriorMask</c> compositor node and <c>rt_interiormask</c>). Drawn before the scene, at the scene's size, with its camera.
/// </summary>
public sealed unsafe class InteriorMaskRenderer : IDisposable
{
    /// <summary>The clear of the front channel: no front face here (half float's largest is 65504).</summary>
    public const float NoFront = 65000;

    /// <summary>How far from the eye (X/Z, to the building's footprint) masks are drawn.</summary>
    const float Range = 3000;

    const string Vertex = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMvp;
        void main() { gl_Position = uMvp * vec4(aPos, 1.0); }
        """;

    // Every face writes its view depth + 1 (the game's value, interiormask.hlsl) to R (min blend: the nearest) and A (max blend: the farthest). The game
    // splits the faces by facing instead; for a closed mesh seen from outside the nearest face is a front face and the farthest a back face, the same.
    const string Fragment = """
        #version 330 core
        out vec4 fragColour;
        void main()
        {
            float w = 1.0 / gl_FragCoord.w + 1.0;
            fragColour = vec4(w, 0.0, 0.0, w);
        }
        """;

    sealed class MaskMesh
    {
        public DeviceBuffer? Vertices;
        public int Count;
    }

    readonly GpuContext gpu;
    readonly WorldObjectRenderer objects;
    readonly LegacyProgram program;
    readonly NativeSegment segment;
    readonly UniformHandle mvp;
    readonly VertexArrayBindings vertexSource;
    readonly Dictionary<string, MaskMesh> meshes = new(StringComparer.OrdinalIgnoreCase);
    readonly List<InteriorVolume> near = [];
    readonly List<(Vector2 Min, Vector2 Max)> boxes = [];
    Texture? target;
    (Texture Texture, uint Index)? bindless;

    internal InteriorMaskRenderer(GpuContext gpu, WorldObjectRenderer objects)
    {
        this.gpu = gpu;
        this.objects = objects;
        program = LegacyProgram.Create(gpu, Vertex, Fragment, "interior mask");
        segment = new NativeSegment(gpu, program, Silk.NET.Vulkan.PrimitiveTopology.TriangleList, "interior mask");
        mvp = program.Uniform("uMvp");
        vertexSource = new VertexArrayBindings([new LegacyProgram.Attribute(default, Silk.NET.Vulkan.Format.R32G32B32Sfloat, 12, false)], default);
    }

    /// <summary>Whether the last <see cref="Record"/> drew a mask: only then do <see cref="Index"/> and <see cref="Boxes"/> apply.</summary>
    public bool Active { get; private set; }

    /// <summary>The depth range's bindless 2D index (valid while <see cref="Active"/>).</summary>
    public uint Index => bindless?.Index ?? 0;

    /// <summary>The drawn buildings' footprints' X/Z bounds: terrain patches outside all of them need no cut.</summary>
    public IReadOnlyList<(Vector2 Min, Vector2 Max)> Boxes => boxes;

    /// <summary>The meshes drawn by the last <see cref="Record"/>.</summary>
    public int Drawn { get; private set; }

    /// <summary>Draws the masks of the buildings near <paramref name="eye"/> with the main view's <paramref name="viewProjection"/> into a target of the scene's size.</summary>
    public void Record(Matrix4x4 viewProjection, Vector3 eye, int width, int height)
    {
        Active = false;
        Drawn = 0;
        boxes.Clear();
        if (width <= 0 || height <= 0) return;
        objects.InteriorMasks(eye, Range, near);
        if (near.Count == 0) return;
        if (target is null || target.Desc.Width != width || target.Desc.Height != height)
        {
            if (bindless is { } old) gpu.Bindless.Free(BindlessKind.Texture2D, old.Index);
            bindless = null;
            target?.Dispose();
            target = Texture.Create(gpu, new TextureDesc(Silk.NET.Vulkan.Format.R16G16B16A16Sfloat, width, height, Use: TextureUse.ColourTarget | TextureUse.Sampled, Name: "interior mask"));
        }
        var cmd = gpu.BeginNative("interior mask");
        var viewport = new Silk.NET.Vulkan.Viewport(0, 0, width, height, 0, 1);
        var t = PassTargets.Of(target, null).At(viewport);
        t = t with { Colour = t.Colour with { Load = Silk.NET.Vulkan.AttachmentLoadOp.Clear, Clear = new Silk.NET.Vulkan.ClearValue(new Silk.NET.Vulkan.ClearColorValue(NoFront, 0f, 0f, 0f)) } };
        cmd.BeginRendering(t.Rendering);
        // Min on the colour (the nearest face), max on the alpha (the farthest); every face drawn, no depth test (the game's target has no depth).
        var blend = new BlendState(true, Silk.NET.Vulkan.BlendFactor.One, Silk.NET.Vulkan.BlendFactor.One, Silk.NET.Vulkan.BlendOp.Min, Silk.NET.Vulkan.BlendOp.Max);
        var state = DrawState.For(t.Formats, gpu.Device.DepthClamp, blend: blend, mask: Silk.NET.Vulkan.ColorComponentFlags.RBit | Silk.NET.Vulkan.ColorComponentFlags.ABit);
        state.Record(cmd, t);
        cmd.BindPipeline(segment.Get(state, t.Formats, vertexSource));
        var relative = Matrix4x4.CreateTranslation(eye) * viewProjection;   // camera-relative placement keeps the precision far from the origin
        foreach (var v in near)
        {
            var node = v.Node;
            node.Translation -= eye;
            var m = node * relative;
            bool any = false;
            foreach (var path in v.MaskMeshes)
            {
                if (Mesh(path) is not { Vertices: { } buffer } mesh) continue;
                program.Set(mvp, in m);
                program.Flush(cmd);
                cmd.BindVertexBuffers(0, [new BufferBinding(buffer.Handle, 0)]);
                cmd.Draw((uint)mesh.Count);
                Drawn++;
                any = true;
            }
            if (!any) continue;
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            foreach (var p in v.Footprint) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            boxes.Add((min, max));
        }
        cmd.EndRendering();
        cmd.Barrier(BarrierBatch.Full);
        gpu.EndNative(cmd);
        if (Drawn == 0) return;
        if (bindless is not { } known || known.Texture != target)
            bindless = (target, gpu.Bindless.Register(BindlessKind.Texture2D, new SampledTexture(Nearest(), target.View(), target.Image)));
        Active = true;
    }

    /// <summary>A mask mesh's triangles (positions, unindexed) on the GPU, read the first time it is asked for; null when it cannot be read.</summary>
    MaskMesh? Mesh(string recordPath)
    {
        if (meshes.TryGetValue(recordPath, out var known)) return known;
        var mesh = new MaskMesh();
        meshes[recordPath] = mesh;
        try
        {
            if (objects.FindRecordFile(recordPath) is not { } path) return mesh;
            var ogre = OgreMeshReader.ReadFile(path);
            var data = new List<float>();
            foreach (var sub in ogre.SubMeshes)
            {
                if (sub.Operation != OgreOperationType.TriangleList || (sub.VertexData ?? ogre.SharedVertexData) is not { } v || v.ReadPositions() is not { } positions) continue;
                var indices = sub.Indices.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        var p = positions[indices[i + k]];
                        data.AddRange([p.X, p.Y, p.Z]);
                    }
            }
            if (data.Count == 0) return mesh;
            mesh.Vertices = DeviceBuffer.Create(gpu, sizeof(float) * (ulong)data.Count, BufferUse.Vertex, "interior mask mesh");
            using (var batch = gpu.Uploads.Begin()) batch.Write(mesh.Vertices, 0, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data)));
            mesh.Count = data.Count / 3;
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Console.Error.WriteLine($"interior mask {recordPath} skipped: {e.Message}");
        }
        return mesh;
    }

    Silk.NET.Vulkan.Sampler Nearest() => gpu.Samplers.Get(SamplerDesc.FromGl(TextureMinFilter.Nearest, TextureMagFilter.Nearest, TextureWrapMode.ClampToEdge,
        TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, false, DepthFunction.Lequal, false, 1, false, 0));

    public void Dispose()
    {
        if (bindless is { } b) gpu.Bindless.Free(BindlessKind.Texture2D, b.Index);
        target?.Dispose();
        foreach (var m in meshes.Values) m.Vertices?.Dispose();
    }
}
