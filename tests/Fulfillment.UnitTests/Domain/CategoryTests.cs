using Fulfillment.Domain.Entities;

namespace Fulfillment.UnitTests.Domain;

public class CategoryTests
{
    [Fact]
    public void CanDelete_WhenNoProductsExist_ReturnsTrue()
    {
        var category = new Category { Name = "Electronics" };
        Assert.True(category.CanDelete());
    }

    [Fact]
    public void CanDelete_WhenActiveProductsExist_ReturnsFalse()
    {
        var category = new Category { Name = "Electronics" };
        category.Products.Add(new Product { Name = "Laptop", SKU = "LAP-01", IsDeleted = false });

        Assert.False(category.CanDelete());
    }

    [Fact]
    public void CanDelete_WhenOnlySoftDeletedProductsExist_ReturnsTrue()
    {
        var category = new Category { Name = "Electronics" };
        category.Products.Add(new Product { Name = "Old Laptop", SKU = "LAP-OLD", IsDeleted = true });

        Assert.True(category.CanDelete());
    }
}
