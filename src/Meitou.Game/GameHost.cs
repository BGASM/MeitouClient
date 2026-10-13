using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Meitou.Content;
using Meitou.Engine;
using Meitou.Engine.Cameras;
using Meitou.Engine.Input;
using Meitou.Engine.Time;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Rendering.Gpu;
using Meitou.Rendering.Display;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using EngineKey = Meitou.Engine.Input.Key;
using EngineButton = Meitou.Engine.Input.MouseButton;
using SilkKey = Silk.NET.Input.Key;
using SilkButton = Silk.NET.Input.MouseButton;

namespace Meitou.Game;

/// <summary>
/// The game loop: a real-time control tick (<see cref="FixedStepClock"/>, 30 Hz by default) runs the input actions, the camera rig
/// and pause/speed; a game-time simulation tick (<see cref="SimulationClock"/>) advances the game clock; each displayed frame draws the camera interpolated between the last two ticks at the display rate.
/// </summary>
sealed partial class GameHost(GameInstall install, WorldScene scene, AssetLocator assets, WorldOptions o, GameOptions g, UserConfig config, Meitou.Data.Gameplay.NewGameStart? start)
{
    WorldSession session = null!;   // disposed with the host (Run)
    Meitou.Simulation.PopulationSystem? population;
    // The game data the world was built from, kept to build the next world of a quickload (GameHost.Saving.cs).
    Meitou.Simulation.PopulationData? popData;
    IReadOnlyList<Meitou.Data.World.TownPlacement> townPlacements = [];
    Meitou.Simulation.IWalkability? simWalkability;
    Meitou.Navigation.NavSystem? nav;
    volatile bool navChanged;
    readonly Dictionary<Meitou.Data.World.ZoneCoordinate, long> navZones = [];
    PlayerInterface player = null!;
    Meitou.Simulation.Squad? playerSquad;
    WorldFrame.Gpu gpu = null!;
    WorldCamera camera = null!;
    WorldRenderOptions render = null!;
    readonly Stopwatch realTime = Stopwatch.StartNew();
    Meitou.Rendering.Upscalers.Streamline? streamline;
    bool screenshotRequested;
    // The interactive run's animation clock (foliage sway, clouds, water): the frame steps, which are exact while the cinema records; off, the real time.
    double animSeconds;
    bool animClock;
    // Draw-list state (GameHost.Characters.cs): the previous snapshot indexed by id, and the last camera focus sent to the world.
    WorldSnapshot? indexedFor;
    readonly Dictionary<CharacterId, CharacterSnapshot> previousById = [];
    Meitou.Data.Gameplay.AnimationLengths? animationLengths;
    Meitou.Data.Gameplay.AnimationLibrary? animationLibrary;
    Vector3 lastFocus;
    bool sentFocus;
    // Profile of the interactive run (printed by --quit-after).
    double advanceTotal, fillTotal;
    long fillCalls;

    public int Run()
    {
        try { return o.Screenshot is not null ? Screenshot() : Interactive(); }
        finally { session?.Dispose(); nav?.Dispose(); }
    }

