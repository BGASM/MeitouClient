using System.Diagnostics;

namespace Meitou.Game;

/// <summary>
/// Loads between scenes (a new game, a loaded save, the main menu) under the loading screen (docs/engine.md "Loading"): a load is a list of stages that run on this
/// thread at frame boundaries, one per frame, each after a covered frame was drawn; a stage may hand back work to wait for (a worker's read, the navmesh), and the
/// frames go on drawing the loading screen while it runs. After the last stage, and after the start, the world is drawn under the loading screen until its frames run
/// smoothly (the first frames of a new place build pipelines and stream its terrain and objects), so play starts on a smooth picture.
/// </summary>
sealed partial class GameHost
{
    sealed class SceneLoad
    {
        public readonly Queue<(string Step, Func<Task?> Run)> Stages = new();
        public Task? Waiting;
        /// <summary>A covered frame was drawn since the last stage ran: the next one may run (it may hold the thread).</summary>
        public bool Shown;
        public readonly Stopwatch Clock = Stopwatch.StartNew();
    }

    /// <summary>A stage found the load cannot go on; the message is shown, the world as it was stays.</summary>
    sealed class SceneLoadFailed(string message) : Exception(message);

    SceneLoad? sceneLoad;
    bool warming;
    double smoothSeconds;
    double warmSeconds;
    /// <summary>A smooth frame for the warm-up, and how long a run of them ends it; it ends after <see cref="WarmLimit"/> seconds anyway.</summary>
    const double SmoothFrame = 0.04, SmoothRun = 0.5, WarmLimit = 5;

    /// <summary>The loading screen covers the world: a scene load runs, or the world warms up under it.</summary>
    bool Covered => sceneLoad is not null || warming;

    static (string Step, Func<Task?> Run) Stage(string step, Func<Task?> run) => (step, run);

    void StartSceneLoad(params (string Step, Func<Task?> Run)[] stages)
    {
        if (sceneLoad is not null) return;
        sceneLoad = new();
        foreach (var stage in stages) sceneLoad.Stages.Enqueue(stage);
        loading?.Restart();
        loadingSteps = SceneSteps;
        SetLoadingStep(stages.Length > 0 ? stages[0].Step : "build");
    }

    /// <summary>Once a frame, between ticks: the next stage of the running load when its work is done and a covered frame was drawn.</summary>
    void StepSceneLoad()
    {
        if (sceneLoad is not { } load || !load.Shown || load.Waiting is { IsCompleted: false }) return;
        try
        {
            if (load.Waiting is { IsFaulted: true } failed) throw failed.Exception!.GetBaseException();
            if (!load.Stages.TryDequeue(out var stage))
            {
                sceneLoad = null;
                ReportLoading($"scene loaded in {load.Clock.Elapsed.TotalSeconds:0.0} s");
                BeginWarm();
                return;
            }
            SetLoadingStep(stage.Step);
            load.Waiting = stage.Run();
            load.Shown = false;
        }
        catch (Exception e)
        {
            sceneLoad = null;
            if (e is not SceneLoadFailed) Console.Error.WriteLine($"scene     load failed: {e}");
            player.Notice(e is SceneLoadFailed ? e.Message : $"Load failed: {e.Message}", true);
        }
    }

    /// <summary>The world is drawn under the loading screen until its frames run smoothly.</summary>
    void BeginWarm()
    {
        if (loading is null) return;
        (warming, smoothSeconds, warmSeconds) = (true, 0, 0);
        SetLoadingStep("view");
    }

    /// <summary>After a covered frame of <paramref name="seconds"/>: the warm-up ends after <see cref="SmoothRun"/> seconds of smooth frames in a row.</summary>
    void CoveredFrame(double seconds)
    {
        if (sceneLoad is { } load) load.Shown = true;
        // The frames under the cover count for the loading report (LoadingFrame counts the start's).
        loadingFrames++;
        if (seconds * 1000 > longestGap.Ms) longestGap = (seconds * 1000, currentStep);
        if (!warming) return;
        warmSeconds += seconds;
        smoothSeconds = seconds < SmoothFrame ? smoothSeconds + seconds : 0;
        if (smoothSeconds < SmoothRun && warmSeconds < WarmLimit) return;
        warming = false;
        ReportLoading($"the view warmed up in {warmSeconds:0.0} s");
    }

    /// <summary>The host's side of a new world: the camera focus sent again, the first tick (it publishes the snapshot), the draw list and the mouse state dropped, the menus closed.</summary>
    void SettleNewWorld()
    {
        sentFocus = false;
        SendFocus();
        session.World.RunTick();
        indexedFor = null;
        previousById.Clear();
        player.Reset();
        player.CloseFrontend();
    }
}
