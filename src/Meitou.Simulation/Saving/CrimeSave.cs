using Meitou.Data.Fcs;

namespace Meitou.Simulation.Saving;

/// <summary>
/// The crime markers of a character in its GAMESTATE_CHARACTER record (docs/formats/save.md "Locks, cages and sentences"): Meitou's own keys, as the original's keys for them are not known.
/// <c>meitou escaped</c> (bool, <see cref="CharacterCold.Escaped"/>), <c>meitou recently wanted</c> (float game seconds left, <see cref="CharacterCold.RecentlyWanted"/>) and
/// <c>meitou recognition</c> (float 0..1, the bounty recognition meter <see cref="CharacterCold.Recognition"/>). Each is written only while set and removed otherwise, so a save without
/// them is written as it was read. The bounty ledger, the held state and the sentence have their own savers (<see cref="BountySave"/>, <see cref="HeldSave"/>).
/// </summary>
public static class CrimeSave
{
    public const string EscapedKey = "meitou escaped", RecentlyWantedKey = "meitou recently wanted", RecognitionKey = "meitou recognition";

    public static void Read(FcsRecord? state, CharacterCold cold)
    {
        if (state is null) return;
        cold.Escaped = state.Bools.GetValueOrDefault(EscapedKey);
        cold.RecentlyWanted = MathF.Max(0, state.Floats.GetValueOrDefault(RecentlyWantedKey));
        cold.Recognition = Math.Clamp(state.Floats.GetValueOrDefault(RecognitionKey), 0, 1);
    }

    public static void Write(FcsRecord state, CharacterCold cold)
    {
        if (cold.Escaped) state.Bools[EscapedKey] = true; else state.Bools.Remove(EscapedKey);
        if (cold.RecentlyWanted > 0) state.Floats[RecentlyWantedKey] = cold.RecentlyWanted; else state.Floats.Remove(RecentlyWantedKey);
        if (cold.Recognition > 0) state.Floats[RecognitionKey] = cold.Recognition; else state.Floats.Remove(RecognitionKey);
    }
}
