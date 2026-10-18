using System.Numerics;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>
/// Numpad / in the game (docs/viewer.md "Squad tour"): the camera flies from squad member to squad member in portrait order, keeping the
/// view's yaw, pitch and distance at each stop. Each hop is an arc: the camera climbs first (pulling back and looking more steeply down),
/// crosses at height and comes down onto the next member, who is tracked while they move. Presentation only; nothing is saved.
/// </summary>
sealed class SquadTour
{
    /// <summary>Seconds the camera stays on each member before the next hop.</summary>
    const float Hold = 1.2f;
    /// <summary>The arc's extra boom at its top, per unit of ground crossed (at least <see cref="MinLift"/> times the view's own distance).</summary>
    const float LiftPerDistance = 0.55f, MinLift = 1.5f;
    /// <summary>How much steeper the camera looks down at the top of an arc (radians), and its limit.</summary>
    const float PitchLift = 0.35f, MaxPitch = 1.35f;

    readonly List<CharacterId> stops = [];
    int next;
    float time, hopSeconds, yaw, pitch, distance;
    Vector3 from;
    bool holding;

    public bool Active { get; private set; }

    /// <summary>Starts at the camera's view of <paramref name="members"/>[0] (flying there first when the view is elsewhere).</summary>
    public void Start(WorldCamera camera, IReadOnlyList<CharacterId> members, Func<CharacterId, Vector3?> position)
    {
        stops.Clear();
        stops.AddRange(members);
        (yaw, pitch, distance) = (camera.Yaw, camera.Pitch, camera.Distance);
        next = 0;
        if (stops.Count > 0 && position(stops[0]) is { } first && Ground(first - camera.Target) < 0.25f * distance) next = 1;
        Active = next < stops.Count;
        if (Active) BeginHop(camera, position);
    }

    public void Stop() => Active = false;

    /// <summary>Moves the camera by <paramref name="dt"/> seconds; false once the tour is over (the camera is left on the last member).</summary>
    public bool Advance(double dt, WorldCamera camera, Func<CharacterId, Vector3?> position)
    {
        if (!Active) return false;
        time += (float)dt;
        if (holding)
        {
            if (position(stops[next - 1]) is { } held) camera.Target = held;
            if (time < Hold) return true;
            if (next >= stops.Count) { Active = false; return false; }
            BeginHop(camera, position);
            if (!Active) return false;
        }
        // A member gone mid-hop (dead and removed, left the squad): the hop ends where the camera is.
        var to = position(stops[next]) ?? camera.Target;
        float s = Math.Clamp(time / hopSeconds, 0, 1);
        // The ground move waits for the climb to get going and lands before the descent ends; the lift rises fast and flattens at the top.
        float across = SmootherStep(Math.Clamp((s - 0.12f) / 0.76f, 0, 1));
        float lift = MathF.Pow(MathF.Sin(MathF.PI * s), 0.6f);
        float height = MathF.Max(LiftPerDistance * Ground(to - from), MinLift * distance);
        camera.Target = Vector3.Lerp(from, to, across);
        camera.Yaw = yaw;
        camera.Pitch = pitch + (MathF.Max(MathF.Min(pitch + PitchLift, MaxPitch), pitch) - pitch) * lift;
        camera.Distance = MathF.Min(distance + height * lift, camera.ViewDistance * 0.5f);
        if (s >= 1) { next++; holding = true; time = 0; }
        return true;
    }

    void BeginHop(WorldCamera camera, Func<CharacterId, Vector3?> position)
    {
        while (next < stops.Count && position(stops[next]) is null) next++;
        if (next >= stops.Count) { Active = false; return; }
        from = camera.Target;
        // Longer hops take longer, but not proportionally: 2 s next door, up to 6 s across the map.
        float d = Ground(position(stops[next])!.Value - from);
        hopSeconds = Math.Clamp(2 + 1.1f * MathF.Log2(1 + d / MathF.Max(distance, 1)), 2, 6);
        (time, holding) = (0, false);
    }

    static float Ground(Vector3 v) => new Vector2(v.X, v.Z).Length();
    static float SmootherStep(float x) => x * x * x * (x * (6 * x - 15) + 10);
}
