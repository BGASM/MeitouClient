using System.Numerics;
using Meitou.Rendering;

namespace Meitou.Game;

/// <summary>Presentation-only flight through the sky; the simulation clock and camera rig never change.</summary>
sealed class SkyArrival(WorldCamera source, float hour, float speed = 1)
{
    internal const double AscentSeconds = 3.4, DescentSeconds = 2.4;
    internal const float MinSpeed = 0.25f, MaxSpeed = 4;
    internal const float SkyPitch = -1.45f;
    readonly Vector3 sourceEye = source.Eye;
    readonly float sourceYaw = source.Yaw, sourcePitch = source.Pitch, sourceDistance = source.Distance, sourceFov = source.FieldOfView;
    readonly float sourceHour = hour;
    readonly float flightSpeed = float.IsFinite(speed) ? Math.Clamp(speed, MinSpeed, MaxSpeed) : 1;
    double elapsed;
    bool arriving;

    public bool AtStars => elapsed >= AscentSeconds;
    public bool Finished => arriving && elapsed >= DescentSeconds;
    public bool Arriving => arriving;
    public void Advance(double seconds) => elapsed += Math.Max(0, seconds) * flightSpeed;
    public void Arrive() { arriving = true; elapsed = 0; }
    public void Skip() => elapsed = arriving ? DescentSeconds : AscentSeconds;

    public float Apply(WorldCamera camera, float destinationHour, bool destinationReady = false)
    {
        float progress = (float)(elapsed / (arriving ? DescentSeconds : AscentSeconds));
        float t = arriving ? Ease(progress, 0.3f, 0.15f) : Ease(progress);
        if (!arriving)
        {
            Place(camera, destinationReady ? camera.Eye : sourceEye, sourceYaw, float.Lerp(sourcePitch, SkyPitch, t), sourceDistance, sourceFov);
            // Reach night halfway up, while the moons and foreground are still in view.
            return NearestHour(sourceHour, 0, Ease((float)(elapsed / (AscentSeconds * 0.5))));
        }
        var eye = camera.Eye;
        float yaw = sourceYaw + MathF.IEEERemainder(camera.Yaw - sourceYaw, MathF.Tau) * t;
        Place(camera, eye, yaw, float.Lerp(SkyPitch, camera.Pitch, t), camera.Distance, float.Lerp(sourceFov, camera.FieldOfView, t));
        // Restore the destination lighting before terrain enters the frame.
        return ForwardHour(0, destinationHour, Ease(Math.Min(t * 2, 1)));
    }

    // Descent holds its cruising speed longer and brakes briefly at the end; ascent uses symmetric ramps.
    // Integrating smoothstep velocity keeps acceleration continuous through each join and at rest.
    static float Ease(float t, float startRamp = 0.4f, float endRamp = 0.4f)
    {
        t = Math.Clamp(t, 0, 1);
        float area = 1 - (startRamp + endRamp) * 0.5f;
        static float RampDistance(float u) => u * u * u * (1 - u * 0.5f);
        if (t < startRamp) return startRamp * RampDistance(t / startRamp) / area;
        if (t > 1 - endRamp) return 1 - endRamp * RampDistance((1 - t) / endRamp) / area;
        return (t - startRamp * 0.5f) / area;
    }
    /// <summary>The hour <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/> the shorter way round: a dawn title rewinds to midnight rather than running through the day.</summary>
    internal static float NearestHour(float from, float to, float t) =>
        (from + ((to - from + 36) % 24 - 12) * Math.Clamp(t, 0, 1) + 24) % 24;
    internal static float ForwardHour(float from, float to, float t) =>
        (from + ((to - from + 24) % 24) * Math.Clamp(t, 0, 1)) % 24;

    internal static void Place(WorldCamera camera, Vector3 eye, float yaw, float pitch, float distance, float fov)
    {
        camera.Yaw = yaw; camera.Pitch = pitch; camera.Distance = distance; camera.FieldOfView = fov;
        camera.Target = eye + camera.Forward * distance;
    }
}
