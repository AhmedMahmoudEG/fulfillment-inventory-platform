using Fulfillment.Application.Warehouses.DTOs;

namespace Fulfillment.Application.Warehouses;

public interface IWarehouseService
{
    Task<WarehouseResponse> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default);
    Task<List<WarehouseResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<WarehouseResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<WarehouseInventoryItemResponse>> GetWarehouseInventoryAsync(Guid warehouseId, CancellationToken cancellationToken = default);
}
