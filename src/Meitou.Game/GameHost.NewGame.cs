using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class GameHost
{
    string? newGameRequested;
    void HandleNewGame()
    {
        if (newGameRequested is not { } id) return;
        newGameRequested = null;
        if (!SavesAvailable(out string why) || Busy()) { player.Notice(why.Length > 0 ? why : "Save or load still in progress", true); return; }
        if (scene.Database is not { } database || NewGameStart.Find(NewGameStart.LoadAll(database), id) is not { } chosen) return;
        try
        {
            var built = BuildSystems(interactive: true);
            var at = chosen.StartPosition;
            if (!chosen.ForceStartPos)
                foreach (var link in chosen.Towns)
                    if (popData!.Sites.FirstOrDefault(s => s.Town.Id == link.Id) is { } site) { at = new(site.Position.X, site.Position.Z); break; }
            WaitForNavmesh(new(at.X, 0, at.Y));
            Squad? spawned = null;
            session.ReplaceWorld(built.Systems, world => spawned = built.Population!.StartPlayer(world, chosen));
            population = built.Population; aggro = built.Aggro; playerSquad = spawned;
            if (nav is not null) population!.ZoneGate = zone => nav.Walkability.World.Contains(zone);
            loadedSave = null; preparedBase = null; worldEpoch++;
            session.Clock.SetTime(1, 13);
            var lead = session.World.Characters.Previous[spawned!.Leader.Slot].Position;
            var view = session.Camera.Current;
            session.Camera.Place(new Vector2(lead.X, lead.Z), view.Yaw, view.Pitch, view.Distance);
            sentFocus = false; SendFocus();
            session.World.RunTick(); indexedFor = null; previousById.Clear();
            player.Reset(); player.CloseFrontend();
            player.Notice($"Started {chosen.Name}");
        }
        catch (Exception e) { Console.Error.WriteLine($"new game  {e}"); player.Notice($"New game failed: {e.Message}", true); }
    }
}
