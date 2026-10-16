using System.Numerics;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>Damage and healing numbers over the characters (docs/game/ui-screens.md "Damage floaters").</summary>
sealed partial class PlayerInterface
{
    /// <summary>The original's squared distance (4000 units) past which a hit shows no floater.</summary>
    const float FloaterRangeSquared = 1.6e7f;
    const float FloaterSeconds = 1.4f, FloaterRise = 30, FloaterHeight = 15;

    sealed record Live(CharacterId Target, string Text, Vector4 Colour, long Born, float Spread, float Row, FloaterMode Mode)
    {
        public Vector3 Last { get; set; }
    }

    readonly List<Live> floaters = [];
    int floaterCount;

    /// <summary>The "Show damage floaters" option; Off stops the world from queueing them.</summary>
    public FloaterMode Floaters
    {
        get => floaterMode;
        set
        {
            floaterMode = value;
            session.World.Floaters = value == FloaterMode.Off ? null : session.World.Floaters ?? new();
        }
    }

    FloaterMode floaterMode = FloaterMode.Simple;

    void DrawFloaters(DebugOverlay o)
    {
        var world = session.World;
        if (Floaters == FloaterMode.Off)
        {
            world.Floaters = null;
            floaters.Clear();
            return;
        }
        var queue = world.Floaters ??= new();
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        while (queue.TryDequeue(out var f))
        {
            if (f.Kind == FloaterKind.Damage && Vector3.DistanceSquared(Eye, f.Position) >= FloaterRangeSquared) continue;
            if (FloaterText.Format(f, Floaters) is not { } text) continue;
            // Blows landing together would print over each other: spread them side by side and in two rows.
            int n = floaterCount++;
            floaters.Add(new Live(f.Target, text, FloaterText.Colour(f), now, (n % 5 - 2) * 14, n % 2 * 12, Floaters) { Last = f.Position });
        }
        // A long fast-forward queues many at once: only the newest are worth showing.
        if (floaters.Count > 24) floaters.RemoveRange(0, floaters.Count - 24);
        floaters.RemoveAll(l => (now - l.Born) / (double)System.Diagnostics.Stopwatch.Frequency > FloaterSeconds);
        if (floaters.Count == 0) return;

        var ids = floaters.Select(l => l.Target).ToHashSet();
        foreach (var c in session.CurrentSnapshot.Characters)
            if (ids.Contains(c.Id))
                foreach (var l in floaters)
                    if (l.Target == c.Id) l.Last = where(c);
        foreach (var l in floaters)
        {
            float t = (float)((now - l.Born) / (double)System.Diagnostics.Stopwatch.Frequency / FloaterSeconds);
            if (Project(l.Last + new Vector3(0, FloaterHeight, 0)) is not { } at) continue;
            float scale = l.Mode == FloaterMode.Simple ? 1.4f : 1.15f;
            float alpha = t < 0.6f ? 1 : 1 - (t - 0.6f) / 0.4f;
            float x = at.X + l.Spread - o.MeasureText(l.Text, scale) / 2, y = at.Y - l.Row - FloaterRise * (1 - (1 - t) * (1 - t));
            o.TextScaled(l.Text, x + 1, y + 1, new Vector4(0, 0, 0, 0.8f * alpha), scale);
            o.TextScaled(l.Text, x, y, l.Colour with { W = l.Colour.W * alpha }, scale);
        }
    }
}
