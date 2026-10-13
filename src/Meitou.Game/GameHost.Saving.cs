using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Meitou.Data;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Game;

/// <summary>
/// Quicksave (F5) and quickload (F9), docs/simulation.md "Saves". The key only sets a request (a control tick runs inside <c>WorldSession.Advance</c>); the frame loop takes
/// the request right after <c>Advance</c> returns, when no world tick is in flight. A save captures the world there, on the thread that ticks it (a few milliseconds), and writes
/// the files on a worker; a load reads the files on a worker and swaps in a new world there. The slot is Meitou's own <c>%LOCALAPPDATA%\Meitou\save\quicksave</c>
/// (<see cref="QuickSave"/>), never a folder of the original game.
/// </summary>
sealed partial class GameHost
{
    bool quickSaveRequested, quickLoadRequested;
    string? saveRequestedFolder, loadRequestedFolder, readingFolder;
    Task? saveWrite;
    Task<SaveGame>? saveRead;
    // What the world was loaded from: the next capture carries the save's records the world does not model through (null for a world that started new).
    LoadedSave? loadedSave;
    // Counts the worlds the host has had: part of a character's key in the draw list, so a new world's characters never take over the renderer's per-character state of the old one's.
    int worldEpoch;
    // Results of the write worker, shown on the next frame (the HUD is drawn by the main thread only).
    readonly ConcurrentQueue<(string Text, bool Error)> saveNotices = [];
    IReadOnlyList<string>? saveDataFiles;
    // The save a capture starts from (a copy of the loaded one, or a new one from the data) costs tens of milliseconds and holds nothing of the world: it is made ahead on a worker,
    // so the capture at the tick boundary only reads the squad (a few ms). Stale (for another loaded save) after a load.
    Task<SaveBuilder>? preparedBase;
    LoadedSave? preparedFor;
    int captures;

    SaveCaptureOptions CaptureOptions()
    {
        saveDataFiles ??= [.. LoadOrder.FromInstall(install).Entries.Select(e => e.Name)];
        return new SaveCaptureOptions { DataFiles = saveDataFiles, Placements = townPlacements, Seed = session.World.Seed ^ (ulong)++captures << 20 };
    }

    /// <summary>The system list and its order are <see cref="StandardSystems"/>' (docs/simulation.md "Threading"); built again for each world.</summary>
    Meitou.Simulation.StandardSystemSet BuildSystems(bool interactive) =>
        Meitou.Simulation.StandardSystems.Build(popData!, simWalkability!, new Meitou.Simulation.StandardSystemOptions
        {
            // The sandbox compares motion with the original on characters standing still: a hostile foe there must not start a fight by itself. The duel scripts its fight.
            Parts = IsSandbox || IsDuel ? Meitou.Simulation.StandardParts.All & ~Meitou.Simulation.StandardParts.Aggro : Meitou.Simulation.StandardParts.All,
            Aggro = new Meitou.Simulation.AggroSettings { RecordLog = true },
            Population = new Meitou.Simulation.PopulationSettings { Background = interactive && !IsDuel },
            // The duel replays exactly: its paths are answered inside the tick, never on a thread whose timing differs from lap to lap.
            SynchronousPaths = !interactive || IsDuel,
            // The duel prints what its measured lap holds (MeasureLap).
            Combat = new Meitou.Simulation.Combat.CombatOptions { SelfApproach = false, TickMedical = false, RecordLog = IsDuel },
            AnimationLengths = animationLengths,
            AnimationLibrary = animationLibrary,
            AnimationBlendRate = popData!.Bodies.Constants.AnimationBlendRate,
        });

    /// <summary>Once per frame, between ticks: the notices of the write worker, the requests of the keys, a finished read.</summary>
    void HandleSaves()
    {
        while (saveNotices.TryDequeue(out var notice)) player.Notice(notice.Text, notice.Error);
        if (quickSaveRequested) { quickSaveRequested = false; QuickSaveNow(); }
        if (quickLoadRequested) { quickLoadRequested = false; BeginQuickLoad(); }
        if (saveRequestedFolder is { } to) { saveRequestedFolder = null; QuickSaveNow(to); }
        if (loadRequestedFolder is { } sourceFolder) { loadRequestedFolder = null; BeginQuickLoad(sourceFolder); }
        if (saveRead is { IsCompleted: true } read) { saveRead = null; FinishQuickLoad(read); }
        if (preparedBase is null && SavesAvailable(out _) && session.World.Player.Exists)
        {
            var (data, from, options) = (popData!, loadedSave, CaptureOptions());
            (preparedBase, preparedFor) = (Task.Run(() => SaveCapture.PrepareBase(data, from, options)), from);
        }
    }

    /// <summary>Whether this run has a game to save and load: the population (so not <c>--no-population</c> or the sandbox).</summary>
    bool SavesAvailable(out string reason)
    {
        reason = popData is null || population is null || IsSandbox ? "No game to save or load here (needs the town population)" : "";
        return reason.Length == 0;
    }

    bool Busy() => saveWrite is { IsCompleted: false } || saveRead is not null;

