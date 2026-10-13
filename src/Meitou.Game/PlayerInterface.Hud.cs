using System.Numerics;
using Meitou.Engine.Time;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>The minimal HUD: the clock, the speed buttons, the selection panel, rings, paths and the drag box.</summary>
sealed partial class PlayerInterface
{

    static readonly string[] ButtonLabels = ["||", .. SimulationClock.Speeds.Select(s => $"{s:0}x")];
    /// <summary>Pause, then the clock's speeds.</summary>
    static readonly double[] ButtonSpeeds = [0, .. SimulationClock.Speeds];
    const float Margin = 10, Pad = 6;

    float ButtonWidth(DebugOverlay o) => 4 * o.CharWidth + 2 * Pad;
    DebugOverlay? measure;

    (float X0, float Y0, float X1, float Y1) HudBox()
    {
        var o = measure;
        if (o is null) return (0, 0, 0, 0);
        float width = MathF.Max(26 * o.CharWidth, 4 * (ButtonWidth(o) + 4)) + 2 * Pad;
        var selected = Selected();
        int bountyLines = selected.Count == 0 ? 0 : Math.Min(3, selected[0].Bounties.Count) + (selected.Count > 1 && selected.Any(c => c.Bounties.Count > 0) ? 1 : 0);
        float height = Pad + o.LineHeight * 2 + 4 + o.LineHeight + Pad + 6 + o.LineHeight * (1 + Math.Min(8, selected.Count) + 3 + 6 + 10 + bountyLines);
        return (Margin, Margin, Margin + width, Margin + height);
    }

    /// <summary>The bottom of the HUD panel as last drawn (0 before the first frame), where the debug overlays start.</summary>
    public float HudBottom => HudBox().Y1;

    // ---- notices ----

    string? notice;
    bool noticeIsError;
    long noticeUntil;

    /// <summary>Shows a short message at the top of the screen for a few seconds ("Saved", "Loaded", or an error text in red).</summary>
    public void Notice(string text, bool error = false)
    {
        notice = text.ReplaceLineEndings(" ");
        noticeIsError = error;
        noticeUntil = System.Diagnostics.Stopwatch.GetTimestamp() + (long)((error ? 6 : 2.5) * System.Diagnostics.Stopwatch.Frequency);
    }

    /// <summary>Drops the transient state of the mouse (a drag box, a steering right click): the world it belonged to is gone.</summary>
    public void Reset()
    {
        dragFrom = null;
        dragging = false;
        steering = false;
        selectedFor = null;
        selected = [];
    }

    void DrawNotice(DebugOverlay o, int width)
    {
        if (notice is null) return;
        if (System.Diagnostics.Stopwatch.GetTimestamp() > noticeUntil) { notice = null; return; }
        string text = notice.Length > 160 ? notice[..157] + "..." : notice;
        float w = text.Length * o.CharWidth + 4 * Pad, x0 = MathF.Max(Margin, (width - w) / 2), y0 = Margin;
        o.Rect(x0, y0, x0 + w, y0 + o.LineHeight + 2 * Pad, DebugOverlay.PanelColour);
        o.Text(text, x0 + 2 * Pad, y0 + Pad, noticeIsError ? new Vector4(1f, 0.4f, 0.35f, 1f) : new Vector4(0.35f, 1f, 0.45f, 1f));
    }

    bool OverHud(Vector2 p)
    {
        var b = HudBox();
        return p.X >= b.X0 && p.X <= b.X1 && p.Y >= b.Y0 && p.Y <= b.Y1;
    }

    (float X0, float Y0, float X1, float Y1) ButtonBox(int i)
    {
        var o = measure!;
        var box = HudBox();
        float w = ButtonWidth(o), x = box.X0 + Pad + i * (w + 4), y = box.Y0 + Pad + o.LineHeight * 2 + 4;
        return (x, y, x + w, y + o.LineHeight + 4);
    }

