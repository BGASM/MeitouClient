using System.Globalization;
using System.Numerics;

namespace Meitou.Data.Ui;

public static class MyGuiColours
{
    public static readonly Vector4 Ink = new(0.0784314f, 0.0313726f, 0.0235294f, 1);
    public const string InkText = "0.0784314 0.0313726 0.0235294";
    public static Vector4 Parse(string? value, Vector4 fallback)
    {
        if (value is null) return fallback;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4)) return fallback;
        var n = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !float.IsFinite(n[i])) return fallback;
        return new(n[0], n[1], n[2], parts.Length == 4 ? n[3] : 1);
    }
    public static Vector4 Text(MyGuiResources resources, UiControl control, string state, Vector4 fallback)
    {
        var colour = control.TextColour ?? fallback;
        if (control.TextColour is null && resources.Skins.TryGetValue(control.Skin, out var skin))
            foreach (var part in skin.Parts.Where(p => p.Type is "SimpleText" or "EditText"))
            {
                var properties = part.StateProperties?.GetValueOrDefault(state) ?? part.StateProperties?.GetValueOrDefault("normal");
                colour = Parse(properties?.GetValueOrDefault("colour"), colour);
            }
        return Parse(control.Properties.GetValueOrDefault("TextColour"), colour);
    }
}
