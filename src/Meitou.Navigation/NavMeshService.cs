using System.Collections.Concurrent;
using System.Diagnostics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.World;

namespace Meitou.Navigation;

/// <summary>Where a zone's mesh came from.</summary>
public enum NavMeshOrigin { Cache, Built }

/// <summary>What a worker reports for one finished zone.</summary>
public sealed record NavZoneReady(ZoneCoordinate Zone, NavMeshOrigin Origin, double Milliseconds, int Polygons);

/// <summary>
/// Builds zone meshes off the simulation thread and publishes them to a <see cref="NavmeshWalkability"/> (docs/simulation.md: path queries and navmesh
/// building run on their own threads). A finished mesh goes live at once, whenever that is: the walkability's world is swapped atomically, so a query
/// sees the old world or the new one, and the simulation can see a zone appear in the middle of a tick.
/// <see cref="Request"/> queues a zone; a worker takes it from the cache (keyed by the zone's building hash and the settings) or gathers, builds,
/// prunes and caches it. Several zones queued together are built one after another, each using all cores.
/// </summary>
internal sealed class NavMeshService : IDisposable
{
    readonly GameInstall install;
    readonly GameDatabase db;
    readonly WorldLevelData levels;
    readonly CollisionCache collisions;
    readonly NavRuntimeBuildings? runtime;
    // Bumped by Rebuild: a zone whose build started before the bump is queued again after it publishes (its gather may have missed the change).
    readonly ConcurrentDictionary<ZoneCoordinate, int> rebuilds = new();
    readonly BlockingCollection<ZoneCoordinate> queue = [];
    readonly ConcurrentDictionary<ZoneCoordinate, TaskCompletionSource<NavZoneReady>> pending = new();
    readonly Thread worker;
    readonly object publishLock = new();
    volatile bool disposed;

    public NavMeshService(GameInstall install, GameDatabase db, WorldLevelData levels, NavmeshWalkability walkability, NavBuildSettings? settings = null, NavMeshCache? cache = null,
        NavRuntimeBuildings? runtime = null)
    {
        this.install = install;
        this.db = db;
        this.levels = levels;
        this.runtime = runtime;
        Walkability = walkability;
        Settings = settings ?? new NavBuildSettings();
        Cache = cache ?? new NavMeshCache();
        collisions = new CollisionCache(install);
        worker = new Thread(Work) { Name = "NavMeshBuilder", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        worker.Start();
    }

    public NavmeshWalkability Walkability { get; }
    public NavBuildSettings Settings { get; }
    public NavMeshCache Cache { get; }

    /// <summary>Raised on the worker thread after a zone's mesh was published.</summary>
    public event Action<NavZoneReady>? ZoneReady;

    /// <summary>Queues a zone (once); the task completes when its mesh is published.</summary>
    public Task<NavZoneReady> Request(ZoneCoordinate zone)
    {
        if (Walkability.World.Find(zone) is { } loaded) return Task.FromResult(new NavZoneReady(zone, NavMeshOrigin.Cache, 0, loaded.PolygonCount));
        var tcs = new TaskCompletionSource<NavZoneReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = pending.GetOrAdd(zone, tcs);
        if (ReferenceEquals(existing, tcs)) queue.Add(zone);
        return existing.Task;
    }

    /// <summary>
    /// Builds a zone again although its mesh is loaded (the buildings round it changed, <see cref="NavRuntimeBuildings"/>): the new mesh replaces the old one when it is
    /// published, which is when the task completes. A zone not loaded is not built (it is built with the overlay when it is asked for).
    /// </summary>
    public Task<NavZoneReady> Rebuild(ZoneCoordinate zone)
    {
        rebuilds.AddOrUpdate(zone, 1, (_, n) => n + 1);
        var tcs = new TaskCompletionSource<NavZoneReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = pending.GetOrAdd(zone, tcs);
        if (ReferenceEquals(existing, tcs)) queue.Add(zone);
        return existing.Task;
    }

    /// <summary>Drops a zone's mesh from the loaded world (the cache file stays).</summary>
    public void Unload(ZoneCoordinate zone)
    {
        lock (publishLock) Walkability.SetWorld(Walkability.World.Without(zone));
    }

    void Work()
    {
        using var gatherer = new ZoneGeometryGatherer(install, db, levels, collisions, runtime);
        uint settingsHash = NavMeshCache.SettingsHash(Settings);
        foreach (var zone in queue.GetConsumingEnumerable())
        {
            if (disposed) break;
            var watch = Stopwatch.StartNew();
            try
            {
                int rebuildVersion = rebuilds.GetValueOrDefault(zone);
                uint hash = gatherer.BuildingHash(zone);
                var mesh = Cache.TryLoad(zone.X, zone.Y, hash, settingsHash);
                var origin = NavMeshOrigin.Cache;
                if (mesh is null)
                {
                    var geometry = gatherer.Gather(zone, buildingHash: hash);
                    geometry.Seeds.AddRange(NeighbourSeeds.Collect(Walkability.World, zone, geometry));
                    mesh = NavMeshPipeline.BuildZone(gatherer, geometry, Settings, out _).WithoutPruned();
                    origin = NavMeshOrigin.Built;
                    try { Cache.Save(mesh, hash, settingsHash); }
                    catch (IOException) { /* a read-only or full disk only costs the next build */ }
                }
                mesh.Terrain = gatherer.ZoneHeights(zone);
                lock (publishLock) Walkability.SetWorld(Walkability.World.With(mesh));
                var ready = new NavZoneReady(zone, origin, watch.Elapsed.TotalMilliseconds, mesh.PolygonCount);
                if (pending.TryRemove(zone, out var tcs)) tcs.TrySetResult(ready);
                ZoneReady?.Invoke(ready);
                // The buildings changed while this one was built: it may have missed it.
                if (rebuilds.GetValueOrDefault(zone) != rebuildVersion) _ = Rebuild(zone);
            }
            catch (Exception e)
            {
                if (pending.TryRemove(zone, out var tcs)) tcs.TrySetException(e);
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        queue.CompleteAdding();
        worker.Join(TimeSpan.FromSeconds(30));
        foreach (var tcs in pending.Values) tcs.TrySetCanceled();
    }
}
