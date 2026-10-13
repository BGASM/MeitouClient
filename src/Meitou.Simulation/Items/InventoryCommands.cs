namespace Meitou.Simulation.Items;

/// <summary>Snapshot identity prevents a drag started before eating/looting from moving a different item.</summary>
public sealed record InventoryItemToken(int Index, string Record, string Section, int X, int Y, int Quantity, float Quality);
public sealed record MoveInventoryItem(CharacterId Character, InventoryItemToken Item, string Section, int X, int Y) : SimCommand;
public sealed record ArrangeInventory(CharacterId Character) : SimCommand;

public static class InventoryCommands
{
    public static bool Move(Inventory root, InventoryItemToken token, string section, int x, int y)
    {
        var item = root.All().ElementAtOrDefault(token.Index);
        if (item is null || item.Record != token.Record || item.Section != token.Section || item.X != token.X || item.Y != token.Y || item.Quantity != token.Quantity || item.Quality != token.Quality ||
            item.Contents is not null || item.Section is not ("main" or "backpack_content") || section is not ("main" or "backpack_content")) return false;
        var owner = root.Items.Contains(item) ? root : root.Backpack?.Contents;
        var target = section == "main" ? root : root.Backpack?.Contents;
        if (owner is null || target is null || !owner.Items.Contains(item) || x < 0 || y < 0 || item.Width <= 0 || item.Height <= 0 ||
            x > target.Width - item.Width || y > target.Height - item.Height || item.Quantity > target.LimitOf(item)) return false;
        foreach (var other in target.Items)
            if (other != item && other.Section == section && x < other.X + other.Width && other.X < x + item.Width && y < other.Y + other.Height && other.Y < y + item.Height) return false;
        if (owner != target) { owner.Items.Remove(item); target.Items.Add(item); }
        item.Section = section; item.X = x; item.Y = y; return true;
    }
    public static bool Arrange(Inventory root)
    {
        var saved = root.All().Select(i => (Item: i, i.X, i.Y)).ToArray();
        foreach (var (grid, section) in new[] { (root, "main"), (root.Backpack?.Contents, "backpack_content") })
        {
            if (grid is null) continue;
            var items = grid.Items.Where(i => i.Section == section).OrderByDescending(i => i.Width * (long)i.Height).ToArray();
            foreach (var item in items) { item.X = -item.Width; item.Y = -item.Height; }
            foreach (var item in items)
            {
                if (item.Width <= 0 || item.Height <= 0 || !grid.FindCell(section, item.Width, item.Height, out int x, out int y))
                {
                    foreach (var old in saved) { old.Item.X = old.X; old.Item.Y = old.Y; } return false;
                }
                item.X = x; item.Y = y;
            }
        }
        return true;
    }
}
