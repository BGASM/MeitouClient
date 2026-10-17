using Meitou.Content;
using Meitou.Data.Audio;
using Meitou.Data.World;

namespace Meitou.Audio;

/// <summary>
/// The audio of a viewer or game session: the codebook library (from the cache, else the exe), the bank player, the ambience and music
/// rules and the output device, wired together. <see cref="TryCreate"/> returns null with the reason when any part is missing, so a host
/// simply runs without sound.
/// </summary>
public sealed class AudioSystem : IDisposable
{
    public const int SampleRate = 44100;

    readonly AudioOutput output;

    public WwisePlayer Player { get; }
    public AmbienceDirector Ambience { get; }
    public MusicDirector Music { get; }
    public string DeviceName => output.DeviceName;

    AudioSystem(WwisePlayer player, AudioOutput output, MusicDirector music)
    {
        Player = player;
        this.output = output;
        Music = music;
        Ambience = new AmbienceDirector(player);
        Ambience.Start();
    }

    /// <summary>
    /// <paramref name="ambientVolume"/>: the install's <c>settings.cfg</c> <c>Ambient volume</c> (0..1, missing → 0.5). The game sends it as the
    /// game parameter <c>User_Volume_Ambient_SFX</c> (× 100), which the ambience bus's curve turns into its gain (0.5 → about −8 dB). The
    /// music takes its <c>Music volume</c> and <c>Music frequency</c> from the same file.
    /// </summary>
    public static AudioSystem? TryCreate(GameInstall install, float ambientVolume, out string? error)
    {
        var exe = Path.Combine(install.Root, "kenshi_x64.exe");
        var library = WwiseCodebookLibrary.Load(exe);
        if (library is null)
        {
            error = $"no Wwise codebook library in {exe}";
            return null;
        }
        var player = new WwisePlayer(library, Path.Combine(install.DataDirectory, "audio"), SampleRate);
        player.SetParameter("User_Volume_Ambient_SFX", ambientVolume * 100);
        var music = new MusicDirector(player, Setting(install, "Music frequency", 5, 0, 1000), Setting(install, "Music volume", 0.5f));
        var output = AudioOutput.TryOpen(SampleRate, player.Mix, out error);
        if (output is null)
        {
            player.Dispose();
            return null;
        }
        return new AudioSystem(player, output, music);
    }

    /// <summary>
    /// Feeds the ambience and the music the frame's state the way the game's camera update does (docs/audio.md): <paramref name="region"/> is the
    /// weather region under the camera's pivot (null: none), <paramref name="weather"/> the weather at the camera (null: clear), <paramref name="hour"/>
    /// the game hour against <paramref name="sunrise"/> and <paramref name="sunset"/>, <paramref name="eyeAboveGround"/> the eye's height over the
    /// ground under it and <paramref name="realSeconds"/> the real time since the last call.
    /// </summary>
    public void Update(WeatherRegion? region, WeatherState? weather, double hour, double sunrise, double sunset, float eyeAboveGround, double realSeconds)
    {
        Ambience.Update(new AmbienceInput
        {
            Biome = region?.Def.Ambience,
            Hour = ((hour % 24) + 24) % 24,
            Sunrise = (float)sunrise,
            Sunset = (float)sunset,
            EyeAboveGround = eyeAboveGround,
            Rain = weather?.Rain ?? 0,
            Wind = weather is null ? 0 : weather.Weather.WindIntensity * weather.WindSpeed * 0.01f,
            RealSeconds = realSeconds,
        });
        Music.Update(region?.Def.Music);
    }

    /// <summary>One line for a statistics overlay: the biome heard, day or night, rain, wind, voices and clips, and the music (the one remembered, seconds until its timer posts it, music voices).</summary>
    public string StatsLine()
    {
        string music = Music.Remembered is null ? "nothing to post" : Music.Volume <= 0 ? "volume 0" : $"next in {Math.Max(0, Music.SecondsToNext):0} s";
        string biome = Ambience.Biome is { } heard ? $"{heard} {(Ambience.Night ? "night" : "day")}" : "no biome";
        return $"{biome}{(Ambience.Raining ? ", rain" : "")}{(Ambience.Windy ? ", wind" : "")}: {Player.Stats.Voices} voices, {Player.Stats.Clips} clips loaded; " +
            $"music {(Music.TitleScreen ? "main theme" : Music.Biome ?? "none")}, {music}, {Player.MusicVoices} voices";
    }

    /// <summary><c>settings.cfg</c>'s <c>Ambient volume</c> (0..1); 0.5 when missing or unreadable.</summary>
    public static float AmbientVolumeSetting(GameInstall install) => Setting(install, "Ambient volume", 0.5f);

    /// <summary>A number of the install's <c>settings.cfg</c> (<c>key=value</c> lines), clamped to <paramref name="min"/>..<paramref name="max"/>; <paramref name="fallback"/> when missing or unreadable.</summary>
    public static float Setting(GameInstall install, string key, float fallback, float min = 0, float max = 1)
    {
        try
        {
            var file = Path.Combine(install.Root, "settings.cfg");
            if (!File.Exists(file)) return fallback;
            foreach (var line in File.ReadLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(line[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return Math.Clamp(v, min, max);
            }
        }
        catch (IOException) { }
        return fallback;
    }

    public void Dispose()
    {
        output.Dispose();
        Player.Dispose();
    }
}
