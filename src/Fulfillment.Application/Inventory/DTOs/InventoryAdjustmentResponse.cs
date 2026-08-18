namespace Fulfillment.Application.Inventory.DTOs;

public record InventoryAdjustmentResponse(
    Guid InventoryId,
    Guid ProductId,
    Guid WarehouseId,
    int PreviousQuantity,
    int NewQuantity,
    string? Reason,
    string AdjustedByUserId,
    DateTime AdjustedAtUtc);
