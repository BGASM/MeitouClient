using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    /// <summary>Ctrl and Alt as the host last saw them: they turn the pointer over a downed character into the carry and loot pointers.</summary>
    public bool CtrlHeld, AltHeld;
    // Whether the mouse was over a control in the last arranged UI (ArrangeUi), so the pointer needs no arrange of its own.
    bool pointerOverUi;
    // The pointer and the character it is over, chosen again only when the tick, the mouse or a modifier changed (the picks walk every character).
    (long Tick, Vector2 Mouse, bool Ctrl, bool Alt, string Name, CharacterId? Target) pointer = (-1, default, false, false, "arrow", null);

    /// <summary>
    /// The mouse pointer (kenshi_pointers.xml) for what the right button would do under the cursor (docs/game/ui-input.md, pointers): the quick order's pointer
    /// (<see cref="QuickOrder"/>: talk, attk, med, lift), with Ctrl lift and with Alt search over a downed character; over the interface, while dragging or with no
    /// selection, the arrow.
    /// </summary>
    public string PointerName => Point().Name;

    /// <summary>
    /// The character under the cursor for the hover outline and its colour: the one a right click would act on, in the colour of that order (attack red, treat
    /// green, talk gold, carry or loot blue), else any character under the cursor in white; null over the interface or nobody.
    /// </summary>
    public (CharacterId Id, Vector4 Colour)? HoverTarget => Point() is { Target: { } id } p ? (id, p.Name switch
    {
        "attk" => new Vector4(1f, 0.3f, 0.22f, 1),
        "med" => new Vector4(0.4f, 1f, 0.5f, 1),
        "talk" => new Vector4(1f, 0.82f, 0.4f, 1),
        "lift" or "search" => new Vector4(0.45f, 0.75f, 1f, 1),
        _ => new Vector4(0.95f, 0.93f, 0.85f, 0.85f),
    }) : null;

    (long Tick, Vector2 Mouse, bool Ctrl, bool Alt, string Name, CharacterId? Target) Point()
    {
        if (UiVisible && pointerOverUi || UiCapturesPointer || ContextMenuOpen || dragging || settingsOpen || TitleOpen) return (-1, mouse, CtrlHeld, AltHeld, "arrow", null);
        var snapshot = session.CurrentSnapshot;
        if (pointer.Tick == snapshot.Tick && pointer.Mouse == mouse && pointer.Ctrl == CtrlHeld && pointer.Alt == AltHeld) return pointer;
        (string Name, CharacterId? Target) chosen = ("arrow", null);
        if (SelectedIds().Count > 0)
        {
            if ((CtrlHeld || AltHeld) && PickDowned(mouse) is { } downed) chosen = (AltHeld ? "search" : "lift", downed);
            else if (QuickOrder(mouse) is { } order) chosen = (order.Pointer, order.Target);
        }
        chosen.Target ??= PickWhere(mouse, _ => true);
        return pointer = (snapshot.Tick, mouse, CtrlHeld, AltHeld, chosen.Name, chosen.Target);
    }

    /// <summary>Draws the pointer <paramref name="name"/> at <paramref name="at"/> (framebuffer pixels) over everything drawn so far; nothing without the UI painter.</summary>
    public void DrawPointer(string name, Vector2 at, int width, int height) => uiPainter?.DrawPointer(name, at, width, height);
}
