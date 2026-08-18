namespace Fulfillment.Application.Inventory.DTOs;

public record InventoryResponse(
    Guid ProductId,
    string ProductName,
    string SKU,
    Guid WarehouseId,
    string WarehouseName,
    int Quantity);
