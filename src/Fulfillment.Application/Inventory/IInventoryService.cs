using Fulfillment.Application.Inventory.DTOs;

namespace Fulfillment.Application.Inventory;

public interface IInventoryService
{
    Task<List<InventoryResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<InventoryResponse> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<InventoryAdjustmentResponse> AdjustStockAsync(Guid productId, AdjustStockRequest request, string userId, CancellationToken cancellationToken = default);
    Task<List<InventoryAdjustmentResponse>> GetRecentChangesAsync(CancellationToken cancellationToken = default);
}
