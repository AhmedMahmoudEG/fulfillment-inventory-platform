using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Inventory;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Warehouses;

public class WarehouseService : IWarehouseService
{
    private readonly IWarehouseRepository _repository;
    private readonly IInventoryRepository _inventoryRepository;

    public WarehouseService(
        IWarehouseRepository repository,
        IInventoryRepository inventoryRepository)
    {
        _repository = repository;
        _inventoryRepository = inventoryRepository;
    }

    public async Task<WarehouseResponse> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        var trimmedName = request.Name?.Trim();
        var trimmedAddress = request.Address?.Trim();
        var trimmedLocation = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ValidationException("Warehouse name is required.");
        }

        if (string.IsNullOrWhiteSpace(trimmedAddress))
        {
            throw new ValidationException("Warehouse address is required.");
        }

        if (await _repository.HasActiveWarehouseAsync(cancellationToken))
        {
            throw new ConflictException("Only one active warehouse is allowed in the system.");
        }

        if (await _repository.ExistsByNameAsync(trimmedName, cancellationToken))
        {
            throw new ConflictException("A warehouse with this name already exists.");
        }

        var warehouse = new Warehouse
        {
            Name = trimmedName,
            Address = trimmedAddress,
            Location = trimmedLocation
        };

        await _repository.AddAsync(warehouse, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return new WarehouseResponse(warehouse.Id, warehouse.Name, warehouse.Address, warehouse.Location);
    }

    public async Task<List<WarehouseResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var warehouses = await _repository.GetAllActiveAsync(cancellationToken);
        return warehouses.Select(w => new WarehouseResponse(w.Id, w.Name, w.Address, w.Location)).ToList();
    }

    public async Task<WarehouseResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _repository.GetByIdAsync(id, cancellationToken);
        if (warehouse == null)
        {
            throw new NotFoundException("Warehouse not found.");
        }

        return new WarehouseResponse(warehouse.Id, warehouse.Name, warehouse.Address, warehouse.Location);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _repository.GetByIdForDeletionAsync(id, cancellationToken);
        if (warehouse == null)
        {
            throw new NotFoundException("Warehouse not found.");
        }

        if (!warehouse.CanDelete())
        {
            throw new ConflictException("Cannot delete warehouse because active inventory is associated with it.");
        }

        warehouse.IsDeleted = true;
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<WarehouseInventoryItemResponse>> GetWarehouseInventoryAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        var warehouse = await _repository.GetByIdAsync(warehouseId, cancellationToken);
        if (warehouse == null)
        {
            throw new NotFoundException("Warehouse not found.");
        }

        return await _inventoryRepository.GetByWarehouseIdAsync(warehouseId, cancellationToken);
    }
}
