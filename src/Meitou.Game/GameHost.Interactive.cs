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

sealed partial class GameHost
{
    int Interactive()
    {
        bool vsync = g.VSync ?? config.VSync;
        int fpsLimit = g.FpsLimit ?? config.FpsLimit;
        VulkanDisplay.RayTracing = o.WantsRayTracing;
        using var display = new VulkanDisplay(WindowOptions.Default with
        {
            Size = new Vector2D<int>(o.Width, o.Height),
            Title = "Meitou",
            WindowState = WindowState.Maximized,
            FramesPerSecond = 0,
            UpdatesPerSecond = 0,
        }, vsync, streamline: WantsDlss());
        streamline = display.Streamline;
        var window = display.Window!;
        // Borderless full screen as the viewer starts (Alt+Enter and the Tab panel switch it); --windowed keeps the window for this run.
        bool fullScreen = config.FullScreen && !o.Windowed;
        if (fullScreen) BorderlessFullScreen.Toggle(window);
        Boot(display, interactive: true);
        var overlay = DebugOverlay.TryCreate(display.Context);
        // The viewer's debug overlays (docs/engine.md): F10 the key list, F11 the frame statistics, F12 the profiler chart.
        var profiler = overlay is null ? null : new FrameProfiler(display.Context, () => display.Context.GpuFrameMs);
        // The Faithful / Meitou switches; Shift+F1 turns Meitou off and back on (plain F2..F4 and F8 are the game's speed and screenshot keys), the Tab panel sets them one by one.
        var switches = WorldFrame.LiveSwitches(o, () => gpu, () => camera, () => render);
        var meitou = new MeitouToggle(switches);
        // The viewer's panel: the game clock's time of day, vsync and full screen on top of the graphics sliders.
        var panel = overlay is null ? null : WorldFrame.CreateSettingsPanel(overlay, gpu, render,
            getHour: () => (float)session.Clock.HourOfDay, setHour: v => { session.Clock.SetHourOfDay(v); if (IsDuel) duelHour = v; },
            getVSync: () => display.VSync, setVSync: v => display.VSync = v, switches: switches,
            getFullScreen: () => BorderlessFullScreen.IsOn(window), setFullScreen: _ => BorderlessFullScreen.Toggle(window));
        if (panel is not null)
            foreach (var slider in panel.Sliders)
                if (!SessionSliders.Contains(slider.Label) && config.Graphics.TryGetValue(slider.Label, out float v) && !(o.Post.Upscale.Explicit && WorldFrame.UpscalerSliders.Contains(slider.Label)))
                    slider.Set(Math.Clamp(v, slider.Min, slider.Max));
        // The switches as saved, unless the command line chose them (--meitou / --faithful).
        if (panel is not null && !Environment.GetCommandLineArgs().Any(a => a is "--meitou" or "--faithful"))
            foreach (var toggle in panel.Toggles)
                if (config.Graphics.TryGetValue(SwitchKey(toggle.Label), out float v) && (v >= 0.5f) != toggle.Get()) toggle.Set(v >= 0.5f);
        var keyItems = DebugOverlay.KeyItems(GameOptions.Usage);
        var stats = new List<string>();
        bool statsVisible = false, fpsVisible = false;
        string fpsText = "";
        double fpsWindow = 0;
        int fpsWindowFrames = 0;
        // The cinematic camera (docs/viewer.md "Cinematic camera", F6 its editor): while it flies, previews or cuts to a key it holds the drawn
        // camera and the rig's state is not applied; the first camera input after that hands the camera back to the rig (CameraRig.TakeOver).
        // The hour is the game clock's (time does not follow the keys); PageUp/PageDown/Home set the lens while the editor is open and in the free camera.
        var cinema = new Cinema(() => camera, () => (float)session.Clock.HourOfDay, null, () => BorderlessFullScreen.IsOn(window), () => BorderlessFullScreen.Toggle(window),
            Cinema.GameFile, lensKeysAlways: false);
        bool cinemaHolds = false, interfaceHidden = false;
        cinema.Moved = () => cinemaHolds = true;
        // PageUp/PageDown zoom only the strategy camera: in the free camera and on a camera the cinema holds they set the lens (Home puts it back).
        cinema.LensKeys = () => session.Camera.IsFree || cinemaHolds;
        cinema.Load();

        var silkInput = Silk.NET.Input.InputWindowExtensions.CreateInput(window);
        Vector2? lastMouse = null;
        bool panelDrag = false, shiftDown = false;
        Vector2 Pixels(Vector2 p) => p * new Vector2(window.FramebufferSize.X / (float)Math.Max(window.Size.X, 1), window.FramebufferSize.Y / (float)Math.Max(window.Size.Y, 1));
        // The settings panel takes the mouse only while it is drawn (F7 and the cinema's clean picture hide it).
        bool PanelLive() => !interfaceHidden && !cinema.Clean;
        foreach (var kb in silkInput.Keyboards)
        {
            kb.KeyDown += (keyboard, k, _) =>
            {
                if (k is SilkKey.ShiftLeft or SilkKey.ShiftRight) shiftDown = true;
                if (k == SilkKey.Enter && (keyboard.IsKeyPressed(SilkKey.AltLeft) || keyboard.IsKeyPressed(SilkKey.AltRight))) { BorderlessFullScreen.Toggle(window); return; }
                // F7 hides the game's interface; F6 and the numpad are the cinema's (Space, Delete and the lens keys too while its editor is open).
                if (k == SilkKey.F7) { interfaceHidden = !interfaceHidden; Console.WriteLine(interfaceHidden ? "interface hidden (F7 shows it)" : "interface shown"); return; }
                bool ctrl = keyboard.IsKeyPressed(SilkKey.ControlLeft) || keyboard.IsKeyPressed(SilkKey.ControlRight);
                if (Enum.TryParse(k.ToString(), out CinemaKey cinemaKey) && cinema.OnKey(cinemaKey, ctrl, shiftDown)) return;
                // F9 is the game's quickload; the fps counter is on Ctrl+F9 (a modifier, so the key never reaches the input bindings).
                if (k == SilkKey.F9 && ctrl) { fpsVisible = !fpsVisible; return; }
                if (KeyMap.Map(k) is { } key)
                {
                    if (shiftDown && key == EngineKey.F1) { Console.WriteLine(meitou.Toggle()); return; }
                    session.Input.SetKey(key, true);
                    player.Key(key, shiftDown);
                    SandboxKey(key);
                    if (IsDuel && key == EngineKey.Backspace) duelRestartRequested = true;
                }
            };
            kb.KeyUp += (_, k, _) =>
            {
                if (k is SilkKey.ShiftLeft or SilkKey.ShiftRight) shiftDown = false;
                if (KeyMap.Map(k) is { } key) session.Input.SetKey(key, false);
            };
        }
        foreach (var mouse in silkInput.Mice)
        {
            mouse.MouseDown += (m, b) =>
            {
                var at = Pixels(m.Position);
                if (b == SilkButton.Left && PanelLive() && panel?.MouseDown(at) == true) { panelDrag = true; return; }
                if (PanelLive() && panel?.Contains(at) == true) return;
                // The cinema's editor: its clicks never select or move characters.
                if (b == SilkButton.Left && cinema.MouseDown(at)) return;
                if (cinema.Contains(at)) return;
                if (KeyMap.Map(b) is { } button)
                {
                    // Ctrl and Alt with the right button: pick up / put down and loot (docs/game/ui-input.md, Meitou's carry bindings).
                    bool Held(SilkKey a, SilkKey b) => silkInput.Keyboards.Any(k => k.IsKeyPressed(a) || k.IsKeyPressed(b));
                    if (player.MouseDown(button, at, shiftDown, Held(SilkKey.ControlLeft, SilkKey.ControlRight), Held(SilkKey.AltLeft, SilkKey.AltRight))) return;
                    session.Input.SetMouseButton(button, true);
                }
            };
            mouse.MouseUp += (m, b) =>
            {
                bool cinemaDrag = cinema.MouseUp();
                if (!cinemaDrag && KeyMap.Map(b) is { } released) player.MouseUp(released, Pixels(m.Position), shiftDown);
                panel?.MouseUp();
                panelDrag = false;
                if (KeyMap.Map(b) is { } button) session.Input.SetMouseButton(button, false);
            };
            mouse.MouseMove += (_, p) =>
            {
                session.Input.SetMousePosition(p.X, p.Y);
                if (cinema.MouseMove(Pixels(p))) { lastMouse = p; return; }
                player.MouseMove(Pixels(p));
                if (PanelLive() && panel?.MouseMove(Pixels(p)) == true || panelDrag) { lastMouse = p; return; }
                if (lastMouse is { } last) session.Input.AddMouseDelta(p.X - last.X, p.Y - last.Y);
                lastMouse = p;
            };
            mouse.Scroll += (m, wheel) => { if (!(PanelLive() && panel?.Contains(Pixels(m.Position)) == true) && !cinema.Contains(Pixels(m.Position))) session.Input.AddWheel(wheel.Y); };
        }
        Console.WriteLine(GameOptions.Usage[GameOptions.Usage.IndexOf("Keys:", StringComparison.Ordinal)..]);
        Console.WriteLine($"display   vsync {(vsync ? "on" : "off")}, frame limit {(vsync || fpsLimit <= 0 ? "none" : fpsLimit + " fps")}, control tick {session.Ticks.TickRate:0} Hz, world tick {SimulationClock.TickRate:0} per game second; settings in {config.Path}");

        var frame = Stopwatch.StartNew();
        double last = 0, titleTimer = 0, cpuSum = 0;
        int frames = 0, totalFrames = 0;
        bool quit = false;
        bool cinemaWasPlaying = false, cinemaWasRecording = false;
        session.Ticked += actions =>
        {
            if (actions.Pressed(InputAction.Quit)) quit = true;
            if (actions.Pressed(InputAction.ToggleSettings) && panel is not null) panel.Visible = !panel.Visible;
            if (actions.Pressed(InputAction.Screenshot)) screenshotRequested = true;
            if (actions.Pressed(InputAction.ToggleKeys) && overlay is not null) overlay.Visible = !overlay.Visible;
            if (actions.Pressed(InputAction.ToggleStats)) statsVisible = !statsVisible;
            if (actions.Pressed(InputAction.ToggleFps)) fpsVisible = !fpsVisible;
            if (actions.Pressed(InputAction.QuickSave)) quickSaveRequested = true;
            if (actions.Pressed(InputAction.QuickLoad)) quickLoadRequested = true;
            if (actions.Pressed(InputAction.CycleProfiler) && profiler is not null) profiler.Showing = (FrameProfiler.Mode)(((int)profiler.Showing + 1) % 3);
            // The camera the cinema left (a cut, a scrub, the end of a flight) goes back to the rig at the first camera input: the free camera at
            // that eye (only it can stand anywhere), or with ; the strategy camera where the view meets the ground. A flight in progress keeps it.
            if (cinemaHolds && !cinema.Playing && CameraInput(actions))
            {
                cinemaHolds = false;
                session.Camera.TakeOver(new CameraState(camera.Eye, camera.Forward, Vector3.UnitY, camera.Target, camera.Yaw, camera.Pitch, camera.Distance, true),
                    strategy: actions.Pressed(InputAction.ToggleFreeCamera));
            }
        };
        long windowRan = 0, windowDropped = 0;
        double drawTotal = 0, gpuSum = 0, statsTimer = 0;
        int gpuSamples = 0, statsFrames = 0;
        long statsTicks = 0;
        while (!window.IsClosing && !quit)
        {
            window.DoEvents();
            if (window.IsClosing) break;
            double now = frame.Elapsed.TotalSeconds, dt = now - last;
            last = now;
            // The counter: the mean over the last tenth of a second, so a single late frame doesn't read as a drop.
            fpsWindow += dt;
            fpsWindowFrames++;
            if (fpsWindow >= 0.1)
            {
                fpsText = $"{fpsWindowFrames / fpsWindow:0} fps";
                fpsWindow = 0;
                fpsWindowFrames = 0;
            }
            // While the cinema records, every frame is exactly one frame of its rate, for the simulation too (the take plays at game speed
            // however slowly the frames are drawn and saved); otherwise the real time.
            double frameDt = cinema.Recording ? cinema.FrameTime(dt) : dt;
            // The duel starts a lap when a flight starts (played or recorded), so every take begins with the fight's first frame, and when its lap is over.
            DuelFrame(cinema.Playing && !cinemaWasPlaying || cinema.Recording && !cinemaWasRecording);
            (cinemaWasPlaying, cinemaWasRecording) = (cinema.Playing, cinema.Recording);
            long a0 = Stopwatch.GetTimestamp();
            session.Advance(frameDt);
            DuelHoldClock();
            advanceTotal += Stopwatch.GetElapsedTime(a0).TotalMilliseconds;
            WorldNews();
            HandleSaves();   // a quicksave or quickload asked for by a key: between ticks, none in flight
            animSeconds += frameDt;
            animClock = true;
            UpdateNav();
            if (!cinemaHolds) ApplyCamera(session.CameraAt());
            cinema.Advance(cinema.FrameTime(dt));   // a flight through the keys moves the camera (and takes it from the rig)
            string? takeFile = cinema.TakeFrame();
            SendFocus(cinemaHolds ? camera.Target : null);
            if (!cinemaHolds) SandboxFollowCamera();
            var size = window.FramebufferSize;
            UpdateView(size.X, size.Y);
            if (display.BeginFrame(size.X, size.Y))
            {
                long t0 = Stopwatch.GetTimestamp();
                // The chain and the panel draw into the window's backbuffer (made again when the size changes).
                var backbuffer = display.Backbuffer!;
                gpu.Post!.Target = backbuffer;
                if (overlay is not null) overlay.Target = backbuffer;
                profiler?.BeginFrame();
                DrawWorld(size.X, size.Y);
                cpuSum += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                drawTotal += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                bool shot = screenshotRequested, take = takeFile is not null;
                // The game's interface (markers, HUD, panels, debug overlays): hidden by F7, the cinema's clean picture and in recorded frames.
                bool ui = !interfaceHidden && !cinema.Clean && !take;
                screenshotRequested = false;
                if (ui && overlay is not null && !g.NoPopulation) DrawMarkers(overlay, size.X, size.Y);
                if (ui && overlay is not null && IsSandbox) DrawSandbox(overlay, size.X, size.Y);
                if (!g.NoPopulation) player.Steer();
                if (ui && overlay is not null && !g.NoPopulation) player.Draw(overlay, size.X, size.Y);
                // The debug overlays are left out of saved pictures: the statistics at the top left, the key list below them, the profiler.
                if (ui && overlay is not null && !shot)
                {
                    // Below the HUD, which holds the top left corner in the game (the viewer's overlays start at the top).
                    float panelsBottom = g.NoPopulation ? 0 : player.HudBottom;
                    if (fpsVisible && fpsText.Length > 0)
                    {
                        float w = fpsText.Length * overlay.CharWidth + 12, h = overlay.LineHeight + 8, top = panelsBottom + 8;
                        overlay.Rect(8, top, 8 + w, top + h, DebugOverlay.PanelColour);
                        overlay.Text(fpsText, 14, top + 4, DebugOverlay.TextColour);
                        overlay.Flush(size.X, size.Y);
                        panelsBottom = top + h - 8;   // the panels add their own 16 px margin
                    }
                    if (statsVisible && stats.Count > 0) panelsBottom = overlay.Panel(size.X, size.Y, "Meitou   (F11 hides this)", stats, panelsBottom);
                    if (overlay.Visible)
                    {
                        int width = keyItems.Max(i => i.Length) + 2;
                        var lines = keyItems.Select(item => KeyState(item.Split(' ')[0]) is { } state ? item.PadRight(width) + state : item).ToList();
                        overlay.Draw(size.X, size.Y, "Keys   (F10 hides this)", lines, panelsBottom);
                    }
                    profiler?.Draw(overlay, size.X, size.Y);
                }
                if (ui) panel?.Draw(size.X, size.Y);
                // Over everything: the letterbox, and the cinema's editor (F6, also with F7) or its status line (not with F7).
                if (overlay is not null) cinema.Draw(overlay, size.X, size.Y, shot || take || (interfaceHidden && !cinema.EditorVisible));
                display.Present();
                profiler?.EndFrame();
                if (display.Context.GpuFrameMs > 0) { gpuSum += display.Context.GpuFrameMs; gpuSamples++; }
                statsFrames++;
                if (takeFile is not null) { FramebufferCapture.SavePng(display.Context, backbuffer, takeFile, size.X, size.Y); cinema.FrameDone(); }
                else if (shot) SaveScreenshot(display.Context, backbuffer, size.X, size.Y);
                frames++;
            }
            titleTimer += dt;
            statsTimer += dt;
            if (statsTimer >= 1)
            {
                // Once a second: the frame, the simulation, then the renderer's lines.
                stats.Clear();
                stats.Add($"{statsFrames / statsTimer:0} fps ({(display.VSync ? "vsync" : fpsLimit > 0 ? $"limit {fpsLimit}" : "uncapped")}), gpu {(gpuSamples > 0 ? $"{gpuSum / gpuSamples:0.00}" : "-")} ms");
                long ticks = session.Simulation.TotalTicks - statsTicks;
                statsTicks = session.Simulation.TotalTicks;
                stats.Add($"simulation  {ticks / statsTimer:0} ticks/s, {session.CurrentSnapshot.Characters.Count} characters, dropped {session.Simulation.DroppedTicks} ticks in all");
                WorldStats.Add(stats, display.Context, gpu, render, camera.Eye);
                statsTimer = gpuSum = 0;
                statsFrames = gpuSamples = 0;
            }
            if (titleTimer >= 0.5)
            {
                var s = session.Camera.Current;
                // The achieved speed over this window: the asked speed scaled by the share of the owed ticks that ran.
                long ran = session.Simulation.TotalTicks - windowRan, dropped = session.Simulation.DroppedTicks - windowDropped;
                windowRan = session.Simulation.TotalTicks;
                windowDropped = session.Simulation.DroppedTicks;
                double achieved = SimulationClock.AchievedSpeedOver(session.TimeScale, ran, dropped);
                string speedText = session.Simulation.IsPaused ? "paused" + (session.Simulation.RequestedPause ? "" : $" (x{session.Simulation.LastNonZeroSpeed:0})") : $"x{session.TimeScale:0}" + (achieved < session.TimeScale - 0.05 ? $" (running x{achieved:0.0})" : "");
                window.Title = $"Meitou | {frames / titleTimer:0} fps, cpu {cpuSum / Math.Max(frames, 1):0.00} ms | {(session.Camera.IsFree ? "free camera" : $"boom {s.Distance:0}")} | " +
                    $"{session.Clock.TimeText} {session.Clock.DayText} {speedText} | {s.Target.X:0}, {s.Target.Z:0}{(gpu.Post is { Temporal: true } p ? $" | {p.ActiveUpscaler}" : "")}";
                titleTimer = cpuSum = 0;
                frames = 0;
            }
            totalFrames++;
            if (g.QuitAfter is { } quitAfter && now >= quitAfter) { Console.WriteLine($"profile   {session.CurrentSnapshot.Characters.Count} characters in the snapshot, {session.CurrentSnapshot.Characters.Count(c => c.Appearance is not null)} drawable; per frame: simulation advance {advanceTotal / Math.Max(totalFrames, 1):0.000} ms ({session.Simulation.TotalTicks} ticks, {advanceTotal / Math.Max(session.Simulation.TotalTicks, 1):0.000} ms per tick), host draw-list fill {fillTotal / Math.Max(fillCalls, 1):0.000} ms, world draw cpu {drawTotal / Math.Max(totalFrames, 1):0.00} ms, {(gpu.Characters is { } chars ? chars.Statistics() : "no characters")}");
                Console.WriteLine($"smoke     {totalFrames} frames in {now:0.0} s: {totalFrames / now:0} fps on average, {session.Ticks.TotalTicks} control ticks, {session.Simulation.TotalTicks} simulation ticks, game time {session.Clock.TimeText}"); quit = true; }
            if (!display.VSync && fpsLimit > 0) Limit(frame, now, 1.0 / fpsLimit);
        }
        // Vsync and full screen are kept when changed in play (a command line's --vsync or --windowed alone is not saved).
        if (display.VSync != vsync) config.VSync = display.VSync;
        if (BorderlessFullScreen.IsOn(window) != fullScreen) config.FullScreen = BorderlessFullScreen.IsOn(window);
        if (panel is not null)
            foreach (var slider in panel.Sliders)
                if (!SessionSliders.Contains(slider.Label)) config.Graphics[slider.Label] = slider.Get();
        if (panel is not null)
            foreach (var toggle in panel.Toggles) config.Graphics[SwitchKey(toggle.Label)] = toggle.Get() ? 1 : 0;
        profiler?.Dispose();
        config.Bindings = session.Bindings.ToDictionary();
        config.Save();
        overlay?.Dispose();
        gpu.Dispose();
        silkInput.Dispose();
        return 0;

        static string SwitchKey(string label) => "Meitou: " + label;

        string? FullScreenState() => BorderlessFullScreen.IsOn(window) ? "borderless" : "window";

        // The state shown beside a key in the F10 list, by the item's first word.
        string? KeyState(string key)
        {
            static string OnOff(bool on) => on ? "on" : "off";
            return key switch
            {
                ";" => OnOff(session.Camera.IsFree),
                "Space" => session.Simulation.IsPaused ? "paused" : "running",
                "F2/F3/F4" => $"x{session.TimeScale:0}",
                "Tab" => OnOff(panel?.Visible == true),
                "F12" => profiler?.Showing.ToString().ToLowerInvariant(),
                "Shift+F1" => meitou.On ? "Meitou" : "Faithful",
                "Alt+Enter" => FullScreenState(),
                "Ctrl+F9" => OnOff(fpsVisible),
                "F11" => OnOff(statsVisible),
                "F6" => OnOff(cinema.EditorVisible),
                "F7" => OnOff(interfaceHidden),
                _ => null,
            };
        }
    }

