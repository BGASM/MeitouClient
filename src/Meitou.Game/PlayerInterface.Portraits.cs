using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Rendering.Gpu;
using Meitou.Simulation;

namespace Meitou.Game;

/// <summary>
/// The squad bar's portraits (both UI modes; docs/game/ui-screens.md): a cell per member as the original's <c>Kenshi_PortraitCharacter</c> lays it out (the
/// background, the portrait the renderer draws into its atlas, the name along the bottom, the frame that is the button), left to right on the squad tab's page.
/// </summary>
sealed partial class PlayerInterface
{
    /// <summary>The ImageBox texture name of the renderer's portrait atlas (<see cref="CharacterRenderer.PortraitAtlas"/>).</summary>
    public const string PortraitTexture = "meitou:portraits";
    const int PortraitCells = CharacterRenderer.PortraitColumns * CharacterRenderer.PortraitRows;
    /// <summary>The cell layout's size (Kenshi_PortraitCharacter: 136 x 136); the parts below are placed in its pixels.</summary>
    const float PortraitLayoutSize = 136;

    /// <summary>The characters the squad bar shows, in order: the host has their portraits drawn into the atlas cells of the same index.</summary>
    public IReadOnlyList<CharacterSnapshot> SquadBarMembers() => [.. session.CurrentSnapshot.Characters.Where(c => c.IsPlayer).Take(PortraitCells)];

    /// <summary>Gives the interface a texture drawn at run time (the portrait atlas) under <paramref name="name"/>.</summary>
    public void SetUiTexture(string name, Texture texture, SampledTexture sampled) => uiPainter?.SetTexture(name, texture, sampled);

    void AddSquadPortraits(List<UiControl> controls, UiRect panel)
    {
        var members = SquadBarMembers();
        if (members.Count == 0) return;
        // The tab's page (the layout's TabItem rectangle inside SquadPanel).
        var page = new UiRect(panel.X + panel.Width * 0.0183727f, panel.Y + panel.Height * 0.206897f, panel.Width * 0.965879f, panel.Height * 0.737069f);
        // The original leaves space below portraits instead of filling the tab page (docs/game/ui-screens.md).
        float size = Math.Min(page.Height * 0.72f, page.Width / members.Count), k = size / PortraitLayoutSize;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            float left = page.X + i * size, top = page.Y;
            UiRect At(float x, float y, float w, float h) => new(left + x * k, top + y * k, w * k, h * k);
            bool hurt = m.Body is { } body && (body.Unconscious || body.Dead);
            controls.Add(new($"portrait-back-{i}", "ImageBox", At(0, 0, 135, 135), "", "", "", true,
                new Dictionary<string, string> { ["ImageResource"] = "Kenshi_PortraitImage", ["ImageGroup"] = "Background", ["ImageName"] = hurt ? "Hurt" : "Normal" }, Vector4.Zero));
            int column = i % CharacterRenderer.PortraitColumns, row = i / CharacterRenderer.PortraitColumns, cell = CharacterRenderer.PortraitSize;
            controls.Add(new($"portrait-{i}", "ImageBox", At(6, 6, 124, 123), "", "", "", true,
                new Dictionary<string, string> { ["ImageTexture"] = PortraitTexture, ["ImageFlipV"] = "true", ["ImageRect"] = FormattableString.Invariant($"{column * cell} {row * cell} {cell} {cell}") }, Vector4.Zero));
            controls.Add(new($"portrait-name-{i}", "TextBox", At(4, 4, 128, 126), m.Name, "", "Kenshi_TextboxStandardText", true,
                new Dictionary<string, string> { ["TextAlign"] = "HCenter Bottom", ["TextColour"] = "1 1 1", ["TextShadow"] = "true" }, Vector4.Zero, Pickable: false));
            controls.Add(new($"member-{i}", "Button", At(0, 0, 136, 136), "", "meitou:member", "Kenshi_PortraitFrameSkin", true,
                new Dictionary<string, string> { ["Checked"] = m.Selected ? "true" : "false" }, Vector4.Zero, Argument: i.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
