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
    unsafe int Screenshot()
    {
        VulkanDisplay.RayTracing = o.WantsRayTracing;
        using var display = new VulkanDisplay(null, vsync: false, streamline: WantsDlss());
        streamline = display.Streamline;
        Boot(display, interactive: false);
        if (g.UiShowcase && playerSquad is not null) player.Select([playerSquad.Leader], additive: false);
        if (playerSquad is not null && g.MoveTo is { } to)
        {
            session.World.Commands.Enqueue(new Meitou.Simulation.SelectCommand(playerSquad.Members.ToList()) { Tick = session.World.Tick });
            session.World.Commands.Enqueue(new Meitou.Simulation.MoveOrder([], new Vector3(to.X, 0, to.Z)) { Tick = session.World.Tick });
        }
        for (int i = 0; i < Math.Max(g.Ticks, g.UiShowcase ? 1 : 0); i++)
        {
            session.Tick();
            session.AdvanceSimulation(session.Ticks.TickSeconds);   // one control tick of real time, at speed 1
            UpdateNav(wait: true);
            DuelHoldClock();
            DuelFrame(false);   // the duel's laps run on in a long still too
        }
        SandboxFollowCamera();
        ApplyCamera(session.Camera.Current);
        gpu.Streamer?.Settle(camera.Eye);
        gpu.Objects?.Settle(camera.Eye);
        gpu.Foliage?.Settle(camera.Eye);
        gpu.Characters?.Settle(camera.Eye);
        WorldFrame.FinishLoading(display.Context);
        var context = display.Context;
        int w = o.Width, h = o.Height;
        using var target = Meitou.Rendering.Gpu.Texture.Create(context, new TextureDesc(Silk.NET.Vulkan.Format.R8G8B8A8Unorm, w, h,
            Use: TextureUse.ColourTarget | TextureUse.TransferSrc | TextureUse.Sampled, Name: "offscreen picture"));
        gpu.Post!.Target = target;
        gpu.Post.InstantAdaptation = true;
        for (int i = 0; i < gpu.Post.WarmupFrames; i++) { DrawWorld(w, h); display.EndFrame(); }   // a temporal upscaler converges first
        DrawWorld(w, h);
        display.EndFrame();
        using var overlay = playerSquad is not null || player.UiMode == Meitou.Data.Ui.UiMode.Faithful ? DebugOverlay.TryCreate(context) : null;
        if (overlay is not null) player.CreateUiPainter(overlay, assets.Find);
        if (gpu.Characters is { } portraits) { var (atlas, sampled) = portraits.PortraitAtlas; player.SetUiTexture(PlayerInterface.PortraitTexture, atlas, sampled); }
        string?[] panels = g.UiShowcase ? player.UiMode == Meitou.Data.Ui.UiMode.Faithful
            ? [null, "inventory", "stats", "overview", "menu", "new-game", "save", "load", "title"]
            : [null, "inventory", "stats", "menu"] : [null];
        foreach (string? panelName in panels)
        {
            DrawWorld(w, h);
            if (overlay is not null)
            {
                overlay.Target = target;
                UpdateView(w, h);
                player.ShowUiPanel(panelName);
                if (IsSandbox && !g.UiShowcase) DrawSandbox(overlay, w, h);
                player.Draw(overlay, w, h);
                if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.IsPlayer) is { } me && player.Project(DrawnPosition(me) + new Vector3(0, 1, 0)) is { } px)
                {
                    var picked = player.Pick(px);
                    var ray = player.Ray(px + new Vector2(0, 60));
                    var hit = ray is { } r ? player.GroundHit(r.Origin, r.Direction) : null;
                    Console.WriteLine($"pickcheck pixel {px.X:0}, {px.Y:0} picks {(picked is { } id && id == me.Id ? "the leader" : "NOTHING")}; ground 60 px lower: {(hit is { } gh ? $"{gh.X:0}, {gh.Z:0} (leader at {me.Position.X:0}, {me.Position.Z:0})" : "no hit")}");
                }
            }
            display.EndFrame();
            context.Finish();
            string output = g.UiShowcase ? Path.Combine(Path.GetDirectoryName(o.Screenshot!) ?? "", $"{Path.GetFileNameWithoutExtension(o.Screenshot!)}-{panelName ?? "hud"}.png") : o.Screenshot!;
            FramebufferCapture.SavePng(context, target, output, w, h);
            Console.WriteLine($"saved     {Path.GetFullPath(output)}");
        }
        player.DisposeUi();
        var s = session.Camera.Current;
        Console.WriteLine($"camera    {(session.Camera.IsFree ? "free" : "strategy")}: pivot {s.Target.X:0}, {s.Target.Y:0}, {s.Target.Z:0}, eye {s.Eye.X:0}, {s.Eye.Y:0}, {s.Eye.Z:0}, " +
            $"yaw {s.Yaw * 180 / MathF.PI:0.#}, pitch {s.Pitch * 180 / MathF.PI:0.#}, boom {s.Distance:0.#}; {session.Ticks.TotalTicks} control ticks, {session.Simulation.TotalTicks} simulation ticks, game time {session.Clock.TimeText} ({session.Clock.DayText})");
        gpu.Dispose();
        return 0;
    }
}
