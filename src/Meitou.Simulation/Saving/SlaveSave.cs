using Meitou.Data.Fcs;

namespace Meitou.Simulation.Saving;

/// <summary>
/// A character's slave state in its GAMESTATE_CHARACTER record: the original's <c>slavestate</c> int (0 free, 1 slave, 2 escaped; <c>FUN_140626580</c> writes it with <c>personality</c>,
/// docs/formats/save.md) and Meitou's <c>meitou slave owner</c> (the owner FACTION's string id), since the original's <c>slaver</c> handle is not mapped. The owner key is written only while
/// set and removed otherwise.
/// </summary>
public static class SlaveSave
{
    public const string StateKey = "slavestate", OwnerKey = "meitou slave owner";

    public static void Read(FcsRecord? state, CharacterCold cold, PopulationData data)
    {
        if (state is null) return;
        cold.SlaveState = (byte)Math.Clamp(state.Ints.GetValueOrDefault(StateKey), 0, 2);
        cold.SlaveOwner = cold.SlaveState != 0 && state.Strings.GetValueOrDefault(OwnerKey) is { Length: > 0 } owner ? data.FactionIndex(owner) : -1;
    }

    public static void Write(FcsRecord state, CharacterCold cold, PopulationData data)
    {
        state.Ints[StateKey] = cold.SlaveState;
        if (cold.SlaveState != 0 && (uint)cold.SlaveOwner < (uint)data.Factions.Count) state.Strings[OwnerKey] = data.Factions[cold.SlaveOwner].Id;
        else state.Strings.Remove(OwnerKey);
    }
}