    int? HudButtonAt(Vector2 p)
    {
        if (measure is null) return null;
        for (int i = 0; i < ButtonLabels.Length; i++)
        {
            var b = ButtonBox(i);
            if (p.X >= b.X0 && p.X <= b.X1 && p.Y >= b.Y0 && p.Y <= b.Y1) return i;
        }
        return null;
    }

    void PressButton(int index)
    {
        if (index == 0) session.Simulation.TogglePause();
        else session.Simulation.SetSpeed(ButtonSpeeds[index]);
    }

    /// <summary>The selected characters of the current snapshot, found once per snapshot (the HUD box is asked for on every hit test).</summary>
    IReadOnlyList<CharacterSnapshot> Selected()
    {
        var snapshot = session.CurrentSnapshot;
        if (!ReferenceEquals(snapshot, selectedFor))
        {
            selected = [.. snapshot.Characters.Where(c => c.Selected)];
            selectedFor = snapshot;
        }
        return selected;
    }

    WorldSnapshot? selectedFor;
    IReadOnlyList<CharacterSnapshot> selected = [];

    /// <summary>The HUD, the selection rings and the drag box, over the finished picture.</summary>
    public void Draw(DebugOverlay o, int width, int height)
    {
        measure = o;
        Width = width;
        Height = height;
        var white = DebugOverlay.TextColour;
        var green = new Vector4(0.35f, 1f, 0.45f, 1f);
        var red = new Vector4(1f, 0.4f, 0.35f, 1f);

        // Rings under the selected characters.
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (!c.Selected) continue;
            var centre = where(c);
            Vector2? previous = null, first = null;
            for (int i = 0; i <= 24; i++)
            {
                float a = i * MathF.Tau / 24;
                var point = centre + new Vector3(MathF.Sin(a) * 1.6f, 0.15f, MathF.Cos(a) * 1.6f);
                var s = Project(point);
                if (s is null) { previous = null; continue; }
                if (previous is { } prev) o.Line(prev.X, prev.Y, s.Value.X, s.Value.Y, 2, green);
                first ??= s;
                previous = s;
            }
        }

