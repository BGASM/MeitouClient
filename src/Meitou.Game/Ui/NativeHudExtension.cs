using System.Globalization;
using Meitou.Ui;
using Meitou.Data.Ui;

namespace Meitou.Game;

/// <summary>The built-in HUD uses the public registration API offered to future code mods.</summary>
sealed class NativeHudExtension(Action<string, string> command) : IUiExtension
{
    public string Id => "meitou";
    public void Register(UiRegistration r)
    {
        string panel = r.RegisterStyle("panel", new() { Skin = "Kenshi_GenericWindowSkin", Background = new(0.13f, 0.12f, 0.1f, 0.98f) });
        string squadPanel = r.RegisterStyle("squad-panel", new() { Skin = "Kenshi_SquadPanelSkin", Background = new(0.13f, 0.12f, 0.1f, 0.98f) });
        string header = r.RegisterStyle("header", new() { Skin = "Kenshi_GenericWindowHeaderSkin", FontName = "Kenshi_PaintedTextFont_Large", Text = new(0.86f, 0.81f, 0.68f, 1), TextScale = 1.15f });
        string title = r.RegisterStyle("title", new() { FontName = "Kenshi_PaintedTextFont_Large", Text = new(0.86f, 0.81f, 0.68f, 1), TextScale = 1.1f });
        string label = r.RegisterStyle("label", new() { Text = new(0.83f, 0.8f, 0.71f, 1) });
        string muted = r.RegisterStyle("muted", new() { Text = new(0.62f, 0.6f, 0.52f, 1), TextScale = 0.85f });
        string squadInk = r.RegisterStyle("squad-ink", new() { Text = new(0.16f, 0.14f, 0.1f, 1), TextScale = 0.9f });
        string squadTitle = r.RegisterStyle("squad-title", new() { Text = new(0.16f, 0.14f, 0.1f, 1), FontName = "Kenshi_PaintedTextFont_Large", TextScale = 1.1f });
        string button = r.RegisterStyle("button", new()
        {
            Skin = "Kenshi_Button1Skin", Background = new(0.19f, 0.17f, 0.13f, 1),
            Hover = new(0.3f, 0.27f, 0.2f, 1), Active = new(0.66f, 0.56f, 0.32f, 1),
            Disabled = new(0.13f, 0.12f, 0.1f, 1), Text = new(0.86f, 0.82f, 0.71f, 1),
            FontName = "Kenshi_PaintedTextFont_Medium", TextAlign = "Center",
        });
        string track = r.RegisterStyle("track", new() { Background = new(0.07f, 0.07f, 0.05f, 1) });
        string health = r.RegisterStyle("health", new() { Background = new(0.38f, 0.42f, 0.24f, 1) });
        string food = r.RegisterStyle("food", new() { Background = new(0.56f, 0.46f, 0.25f, 1) });
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            r.RegisterAction($"speed.{i}", _ => command($"speed:{index}", ""));
        }
        foreach (string name in new[] { "inventory", "stats", "close", "squad", "stop", "medic", "rescue", "member", "buy", "sell", "trade.close", "trade.page", "menu", "context" })
            r.RegisterAction(name, c => command(name, c.Argument));
        foreach (string name in Enum.GetNames<Meitou.Simulation.CharacterOrders>().Where(n => n != "None").Select(n => "order." + n).Concat(["runspeed.-1", "runspeed.1"]))
            r.RegisterAction(name, c => command(name, c.Argument));

        UiNode Text(string id, string value, UiPlacement at, string? style = null) => new(r.Key(id), UiNodeKind.Label) { Style = style ?? label, Placement = at, Text = value };
        UiNode Button(string id, string caption, string action, UiPlacement at, bool enabled = true, string argument = "") => new(r.Key(id), UiNodeKind.Button)
            { Style = button, Placement = at, Text = caption, Action = r.Key(action), Enabled = enabled, Argument = argument };
        UiNode Heading(string id, string caption) => new(r.Key(id))
        {
            Style = header, Placement = new(UiLength.Fill, 38), Padding = 6, Pickable = false,
            Children = { Text(id + "-caption", caption, UiPlacement.Fill, title) },
        };
        UiNode Meter(string id, string caption, string fraction, float top, string fillStyle)
        {
            float.TryParse(fraction, NumberStyles.Float, CultureInfo.InvariantCulture, out float fill);
            var meter = new UiNode(r.Key(id)) { Placement = new(UiLength.Fill, 28, Top: top), Pickable = false };
            meter.Children.Add(Text(id + "-label", caption, new(UiLength.Fill, 20)));
            var bar = new UiNode(r.Key(id + "-track")) { Placement = new(UiLength.Fill, 4, Top: 23), Style = track, Pickable = false };
            bar.Children.Add(new(r.Key(id + "-fill")) { Placement = new(UiLength.Percent(Math.Clamp(fill, 0, 1) * 100), 4), Style = fillStyle, Pickable = false });
            meter.Children.Add(bar);
            return meter;
        }

