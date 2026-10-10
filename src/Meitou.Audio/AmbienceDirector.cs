using Meitou.Data.World;

namespace Meitou.Audio;

/// <summary>What the ambience follows each frame (docs/audio.md "What the game sends").</summary>
public readonly record struct AmbienceInput
{
    /// <summary>The ambience of the region under the camera's pivot (BIOME_GROUP <c>ambience</c>); null when there is none.</summary>
    public Ambience? Biome { get; init; }
    /// <summary>Game hour 0..24.</summary>
    public double Hour { get; init; }
    /// <summary>GLOBAL CONSTANTS <c>sunrise</c> / <c>sunset</c> (5 and 23 in the base game).</summary>
    public float Sunrise { get; init; }
    public float Sunset { get; init; }
    /// <summary>The camera eye's height above the ground under it.</summary>
    public float EyeAboveGround { get; init; }
    /// <summary>The weather's rain level 0..100 (<c>rain intensity</c> × strength).</summary>
    public float Rain { get; init; }
    /// <summary>The weather's <c>wind intensity</c> × wind speed × 0.01.</summary>
    public float Wind { get; init; }
    /// <summary>Real seconds since the last update.</summary>
    public double RealSeconds { get; init; }
}

/// <summary>
/// The game's ambience rules (docs/audio.md, from the exe's camera update and audio manager): the biome of the region under the pivot, day or
/// night by the hour, the <c>Camera_Zoom</c> and <c>Time_in_Game</c> game parameters, and the weather's rain and wind beds.
/// </summary>
public sealed class AmbienceDirector(WwisePlayer player)
{
    /// <summary>The state the game compares each frame: the biome index, with bit 8 set at night; -1 before the first.</summary>
    int state = -1;
    bool? day;
    bool raining, windy;
    double clock;

    public Ambience? Biome => state < 0 ? null : (Ambience)(state & 0xFF);
    public bool Night => (state & 0x100) != 0;
    public bool Raining => raining;
    public bool Windy => windy;

    /// <summary>The banks the game loads at start (with Init, the music, the general sounds and the voices, which the viewer does not play).</summary>
    public void Start()
    {
        player.LoadBank("SFX_Amb_Weather_Bank");
        player.LoadBank("SFX_Amb_Indoors_Bank");
    }

    public void Update(in AmbienceInput input)
    {
        // Day strictly between sunrise and sunset; Time_in_Game (the state group) changes only when that flips.
        bool isDay = input.Hour > input.Sunrise && input.Hour < input.Sunset;
        if (day != isDay)
        {
            day = isDay;
            player.SetState("Time_in_Game", isDay ? "Day" : "Night");
        }

        // The game parameter Time_in_Game is not the clock: a 0..10 sawtooth over 10 real minutes.
        clock += input.RealSeconds;
        player.SetParameter("Time_in_Game", (float)(clock % 600 / 60));
        player.SetParameter("Camera_Zoom", input.EyeAboveGround / 2000 * 100);

        if (input.Biome is { } biome)
        {
            int next = (int)biome | (isDay ? 0 : 0x100);
            if (next != state)
            {
                if (state >= 0 && (state & 0xFF) != (int)biome) player.Post($"Leave_{(Ambience)(state & 0xFF)}");
                state = next;
                // The game posts Enter at once when the bank is loaded, otherwise after loading it (and then, by a bug, with the opposite
                // suffix); the player keeps the event until its bank arrives, so the suffix here is always the right one.
                player.LoadBank($"SFX_Amb_{biome}");
                player.Post($"Enter_{biome}_{(isDay ? "Day" : "Night")}");
            }
        }

        if (input.Rain > 0 != raining)
        {
            raining = input.Rain > 0;
            player.Post(raining ? "Weather_Rain_Start" : "Weather_Rain_End");
        }
        if (input.Wind > 0 != windy)
        {
            windy = input.Wind > 0;
            // The game posts Weather_Wind_Stop, which no bank has, so its wind never stops; Meitou posts the bank's Weather_Wind_End.
            player.Post(windy ? "Weather_Wind_Start" : "Weather_Wind_End");
        }
        player.SetParameter("Rain_Intensity", input.Rain);
        player.SetParameter("Intensity", input.Wind);
    }
}
