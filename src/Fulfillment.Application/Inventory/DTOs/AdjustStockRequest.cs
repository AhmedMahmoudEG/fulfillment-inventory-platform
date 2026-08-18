namespace Fulfillment.Application.Inventory.DTOs;

public record AdjustStockRequest(
    int NewQuantity,
    string? Reason);