        string toolbar = r.RegisterControl("toolbar", c =>
        {
            var root = new UiNode(r.Key("toolbar")) { Style = panel, Padding = 12 };
            root.Children.Add(Heading("orders-title", "TIME & ORDERS"));
            root.Children.Add(Text("clock", c.Text("clock"), new(UiLength.Percent(68), 24, Top: 38)));
            root.Children.Add(Text("funds", c.Text("MoneyAmountText"), new(UiLength.Percent(30), 24, Top: 38, Right: 0)));
            var row = new UiNode(r.Key("speed-row")) { Placement = new(UiLength.Fill, 32, Top: 68), Flow = UiFlow.Row, Gap = 8, Pickable = false };
            for (int i = 0; i < 4; i++) row.Children.Add(Button($"speed-{i}", new[] { "PAUSE", "1x", "2x", "5x" }[i], $"speed.{i}", new(UiLength.Percent(23), 32)));
            root.Children.Add(row);
            var orders = new UiNode(r.Key("orders-row")) { Placement = new(UiLength.Fill, 32, Top: 114), Flow = UiFlow.Row, Gap = 8, Pickable = false };
            foreach (string action in new[] { "medic", "rescue", "stop" })
                orders.Children.Add(Button(action, action.ToUpperInvariant(), action, new(UiLength.Percent(31), 32), c.Enabled(action)));
            root.Children.Add(orders);
            return root;
        });
        string selection = r.RegisterControl("selection", c =>
        {
            var root = new UiNode(r.Key("selection")) { Style = panel, Padding = 12 };
            root.Children.Add(Heading("character-title", c.Text("NameText").ToUpperInvariant()));
            root.Children.Add(Text("condition", c.Text("condition", "No character selected"), new(UiLength.Fill, 20, Top: 38), muted));
            root.Children.Add(Meter("blood", c.Text("blood", "Blood --"), c.Text("bloodFraction"), 66, health));
            root.Children.Add(Meter("body", c.Text("body", "Body --"), c.Text("bodyFraction"), 102, health));
            root.Children.Add(Meter("hunger", c.Text("hunger"), c.Text("hungerFraction"), 138, food));
            var details = new UiNode(r.Key("details-row")) { Placement = new(UiLength.Fill, 32, Bottom: 0), Flow = UiFlow.Row, Gap = 12, Pickable = false };
            details.Children.Add(Button("inventory", "INVENTORY", "inventory", new(UiLength.Percent(48), 32)));
            details.Children.Add(Button("stats", "STATS", "stats", new(UiLength.Percent(48), 32)));
            root.Children.Add(details);
            return root;
        });
        string squad = r.RegisterControl("squad", c =>
        {
            var root = new UiNode(r.Key("squad")) { Style = squadPanel, Padding = 14 };
            root.Children.Add(Text("squad-title", "SQUAD", new(120, 22), squadTitle));
            root.Children.Add(Text("members", c.Text("members"), new(230, 22, Right: 0), squadInk));
            int.TryParse(c.Text("squad.count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count);
            for (int i = 0; i < Math.Min(8, count); i++)
                root.Children.Add(Button($"member-{i}", c.Text($"squad.{i}.name"), "member", new(96, 46, Left: i * 104, Top: 32), argument: i.ToString(CultureInfo.InvariantCulture)));
            root.Children.Add(Button("select-squad", "SELECT ALL", "squad", new(112, 28, Right: 0, Bottom: 0)));
            return root;
        });
        r.Contribute(UiSlots.Toolbar, c => c.CreateControl(toolbar), mode: UiMode.Meitou);
        r.Contribute(UiSlots.Selection, c => c.CreateControl(selection), mode: UiMode.Meitou);
        r.Contribute(UiSlots.Squad, c => c.CreateControl(squad), mode: UiMode.Meitou);
    }
}
