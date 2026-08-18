using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Warehouses;

public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Warehouse?> GetByIdForDeletionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Warehouse>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> HasActiveWarehouseAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
