namespace Fulfillment.Application.Warehouses.DTOs;

public record WarehouseInventoryItemResponse(
    Guid ProductId,
    string ProductName,
    string Sku,
    int Quantity);
