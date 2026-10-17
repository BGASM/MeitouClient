using System.Diagnostics;
using System.Globalization;

namespace Meitou.Game;

sealed partial class SettingsMenu
{
    float scrollTarget, menuProgress, contentProgress, frameDelta;
    long lastFrameAt;
    bool pointerDown;
    string? animationScope;
    Dictionary<string, float>? hoverProgress;
    Choice? animatedChoice;
    float choiceProgress;

    static float Ease(float t) => t * t * (3 - 2 * t);

    void AdvanceMotion()
    {
        long now = Stopwatch.GetTimestamp();
        frameDelta = lastFrameAt == 0 ? 1 / 60f : Math.Clamp((float)Stopwatch.GetElapsedTime(lastFrameAt, now).TotalSeconds, 0, 0.05f);
        lastFrameAt = now;
        menuProgress = Math.Clamp(menuProgress + frameDelta / (visible ? 0.14f : -0.1f), 0, 1);
        contentProgress = Math.Min(1, contentProgress + frameDelta / 0.12f);
        if (!visible && menuProgress == 0) player.SetSettingsOpen(false);
    }

    float Hover(string label, bool hovered)
    {
        var states = hoverProgress ??= [];
        string key = $"{page}:{graphicsPage}:{animationScope}:{label}";
        states.TryGetValue(key, out float progress);
        progress = Math.Clamp(progress + frameDelta / (hovered ? 0.16f : -0.16f), 0, 1);
        states[key] = progress;
        return Ease(progress);
    }

    void Opacity(int first, float amount)
    {
        if (amount >= 1) return;
        for (int i = first; i < controls.Count; i++)
        {
            var control = controls[i];
            float old = float.TryParse(control.Properties.GetValueOrDefault("Alpha"), NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha) ? alpha : 1;
            controls[i] = control with
            {
                Properties = new Dictionary<string, string>(control.Properties) { ["Alpha"] = (old * amount).ToString(CultureInfo.InvariantCulture) },
            };
        }
    }

    void AdvanceScroll()
    {
        scrollTarget = Math.Clamp(scrollTarget, 0, maxScroll);
        scroll += (scrollTarget - scroll) * (1 - MathF.Exp(-frameDelta * 20));
        if (MathF.Abs(scrollTarget - scroll) < 0.1f) scroll = scrollTarget;
        scroll = Math.Clamp(scroll, 0, maxScroll);
    }
}
