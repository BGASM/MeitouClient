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
    /// <summary>
    /// While the right button is held after a move order, the order is given again at the cursor when its ground point has moved this far (world units)
    /// and at least <see cref="SteerTicks"/> world ticks have passed (engine choice: often enough to look continuous, rarely enough for the path service).
    /// </summary>
    public const float SteerDistance = 5;
    public const int SteerTicks = 3;

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
    public bool MouseDown(MouseButton button, Vector2 p, bool shift)
    {
        mouse = p;
        if (button == MouseButton.Left)
        {
            if (HudButtonAt(p) is { } index)
            {
                PressButton(index);
                return true;
            }
            if (OverHud(p)) return true;
            dragFrom = p;
            dragging = false;
            return true;
        }
        if (button == MouseButton.Right && !OverHud(p))
        {
            if (PickTarget(p) is { } victim && SelectedIds() is { Count: > 0 } attackers)
                Say(new Meitou.Simulation.Combat.AttackOrder(attackers, victim) { Tick = session.World.Tick });
            else if (Ray(p) is { } ray && GroundHit(ray.Origin, ray.Direction) is { } hit)
            {
                Move(hit, shift);
                (steering, steerTarget, steerTick) = (!shift, hit, session.World.Tick);
            }
            return true;
        }
        return false;
    }

    public void MouseMove(Vector2 p)
    {
        mouse = p;
        if (dragFrom is { } from && !dragging && Vector2.Distance(from, p) >= DragThreshold) dragging = true;
    }

    public void MouseUp(MouseButton button, Vector2 p, bool shift)
    {
        mouse = p;
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
    /// Once a frame: while the right button is held after a move order, the order follows the ground point under the cursor (also when the camera moves
    /// and the cursor does not). <paramref name="final"/> (the button is let go) gives the last point regardless of the tick spacing.
    /// </summary>
    public void Steer(bool final = false)
    {
        if (!steering || OverHud(mouse)) return;
        if (!final && session.World.Tick - steerTick < SteerTicks) return;
        if (Ray(mouse) is not { } ray || GroundHit(ray.Origin, ray.Direction) is not { } hit) return;
        if (Vector2.Distance(new Vector2(hit.X, hit.Z), new Vector2(steerTarget.X, steerTarget.Z)) < SteerDistance) return;
        Move(hit, queued: false);
        (steerTarget, steerTick) = (hit, session.World.Tick);
    }

    public void Key(Key key, bool shift)
    {
        if (key >= Meitou.Engine.Input.Key.D1 && key <= Meitou.Engine.Input.Key.D9)
        {
            var members = PlayerCharacters();
            int n = key - Meitou.Engine.Input.Key.D1;
            if (n < members.Count) Select([members[n]], shift);
        }
        else if (key == Meitou.Engine.Input.Key.Grave) Select(PlayerCharacters(), shift);
        else if (key == Meitou.Engine.Input.Key.R && !session.Camera.IsFree) Say(new StopCommand(SelectedIds()) { Tick = session.World.Tick });
    }
}
