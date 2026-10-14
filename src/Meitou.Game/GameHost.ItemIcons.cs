using Meitou.Data.Gameplay;
using Meitou.Rendering.Characters;
using Meitou.Rendering.Ui;

namespace Meitou.Game;

sealed partial class GameHost
{
    /// <summary>The item icons of the install: the files records name, then the images the game cached under <c>data/icons</c> (null without game data).</summary>
    ItemIcons? ItemIconsOfInstall() => scene.Database is { } db ? new(db, assets.Find, new ItemIconCache(Path.Combine(install.DataDirectory, "icons"))) : null;

    /// <summary>Gives the interface the install's item icons and, where the game has no saved picture, the character renderer's drawing of the item.</summary>
    void ConnectItemIcons()
    {
        player.SetItemIcons(ItemIconsOfInstall());
        if (gpu.Characters is { } characters)
            player.SetRenderedIcons(name => UiPainter.TryParseRenderedIcon(name, out string record, out string material) && characters.TryGetIcon(new IconKey(record, material), out var icon) ? icon : null);
    }
}
