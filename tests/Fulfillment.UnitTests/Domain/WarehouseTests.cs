using Fulfillment.Domain.Entities;

namespace Fulfillment.UnitTests.Domain;

public class WarehouseTests
{
    [Fact]
    public void CanDelete_WhenNoInventoryExists_ReturnsTrue()
    {
        var warehouse = new Warehouse { Name = "Main Hub", Address = "123 Street" };
        Assert.True(warehouse.CanDelete());
    }

    [Fact]
    public void CanDelete_WhenActiveProductInventoryExists_ReturnsFalse()
    {
        var warehouse = new Warehouse { Name = "Main Hub", Address = "123 Street" };
        var product = new Product { Name = "Phone", SKU = "PHN-01", IsDeleted = false };
        var inventory = new Inventory(product.Id, warehouse.Id, 10) { Product = product };
        
        warehouse.Inventories.Add(inventory);

        Assert.False(warehouse.CanDelete());
    }

    [Fact]
    public void CanDelete_WhenInventoryProductIsSoftDeleted_ReturnsTrue()
    {
        var warehouse = new Warehouse { Name = "Main Hub", Address = "123 Street" };
        var softDeletedProduct = new Product { Name = "Old Phone", SKU = "PHN-OLD", IsDeleted = true };
        var inventory = new Inventory(softDeletedProduct.Id, warehouse.Id, 10) { Product = softDeletedProduct };

        warehouse.Inventories.Add(inventory);

        Assert.True(warehouse.CanDelete());
    }
}
