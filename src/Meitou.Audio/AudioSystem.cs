using Meitou.Content;
using Meitou.Data.Audio;

namespace Meitou.Audio;

/// <summary>
/// The audio of a viewer or game session: the codebook library (from the cache, else the exe), the bank player, the ambience rules and
/// the output device, wired together. <see cref="TryCreate"/> returns null with the reason when any part is missing, so a host simply runs
/// without sound.
/// </summary>
public sealed class AudioSystem : IDisposable
{
    public const int SampleRate = 44100;

    readonly AudioOutput output;

    public WwisePlayer Player { get; }
    public AmbienceDirector Ambience { get; }
    public string DeviceName => output.DeviceName;

    AudioSystem(WwisePlayer player, AudioOutput output)
    {
        Player = player;
        this.output = output;
        Ambience = new AmbienceDirector(player);
        Ambience.Start();
    }

    /// <summary>
    /// <paramref name="ambientVolume"/>: the install's <c>settings.cfg</c> <c>Ambient volume</c> (0..1, missing → 0.5). The game sends it as the
    /// game parameter <c>User_Volume_Ambient_SFX</c> (× 100), which the ambience bus's curve turns into its gain (0.5 → about −8 dB).
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
        var output = AudioOutput.TryOpen(SampleRate, player.Mix, out error);
        if (output is null)
        {
            player.Dispose();
            return null;
        }
        return new AudioSystem(player, output);
    }

    /// <summary><c>settings.cfg</c>'s <c>Ambient volume</c> (0..1); 0.5 when missing or unreadable.</summary>
    public static float AmbientVolumeSetting(GameInstall install)
    {
        try
        {
            var file = Path.Combine(install.Root, "settings.cfg");
            if (!File.Exists(file)) return 0.5f;
            foreach (var line in File.ReadLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals("Ambient volume", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(line[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return Math.Clamp(v, 0, 1);
            }
        }
        catch (IOException) { }
        return 0.5f;
    }

    public void Dispose()
    {
        output.Dispose();
        Player.Dispose();
    }
}
