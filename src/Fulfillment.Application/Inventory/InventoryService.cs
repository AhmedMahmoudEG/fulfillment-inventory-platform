using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Inventory.DTOs;
using Fulfillment.Domain.Entities;
using InventoryEntity = Fulfillment.Domain.Entities.Inventory;

namespace Fulfillment.Application.Inventory;

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _inventoryRepository;

    public InventoryService(IInventoryRepository inventoryRepository)
    {
        _inventoryRepository = inventoryRepository;
    }

    public async Task<List<InventoryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var inventories = await _inventoryRepository.GetAllActiveAsync(cancellationToken);
        return inventories.Select(MapToResponse).ToList();
    }

    public async Task<InventoryResponse> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var inventory = await _inventoryRepository.GetByProductIdAsync(productId, cancellationToken);
        if (inventory == null)
        {
            throw new NotFoundException("Product inventory not found.");
        }

        return MapToResponse(inventory);
    }

    public async Task<InventoryAdjustmentResponse> AdjustStockAsync(
        Guid productId,
        AdjustStockRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (request.NewQuantity < 0)
        {
            throw new ValidationException("NewQuantity cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ValidationException("User identity is required for stock adjustment.");
        }

        var inventory = await _inventoryRepository.GetForAdjustmentAsync(productId, cancellationToken);
        if (inventory == null)
        {
            throw new NotFoundException("Product inventory not found.");
        }

        var previousQuantity = inventory.Quantity;
        inventory.UpdateQuantity(request.NewQuantity);

        var trimmedReason = request.Reason?.Trim();

        var adjustment = new InventoryAdjustment
        {
            InventoryId = inventory.Id,
            PreviousQuantity = previousQuantity,
            NewQuantity = request.NewQuantity,
            Reason = trimmedReason,
            AdjustedByUserId = userId,
            AdjustedAtUtc = DateTime.UtcNow
        };

        var success = await _inventoryRepository.TryAdjustStockAtomicAsync(
            inventory.Id,
            previousQuantity,
            request.NewQuantity,
            adjustment,
            cancellationToken);

        if (!success)
        {
            throw new ConflictException("The inventory was modified by another user. Please retry.");
        }

        return new InventoryAdjustmentResponse(
            adjustment.InventoryId,
            inventory.ProductId,
            inventory.WarehouseId,
            adjustment.PreviousQuantity,
            adjustment.NewQuantity,
            adjustment.Reason,
            adjustment.AdjustedByUserId,
            adjustment.AdjustedAtUtc);
    }

    public async Task<List<InventoryAdjustmentResponse>> GetRecentChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _inventoryRepository.GetRecentChangesAsync(cancellationToken);
    }

    private static InventoryResponse MapToResponse(InventoryEntity inv)
    {
        return new InventoryResponse(
            inv.ProductId,
            inv.Product?.Name ?? string.Empty,
            inv.Product?.SKU ?? string.Empty,
            inv.WarehouseId,
            inv.Warehouse?.Name ?? string.Empty,
            inv.Quantity);
    }
}
