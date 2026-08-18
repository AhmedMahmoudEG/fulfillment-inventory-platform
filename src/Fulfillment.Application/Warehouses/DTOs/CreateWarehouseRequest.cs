namespace Fulfillment.Application.Warehouses.DTOs;

public record CreateWarehouseRequest(string Name, string Address, string? Location);
