using System.Numerics;
using Meitou.Engine;
using Meitou.Engine.Input;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>
/// The player's hands and the minimal HUD (docs/game/ui-input.md): click or box select, right click move orders (shift queues; holding the
/// button steers: the order follows the cursor),
/// the squad keys, the clock and speed buttons. Everything the player does becomes a <see cref="SimCommand"/> at the world's
/// current tick; this class never touches the simulation's state, it reads the published snapshot.
/// Positions are framebuffer pixels, top-left origin. The host sets <see cref="ViewProjection"/>, <see cref="Eye"/>, <see cref="Width"/>
/// and <see cref="Height"/> each frame (events arrive on the same thread, between frames).
/// </summary>
sealed partial class PlayerInterface(WorldSession session, Func<float, float, float> ground, Func<CharacterSnapshot, Vector3> where)
{
    public Matrix4x4 ViewProjection = Matrix4x4.Identity;
    public Vector3 Eye;
    public int Width = 1, Height = 1;
    /// <summary>How far from the camera a box select still takes characters.</summary>
    public const float BoxSelectRange = 7500;
    /// <summary>Character height used for picking and for the box select's aim point.</summary>
    public const float CharacterHeight = 2.2f;
    const float DragThreshold = 5;

    Vector2? dragFrom;
    Vector2 mouse;
    bool dragging;
    // The held right button's move order: where it goes now and the tick it was given.
    bool steering;
    Vector3 steerTarget;
    long steerTick;


    IReadOnlyList<CharacterId> SelectedIds() => [.. Selected().Select(c => c.Id)];

    IReadOnlyList<CharacterId> PlayerCharacters() =>
        [.. session.CurrentSnapshot.Characters.Where(c => c.IsPlayer).Select(c => c.Id)];

    // ---- commands ----

    void Say(SimCommand command) => session.World.Commands.Enqueue(command);

    public void Select(IReadOnlyList<CharacterId> ids, bool additive) =>
        Say(new SelectCommand(ids, additive) { Tick = session.World.Tick });

    public void Move(Vector3 target, bool queued) =>
        Say(new MoveOrder([], target) { Tick = session.World.Tick, Queued = queued });

    // ---- events ----

    /// <summary>Returns true when the click belonged to the HUD or the interface (the camera input should not see it).</summary>
    public bool MouseDown(MouseButton button, Vector2 p, bool shift, bool ctrl = false, bool alt = false)
    {
        mouse = p;
        if (button == MouseButton.Left)
        {
            if (UiMouseDown(p)) return true;
            if (OverHud(p)) return true;
            dragFrom = p;
            dragging = false;
            return true;
        }
        if (button == MouseButton.Right && MapMoveOrder(p, shift)) return true;
        if (button == MouseButton.Right && !OverHud(p))
        {
            // Alt + right click on a downed or dead character: the nearest selected loots it (take all). Ctrl + right click: picks it up, or puts it down when a selected
            // character carries it. A plain right click on one of your own downed characters that needs no first aid picks it up too (the original's quick right click).
            var downed = alt || ctrl ? PickDowned(p) : null;
            if (alt && downed is { } loot && SelectedIds() is { Count: > 0 })
                Say(new Meitou.Simulation.Carrying.LootOrder([], loot) { Tick = session.World.Tick });
            else if (ctrl && downed is { } body && Selected().Where(c => c.Carrying == body).Select(c => c.Id).ToArray() is { Length: > 0 } carriers)
                Say(new Meitou.Simulation.Carrying.PutDownOrder(carriers) { Tick = session.World.Tick });
            else if (ctrl && downed is { } lift && SelectedIds() is { Count: > 0 })
                Say(new Meitou.Simulation.Carrying.PickUpOrder([], lift) { Tick = session.World.Tick });
            else if (PickTrader(p) is { } trader && SelectedIds() is { Count: > 0 } customers)
                Say(new Meitou.Simulation.Trading.OpenTrade(customers[0], trader) { Tick = session.World.Tick });
            else if (PickTarget(p) is { } victim && SelectedIds() is { Count: > 0 } attackers)
                Say(new Meitou.Simulation.Combat.AttackOrder(attackers, victim) { Tick = session.World.Tick });
            else if (PickHurtAlly(p) is { } patient && SelectedIds() is { Count: > 0 } medics)
                Say(new Meitou.Simulation.FirstAid.TreatOrder(medics, patient) { Tick = session.World.Tick });
            else if (PickDowned(p, ownOnly: true) is { } ally && SelectedIds() is { Count: > 0 })
                Say(new Meitou.Simulation.Carrying.PickUpOrder([], ally) { Tick = session.World.Tick });
            else if (Ray(p) is { } ray && GroundHit(ray.Origin, ray.Direction) is { } hit)
            {
                Move(hit, shift);
                (steering, steerTarget, steerTick) = (!shift, hit, session.World.Tick);
            }
            return true;
        }
        return OverHud(p);
    }

