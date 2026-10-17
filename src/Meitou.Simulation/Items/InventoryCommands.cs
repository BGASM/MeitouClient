namespace Meitou.Simulation.Items;

/// <summary>Snapshot identity prevents a drag started before eating/looting from moving a different item.</summary>
public sealed record InventoryItemToken(int Index, string Record, string Section, int X, int Y, int Quantity, float Quality);
public sealed record MoveInventoryItem(CharacterId Character, InventoryItemToken Item, string Section, int X, int Y) : SimCommand;
public sealed record ArrangeInventory(CharacterId Character) : SimCommand;

public static class InventoryCommands
{
    public static bool IsEquipmentSection(string section) => section is "back" or "hip" or "shirt" or "head" or "legs" or "boots" or "armour" or "belt" or "backpack_attach";
    public static bool IsMovableSection(string section) => section is "main" or "backpack_content" || IsEquipmentSection(section);

    // Engine slot capacities, in inventory cells. Clothing and backpack slots hold one item.
    public static (int Width, int Height) SectionSize(string section) => section switch
    {
        "back" => (10, 2), "hip" => (7, 1), "backpack_attach" => (5, 4),
        "shirt" or "boots" => (4, 2), "head" => (4, 3), "legs" => (4, 5),
        "armour" => (4, 6), "belt" => (2, 2), _ => (0, 0),
    };

    public static bool Move(Inventory root, InventoryItemToken token, string section, int x, int y)
    {
        var item = root.All().ElementAtOrDefault(token.Index);
        if (item is null || item.Record != token.Record || item.Section != token.Section || item.X != token.X || item.Y != token.Y || item.Quantity != token.Quantity || item.Quality != token.Quality ||
            !IsMovableSection(item.Section) || !IsMovableSection(section)) return false;
        var owner = root.Items.Contains(item) ? root : root.Backpack?.Contents;
        var target = section == "backpack_content" ? root.Backpack?.Contents : root;
        // A bag cannot contain itself or another container; moving it preserves all its contents.
        if (section == "backpack_content" && item.Contents is not null) return false;
        var size = section is "back" or "hip" ? SectionSize(section) : IsEquipmentSection(section) ? (item.Width, item.Height) : (target?.Width ?? 0, target?.Height ?? 0);
        if (owner is null || target is null || !owner.Items.Contains(item) || x < 0 || y < 0 || item.Width <= 0 || item.Height <= 0 ||
            x > size.Item1 - item.Width || y > size.Item2 - item.Height || item.Quantity > target.LimitOf(item)) return false;
        if (IsEquipmentSection(section))
        {
            bool allowed = section switch
            {
                "back" or "hip" => item.Type is Meitou.Data.Fcs.FcsRecordType.WEAPON or Meitou.Data.Fcs.FcsRecordType.CROSSBOW,
                "backpack_attach" => item.Type == Meitou.Data.Fcs.FcsRecordType.CONTAINER && item.Contents is not null,
                _ => item.Type == Meitou.Data.Fcs.FcsRecordType.ARMOUR && item.EquipmentSection == section,
            };
            if (!allowed || item.Quantity != 1) return false;
            if (section is not ("back" or "hip") && (x != 0 || y != 0 || target.Items.Any(i => i != item && i.Section == section))) return false;
        }
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