    void QuickSaveNow(string? destination = null)
    {
        if (!SavesAvailable(out string reason)) { player.Notice(reason, true); return; }
        var world = session.World;
        if (!world.Player.Exists) { player.Notice("Nothing to save: no player squad (start a new game)", true); return; }
        if (Busy()) { player.Notice("Save or load still in progress", true); return; }
        var watch = Stopwatch.StartNew();
        SaveGame game;
        bool ahead = preparedBase is { IsCompletedSuccessfully: true } && ReferenceEquals(preparedFor, loadedSave);
        try
        {
            var clock = session.Clock;
            var prepared = ahead ? preparedBase!.Result : null;
            preparedBase = null;   // used, or stale
            game = SaveCapture.Capture(world, popData!, new SaveClock((int)clock.Day, clock.Hour, clock.Minute), loadedSave, CaptureOptions(), prepared);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"save      capture failed: {e}");
            player.Notice($"Save failed: {e.Message}", true);
            return;
        }
        double captureMs = watch.Elapsed.TotalMilliseconds;
        string folder = destination ?? QuickSave.Folder();
        saveWrite = Task.Run(() =>
        {
            try
            {
                var written = Stopwatch.StartNew();
                game.Write(folder);
                Console.WriteLine($"save      {folder}: captured in {captureMs:0.0} ms{(ahead ? "" : " (the base made on the spot)")}, written in {written.ElapsedMilliseconds} ms");
                saveNotices.Enqueue(("Saved", false));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"save      write failed: {e}");
                saveNotices.Enqueue(($"Save failed: {e.Message}", true));
            }
        });
    }

    void BeginQuickLoad(string? source = null)
    {
        if (!SavesAvailable(out string reason)) { player.Notice(reason, true); return; }
        if (Busy()) { player.Notice("Save or load still in progress", true); return; }
        string folder = source ?? QuickSave.Folder();
        if (!File.Exists(Path.Combine(folder, SaveFolder.QuickSaveName))) { player.Notice(source is null ? "No quicksave yet (F5 saves)" : "Save is no longer available", true); return; }
        readingFolder = folder;
        saveRead = Task.Run(() => SaveGame.Load(folder));
    }

    /// <summary>Swaps in a world made from the save (at a tick boundary, the frame loop's) and hooks the host to it: the game clock, the camera on the player's squad, the draw list, the mouse state.</summary>
    void FinishQuickLoad(Task<SaveGame> read)
    {
        if (read.IsFaulted || read.IsCanceled)
        {
            string why = read.Exception?.GetBaseException().Message ?? "cancelled";
            Console.Error.WriteLine($"load      read failed: {read.Exception?.GetBaseException()}");
            player.Notice($"Load failed: {why}", true);
            return;
        }
        var watch = Stopwatch.StartNew();
        LoadedSave? loaded = null;
        StandardSystemSet built;
        try
        {
            built = BuildSystems(interactive: true);
            var data = popData!;
            // A world that fails to load leaves the old one running (ReplaceWorld drops the half-made one).
            session.ReplaceWorld(built.Systems, world => loaded = SaveLoader.Load(world, read.Result, data, new SaveLoadOptions { Appearances = data.Appearances }));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"load      failed: {e}");
            player.Notice($"Load failed: {e.Message}", true);
            return;
        }
        population = built.Population;
        aggro = built.Aggro;
        if (nav is not null) population!.ZoneGate = zone => nav.Walkability.World.Contains(zone);
        loadedSave = loaded;
        preparedBase = null;
        worldEpoch++;
        var world = session.World;
        session.Clock.SetTime(loaded!.Clock.Day, loaded.Clock.HourOfDay);
        playerSquad = world.Squads.Find(world.Player.Squad);
        foreach (var note in loaded.Notes) Console.WriteLine($"load      {note}");

        // The camera on the leader, keeping the view's heading, pitch and boom; the zones round it get their navmesh before the first tick (like a new game's start).
        if (playerSquad is { } squad && !squad.Leader.IsNone)
        {
            var at = world.Characters.Previous[squad.Leader.Slot].Position;
            var view = session.Camera.Current;
            session.Camera.Place(new Vector2(at.X, at.Z), view.Yaw, view.Pitch, view.Distance);
            WaitForNavmesh(at);
        }
        sentFocus = false;
        SendFocus();
        // The first tick publishes the snapshot (a paused game would draw nothing until it ran) and takes the first look at the zones round the player.
        world.RunTick();
        indexedFor = null;
        previousById.Clear();
        player.Reset();
        player.CloseFrontend();
        Console.WriteLine($"load      {readingFolder}: {loaded.PlayerPlatoons.Sum(p => p.Squad.Members.Count)} player character(s), {loaded.RoamingPlatoons.Count} roaming platoon(s), day {loaded.Clock.Day} {loaded.Clock.Hour:00}:{loaded.Clock.Minute:00}, in {watch.ElapsedMilliseconds} ms");
        player.Notice("Loaded");
    }

    /// <summary>Waits (at most 15 s) for the navmesh of the zones round <paramref name="at"/>; the ones already loaded cost nothing.</summary>
    void WaitForNavmesh(Vector3 at)
    {
        if (nav is null) return;
        long now = Stopwatch.GetTimestamp();
        var pending = new List<Task>();
        foreach (var z in ZoneActivation.ZonesAround(at, 0))
        {
            if (!navZones.ContainsKey(z)) pending.Add(nav.LoadZone(z));
            navZones[z] = now;
        }
        if (pending.Count > 0 && !Task.WaitAll([.. pending], TimeSpan.FromSeconds(15))) Console.Error.WriteLine("load      the navmesh round the player was not ready after 15 s; loading goes on");
    }
}
