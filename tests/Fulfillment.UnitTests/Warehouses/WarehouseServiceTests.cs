using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Warehouses;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;

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

    [Fact]
    public async Task CreateAsync_ValidWarehouse_Succeeds()
    {
        var repo = new FakeWarehouseRepository();
        var service = new WarehouseService(repo);
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
        var service = new WarehouseService(repo);
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
        var service = new WarehouseService(repo);
        var request = new CreateWarehouseRequest("Main Warehouse", invalidAddress!, "Cairo");

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_OptionalLocation_AcceptsNullOrWhitespaceAsNull()
    {
        var repo = new FakeWarehouseRepository();
        var service = new WarehouseService(repo);
        var request = new CreateWarehouseRequest("Main Warehouse", "123 Main St", "   ");

        var result = await service.CreateAsync(request);

        Assert.Null(result.Location);
    }

    [Fact]
    public async Task CreateAsync_TrimsLeadingAndTrailingWhitespace()
    {
        var repo = new FakeWarehouseRepository();
        var service = new WarehouseService(repo);
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
        var service = new WarehouseService(repo);

        var request = new CreateWarehouseRequest("Main Warehouse", "New St", null);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSoftDeletedName_ThrowsConflictException()
    {
        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(new Warehouse { Name = "Main Warehouse", Address = "Old St", IsDeleted = true });
        var service = new WarehouseService(repo);

        var request = new CreateWarehouseRequest("Main Warehouse", "New St", null);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_SecondActiveWarehouse_ThrowsConflictException()
    {
        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(new Warehouse { Name = "Warehouse 1", Address = "123 St", IsDeleted = false });
        var service = new WarehouseService(repo);

        var request = new CreateWarehouseRequest("Warehouse 2", "456 St", null);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task DeleteAsync_WarehouseWithActiveInventory_ThrowsConflictException()
    {
        var warehouseId = Guid.NewGuid();
        var warehouse = new Warehouse { Id = warehouseId, Name = "Main Warehouse", Address = "123 St" };
        var product = new Product { Name = "Laptop", SKU = "LAP-1", IsDeleted = false };
        warehouse.Inventories.Add(new Inventory(product.Id, warehouseId, 10) { Product = product });

        var repo = new FakeWarehouseRepository();
        repo.Warehouses.Add(warehouse);
        var service = new WarehouseService(repo);

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
        var service = new WarehouseService(repo);

        await service.DeleteAsync(warehouseId);

        Assert.True(warehouse.IsDeleted);
        Assert.True(repo.SaveChangesCalled);
    }
}
