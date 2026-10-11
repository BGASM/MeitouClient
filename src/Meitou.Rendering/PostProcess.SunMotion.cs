using System.Numerics;

namespace Meitou.Rendering;

/// <summary>
/// The sun's own motion in the upscalers' motion vectors (docs/render-post.md "Sun motion"). The sky's motion is the camera's alone, so while the clock runs
/// and the camera stands still the upscalers read the sun disc and its glow as unmoving and blend last frame's sun into this frame's (DLSS left rows of streaks
/// beside it). With <see cref="SunMotion"/> the sky pixels round the sun also get the sun's motion across the screen since the last frame.
/// </summary>
public sealed partial class PostProcess
{
    /// <summary>Meitou: the sky round the sun moves with the sun in the motion vectors (default on; <c>MEITOU_SUN_MOTION=0</c>, <c>--ab sun-motion</c>).</summary>
    public bool SunMotion { get; set; } = Environment.GetEnvironmentVariable("MEITOU_SUN_MOTION") != "0";

    /// <summary>The angles from the sun's centre (degrees) within which the sky takes all of the sun's motion, and past which none: the disc and its glow.</summary>
    public const float SunMotionInner = 10, SunMotionOuter = 16;
    /// <summary>A larger step of the sun in one frame (degrees) is a jump of the clock, not a motion to follow.</summary>
    const float SunMotionJump = 2;

    Vector3 sunPrevious;
    bool sunPreviousValid;
    /// <summary>This frame's: xyz the direction to the sun, w cos <see cref="SunMotionInner"/>; and xy the motion to add (the motion target's units), z cos <see cref="SunMotionOuter"/>, w 1 when on.</summary>
    (Vector4 Sun, Vector4 Motion) sunMotion;

    /// <summary>The sun's motion for this frame's velocity pass, from the frame globals' sun direction and the last frame's.</summary>
    void PrepareSunMotion()
    {
        var g = Gpu.Globals;
        var raw = ReadGlobal<Vector4>(g, "uAtmoSun");
        var sun = new Vector3(raw.X, raw.Y, raw.Z);
        sunMotion = default;
        if (sun.LengthSquared() < 1e-6f) { sunPreviousValid = false; return; }
        sun = Vector3.Normalize(sun);
        if (SunMotion && sunPreviousValid && previousValid
            && Vector3.Dot(sun, sunPrevious) is float c && c < 1 && c > MathF.Cos(SunMotionJump * MathF.PI / 180)
            && Project(sun) is { } now && Project(sunPrevious) is { } before)
        {
            // Both with this frame's camera: the camera's own motion is the reprojection's part already.
            var delta = (now - before) * 0.5f;
            sunMotion = (new Vector4(sun, MathF.Cos(SunMotionInner * MathF.PI / 180)), new Vector4(delta, MathF.Cos(SunMotionOuter * MathF.PI / 180), 1));
        }
        (sunPrevious, sunPreviousValid) = (sun, true);
    }

    /// <summary>A direction's place on the screen (NDC) with this frame's camera, or null when it is not well in front.</summary>
    Vector2? Project(Vector3 dir)
    {
        var r = viewRotation;
        float x = Vector3.Dot(dir, new Vector3(r.M11, r.M21, r.M31));
        float y = Vector3.Dot(dir, new Vector3(r.M12, r.M22, r.M32));
        float z = -Vector3.Dot(dir, new Vector3(r.M13, r.M23, r.M33));
        if (z < 0.05f) return null;
        float tanY = MathF.Tan(fovNow * 0.5f);
        return new Vector2(x / (z * tanY * aspectNow), y / (z * tanY));
    }
}
