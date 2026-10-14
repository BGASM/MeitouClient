using Meitou.Data.Gameplay;

namespace Meitou.Game;

sealed partial class GameHost
{
    /// <summary>The item icons of the install: the files records name, then the images the game cached under <c>data/icons</c> (null without game data).</summary>
    ItemIcons? ItemIconsOfInstall() => scene.Database is { } db ? new(db, assets.Find, new ItemIconCache(Path.Combine(install.DataDirectory, "icons"))) : null;
}