    /// <summary>Panel rows that are the session's state, not graphics settings: kept out of <c>graphics</c> in meitou.user.json (vsync and full screen have their own keys).</summary>
    static readonly HashSet<string> SessionSliders = ["Time of day", "VSync", "Full screen"];

    /// <summary>The player moved, turned, zoomed or switched the camera this tick (what hands a camera the cinema left back to the rig).</summary>
    static bool CameraInput(ActionState a) =>
        a.Held(InputAction.MoveForward) || a.Held(InputAction.MoveBack) || a.Held(InputAction.MoveLeft) || a.Held(InputAction.MoveRight) ||
        a.Held(InputAction.MoveUp) || a.Held(InputAction.MoveDown) || a.Held(InputAction.RotateLeft) || a.Held(InputAction.RotateRight) ||
        a.Held(InputAction.PitchUp) || a.Held(InputAction.PitchDown) || a.Held(InputAction.ZoomIn) || a.Held(InputAction.ZoomOut) ||
        a.Held(InputAction.Orbit) && a.MouseDelta != Vector2.Zero || a.Wheel != 0 || a.Pressed(InputAction.ToggleFreeCamera);

    static void SaveScreenshot(GpuContext context, Texture backbuffer, int width, int height)
    {
        var file = Path.Combine(Directory.CreateDirectory(@"C:\Temp").FullName, $"meitou-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        FramebufferCapture.SavePng(context, backbuffer, file, width, height);
        Console.WriteLine($"saved     {file}");
    }

    /// <summary>Waits until <paramref name="period"/> has passed since <paramref name="start"/>: sleeps most of it, spins the last 1.5 ms.</summary>
    static void Limit(Stopwatch clock, double start, double period)
    {
        double end = start + period;
        double remaining = end - clock.Elapsed.TotalSeconds;
        if (remaining > 0.002) Thread.Sleep(TimeSpan.FromSeconds(remaining - 0.0015));
        while (clock.Elapsed.TotalSeconds < end) Thread.SpinWait(64);
    }

    /// <summary>Between ticks: the world's messages for the player go to the HUD and the log, the fights hostility started to the log.</summary>
    void WorldNews()
    {
        var world = session.World;
        foreach (string message in world.TakeMessages())
        {
            Console.WriteLine($"relations {message}");
            player.Notice(message);
        }
        if (aggro is null || aggro.Log.Count == 0) return;
        var table = world.Characters;
        string Who(Meitou.Simulation.CharacterId id, int faction) =>
            $"{(table.IsAlive(id) ? table.Cold(id.Slot)!.Name : "?")} ({(popData is { } d && faction >= 0 && faction < d.Factions.Count ? d.Factions[faction].Name : "no faction")})";
        foreach (var e in aggro.TakeLog())
            Console.WriteLine($"aggro     tick {e.Tick}: {Who(e.Attacker, e.AttackerFaction)} attacks {Who(e.Target, e.TargetFaction)} at {e.Distance:0}{(e.Joined > 0 ? $", {e.Joined} squad mate(s) join" : "")}");
    }
}
