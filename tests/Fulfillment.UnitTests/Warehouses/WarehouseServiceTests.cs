using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Inventory;
using Fulfillment.Application.Warehouses;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;
using InventoryEntity = Fulfillment.Domain.Entities.Inventory;

namespace Fulfillment.UnitTests.Warehouses;

public class WarehouseServiceTests
{
    private class FakeWarehouseRepository : IWarehouseRepository
    {
        public List<Warehouse> Warehouses { get; } = new();
        public bool SaveChangesCalled { get; private set; }

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
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }
    }

    private class FakeInventoryRepository : IInventoryRepository
    {
        public List<WarehouseInventoryItemResponse> ItemsToReturn { get; set; } = new();

        public Task<InventoryEntity?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<InventoryEntity?>(null);
        }

        public Task<InventoryEntity?> GetForAdjustmentAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<InventoryEntity?>(null);
        }

        public Task<List<InventoryEntity>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<InventoryEntity>());
        }

        public Task<List<WarehouseInventoryItemResponse>> GetByWarehouseIdAsync(Guid warehouseId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ItemsToReturn);
        }

        public Task<List<Fulfillment.Application.Inventory.DTOs.InventoryAdjustmentResponse>> GetRecentChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<Fulfillment.Application.Inventory.DTOs.InventoryAdjustmentResponse>());
        }

        public Task<bool> TryAdjustStockAtomicAsync(
            Guid inventoryId,
            int previousQuantity,
            int newQuantity,
            InventoryAdjustment adjustment,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task CreateAsync_ValidWarehouse_Succeeds()
    {
        var repo = new FakeWarehouseRepository();
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);
        var request = new CreateWarehouseRequest("Main Warehouse", "123 Main St", "Cairo");

        var result = await service.CreateAsync(request);

        Assert.Equal("Main Warehouse", result.Name);
        Assert.Equal("123 Main St", result.Address);
        Assert.Equal("Cairo", result.Location);
        Assert.Single(repo.Warehouses);
        Assert.True(repo.SaveChangesCalled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidName_ThrowsValidationException(string? invalidName)
    {
        var repo = new FakeWarehouseRepository();
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);
        var request = new CreateWarehouseRequest(invalidName!, "123 Main St", "Cairo");

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidAddress_ThrowsValidationException(string? invalidAddress)
    {
        var repo = new FakeWarehouseRepository();
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);
        var request = new CreateWarehouseRequest("Main Warehouse", invalidAddress!, "Cairo");

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_OptionalLocation_AcceptsNullOrWhitespaceAsNull()
    {
        var repo = new FakeWarehouseRepository();
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);
        var request = new CreateWarehouseRequest("Main Warehouse", "123 Main St", "   ");

        var result = await service.CreateAsync(request);

        Assert.Null(result.Location);
    }

    [Fact]
    public async Task CreateAsync_TrimsLeadingAndTrailingWhitespace()
    {
        var repo = new FakeWarehouseRepository();
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);
        var request = new CreateWarehouseRequest("  Main Warehouse  ", "  123 Main St  ", "  Cairo  ");

        var result = await service.CreateAsync(request);

        Assert.Equal("Main Warehouse", result.Name);
        Assert.Equal("123 Main St", result.Address);
        Assert.Equal("Cairo", result.Location);
    }

    [Fact]
    public async Task CreateAsync_DuplicateActiveName_ThrowsConflictException()
    {
        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(new Warehouse { Name = "Main Warehouse", Address = "Old St", IsDeleted = false });
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);

        var request = new CreateWarehouseRequest("Main Warehouse", "New St", null);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSoftDeletedName_ThrowsConflictException()
    {
        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(new Warehouse { Name = "Main Warehouse", Address = "Old St", IsDeleted = true });
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);

        var request = new CreateWarehouseRequest("Main Warehouse", "New St", null);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_SecondActiveWarehouse_ThrowsConflictException()
    {
        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(new Warehouse { Name = "Warehouse 1", Address = "123 St", IsDeleted = false });
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);

        var request = new CreateWarehouseRequest("Warehouse 2", "456 St", null);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task DeleteAsync_WarehouseWithActiveInventory_ThrowsConflictException()
    {
        var warehouseId = Guid.NewGuid();
        var warehouse = new Warehouse { Id = warehouseId, Name = "Main Warehouse", Address = "123 St" };
        var product = new Product { Name = "Laptop", SKU = "LAP-1", IsDeleted = false };
        warehouse.Inventories.Add(new InventoryEntity(product.Id, warehouseId, 10) { Product = product });

        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(warehouse);
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(warehouseId));
        Assert.False(warehouse.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WarehouseWithoutActiveInventory_Succeeds()
    {
        var warehouseId = Guid.NewGuid();
        var warehouse = new Warehouse { Id = warehouseId, Name = "Main Warehouse", Address = "123 St" };

        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(warehouse);
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);

        await service.DeleteAsync(warehouseId);

        Assert.True(warehouse.IsDeleted);
        Assert.True(repo.SaveChangesCalled);
    }

    [Fact]
    public async Task GetWarehouseInventoryAsync_ExistingWarehouse_ReturnsInventoryList()
    {
        var warehouseId = Guid.NewGuid();
        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Main Warehouse", Address = "123 St" });

        var invRepo = new FakeInventoryRepository
        {
            ItemsToReturn = new List<WarehouseInventoryItemResponse>
            {
                new(Guid.NewGuid(), "Laptop", "LAP-1", 15),
                new(Guid.NewGuid(), "Mouse", "MOU-1", 40)
            }
        };

        var service = new WarehouseService(repo, invRepo);
        var result = await service.GetWarehouseInventoryAsync(warehouseId);

        Assert.Equal(2, result.Count);
        Assert.Equal("Laptop", result[0].ProductName);
        Assert.Equal("LAP-1", result[0].Sku);
        Assert.Equal(15, result[0].Quantity);
    }

    [Fact]
    public async Task GetWarehouseInventoryAsync_NonexistentWarehouse_ThrowsNotFoundException()
    {
        var repo = new FakeWarehouseRepository();
        var invRepo = new FakeInventoryRepository();
        var service = new WarehouseService(repo, invRepo);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetWarehouseInventoryAsync(Guid.NewGuid()));
    }
}
