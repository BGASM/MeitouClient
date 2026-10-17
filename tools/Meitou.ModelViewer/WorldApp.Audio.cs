using Meitou.Audio;
using Meitou.Rendering;
using static Meitou.Rendering.WorldFrame;

namespace Meitou.ModelViewer;

static partial class WorldApp
{
    /// <summary>
    /// Feeds the ambience and the music the frame's state the way the game's camera update does (docs/audio.md): the region under the
    /// camera's target (the game's pivot), the hour against sunrise and sunset, the eye's height above the ground, and the weather's rain and
    /// wind.
    /// </summary>
    static void UpdateAudio(AudioSystem audio, Gpu gpu, WorldScene scene, WorldCamera camera, float hour, double realSeconds)
    {
        var target = camera.Target;
        var eye = camera.Eye;
        var weather = gpu.Weather;
        var state = weather?.State;
        var region = weather?.World.RegionAt(target.X, target.Z);
        audio.Ambience.Update(new AmbienceInput
        {
            Biome = region?.Def.Ambience,
            Hour = ((hour % 24) + 24) % 24,
            Sunrise = scene.Clock.Sunrise,
            Sunset = scene.Clock.Sunset,
            EyeAboveGround = eye.Y - gpu.Terrain.HeightAt(eye.X, eye.Z),
            Rain = state?.Rain ?? 0,
            Wind = state is null ? 0 : state.Weather.WindIntensity * state.WindSpeed * 0.01f,
            RealSeconds = realSeconds,
        });
        audio.Music.Update(region?.Def.Music);
    }

    /// <summary>F11: the music the director remembers, when its timer posts it next, and the music voices playing.</summary>
    static string MusicStats(AudioSystem audio)
    {
        var music = audio.Music;
        string next = music.Remembered is null ? "nothing to post" : music.Volume <= 0 ? "volume 0" : $"next in {Math.Max(0, music.SecondsToNext):0} s";
        return $"music {music.Biome ?? "none"}, {next}, {audio.Player.MusicVoices} voices";
    }
}
