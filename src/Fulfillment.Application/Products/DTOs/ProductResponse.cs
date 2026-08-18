namespace Fulfillment.Application.Products.DTOs;

public record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string SKU,
    decimal Price,
    Guid CategoryId,
    Guid WarehouseId,
    int InventoryQuantity);
