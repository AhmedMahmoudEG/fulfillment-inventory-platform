using Fulfillment.Domain.Entities;
using InventoryEntity = Fulfillment.Domain.Entities.Inventory;

namespace Fulfillment.UnitTests.Domain;

public class InventoryTests
{
    [Fact]
    public void NewInventory_DefaultConstructor_InitialQuantityIsZero()
    {
        var inventory = new InventoryEntity();
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void NewInventory_ParameterizedConstructor_SetsProperties()
    {
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var inventory = new InventoryEntity(productId, warehouseId, 0);

        Assert.Equal(productId, inventory.ProductId);
        Assert.Equal(warehouseId, inventory.WarehouseId);
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void Constructor_NegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();

        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryEntity(productId, warehouseId, -1));
    }

    [Fact]
    public void UpdateQuantity_ZeroQuantity_Succeeds()
    {
        var inventory = new InventoryEntity();
        inventory.UpdateQuantity(0);

        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void UpdateQuantity_PositiveQuantity_Succeeds()
    {
        var inventory = new InventoryEntity();
        inventory.UpdateQuantity(150);

        Assert.Equal(150, inventory.Quantity);
    }

    [Fact]
    public void UpdateQuantity_NegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var inventory = new InventoryEntity();

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.UpdateQuantity(-5));
    }
}
