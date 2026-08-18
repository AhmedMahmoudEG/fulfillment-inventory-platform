using Fulfillment.Domain.Entities;

namespace Fulfillment.UnitTests.Domain;

public class ProductTests
{
    [Fact]
    public void Product_Initialization_PropertiesAreSet()
    {
        var categoryId = Guid.NewGuid();
        var product = new Product
        {
            Name = "Monitor",
            Description = "4K Display",
            SKU = "MON-4K",
            Price = 299.99m,
            CategoryId = categoryId
        };

        Assert.Equal("Monitor", product.Name);
        Assert.Equal("4K Display", product.Description);
        Assert.Equal("MON-4K", product.SKU);
        Assert.Equal(299.99m, product.Price);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.False(product.IsDeleted);
    }
}
