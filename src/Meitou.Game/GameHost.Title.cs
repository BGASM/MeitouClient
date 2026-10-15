using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Data.World;
using Meitou.Rendering;
using Meitou.Rendering.Characters;

namespace Meitou.Game;

sealed partial class GameHost
{
    TitleComposition? titleComposition;
    WorldFrame.Gpu? titleFrame;
    WorldScene? titleScene;
    float? gameplayLens;
    bool TitleVisible => player.TitleOpen && player.UiMode == UiMode.Meitou;
    float TitleHour => scene.Clock.Sunset - 0.65f;

    void EnsureTitleScene()
    {
        if (titleFrame is not null || scene.Database is not { } db) return;
        titleScene = TitleComposition.CreateTerrain(scene.Focus, db, scene.Clock);
        var terrain = new TerrainRenderer(gpu.Post!.Gpu, titleScene.Coarse, titleScene.CoarseSize, titleScene.Window);
        terrain.SetTextures(gpu.Terrain.TextureSet, owns: false);
        titleComposition = new(install, db, scene.Focus, terrain.HeightAt, SandboxRecord(db));
        var objects = WorldObjects.Stage(install, db, titleComposition.Meshes);
        titleFrame = new()
        {
            Terrain = terrain, Sky = gpu.Sky, Post = gpu.Post, Shadow = gpu.Shadow,
            Objects = new WorldObjectRenderer(gpu.Post!.Gpu, assets, objects) { LoadBudget = 0, ObjectDistance = 24000 },
            Characters = new CharacterRenderer(gpu.Post.Gpu, install, db, assets)
            {
                LoadBudget = 0,
                Source = list => titleComposition.Fill(list, (float)realTime.Elapsed.TotalSeconds),
            },
            GiActive = false,
        };
    }

    /// <summary>Poster framing independent of the gameplay camera rig, facing the original game's fixed moons.</summary>
    void ApplyTitleCamera()
    {
        if (!TitleVisible)
        {
            if (gameplayLens is { } lens) { camera.FieldOfView = lens; gameplayLens = null; }
            return;
        }
        EnsureTitleScene();
        if (titleComposition is null) return;
        gameplayLens ??= camera.FieldOfView;
        SkyArrival.Place(camera, titleComposition.Eye, TitleComposition.Yaw, TitleComposition.Pitch, 100, TitleComposition.FieldOfView);
    }

    void DrawTitleWorld(int width, int height)
    {
        EnsureTitleScene();
        if (titleFrame is null) return;
        var sky = gpu.Sky;
        var weather = sky.Weather;
        var coverage = sky.CloudCoverage;
        sky.Weather = SkyWeather.Default;
        sky.CloudCoverage = 0;
        titleFrame.GameHours = 0;
        // The post chain, sky and shadow pass are shared; terrain, objects and actor belong to the dummy scene.
        titleFrame.Shadow = gpu.Shadow;
        try
        {
            WorldFrame.Draw(titleFrame, titleScene!, camera, new WorldRenderOptions { Water = false, Reflections = false }, width, height,
                presentationHour ?? TitleHour, (float)realTime.Elapsed.TotalSeconds / 600f, o.FogDistance);
        }
        finally { sky.Weather = weather; sky.CloudCoverage = coverage; }
    }

    void DisposeTitleScene()
    {
        titleFrame?.Characters?.Dispose();
        titleFrame?.Objects?.Dispose();
        titleFrame?.Terrain.Dispose();
        titleScene?.Dispose();
        titleFrame = null;
        titleScene = null;
        titleComposition = null;
    }
}
