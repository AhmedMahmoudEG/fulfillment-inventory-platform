namespace Fulfillment.Application.Warehouses.DTOs;

public record WarehouseResponse(Guid Id, string Name, string Address, string? Location);
