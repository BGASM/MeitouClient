namespace Meitou.Data.Gameplay;

/// <summary>
/// Where an inventory item's icon image comes from (docs/game/ui-screens.md, "Item icons"). A record with an <c>icon</c> file names a PNG of its own
/// (<see cref="StaticIconOf"/>); the others are drawn by the game from their mesh and kept in <c>data/icons/</c> as <c>&lt;item&gt;.&lt;material&gt;[.&lt;colour&gt;].png</c>
/// (<see cref="ItemIconCache"/>). <see cref="Find"/> tries them in that order; null means neither exists and the caller shows the item's name.
/// </summary>
public sealed class ItemIcons(GameDatabase database, Func<string, string?> resolve, ItemIconCache cache)
{
    /// <summary>The icon file a record names (an install-relative path such as <c>.\data\items\icons\Fuel.png</c>), or "".</summary>
    public static string StaticIconOf(GameRecord record) => record.GetPath("icon");

    /// <summary>The full path of the icon image for an item of record <paramref name="record"/> made of <paramref name="material"/> (the instance's <c>material sid</c>, "" for none), or null.</summary>
    public string? Find(string record, string material)
    {
        if (database.Find(record) is { } r && StaticIconOf(r) is { Length: > 0 } icon && resolve(icon) is { } file) return file;
        return cache.Find(record, material);
    }
}

/// <summary>
/// The icons the game rendered and saved under <c>data/icons/</c> (the cache is made on the player's machine, so it holds only items they have seen).
/// A file name is <c>&lt;item string id&gt;.&lt;material string id&gt;[.&lt;colour string id&gt;].png</c>, a string id being <c>&lt;number&gt;-&lt;file&gt;</c>.
/// </summary>
public sealed class ItemIconCache
{
    readonly Dictionary<string, List<(string Material, string Colour, string Path)>> byItem = new(StringComparer.Ordinal);

    /// <summary>Indexes the PNGs of <paramref name="directory"/> (empty when it does not exist).</summary>
    public ItemIconCache(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.EnumerateFiles(directory, "*.png"))
        {
            if (Parse(System.IO.Path.GetFileNameWithoutExtension(file)) is not var (item, material, colour)) continue;
            if (!byItem.TryGetValue(item, out var list)) byItem[item] = list = [];
            list.Add((material, colour, file));
        }
    }

    /// <summary>Splits a cache file name (without <c>.png</c>) into item, material and colour string ids; null when it is not of that form (the colour is "" when absent).</summary>
    public static (string Item, string Material, string Colour)? Parse(string name)
    {
        var ids = new List<string>(3);
        int start = 0;
        // A string id is "<number>-<file>" and the file may hold dots ("gamedata.base"), so the ids are cut where a number starts after a dot.
        for (int i = 1; i < name.Length; i++)
        {
            if (name[i - 1] != '.' || !char.IsAsciiDigit(name[i])) continue;
            int dash = i;
            while (dash < name.Length && char.IsAsciiDigit(name[dash])) dash++;
            if (dash >= name.Length || name[dash] != '-') continue;
            ids.Add(name[start..(i - 1)]);
            start = i;
        }
        ids.Add(name[start..]);
        return ids.Count is 2 or 3 && ids.All(IsStringId) ? (ids[0], ids[1], ids.Count == 3 ? ids[2] : "") : null;
    }

    static bool IsStringId(string id)
    {
        int dash = id.IndexOf('-');
        return dash > 0 && dash < id.Length - 1 && id[..dash].All(char.IsAsciiDigit);
    }

    /// <summary>The cached icon of <paramref name="item"/> in <paramref name="material"/>: the file of exactly those two ids, else one with the same material (any colour), else any of the item; null when none.</summary>
    public string? Find(string item, string material)
    {
        if (!byItem.TryGetValue(item, out var list)) return null;
        string? sameMaterial = null;
        foreach (var (m, colour, path) in list)
        {
            if (m != material) continue;
            if (colour.Length == 0) return path;
            sameMaterial ??= path;
        }
        return sameMaterial ?? list[0].Path;
    }
}