        // The way the selected characters are going (the leg to the next waypoint, then the rest).
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (!c.Selected || c.Path.Count == 0) continue;
            Vector2? last = Project(where(c) + new Vector3(0, 0.3f, 0));
            foreach (var point in c.Path)
            {
                var s = Project(point + new Vector3(0, 0.3f, 0));
                if (s is not null && last is { } l) o.Line(l.X, l.Y, s.Value.X, s.Value.Y, 2, new Vector4(1f, 0.85f, 0.2f, 0.9f));
                last = s;
            }
        }

        // The box.
        if (dragging && dragFrom is { } from)
        {
            var edge = new Vector4(0.6f, 1f, 0.6f, 1);
            o.Rect(MathF.Min(from.X, mouse.X), MathF.Min(from.Y, mouse.Y), MathF.Max(from.X, mouse.X), MathF.Max(from.Y, mouse.Y), new Vector4(0.4f, 1f, 0.5f, 0.12f));
            o.Line(from.X, from.Y, mouse.X, from.Y, 1, edge);
            o.Line(mouse.X, from.Y, mouse.X, mouse.Y, 1, edge);
            o.Line(mouse.X, mouse.Y, from.X, mouse.Y, 1, edge);
            o.Line(from.X, mouse.Y, from.X, from.Y, 1, edge);
        }

        // The panel.
        var box = HudBox();
        o.Rect(box.X0, box.Y0, box.X1, box.Y1, DebugOverlay.PanelColour);
        var clock = session.Clock;
        o.Text($"{clock.TimeText}   {clock.DayText}", box.X0 + Pad, box.Y0 + Pad, white);
        o.Text($"Money: {session.World.Player.Money}", box.X0 + Pad, box.Y0 + Pad + o.LineHeight, white);
        double speed = session.Simulation.Speed;
        for (int i = 0; i < ButtonLabels.Length; i++)
        {
            var b = ButtonBox(i);
            bool active = i == 0 ? speed == 0 : speed == ButtonSpeeds[i];
            o.Rect(b.X0, b.Y0, b.X1, b.Y1, active ? new Vector4(0.25f, 0.55f, 0.3f, 0.95f) : new Vector4(0.2f, 0.2f, 0.22f, 0.95f));
            o.Text(ButtonLabels[i], b.X0 + (b.X1 - b.X0 - ButtonLabels[i].Length * o.CharWidth) / 2, b.Y0 + 2, white);
        }
        float y = ButtonBox(0).Y1 + 6;
        var chosen = Selected();
        var names = chosen.Select(c => string.IsNullOrEmpty(c.Name) ? "?" : c.Name).ToList();
        o.Text(names.Count == 0 ? "Selected: none" : $"Selected ({names.Count}):", box.X0 + Pad, y, white);
        y += o.LineHeight;
        foreach (var name in names.Take(8)) { o.Text(name, box.X0 + Pad + o.CharWidth, y, green); y += o.LineHeight; }
        if (chosen.FirstOrDefault() is { Body: { } body })
        {
            string state = body.Dead ? "dead" : body.Unconscious ? "knocked out" : body.Care == CareKind.Treating ? "treating" : body.Care == CareKind.Resting ? "resting" : "ok";
            if (body.MedicJob) state += "  medic job (H)";
            else if (body.NeedsAid) state += "  needs first aid (right click)";
            if (body.RescueJob) state += "  rescue job (J)";
            if (!chosen[0].Carrying.IsNone) state += "  carrying (K puts down)";
            o.Text($"Health: blood {body.Blood * 100:0}%  worst part {body.WorstPart * 100:0}%  {state}", box.X0 + Pad + o.CharWidth, y, body.Dead || body.Unconscious ? red : white);
            y += o.LineHeight;
            o.Text($"Hunger: {body.Hunger:0.00} of 3", box.X0 + Pad + o.CharWidth, y, body.Hunger < 1 ? red : white);
            y += o.LineHeight;
        }
        // Bounties (the game's status panel: "Bounty:", "-Wanted For:", "The bounty will expire in n hrs" / "Notorious: The bounty will never expire.").
        if (chosen.FirstOrDefault() is { } wanted)
            foreach (var b in wanted.Bounties.Take(3))
            {
                string expiry = double.IsPositiveInfinity(b.HoursLeft) ? "never expires" : $"expires in {Math.Max(0, Math.Ceiling(b.HoursLeft)):0} hrs";
                o.Text($"Bounty: c.{b.Amount} {b.Faction} ({b.Crimes}) {expiry}", box.X0 + Pad + o.CharWidth, y, red);
                y += o.LineHeight;
            }
        int squadBounty = chosen.Sum(c => c.Bounties.Sum(b => b.Amount));
        if (chosen.Count > 1 && squadBounty > 0) { o.Text($"Bounties of the selection: c.{squadBounty}", box.X0 + Pad + o.CharWidth, y, red); y += o.LineHeight; }
        if (chosen.FirstOrDefault() is { Skills: { } skills }) { o.Text($"Atk {skills.Attack:0.0} Def {skills.Defence:0.0} Dodge {skills.Dodge:0.0} Tough {skills.Toughness:0.0} Str {skills.Strength:0.0} Ath {skills.Athletics:0.0}", box.X0 + Pad + o.CharWidth, y, white); y += o.LineHeight; }
        if (chosen.FirstOrDefault() is { } carrier)
            foreach (var line in carrier.Inventory.Take(9)) { o.Text(line, box.X0 + Pad + o.CharWidth, y, white); y += o.LineHeight; }
        // The first selected character's animation layers (what the pose is blended from).
        if (chosen.FirstOrDefault() is { } lead)
            foreach (var layer in lead.Animations.Take(6))
            {
                var text = $"{layer.Name} {layer.Weight:0.00}";
                o.Text(text, box.X0 + Pad + o.CharWidth, y, white);
                y += o.LineHeight;
            }
        DrawNotice(o, width);
        o.Flush(width, height);
    }
}
