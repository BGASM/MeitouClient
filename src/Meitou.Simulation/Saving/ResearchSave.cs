using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation.Construction;

namespace Meitou.Simulation.Saving;

/// <summary>
/// The player's research in <c>quick.save</c> (docs/formats/save.md "Research"): the RESEARCH record holds the float <c>num finished</c> and the strings <c>finished&lt;k&gt;</c> (the RESEARCH ids,
/// in the order they were finished), and the float <c>num currents</c> for the ones in progress (<b>Verified</b>: the five sample saves hold <c>num finished</c> 1, <c>finished0</c>
/// <c>5359-gamedata.base</c> and <c>num currents</c> 0). Research in progress is not modelled: its keys are left as they are.
/// </summary>
public static class ResearchSave
{
    /// <summary>The finished research ids a RESEARCH record lists (none for a null record).</summary>
    public static List<string> Read(FcsRecord? record)
    {
        var ids = new List<string>();
        if (record is null) return ids;
        for (int k = 0; record.Strings.TryGetValue("finished" + k, out var id); k++) ids.Add(id);
        return ids;
    }

    /// <summary>Loads the record's finished research into the world.</summary>
    public static void Load(FcsRecord? record, ResearchState research, GameDatabase db) => research.Set(db, Read(record));

    /// <summary>Writes the finished research into the record, replacing the ones it listed.</summary>
    public static void Write(FcsRecord? record, ResearchState research)
    {
        if (record is null) return;
        if (Read(record).SequenceEqual(research.Completed, StringComparer.Ordinal) && record.Floats.TryGetValue("num finished", out float n) && n == research.Completed.Count) return;   // as the save had it
        for (int k = 0; record.Strings.ContainsKey("finished" + k); k++) record.Strings.Remove("finished" + k);
        for (int k = 0; k < research.Completed.Count; k++) record.Strings["finished" + k] = research.Completed[k];
        record.Floats["num finished"] = research.Completed.Count;
        if (!record.Floats.ContainsKey("num currents")) record.Floats["num currents"] = 0;
    }
}
