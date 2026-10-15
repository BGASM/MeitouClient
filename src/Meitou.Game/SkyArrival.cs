using System.Numerics;
using Meitou.Rendering;

namespace Meitou.Game;

/// <summary>Presentation-only flight through the sky; the simulation clock and camera rig never change.</summary>
sealed class SkyArrival(WorldCamera source, float hour)
{
    internal const double AscentSeconds = 2.4, DescentSeconds = 2.8;
    internal const float SkyPitch = -1.45f;
    readonly Vector3 sourceEye = source.Eye;
    readonly float sourceYaw = source.Yaw, sourcePitch = source.Pitch, sourceDistance = source.Distance, sourceFov = source.FieldOfView;
    readonly float sourceHour = hour;
    double elapsed;
    bool arriving;

    public bool AtStars => elapsed >= AscentSeconds;
    public bool Finished => arriving && elapsed >= DescentSeconds;
    public bool Arriving => arriving;
    public void Advance(double seconds) => elapsed += Math.Max(0, seconds);
    public void Arrive() { arriving = true; elapsed = 0; }
    public void Skip() => elapsed = arriving ? DescentSeconds : AscentSeconds;

    public float Apply(WorldCamera camera, float destinationHour, bool destinationReady = false)
    {
        float t = Ease((float)(elapsed / (arriving ? DescentSeconds : AscentSeconds)));
        if (!arriving)
        {
            Place(camera, destinationReady ? camera.Eye : sourceEye, sourceYaw, float.Lerp(sourcePitch, SkyPitch, t), sourceDistance, sourceFov);
            return ForwardHour(sourceHour, 0, t);
        }
        var eye = camera.Eye;
        float yaw = sourceYaw + MathF.IEEERemainder(camera.Yaw - sourceYaw, MathF.Tau) * t;
        Place(camera, eye, yaw, float.Lerp(SkyPitch, camera.Pitch, t), camera.Distance, float.Lerp(sourceFov, camera.FieldOfView, t));
        // Restore the destination lighting before terrain enters the frame.
        return ForwardHour(0, destinationHour, Ease(Math.Min(t * 2, 1)));
    }

    // Quintic easing reaches a hold with both velocity and acceleration at zero.
    static float Ease(float t) { t = Math.Clamp(t, 0, 1); return t * t * t * (t * (t * 6 - 15) + 10); }
    internal static float ForwardHour(float from, float to, float t) =>
        (from + ((to - from + 24) % 24) * Math.Clamp(t, 0, 1)) % 24;

    internal static void Place(WorldCamera camera, Vector3 eye, float yaw, float pitch, float distance, float fov)
    {
        camera.Yaw = yaw; camera.Pitch = pitch; camera.Distance = distance; camera.FieldOfView = fov;
        camera.Target = eye + camera.Forward * distance;
    }
}
