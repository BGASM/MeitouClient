using Meitou.Data.Ui;
using Meitou.Data.World;
using Meitou.Rendering;

namespace Meitou.Game;

sealed partial class GameHost
{
    TitleComposition? titleComposition;
    float? gameplayLens;
    /// <summary>The characters' usual source (the simulation), set aside while the title shows only its wanderer.</summary>
    Action<CharacterDrawList>? gameplayCharacters;
    bool TitleVisible => player.TitleOpen && player.UiMode == UiMode.Meitou;
    float TitleHour => scene.Clock.Sunrise + 0.75f;
    /// <summary>The moons' fill light, and the shafts' air layer (a multiple of the usual) on the title: thick morning air so the low sun casts strong rays.</summary>
    const float TitlePlanetFill = 0.8f, TitleShaftAir = 2, TitleClouds = 0.38f;
    /// <summary>The title's white balance: keeps the orange of the early hour with the sun a little higher than that hour would put it.</summary>
    static readonly System.Numerics.Vector3 TitleBalance = new(1.1f, 0.98f, 0.88f);
    /// <summary>The wanderer's fill light on the title: warm sky light so his back, turned to the low sun, is not a flat silhouette.</summary>
    static readonly System.Numerics.Vector3 TitleFill = new(0.064f, 0.052f, 0.04f);

    void EnsureTitleScene()
    {
        if (titleComposition is not null || scene.Database is not { } db) return;
        titleComposition = new(install, db, scene.GroundAt, SandboxRecord(db));
    }

    /// <summary>
    /// The title's own view of the real world (<see cref="TitleComposition.Spot"/>), independent of the gameplay camera rig; the world's renderers
    /// stream round it as round any camera. While the title shows, the characters drawn are its wanderer alone.
    /// </summary>
    void ApplyTitleCamera()
    {
        if (!TitleVisible)
        {
            if (gameplayLens is { } lens) { camera.FieldOfView = lens; camera.MaxNear = 200; gameplayLens = null; }
            if (gameplayCharacters is { } source && gpu.Characters is { } restore) { restore.Source = source; gameplayCharacters = null; }
            if (gpu.Foliage is { } plants && titleComposition is { } shown && plants.Shift == shown.Ruins) plants.Shift = null;
            if (gpu.Objects is { } things && titleComposition is { } staged && things.Staged == staged.Staged) things.Staged = null;
            return;
        }
        EnsureTitleScene();
        if (titleComposition is not { } title) return;
        gameplayLens ??= camera.FieldOfView;
        player.TitleLogoSource ??= TitleLogo.Load;
        if (gpu.Characters is { } characters && gameplayCharacters is null)
        {
            gameplayCharacters = characters.Source;
            characters.Source = title.Fill;
        }
        if (gpu.Foliage is { } foliage && foliage.Shift != title.Ruins) foliage.Shift = title.Ruins;
        if (gpu.Objects is { } objects && objects.Staged != title.Staged) objects.Staged = title.Staged;
        TitleComposition.Place(camera);
    }

    void DrawTitleWorld(int width, int height)
    {
        EnsureTitleScene();
        if (titleComposition is not { } title) return;
        // Clear sky with a few clouds whatever the region's weather, no placed fog volumes, the sun turned into the right of the view, the moons lit
        // full face so they read by day, and thicker morning air so the hill the sun peeks over casts rays through it.
        var sky = gpu.Sky;
        var (weather, coverage, regions, clock, wind) = (sky.Weather, sky.CloudCoverage, gpu.Weather, scene.Clock, sky.CloudWind);
        sky.Weather = SkyWeather.Default;
        sky.CloudCoverage = TitleClouds;
        sky.CloudWind = TitleComposition.CloudWind(camera);
        sky.PlanetTurn = TitleComposition.MoonTurn(camera);
        sky.PlanetFill = TitlePlanetFill;
        if (gpu.FogVolumes is { } placedFog) placedFog.HidePlaced = true;
        if (gpu.Characters is { } lit) lit.Fill = TitleFill;
        var shafts = gpu.Post?.Options;
        var (air, strength, balance) = shafts is null ? (0f, 0f, System.Numerics.Vector3.One) : (shafts.ShaftAir, shafts.ShaftStrength, shafts.Balance);
        if (shafts is not null) (shafts.ShaftAir, shafts.ShaftStrength, shafts.Balance) = (TitleShaftAir, 1, TitleBalance);
        gpu.Weather = null;
        float hour = presentationHour ?? TitleHour;
        scene.Clock = clock with { SunTurn = TitleComposition.SunTurn(clock, hour, camera) };
        double seconds = realTime.Elapsed.TotalSeconds;
        if (gpu.Foliage is { } foliage && o.Screenshot is null) foliage.SwaySeconds = seconds;
        try { WorldFrame.Draw(gpu, scene, camera, render, width, height, hour, (float)seconds / 600f, o.FogDistance); }
        finally
        {
            (sky.Weather, sky.CloudCoverage, gpu.Weather, scene.Clock, sky.PlanetFill, sky.CloudWind, sky.PlanetTurn) = (weather, coverage, regions, clock, 0, wind, System.Numerics.Quaternion.Identity);
            if (shafts is not null) (shafts.ShaftAir, shafts.ShaftStrength, shafts.Balance) = (air, strength, balance);
            if (gpu.FogVolumes is { } restoreFog) restoreFog.HidePlaced = false;
            if (gpu.Characters is { } unlit) unlit.Fill = default;
        }
    }

    void DisposeTitleScene() => titleComposition = null;
}
