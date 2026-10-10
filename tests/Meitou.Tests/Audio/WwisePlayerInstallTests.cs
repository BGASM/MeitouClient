using Meitou.Audio;
using Meitou.Data.Audio;
using Meitou.Data.World;

namespace Meitou.Tests.Audio;

/// <summary>The bank player and the ambience rules, mixed offline against the install's banks (docs/audio.md).</summary>
[Slow]
public class WwisePlayerInstallTests
{
    static WwisePlayer Player()
    {
        var install = InstallData.Install;
        Assert.SkipWhen(install is null, "Kenshi install not found");
        var library = WwiseCodebookLibrary.Load(Path.Combine(install!.Root, "kenshi_x64.exe"));
        Assert.NotNull(library);
        return new WwisePlayer(library, Path.Combine(install.DataDirectory, "audio"), AudioSystem.SampleRate);
    }

    /// <summary>Mixes <paramref name="seconds"/> and returns the RMS of the last second.</summary>
    static double Run(WwisePlayer player, double seconds)
    {
        var block = new float[AudioOutput.BlockFrames * 2];
        int blocks = (int)(seconds * AudioSystem.SampleRate / AudioOutput.BlockFrames);
        int last = AudioSystem.SampleRate / AudioOutput.BlockFrames;
        double sum = 0;
        long n = 0;
        for (int b = 0; b < blocks; b++)
        {
            Array.Clear(block);
            player.Mix(block, AudioOutput.BlockFrames);
            if (b < blocks - last) continue;
            foreach (var x in block) sum += x * x;
            n += block.Length;
        }
        return Math.Sqrt(sum / Math.Max(1, n));
    }

    [Fact]
    public async Task Entering_the_desert_by_day_plays_its_bed_and_one_shots_and_leaving_fades_them_out()
    {
        using var player = Player();
        await player.LoadBank("SFX_Amb_Desert").WaitAsync(TestContext.Current.CancellationToken);
        player.SetParameter("Camera_Zoom", 5);
        player.Post("Enter_Desert_Day");
        double playing = Run(player, 4);
        Assert.Equal(2, player.Stats.Voices); // the looping bed and one of the random container's one-shots
        Assert.True(playing > 1e-5, $"rms {playing}");

        player.Post("Leave_Desert");
        Run(player, 11); // the stop fades over 10 s
        Assert.Equal(0, player.Stats.Voices);
    }

    [Fact]
    public async Task The_ambience_slider_curve_silences_the_ambience_at_zero()
    {
        using var player = Player();
        await player.LoadBank("SFX_Amb_Desert").WaitAsync(TestContext.Current.CancellationToken);
        player.SetParameter("User_Volume_Ambient_SFX", 0);
        player.Post("Enter_Desert_Day");
        Assert.Equal(0, Run(player, 4));
        player.SetParameter("User_Volume_Ambient_SFX", 100);
        Assert.True(Run(player, 2) > 1e-5);
    }

    [Fact]
    public async Task The_director_enters_the_biome_switches_to_night_and_leaves_for_the_next_biome()
    {
        using var player = Player();
        var director = new AmbienceDirector(player);
        var input = new AmbienceInput { Biome = Ambience.Desert, Hour = 12, Sunrise = 5, Sunset = 23, EyeAboveGround = 100, RealSeconds = 1 / 60.0 };
        director.Update(input);
        // The bank loads on a worker (the director asked for it; the same task comes back) and the event waits for it.
        await player.LoadBank("SFX_Amb_Desert").WaitAsync(TestContext.Current.CancellationToken);
        Run(player, 3);
        Assert.Equal(Ambience.Desert, director.Biome);
        Assert.False(director.Night);
        Assert.True(player.Stats.Voices >= 1);

        director.Update(input with { Hour = 23 }); // night from sunset on (day is strictly between sunrise and sunset)
        Assert.True(director.Night);
        director.Update(input with { Biome = Ambience.Plains, Hour = 23 });
        Assert.Equal(Ambience.Plains, director.Biome);
        await player.LoadBank("SFX_Amb_Plains").WaitAsync(TestContext.Current.CancellationToken);
        Run(player, 14); // Leave_Desert fades the desert out; the plains bank loads and its night bed plays
        Assert.True(player.Stats.Voices >= 1);
    }
}