    void Boot(VulkanDisplay display, bool interactive)
    {
        var context = display.Context;
        o.Day ??= (int)GameClock.DefaultStartDay;   // the weather schedule starts on the clock's first day (--day moves both)
        // --crowd in the game: simulated characters (SpawnCrowd), not the viewer's posed harness, which would take the renderer's place.
        crowd = o.Crowd;
        o.Crowd = 0;
        gpu = WorldFrame.CreateGpu(context, install, scene, assets, o, interactive);
        if (gpu.Post is { } vendorPost) vendorPost.UpscalerFactory = Meitou.Rendering.Upscalers.VendorUpscalers.Factory(display.Context, streamline);
        (camera, render) = WorldFrame.Setup(scene, o);
        render.Grid = IsSandbox;
        // The simulation samples the CPU heightmap (immutable, any thread), not the renderer's terrain.
        var heights = new Meitou.Data.World.GroundHeights(scene.Window, scene.Coarse, scene.CoarseSize, WorldFrame.CoarseStep);
        Meitou.Simulation.IWalkability walkability = new Meitou.Simulation.OpenGroundWalkability(heights.HeightAt);
        IReadOnlyList<Meitou.Simulation.ITickSystem> systems = [];
        Meitou.Data.Gameplay.GameConstants? constants = null;   // parsed once: the body factory's, else for the clock alone
        if (!g.NoPopulation && scene.Database is { } gameDb)
        {
            var levels = IsSandbox ? null : scene.Objects?.Levels ?? Meitou.Data.World.WorldLevelData.Load(install);
            if (!IsSandbox)
            {
                nav = new Meitou.Navigation.NavSystem(install, gameDb, levels!, heights.HeightAt);
                nav.ZoneReady += _ => navChanged = true;
                walkability = new NavAdapter(nav.Walkability);
            }
            var towns = levels is null ? [] : levels.Towns().ToList();
            var data = Meitou.Simulation.PopulationData.Create(gameDb, towns, new Meitou.Simulation.GeneratedAppearances(gameDb, install.Root));
            animationLengths = Meitou.Data.Gameplay.AnimationLengths.Load(install.Root);
            animationLibrary = Meitou.Data.Gameplay.AnimationLibrary.FromDatabase(gameDb);
            popData = data;
            townPlacements = towns;
            simWalkability = walkability;
            var built = BuildSystems(interactive);
            population = built.Population;
            if (nav is not null) population!.ZoneGate = zone => nav.Walkability.World.Contains(zone);
            systems = built.Systems;
            constants = data.Bodies.Constants;
        }
        session = new WorldSession(scene.Focus, (scene.X0, scene.Z0, scene.X1, scene.Z1), gpu.Terrain.HeightAt, o.Hour, config.TickRate,
            clock: GameClockFor(constants ??= scene.Database is { } clockDb ? Meitou.Data.Gameplay.GameConstants.FromDatabase(clockDb) : null, o.Hour, o.Day ?? GameClock.DefaultStartDay),
            simulation: new Meitou.Simulation.WorldSettings { Seed = g.Seed, MinPartitionSize = 128, Threads = Math.Max(1, config.SimThreads ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 8)) },
            systems: systems, walkability: walkability);
        var target = camera.Target;
        foreach (var problem in session.Bindings.Apply(config.Bindings)) Console.Error.WriteLine($"config    binding skipped: {problem}");
        // Right is the command button now; older saved configs bound it to orbit.
        session.Bindings.Set(InputAction.Orbit, [.. session.Bindings.Get(InputAction.Orbit).Where(b => !(b.IsMouse && b.Button == EngineButton.Right))]);
        // F12 is the profiler now (as in the viewer); older saved configs took it for the screenshot.
        if (session.Bindings.Get(InputAction.Screenshot).SequenceEqual([Binding.Of(EngineKey.F12)])) session.Bindings.Set(InputAction.Screenshot, EngineKey.F8, EngineKey.PrintScreen);
        // F9 is the game's quickload now (F5 quicksave); older saved configs took F9 for the fps counter, which is on Ctrl+F9 and unbound here.
        if (session.Bindings.Get(InputAction.ToggleFps).SequenceEqual([Binding.Of(EngineKey.F9)])) session.Bindings.Set(InputAction.ToggleFps);
        player = new PlayerInterface(session, heights.HeightAt, DrawnPosition);
        if (nav is not null && (start is not null || !interactive))
        {
            // The squad is placed on the mesh, and a still has its town at once: wait for the zones round the start (a cold build is about 2 s, then cached).
            var watch = Stopwatch.StartNew();
            var zones = Meitou.Simulation.ZoneActivation.ZonesAround(scene.Focus, 0);
            Task.WaitAll([.. zones.Select(z => (Task)nav.LoadZone(z))]);
            foreach (var z in zones) navZones[z] = Stopwatch.GetTimestamp();
            Console.WriteLine($"navmesh   {zones.Count} zone(s) round the start ready in {watch.ElapsedMilliseconds} ms");
        }
        if (start is not null && population is not null)
        {
            playerSquad = population.StartPlayer(session.World, start);
            var lead = session.World.Characters.Previous[playerSquad.Leader.Slot].Position;
            target = lead;
            Console.WriteLine($"player    {playerSquad.Members.Count} characters, leader at {lead.X:0}, {lead.Z:0}");
        }
        if (IsSandbox && population is not null) target = SandboxSpawn();
        if (!IsSandbox && crowd > 0 && population is not null) SpawnCrowd(target, crowd);
        if (scene.Database is { } characterDb && !g.NoPopulation && gpu.Characters is null)
            gpu.Characters = new CharacterRenderer(context, install, characterDb, assets) { Source = FillDrawList, Guard = gpu.Guard };
        var rig = session.Camera;
        rig.Place(new Vector2(target.X, target.Z), (o.Yaw ?? 30) * MathF.PI / 180,
            (o.Pitch ?? Meitou.Data.World.KenshiCamera.InitialPitchDegrees) * MathF.PI / 180, o.Distance ?? Meitou.Data.World.KenshiCamera.InitialDistance);
        SendFocus();
        ApplyCamera(session.Camera.Current);
        if (interactive) WorldFrame.FinishLoading(context);
    }

    /// <summary>The game clock with sunrise, sunset and days per year from the CONSTANTS record (defaults without data).</summary>
    static GameClock GameClockFor(Meitou.Data.Gameplay.GameConstants? c, double startHour, long startDay)
    {
        if (c is null) return new GameClock(startHour, startDay);
        return new GameClock(startHour, startDay, sunrise: c.Sunrise, sunset: c.Sunset, daysPerYear: c.DaysPerYear);
    }

    /// <summary>DLSS asked for, on the command line or in the saved settings: Streamline must be loaded before the Vulkan device.</summary>
    bool WantsDlss() => o.Post.Upscale.Kind == UpscalerKind.Dlss || !o.Post.Upscale.Explicit && config.Graphics.TryGetValue(WorldFrame.UpscalerSliders[0], out float k) && WorldFrame.AntiAliasingKind(k) == UpscalerKind.Dlss;

    void ApplyCamera(CameraState s)
    {
        camera.Target = s.Target;
        camera.Yaw = s.Yaw;
        camera.Pitch = s.Pitch;
        camera.Distance = s.Distance;
    }

    /// <summary>Keeps the navmesh on the zones the population has active (built on the nav thread, cached), drops the ones idle for 30 s, and has characters ask for their paths again when a mesh arrived (their first path may have crossed a building).</summary>
    void UpdateNav(bool wait = false)
    {
        if (nav is null || population is null) return;
        long now = Stopwatch.GetTimestamp();
        var pending = new List<Task>();
        foreach (var z in population.ActiveZones)
        {
            if (!navZones.ContainsKey(z)) pending.Add(nav.LoadZone(z));
            navZones[z] = now;
        }
        foreach (var (z, seen) in navZones.ToList())
            if (Stopwatch.GetElapsedTime(seen, now).TotalSeconds > 30) { nav.UnloadZone(z); navZones.Remove(z); }
        if (wait) Task.WaitAll([.. pending]);
        if (navChanged || wait && pending.Count > 0)
        {
            navChanged = false;
            session.World.Commands.Enqueue(new Meitou.Simulation.RepathCommand { Tick = session.World.Tick });
        }
    }

    /// <summary>Gives the player interface the camera it picks with.</summary>
    void UpdateView(int width, int height)
    {
        player.ViewProjection = camera.View * camera.Projection(width / (float)Math.Max(height, 1), camera.Near, camera.ViewDistance);
        player.Eye = camera.Eye;
        player.Width = width;
        player.Height = height;
    }

    void DrawWorld(int width, int height)
    {
        double seconds = animClock ? animSeconds : realTime.Elapsed.TotalSeconds;
        if (gpu.Foliage is { } foliage && o.Screenshot is null) foliage.SwaySeconds = seconds;
        // The heat haze's gameTime: game hours since the start (it stops while paused, as in the game).
        gpu.GameHours = session.Clock.HoursSinceStart;
        // The weather's three time bases: the game day and time of day (the hour below), the game speed (the last non-zero one while paused) and the paused flag.
        // TODO(save): the regions' schedules are not saved yet (WeatherWorld.Snapshot() / Restore() exist; the save format has no place for them), so a loaded game rolls the weather anew.
        if (gpu.Weather is { } weather)
        {
            var sim = session.Simulation;
            weather.Day = (int)session.Clock.Day;
            weather.Paused = sim.IsPaused;
            weather.GameSpeed = (float)(sim.Speed > 0 ? sim.Speed : sim.LastNonZeroSpeed);
        }
        WorldFrame.Draw(gpu, scene, camera, render, width, height, (float)session.Clock.HourOfDay, (float)seconds / 600f, o.FogDistance);
    }

}
