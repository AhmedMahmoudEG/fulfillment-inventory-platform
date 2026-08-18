namespace Fulfillment.Application.Products.DTOs;

public record CreateProductRequest(
    string Name,
    string? Description,
    string SKU,
    decimal Price,
    Guid CategoryId,
    Guid WarehouseId);
