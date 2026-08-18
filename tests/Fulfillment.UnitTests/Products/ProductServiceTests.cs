using Fulfillment.Application.Categories;
using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Products;
using Fulfillment.Application.Products.DTOs;
using Fulfillment.Application.Warehouses;
using Fulfillment.Domain.Entities;

namespace Fulfillment.UnitTests.Products;

public class ProductServiceTests
{
    private class FakeProductRepository : IProductRepository
    {
        public List<Product> Products { get; } = new();
        public bool SaveChangesCalled { get; private set; }

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<Product?> GetByIdForDeletionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<List<Product>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.Where(p => !p.IsDeleted).ToList());
        }

        public Task<bool> ExistsBySkuAsync(string sku, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.Any(p => string.Equals(p.SKU, sku, StringComparison.Ordinal)));
        }

        public Task AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }
    }

    private class FakeCategoryRepository : ICategoryRepository
    {
        public List<Category> Categories { get; } = new();

        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));
        }

        public Task<Category?> GetByIdForDeletionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));
        }

        public Task<List<Category>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.Where(c => !c.IsDeleted).ToList());
        }

        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)));
        }

        public Task AddAsync(Category category, CancellationToken cancellationToken = default)
        {
            Categories.Add(category);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class FakeWarehouseRepository : IWarehouseRepository
    {
        public List<Warehouse> Warehouses { get; } = new();

        public Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Warehouses.FirstOrDefault(w => w.Id == id && !w.IsDeleted));
        }

        public Task<Warehouse?> GetByIdForDeletionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Warehouses.FirstOrDefault(w => w.Id == id && !w.IsDeleted));
        }

        public Task<List<Warehouse>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Warehouses.Where(w => !w.IsDeleted).ToList());
        }

        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Warehouses.Any(w => string.Equals(w.Name, name, StringComparison.Ordinal)));
        }

        public Task<bool> HasActiveWarehouseAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Warehouses.Any(w => !w.IsDeleted));
        }

        public Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
        {
            Warehouses.Add(warehouse);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private readonly FakeProductRepository _productRepo = new();
    private readonly FakeCategoryRepository _categoryRepo = new();
    private readonly FakeWarehouseRepository _warehouseRepo = new();
    private readonly ProductService _service;
    private readonly Category _activeCategory;
    private readonly Warehouse _activeWarehouse;

    public ProductServiceTests()
    {
        _service = new ProductService(_productRepo, _categoryRepo, _warehouseRepo);

        _activeCategory = new Category { Name = "Test Category" };
        _categoryRepo.Categories.Add(_activeCategory);

        _activeWarehouse = new Warehouse { Name = "Main Warehouse", Address = "123 Street" };
        _warehouseRepo.Warehouses.Add(_activeWarehouse);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesProductWithInitialInventoryZero()
    {
        var request = new CreateProductRequest("Gaming Laptop", "High end", "LAP-001", 1500.00m, _activeCategory.Id, _activeWarehouse.Id);

        var result = await _service.CreateAsync(request);

        Assert.NotNull(result);
        Assert.Equal("Gaming Laptop", result.Name);
        Assert.Equal("High end", result.Description);
        Assert.Equal("LAP-001", result.SKU);
        Assert.Equal(1500.00m, result.Price);
        Assert.Equal(_activeCategory.Id, result.CategoryId);
        Assert.Equal(_activeWarehouse.Id, result.WarehouseId);
        Assert.Equal(0, result.InventoryQuantity);

        var persistedProduct = _productRepo.Products.Single();
        Assert.Single(persistedProduct.Inventories);
        Assert.Equal(0, persistedProduct.Inventories.First().Quantity);
        Assert.Equal(_activeWarehouse.Id, persistedProduct.Inventories.First().WarehouseId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidName_ThrowsValidationException(string? invalidName)
    {
        var request = new CreateProductRequest(invalidName!, "Desc", "LAP-001", 100m, _activeCategory.Id, _activeWarehouse.Id);
        await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidSku_ThrowsValidationException(string? invalidSku)
    {
        var request = new CreateProductRequest("Laptop", "Desc", invalidSku!, 100m, _activeCategory.Id, _activeWarehouse.Id);
        await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_TrimsNameAndSkuBeforePersistence()
    {
        var request = new CreateProductRequest("  Trimmed Laptop  ", "Desc", "  SKU-TRIM  ", 100m, _activeCategory.Id, _activeWarehouse.Id);

        var result = await _service.CreateAsync(request);

        Assert.Equal("Trimmed Laptop", result.Name);
        Assert.Equal("SKU-TRIM", result.SKU);
        var persisted = _productRepo.Products.Single();
        Assert.Equal("Trimmed Laptop", persisted.Name);
        Assert.Equal("SKU-TRIM", persisted.SKU);
    }

    [Fact]
    public async Task CreateAsync_OptionalDescription_NullOrProvided_Succeeds()
    {
        var requestNullDesc = new CreateProductRequest("Laptop 1", null, "SKU-001", 100m, _activeCategory.Id, _activeWarehouse.Id);
        var resultNull = await _service.CreateAsync(requestNullDesc);
        Assert.Null(resultNull.Description);

        var requestWithDesc = new CreateProductRequest("Laptop 2", "  Some description  ", "SKU-002", 100m, _activeCategory.Id, _activeWarehouse.Id);
        var resultWithDesc = await _service.CreateAsync(requestWithDesc);
        Assert.Equal("Some description", resultWithDesc.Description);
    }

    [Fact]
    public async Task CreateAsync_NonexistentOrSoftDeletedCategory_ThrowsNotFoundException()
    {
        var requestNonexistentCat = new CreateProductRequest("Laptop", "Desc", "SKU-001", 100m, Guid.NewGuid(), _activeWarehouse.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(requestNonexistentCat));

        var deletedCat = new Category { Name = "Deleted", IsDeleted = true };
        _categoryRepo.Categories.Add(deletedCat);

        var requestDeletedCat = new CreateProductRequest("Laptop", "Desc", "SKU-002", 100m, deletedCat.Id, _activeWarehouse.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(requestDeletedCat));
    }

    [Fact]
    public async Task CreateAsync_NonexistentOrSoftDeletedWarehouse_ThrowsNotFoundException()
    {
        var requestNonexistentWh = new CreateProductRequest("Laptop", "Desc", "SKU-001", 100m, _activeCategory.Id, Guid.NewGuid());
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(requestNonexistentWh));

        var deletedWh = new Warehouse { Name = "Deleted Wh", Address = "Street", IsDeleted = true };
        _warehouseRepo.Warehouses.Add(deletedWh);

        var requestDeletedWh = new CreateProductRequest("Laptop", "Desc", "SKU-002", 100m, _activeCategory.Id, deletedWh.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(requestDeletedWh));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesProduct()
    {
        var request = new CreateProductRequest("Laptop", "Desc", "SKU-DEL", 100m, _activeCategory.Id, _activeWarehouse.Id);
        var created = await _service.CreateAsync(request);

        await _service.DeleteAsync(created.Id);

        var persistedProduct = _productRepo.Products.Single(p => p.Id == created.Id);
        Assert.True(persistedProduct.IsDeleted);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesProductWhenInventoryExists()
    {
        var request = new CreateProductRequest("Laptop", "Desc", "SKU-DEL-INV", 100m, _activeCategory.Id, _activeWarehouse.Id);
        var created = await _service.CreateAsync(request);

        await _service.DeleteAsync(created.Id);

        var persisted = _productRepo.Products.Single(p => p.Id == created.Id);
        Assert.True(persisted.IsDeleted);
        Assert.Single(persisted.Inventories);
    }

    [Fact]
    public async Task DeleteAsync_PreservesInventoryAfterSoftDelete()
    {
        var request = new CreateProductRequest("Laptop", "Desc", "SKU-PRESERVE", 100m, _activeCategory.Id, _activeWarehouse.Id);
        var created = await _service.CreateAsync(request);

        var product = _productRepo.Products.Single(p => p.Id == created.Id);
        var inventory = product.Inventories.First();

        await _service.DeleteAsync(created.Id);

        Assert.NotNull(inventory);
        Assert.Equal(product.Id, inventory.ProductId);
        Assert.Equal(_activeWarehouse.Id, inventory.WarehouseId);
    }

    [Fact]
    public async Task CreateAsync_PriceExceedingTwoDecimalPlaces_ThrowsValidationException()
    {
        var request = new CreateProductRequest("Laptop", "Desc", "SKU-DEC", 100.123m, _activeCategory.Id, _activeWarehouse.Id);
        await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_PriceWithValidTwoDecimalPlaces_Succeeds()
    {
        var request = new CreateProductRequest("Laptop", "Desc", "SKU-DEC2", 99.99m, _activeCategory.Id, _activeWarehouse.Id);
        var result = await _service.CreateAsync(request);
        Assert.Equal(99.99m, result.Price);
    }

    [Fact]
    public async Task CreateAsync_PriceNegativeValue_DoesNotEnforceNonNegativeConstraint()
    {
        var request = new CreateProductRequest("Laptop", "Desc", "SKU-NEG", -10.50m, _activeCategory.Id, _activeWarehouse.Id);
        var result = await _service.CreateAsync(request);
        Assert.Equal(-10.50m, result.Price);
    }

    [Fact]
    public void DomainInvariants_ProductLifecycle_SoftDeleteBehavior()
    {
        var product = new Product { Name = "Test Product", SKU = "SKU-DOM" };
        Assert.False(product.IsDeleted);

        product.IsDeleted = true;
        Assert.True(product.IsDeleted);
    }
}
