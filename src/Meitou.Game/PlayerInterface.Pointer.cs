using System.Numerics;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    /// <summary>Ctrl and Alt as the host last saw them: they turn the pointer over a downed character into the carry and loot pointers.</summary>
    public bool CtrlHeld, AltHeld;
    // Whether the mouse was over a control in the last arranged UI (ArrangeUi), so the pointer needs no arrange of its own.
    bool pointerOverUi;
    // The pointer, chosen again only when the tick, the mouse or a modifier changed (its picks walk every character).
    (long Tick, Vector2 Mouse, bool Ctrl, bool Alt, string Name) pointer = (-1, default, false, false, "arrow");

    /// <summary>
    /// The mouse pointer (kenshi_pointers.xml) for what the right button would do under the cursor (docs/game/ui-input.md, pointers): the quick order's pointer
    /// (<see cref="QuickOrder"/>: talk, attk, med, lift), with Ctrl lift and with Alt search over a downed character; over the interface, while dragging or with no
    /// selection, the arrow.
    /// </summary>
    public string PointerName
    {
        get
        {
            if (UiVisible && pointerOverUi || UiCapturesPointer || ContextMenuOpen || dragging || settingsOpen || TitleOpen) return "arrow";
            var snapshot = session.CurrentSnapshot;
            if (pointer.Tick == snapshot.Tick && pointer.Mouse == mouse && pointer.Ctrl == CtrlHeld && pointer.Alt == AltHeld) return pointer.Name;
            string name = "arrow";
            if (SelectedIds().Count > 0)
            {
                var downed = CtrlHeld || AltHeld ? PickDowned(mouse) : null;
                name = downed is not null ? (AltHeld ? "search" : "lift") : QuickOrder(mouse)?.Pointer ?? "arrow";
            }
            pointer = (snapshot.Tick, mouse, CtrlHeld, AltHeld, name);
            return name;
        }
    }

    /// <summary>Draws the pointer <paramref name="name"/> at <paramref name="at"/> (framebuffer pixels) over everything drawn so far; nothing without the UI painter.</summary>
    public void DrawPointer(string name, Vector2 at, int width, int height) => uiPainter?.DrawPointer(name, at, width, height);
}
