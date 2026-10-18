using System.Numerics;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>
/// The stealth indicators of a sneaking player character (docs/game/perception.md "Spotting a sneaker"): a short pointer towards each hostile NPC noticing (yellow) or
/// detecting (red) it, and a tag over its head tinted by its state, the original's name-tag colours (hidden blue, almost seen yellow, seen red).
/// </summary>
sealed partial class PlayerInterface
{
    // The original's pointer colours (FUN_1408586d0) and name-tag fallback colours (NameTagStealthHidden / AlmostSeen / Seen).
    static readonly Vector4 PointerNoticed = new(1, 0xdd / 255f, 0, 1), PointerDetected = new(0xdd / 255f, 0, 0, 1);
    static readonly Vector4 TagHidden = new(0.25f, 0.521f, 0.745f, 1), TagAlmostSeen = new(0.745f, 0.721f, 0.25f, 1), TagSeen = new(0.745f, 0.25f, 0.29f, 1);

    /// <summary>The original's name-tag range: 1.2e8 squared (about 10954 units) from the camera.</summary>
    const float NameTagRangeSquared = 1.2e8f;

    void DrawStealth(DebugOverlay o)
    {
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (c.Stealth is null) continue;
            var feet = where(c);
            // Pointers 12 units up, out towards the looker by min(d - 7, 15) (the original's figures).
            var up = feet + new Vector3(0, 12, 0);
            foreach (var w in c.Watchers)
            {
                var d = new Vector3(w.Position.X - feet.X, 0, w.Position.Z - feet.Z);
                float dist = d.Length();
                if (dist < 1e-3f) continue;
                d /= dist;
                float len = MathF.Min(dist - 7, 15);
                if (len <= 2) continue;
                var colour = w.Level == StealthExposure.Detected ? PointerDetected : PointerNoticed;
                if (Project(up + d * (len * 0.35f)) is not { } a || Project(up + d * len) is not { } b) continue;
                o.Line(a.X, a.Y, b.X, b.Y, 3, colour);
                var along = b - a;
                if (along.LengthSquared() < 1) continue;
                along = Vector2.Normalize(along);
                var side = new Vector2(-along.Y, along.X) * 6;
                o.Triangle(b + along * 8, b + side, b - side, colour);
            }
        }
    }

    /// <summary>
    /// The name tag over each of the player's characters within the original's range (docs/game/perception.md "Spotting a sneaker"), with the stealth diamond
    /// left of the name while it sneaks.
    /// </summary>
    void DrawNameTags(DebugOverlay o)
    {
        float scale = Math.Clamp(Height / 1080f, 0.75f, 1.5f);
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (!c.IsPlayer || EditorOpen || c.CarriedBy != CharacterId.None) continue;
            var feet = where(c);
            if (Vector3.DistanceSquared(Eye, feet) >= NameTagRangeSquared) continue;
            if (Project(feet + new Vector3(0, CharacterHeight + 4, 0)) is not { } head) continue;
            float half = c.Name.Length > 0 ? o.MeasureText(c.Name, scale) / 2 : 0;
            if (c.Stealth is { } state)
            {
                var tint = state switch { StealthExposure.Detected => TagSeen, StealthExposure.Noticed => TagAlmostSeen, _ => TagHidden };
                var dark = new Vector4(0, 0, 0, 0.6f);
                var d = new Vector2(-half - (half > 0 ? 14 : 0), 0) + head;
                o.Triangle(d + new Vector2(0, -9), d + new Vector2(-9, 0), d + new Vector2(9, 0), dark);
                o.Triangle(d + new Vector2(0, 9), d + new Vector2(9, 0), d + new Vector2(-9, 0), dark);
                o.Triangle(d + new Vector2(0, -7), d + new Vector2(-7, 0), d + new Vector2(7, 0), tint);
                o.Triangle(d + new Vector2(0, 7), d + new Vector2(7, 0), d + new Vector2(-7, 0), tint);
            }
            if (c.Name.Length == 0) continue;
            float x = head.X - half, y = head.Y - 8 * scale;
            o.TextScaled(c.Name, x + 1, y + 1, new Vector4(0, 0, 0, 0.8f), scale);
            o.TextScaled(c.Name, x, y, DebugOverlay.TextColour, scale);
        }
    }
}
