using Meitou.Simulation.Items;

namespace Meitou.Tests.Simulation;

public class InventoryCommandTests
{
    static InventoryItemToken Token(int index, ItemInstance item) => new(index, item.Record, item.Section, item.X, item.Y, item.Quantity, item.Quality);
    [Fact]
    public void MovesRejectStaleIdentityOverlapAndEquipmentAndPreserveTheOldPosition()
    {
        var inventory = new Inventory(4, 2);
        var a = new ItemInstance { Record = "a", Width = 2, Height = 1 };
        var b = new ItemInstance { Record = "b", Width = 2, Height = 1, X = 2 };
        inventory.Items.AddRange([a, b]);
        var original = Token(0, a);
        Assert.False(InventoryCommands.Move(inventory, original, "main", 1, 0));
        Assert.False(InventoryCommands.Move(inventory, original, "head", 0, 0));
        Assert.True(InventoryCommands.Move(inventory, original, "main", 2, 1));
        Assert.False(InventoryCommands.Move(inventory, original, "main", 0, 0));
        Assert.Equal((2, 1), (a.X, a.Y));
    }
    [Fact]
    public void BackpackMovesRespectStackLimitsAndFailedArrangementRollsBack()
    {
        var inventory = new Inventory(3, 2); var bag = new Inventory(2, 2) { StackBonusMult = 5 };
        var backpack = new ItemInstance { Record = "bag", Section = "backpack_attach", Contents = bag };
        inventory.Items.Add(backpack);
        var item = new ItemInstance { Record = "food", Quantity = 5, Section = "backpack_content" }; bag.Items.Add(item);
        Assert.False(InventoryCommands.Move(inventory, Token(1, item), "main", 0, 0));
        item.Quantity = 1;
        Assert.True(InventoryCommands.Move(inventory, Token(1, item), "main", 1, 1));
        Assert.Empty(bag.Items); Assert.Contains(item, inventory.Items);
        var tooBig = new ItemInstance { Record = "oversized", Width = 4, X = 9, Y = 9 }; inventory.Items.Add(tooBig);
        Assert.False(InventoryCommands.Arrange(inventory)); Assert.Equal((1, 1), (item.X, item.Y)); Assert.Equal((9, 9), (tooBig.X, tooBig.Y));
        inventory.Items.Remove(tooBig);
        Assert.True(InventoryCommands.Arrange(inventory)); Assert.Equal((0, 0), (item.X, item.Y));
    }
    [Fact]
    public void TryEquipFillsTheBackThenTheHipAndRefusesWhenBothAreFull()
    {
        var inventory = new Inventory();
        ItemInstance Weapon(int width) => new() { Record = "w", Type = Meitou.Data.Fcs.FcsRecordType.WEAPON, Width = width, Height = 1 };
        var (a, b, c, d, e) = (Weapon(5), Weapon(5), Weapon(10), Weapon(7), Weapon(7));
        Assert.True(InventoryCommands.TryEquip(inventory, a));
        Assert.True(InventoryCommands.TryEquip(inventory, b));
        Assert.True(InventoryCommands.TryEquip(inventory, c));
        Assert.True(InventoryCommands.TryEquip(inventory, d));
        Assert.False(InventoryCommands.TryEquip(inventory, e));
        Assert.Equal(("back", 0, 0, "back", 5, 0), (a.Section, a.X, a.Y, b.Section, b.X, b.Y));
        Assert.Equal(("back", 0, 1, "hip", 0, 0), (c.Section, c.X, c.Y, d.Section, d.X, d.Y));
        Assert.DoesNotContain(e, inventory.Items);
    }
}
