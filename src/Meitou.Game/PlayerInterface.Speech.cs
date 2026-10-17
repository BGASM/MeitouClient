using System.Numerics;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>Speech bubbles over the characters that speak (docs/game/dialogue.md "Ambient conversations", the bubble).</summary>
sealed partial class PlayerInterface
{
    /// <summary>
    /// The bubble sits 4 units above the head and fades from opaque at 1000 units from the camera to gone at 2500, the distance counted x0.5 for the large font's events and
    /// x0.75 for the others (<b>Observed</b>, the original's bubble code). The box is Meitou's: the original's is the <c>Kenshi_SpeechPanel</c> layout, not mapped.
    /// </summary>
    void DrawSpeech(DebugOverlay o)
    {
        var speech = session.CurrentSnapshot.Speech;
        if (speech.Count == 0) return;
        float scale = Math.Clamp(Height / 1080f, 0.75f, 1.5f);
        foreach (var bubble in speech)
        {
            if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == bubble.Speaker) is not { } who) continue;
            var head = where(who) + new Vector3(0, CharacterHeight + 4, 0);
            float distance = Vector3.Distance(Eye, head) * (bubble.Large ? 0.5f : 0.75f);
            float alpha = Math.Clamp((2500 - distance) / 1500f, 0, 1);
            if (alpha <= 0 || Project(head) is not { } at) continue;
            float size = (bubble.Large ? 1.3f : 1f) * scale, maxWidth = 280 * scale, pad = 6 * scale;
            var lines = Wrap(o, bubble.Text, size, maxWidth);
            float width = lines.Max(l => o.MeasureText(l, size)), line = o.LineHeight * size;
            float x0 = at.X - width / 2 - pad, y1 = at.Y - 4 * scale, y0 = y1 - lines.Count * line - 2 * pad;
            o.Rect(x0, y0, x0 + width + 2 * pad, y1, new Vector4(0.06f, 0.06f, 0.05f, 0.72f * alpha));
            for (int i = 0; i < lines.Count; i++)
                o.TextScaled(lines[i], at.X - o.MeasureText(lines[i], size) / 2, y0 + pad + i * line, new Vector4(0.96f, 0.94f, 0.86f, alpha), size);
        }
    }

    /// <summary>The words of <paramref name="text"/> in lines no wider than <paramref name="maxWidth"/> pixels (a longer word keeps its own line).</summary>
    static List<string> Wrap(DebugOverlay o, string text, float size, float maxWidth)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string tried = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && o.MeasureText(tried, size) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else current = tried;
        }
        if (current.Length > 0 || lines.Count == 0) lines.Add(current);
        return lines;
    }
}