    public void MouseMove(Vector2 p)
    {
        mouse = p;
        if (MapPointerMove(p)) return;
        if (UiVisible && UiMode == Meitou.Data.Ui.UiMode.Faithful && faithfulScreens.PointerMove(p)) return;
        if (dragFrom is { } from && !dragging && Vector2.Distance(from, p) >= DragThreshold) dragging = true;
    }

    public void MouseUp(MouseButton button, Vector2 p, bool shift)
    {
        mouse = p;
        if (button == MouseButton.Left) { InventoryPointerUp(p); faithfulScreens.PointerUp(); mapDrag = null; }
        if (button == MouseButton.Right && steering)
        {
            Steer(final: true);
            steering = false;
        }
        if (button != MouseButton.Left || dragFrom is not { } from) return;
        dragFrom = null;
        if (dragging)
        {
            dragging = false;
            Select(InBox(from, p), shift);
        }
        else
        {
            var hit = Pick(p);
            Select(hit is { } id ? new[] { id } : [], shift);
        }
    }

    /// <summary>
    /// Once a frame: while the right button is held after a move order, the order is given again at the ground point under the cursor once every world
    /// tick (also when the camera moves and the cursor does not). <paramref name="final"/> (the button is let go) gives the last point unless this tick
    /// already has the same one.
    /// </summary>
    public void Steer(bool final = false)
    {
        if (!steering || OverHud(mouse)) return;
        if (!final && session.World.Tick == steerTick) return;
        if (Ray(mouse) is not { } ray || GroundHit(ray.Origin, ray.Direction) is not { } hit) return;
        if (final && session.World.Tick == steerTick && hit == steerTarget) return;
        Move(hit, queued: false);
        (steerTarget, steerTick) = (hit, session.World.Tick);
    }

    /// <summary>The character the strategy camera follows (a double tap of its number key, F, or a double click on its squad button); panning the camera lets go.</summary>
    public CharacterId? Follow { get; set; }
    // The last number key and when it was pressed: the same key again within DoubleTapSeconds centres on the character (docs/game/ui-input.md, events 100..120).
    Key lastSelectKey;
    long lastSelectAt;
    const double DoubleTapSeconds = 0.5;

    /// <summary>Selects the squad's <paramref name="index"/>th member (<paramref name="shift"/> adds); the second press within half a second centres the camera on it and follows.</summary>
    void SelectMember(int index, bool shift, bool doubleTap)
    {
        var members = PlayerCharacters();
        if (index < 0 || index >= members.Count) return;
        Select([members[index]], shift);
        if (doubleTap) CentreOn(members[index]);
    }

    void CentreOn(CharacterId id)
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } c) return;
        var view = session.Camera.Current;
        session.Camera.Place(new Vector2(c.Position.X, c.Position.Z), view.Yaw, view.Pitch, view.Distance);
        Follow = id;
    }

    public void Key(Key key, bool shift)
    {
        if (key >= Meitou.Engine.Input.Key.D1 && key <= Meitou.Engine.Input.Key.D9)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            bool doubleTap = key == lastSelectKey && System.Diagnostics.Stopwatch.GetElapsedTime(lastSelectAt, now).TotalSeconds <= DoubleTapSeconds;
            (lastSelectKey, lastSelectAt) = (doubleTap ? default : key, now);
            SelectMember(key - Meitou.Engine.Input.Key.D1, shift, doubleTap);
        }
        // focus_char (F): centre on the lead selected character and follow it (the free camera's F is its down key).
        else if (key == Meitou.Engine.Input.Key.F && !session.Camera.IsFree && Selected().FirstOrDefault() is { } lead) CentreOn(lead.Id);
        else if (key == Meitou.Engine.Input.Key.Grave) Select(PlayerCharacters(), shift);
        else if (key == Meitou.Engine.Input.Key.R && !session.Camera.IsFree) Say(new StopCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (key == Meitou.Engine.Input.Key.H && !session.Camera.IsFree) Say(new Meitou.Simulation.FirstAid.MedicJobCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (key == Meitou.Engine.Input.Key.L && !session.Camera.IsFree) Say(new Meitou.Simulation.FirstAid.RestOrder(SelectedIds()) { Tick = session.World.Tick });
        // Rescue job (the original's NUM8 rescue toggle; the numpad is the cinema camera's) and put down (the original's context menu verb "Put Down").
        else if (key == Meitou.Engine.Input.Key.J && !session.Camera.IsFree) Say(new Meitou.Simulation.Carrying.RescueJobCommand(SelectedIds()) { Tick = session.World.Tick });
        else if (key == Meitou.Engine.Input.Key.K && !session.Camera.IsFree) Say(new Meitou.Simulation.Carrying.PutDownOrder(SelectedIds()) { Tick = session.World.Tick });
    }
}
