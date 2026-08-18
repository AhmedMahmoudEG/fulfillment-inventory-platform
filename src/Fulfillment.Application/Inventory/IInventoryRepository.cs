using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Inventory;

public interface IInventoryRepository
{
    Task<Fulfillment.Domain.Entities.Inventory?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<Fulfillment.Domain.Entities.Inventory?> GetForAdjustmentAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<List<Fulfillment.Domain.Entities.Inventory>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<List<WarehouseInventoryItemResponse>> GetByWarehouseIdAsync(Guid warehouseId, CancellationToken cancellationToken = default);
    Task<bool> TryAdjustStockAtomicAsync(
        Guid inventoryId,
        int previousQuantity,
        int newQuantity,
        InventoryAdjustment adjustment,
        CancellationToken cancellationToken = default);
}
