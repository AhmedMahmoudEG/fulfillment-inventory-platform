using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Inventory;
using Fulfillment.Application.Inventory.DTOs;
using Fulfillment.Domain.Entities;
using InventoryEntity = Fulfillment.Domain.Entities.Inventory;

namespace Fulfillment.UnitTests.Inventory;

public class InventoryServiceTests
{
    private class FakeInventoryRepository : IInventoryRepository
    {
        public List<InventoryEntity> Inventories { get; } = new();
        public List<InventoryAdjustment> Adjustments { get; } = new();
        public bool SimulateConcurrencyConflict { get; set; }

        public Task<InventoryEntity?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Inventories.FirstOrDefault(i => i.ProductId == productId && i.Product != null && !i.Product.IsDeleted && i.Warehouse != null && !i.Warehouse.IsDeleted));
        }

        public Task<InventoryEntity?> GetForAdjustmentAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Inventories.FirstOrDefault(i => i.ProductId == productId && i.Product != null && !i.Product.IsDeleted && i.Warehouse != null && !i.Warehouse.IsDeleted));
        }

        public Task<List<InventoryEntity>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Inventories.Where(i => i.Product != null && !i.Product.IsDeleted && i.Warehouse != null && !i.Warehouse.IsDeleted).ToList());
        }

        public Task<List<Fulfillment.Application.Warehouses.DTOs.WarehouseInventoryItemResponse>> GetByWarehouseIdAsync(Guid warehouseId, CancellationToken cancellationToken = default)
        {
            var result = Inventories
                .Where(i => i.WarehouseId == warehouseId && i.Product != null && !i.Product.IsDeleted)
                .Select(i => new Fulfillment.Application.Warehouses.DTOs.WarehouseInventoryItemResponse(i.ProductId, i.Product!.Name, i.Product!.SKU, i.Quantity))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<bool> TryAdjustStockAtomicAsync(
            Guid inventoryId,
            int previousQuantity,
            int newQuantity,
            InventoryAdjustment adjustment,
            CancellationToken cancellationToken = default)
        {
            if (SimulateConcurrencyConflict)
            {
                return Task.FromResult(false);
            }

            var inventory = Inventories.FirstOrDefault(i => i.Id == inventoryId);
            if (inventory == null)
            {
                return Task.FromResult(false);
            }

            inventory.UpdateQuantity(newQuantity);
            Adjustments.Add(adjustment);

            return Task.FromResult(true);
        }
    }

    private readonly FakeInventoryRepository _repository = new();
    private readonly InventoryService _service;
    private readonly Product _activeProduct;
    private readonly Warehouse _activeWarehouse;
    private readonly InventoryEntity _activeInventory;
    private const string TestUserId = "user-123-abc";

    public InventoryServiceTests()
    {
        _service = new InventoryService(_repository);

        var category = new Category { Name = "Category 1" };
        _activeProduct = new Product { Name = "Gaming Laptop", SKU = "LAP-001", Price = 1000m, CategoryId = category.Id };
        _activeWarehouse = new Warehouse { Name = "Main Warehouse", Address = "123 Main St" };

        _activeInventory = new InventoryEntity(_activeProduct.Id, _activeWarehouse.Id, initialQuantity: 10)
        {
            Product = _activeProduct,
            Warehouse = _activeWarehouse
        };

        _repository.Inventories.Add(_activeInventory);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCurrentStockDetails()
    {
        var result = await _service.GetAllAsync();

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(_activeProduct.Id, item.ProductId);
        Assert.Equal("Gaming Laptop", item.ProductName);
        Assert.Equal("LAP-001", item.SKU);
        Assert.Equal(_activeWarehouse.Id, item.WarehouseId);
        Assert.Equal("Main Warehouse", item.WarehouseName);
        Assert.Equal(10, item.Quantity);
    }

    [Fact]
    public async Task GetByProductIdAsync_ActiveProduct_ReturnsCurrentQuantity()
    {
        var result = await _service.GetByProductIdAsync(_activeProduct.Id);

        Assert.NotNull(result);
        Assert.Equal(_activeProduct.Id, result.ProductId);
        Assert.Equal(10, result.Quantity);
    }

    [Fact]
    public async Task GetByProductIdAsync_NonexistentProductId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByProductIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByProductIdAsync_SoftDeletedProduct_ThrowsNotFoundException()
    {
        _activeProduct.IsDeleted = true;
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByProductIdAsync(_activeProduct.Id));
    }

    [Fact]
    public async Task AdjustStockAsync_ValidPositiveNewQuantity_UpdatesQuantityAndCreatesAdjustment()
    {
        var request = new AdjustStockRequest(15, "  Stock received  ");

        var response = await _service.AdjustStockAsync(_activeProduct.Id, request, TestUserId);

        Assert.NotNull(response);
        Assert.Equal(_activeInventory.Id, response.InventoryId);
        Assert.Equal(_activeProduct.Id, response.ProductId);
        Assert.Equal(_activeWarehouse.Id, response.WarehouseId);
        Assert.Equal(10, response.PreviousQuantity);
        Assert.Equal(15, response.NewQuantity);
        Assert.Equal("Stock received", response.Reason);
        Assert.Equal(TestUserId, response.AdjustedByUserId);
        Assert.True((DateTime.UtcNow - response.AdjustedAtUtc).TotalSeconds < 5);

        Assert.Equal(15, _activeInventory.Quantity);
        Assert.Single(_repository.Adjustments);
        var adjustment = _repository.Adjustments.First();
        Assert.Equal(10, adjustment.PreviousQuantity);
        Assert.Equal(15, adjustment.NewQuantity);
    }

    [Fact]
    public async Task AdjustStockAsync_NewQuantityZero_Succeeds()
    {
        var request = new AdjustStockRequest(0, "Cleared stock");

        var response = await _service.AdjustStockAsync(_activeProduct.Id, request, TestUserId);

        Assert.Equal(0, response.NewQuantity);
        Assert.Equal(0, _activeInventory.Quantity);
    }

    [Fact]
    public async Task AdjustStockAsync_NegativeNewQuantity_ThrowsValidationException()
    {
        var request = new AdjustStockRequest(-1, "Invalid negative");

        await Assert.ThrowsAsync<ValidationException>(() => _service.AdjustStockAsync(_activeProduct.Id, request, TestUserId));
    }

    [Fact]
    public async Task AdjustStockAsync_EmptyUserId_ThrowsValidationException()
    {
        var request = new AdjustStockRequest(5, "Reason");

        await Assert.ThrowsAsync<ValidationException>(() => _service.AdjustStockAsync(_activeProduct.Id, request, "   "));
    }

    [Fact]
    public async Task AdjustStockAsync_SoftDeletedProduct_ThrowsNotFoundException()
    {
        _activeProduct.IsDeleted = true;
        var request = new AdjustStockRequest(20, "Reason");

        await Assert.ThrowsAsync<NotFoundException>(() => _service.AdjustStockAsync(_activeProduct.Id, request, TestUserId));
    }

    [Fact]
    public async Task AdjustStockAsync_SoftDeletedWarehouse_ThrowsNotFoundException()
    {
        _activeWarehouse.IsDeleted = true;
        var request = new AdjustStockRequest(20, "Reason");

        await Assert.ThrowsAsync<NotFoundException>(() => _service.AdjustStockAsync(_activeProduct.Id, request, TestUserId));
    }

    [Fact]
    public async Task AdjustStockAsync_ConcurrencyConflict_ThrowsConflictException()
    {
        _repository.SimulateConcurrencyConflict = true;
        var request = new AdjustStockRequest(20, "Reason");

        await Assert.ThrowsAsync<ConflictException>(() => _service.AdjustStockAsync(_activeProduct.Id, request, TestUserId));
    }

    [Fact]
    public void DomainInvariants_QuantityCannotBeNegative()
    {
        var inventory = new InventoryEntity(Guid.NewGuid(), Guid.NewGuid(), initialQuantity: 5);
        Assert.Equal(5, inventory.Quantity);

        inventory.UpdateQuantity(0);
        Assert.Equal(0, inventory.Quantity);

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.UpdateQuantity(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryEntity(Guid.NewGuid(), Guid.NewGuid(), initialQuantity: -1));
    }
}
