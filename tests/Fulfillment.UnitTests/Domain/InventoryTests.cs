using Fulfillment.Domain.Entities;

namespace Fulfillment.UnitTests.Domain;

public class InventoryTests
{
    [Fact]
    public void NewInventory_DefaultConstructor_InitialQuantityIsZero()
    {
        var inventory = new Inventory();
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void NewInventory_ParameterizedConstructor_SetsProperties()
    {
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var inventory = new Inventory(productId, warehouseId, 0);

        Assert.Equal(productId, inventory.ProductId);
        Assert.Equal(warehouseId, inventory.WarehouseId);
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void Constructor_NegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();

        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory(productId, warehouseId, -1));
    }

    [Fact]
    public void UpdateQuantity_ZeroQuantity_Succeeds()
    {
        var inventory = new Inventory();
        inventory.UpdateQuantity(0);

        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void UpdateQuantity_PositiveQuantity_Succeeds()
    {
        var inventory = new Inventory();
        inventory.UpdateQuantity(150);

        Assert.Equal(150, inventory.Quantity);
    }

    [Fact]
    public void UpdateQuantity_NegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var inventory = new Inventory();

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.UpdateQuantity(-5));
    }
}
