using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Meitou.Simulation.Ragdolls;

/// <summary>What a body is to the collision filter: its ragdoll (a running number, 0 for none) and its index there, and the bodies of the same ragdoll joined to it.</summary>
internal struct BodyTag
{
    public int Ragdoll;
    public int Index;
    public ulong Joined;
}

/// <summary>
/// The contact rules (docs/game/ragdolls.md, "Collision groups" and the materials): ragdolls collide with each other, with the ground and the buildings, but two bodies of one
/// ragdoll joined by a joint do not (no shipped joint turns that on). Friction 1 as the game's ragdoll material; its restitution of 0.05 is left out (Bepu has none).
/// </summary>
internal struct ContactRules : INarrowPhaseCallbacks
{
    public CollidableProperty<BodyTag> Tags;

    public void Initialize(BepuPhysics.Simulation simulation) => Tags.Initialize(simulation);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic) return false;
        if (a.Mobility == CollidableMobility.Static || b.Mobility == CollidableMobility.Static) return true;
        ref var ta = ref Tags[a.BodyHandle];
        ref var tb = ref Tags[b.BodyHandle];
        return ta.Ragdoll != tb.Ragdoll || ta.Ragdoll == 0 || tb.Index >= 64 || (ta.Joined & (1UL << tb.Index)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = new PairMaterialProperties(RagdollPhysics.Friction, RagdollPhysics.RecoverySpeed, new SpringSettings(30, 1));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;

    public void Dispose() => Tags.Dispose();
}

/// <summary>Gravity and the damping of a simulated ragdoll body (docs/game/ragdolls.md: linear 0.05, angular 0.01 per second, gravity 98.1 units/s² down).</summary>
internal struct Integration : IPoseIntegratorCallbacks
{
    Vector3Wide gravityDt;
    Vector<float> linear, angular;

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(BepuPhysics.Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        gravityDt = Vector3Wide.Broadcast(new Vector3(0, -RagdollPhysics.Gravity * dt, 0));
        linear = new Vector<float>(1 / (1 + RagdollPhysics.LinearDamping * dt));
        angular = new Vector<float>(1 / (1 + RagdollPhysics.AngularDamping * dt));
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask,
        int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        velocity.Linear = (velocity.Linear + gravityDt) * linear;
        velocity.Angular *= angular;
    }
}

/// <summary>
/// The physics scene of the ragdolls: a Bepu simulation stepped on the calling thread only (no thread dispatcher, so the result is the same whatever the world's thread
/// count), the ragdoll bodies, and static ground and building tiles made around the moving bodies. The scene settings follow the game's PhysX scene where Bepu has a match
/// (docs/game/ragdolls.md, "Middleware and scene").
/// </summary>
internal sealed class RagdollPhysics : IDisposable
{
    public const float Gravity = 98.1f, LinearDamping = 0.05f, AngularDamping = 0.01f, Friction = 1;
    /// <summary>The largest speed a contact pushes bodies apart with (engine choice: 2 m/s).</summary>
    public const float RecoverySpeed = 20;
    /// <summary>Solver substeps per tick and velocity iterations per substep: 4 substeps at 30 Hz is 120 Hz, finer than PhysX's 1/60 s maximum step (engine choice).</summary>
    const int Substeps = 4, Iterations = 2;

    public readonly BufferPool Pool = new();
    public readonly BepuPhysics.Simulation Simulation;
    readonly CollidableProperty<BodyTag> tags;

    public RagdollPhysics()
    {
        tags = new CollidableProperty<BodyTag>(Pool);
        Simulation = BepuPhysics.Simulation.Create(Pool, new ContactRules { Tags = tags }, new Integration(), new SolveDescription(Iterations, Substeps));
        Simulation.Deterministic = true;
    }

    public void Step(float dt) => Simulation.Timestep(dt);

    public ref BodyTag Tag(BodyHandle body) => ref tags[body];

    public BodyHandle AddBody(in BodyDescription description, BodyTag tag)
    {
        var handle = Simulation.Bodies.Add(description);
        tags.Allocate(handle) = tag;
        return handle;
    }

    // ---- static tiles ----

    /// <summary>Tile edge and ground sample spacing in world units (engine choice: a 32-unit tile of 2-unit cells, 512 triangles).</summary>
    public const float TileSize = 32, Cell = 2;
    /// <summary>A tile no moving body has needed for this many ticks is removed.</summary>
    const int TileLife = 90;

    sealed class Tile
    {
        public StaticHandle Ground, Buildings;
        public TypedIndex GroundShape, BuildingShape;
        public bool HasBuildings;
        public long Used;
    }

    readonly Dictionary<(int X, int Z), Tile> tiles = [];
    readonly List<(int X, int Z)> stale = [];
    readonly List<Vector3> gathered = [];

    public int TileCount => tiles.Count;

