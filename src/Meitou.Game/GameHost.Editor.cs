using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Editing;

namespace Meitou.Game;

/// <summary>
/// The character editor's host side (docs/characters.md "Character editor"): its data and preview source, opening it for a new game's squad once the world is shown,
/// and the camera that frames the edited character while it is open.
/// </summary>
sealed partial class GameHost
{
    /// <summary>The new game's squad, waiting for the loading screen to lift before the editor opens on it.</summary>
    List<CharacterId>? pendingCreation;

    void ConfigureEditor()
    {
        if (popData is not { } data) return;
        player.EditorData = (data.Db, install.Root);
        player.PreviewSource = (recordId, loadout) => data.Appearances?.Create(recordId, loadout);
        player.PreviewReady = look => gpu.Characters?.IsPrepared(look) ?? true;
    }

    /// <summary>Opens the editor on the new game's squad once the world shows and the squad is in the snapshot.</summary>
    void OpenPendingEditor()
    {
        if (pendingCreation is not { } squad || Covered) return;
        if (!squad.All(id => session.CurrentSnapshot.Characters.Any(c => c.Id == id))) return;
        pendingCreation = null;
        if (!player.OpenEditor(EditKind.Creation, squad)) session.World.Commands.Enqueue(new FinishCreation { Tick = session.World.Tick });
    }

    /// <summary>
    /// While the editor is open the camera looks at the edited character from the front, a little turned: the whole body on the BODY tab, the head on FACE and HAIR
    /// (engine choice: the original's editor camera is <b>Unknown</b>), the target moved right so the character stands in the space between the editor's panels.
    /// </summary>
    void EditorCamera()
    {
        if (player.EditorFocus is not { } focus || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == focus.Character) is not { } c) return;
        bool body = focus.Tab == EditorTab.Body;
        float height = body ? 10 : 19.5f, distance = body ? 46 : 15;
        float yaw = c.Yaw + 0.35f;
        var at = DrawnPosition(c) + new Vector3(0, height, 0);
        var toEye = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toEye));
        camera.Target = at + right * distance * 0.12f;
        camera.Yaw = yaw;
        camera.Pitch = 0.12f;
        camera.Distance = distance;
    }
}