    /// <summary>Makes sure the tiles round <paramref name="centre"/> within <paramref name="reach"/> exist (ground from <paramref name="environment"/>), marked used at <paramref name="tick"/>.</summary>
    public void Cover(Vector3 centre, float reach, IRagdollEnvironment environment, long tick)
    {
        int x0 = (int)MathF.Floor((centre.X - reach) / TileSize), x1 = (int)MathF.Floor((centre.X + reach) / TileSize);
        int z0 = (int)MathF.Floor((centre.Z - reach) / TileSize), z1 = (int)MathF.Floor((centre.Z + reach) / TileSize);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!tiles.TryGetValue((x, z), out var tile)) tiles[(x, z)] = tile = MakeTile(x, z, centre.Y, environment);
                tile.Used = tick;
            }
    }

    /// <summary>Removes the tiles not used for a while.</summary>
    public void Sweep(long tick)
    {
        stale.Clear();
        foreach (var (key, tile) in tiles) if (tick - tile.Used > TileLife) stale.Add(key);
        stale.Sort();
        foreach (var key in stale)
        {
            RemoveTile(tiles[key]);
            tiles.Remove(key);
        }
    }

    Tile MakeTile(int tx, int tz, float nearY, IRagdollEnvironment environment)
    {
        const int n = (int)(TileSize / Cell);
        var origin = new Vector3(tx * TileSize, 0, tz * TileSize);
        var heights = new float[(n + 1) * (n + 1)];
        for (int i = 0; i <= n; i++)
            for (int k = 0; k <= n; k++)
                heights[i * (n + 1) + k] = environment.GroundHeight(origin.X + i * Cell, origin.Z + k * Cell, nearY);
        Pool.Take<Triangle>(n * n * 2, out var triangles);
        int t = 0;
        for (int i = 0; i < n; i++)
            for (int k = 0; k < n; k++)
            {
                Vector3 P(int a, int b) => new(a * Cell, heights[a * (n + 1) + b], b * Cell);
                // Wound so the face looks up (Bepu's triangles collide on one side).
                triangles[t++] = new Triangle(P(i, k), P(i + 1, k), P(i, k + 1));
                triangles[t++] = new Triangle(P(i + 1, k), P(i + 1, k + 1), P(i, k + 1));
            }
        var tile = new Tile();
        tile.GroundShape = Simulation.Shapes.Add(new Mesh(triangles, Vector3.One, Pool));
        tile.Ground = Simulation.Statics.Add(new StaticDescription(origin, Quaternion.Identity, tile.GroundShape));
        gathered.Clear();
        environment.Buildings(origin, origin + new Vector3(TileSize, 0, TileSize), gathered);
        if (gathered.Count >= 3)
        {
            // Both faces of every triangle: the files' windings are not trusted, and a body may meet a wall or floor from either side.
            int count = gathered.Count / 3;
            Pool.Take<Triangle>(count * 2, out var building);
            for (int i = 0; i < count; i++)
            {
                Vector3 a = gathered[3 * i] - origin, b = gathered[3 * i + 1] - origin, c = gathered[3 * i + 2] - origin;
                building[2 * i] = new Triangle(a, b, c);
                building[2 * i + 1] = new Triangle(a, c, b);
            }
            tile.BuildingShape = Simulation.Shapes.Add(new Mesh(building, Vector3.One, Pool));
            tile.Buildings = Simulation.Statics.Add(new StaticDescription(origin, Quaternion.Identity, tile.BuildingShape));
            tile.HasBuildings = true;
        }
        return tile;
    }

    void RemoveTile(Tile tile)
    {
        Simulation.Statics.Remove(tile.Ground);
        Simulation.Shapes.RemoveAndDispose(tile.GroundShape, Pool);
        if (!tile.HasBuildings) return;
        Simulation.Statics.Remove(tile.Buildings);
        Simulation.Shapes.RemoveAndDispose(tile.BuildingShape, Pool);
    }

    public void Dispose()
    {
        Simulation.Dispose();
        Pool.Clear();
    }
}

/// <summary>
/// What ragdolls fall onto: the ground height (the terrain, or a floor near <c>nearY</c>) and the collision triangles of the buildings in a box. The host gives the real terrain
/// and the buildings' collision files; without one, <see cref="WalkabilityEnvironment"/> uses the world's walkable ground and no buildings.
/// </summary>
public interface IRagdollEnvironment
{
    float GroundHeight(float x, float z, float nearY);
    /// <summary>Adds the triangles (three points each, world space, any winding) of the building collision between <paramref name="min"/> and <paramref name="max"/> (X and Z).</summary>
    void Buildings(Vector3 min, Vector3 max, List<Vector3> triangles);
}

/// <summary>The stand-in environment: the world's walkable ground, no buildings.</summary>
public sealed class WalkabilityEnvironment(IWalkability walkability) : IRagdollEnvironment
{
    public float GroundHeight(float x, float z, float nearY) => walkability.GroundHeight(x, z, nearY);
    public void Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) { }
}
