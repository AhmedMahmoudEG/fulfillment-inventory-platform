using Fulfillment.Application.Inventory;
using Fulfillment.Application.Inventory.DTOs;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using InventoryEntity = Fulfillment.Domain.Entities.Inventory;

namespace Fulfillment.Infrastructure.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly ApplicationDbContext _context;

    public InventoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryEntity?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Warehouse)
            .FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
    }

    public async Task<InventoryEntity?> GetForAdjustmentAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .Include(i => i.Product)
            .Include(i => i.Warehouse)
            .FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
    }

    public async Task<List<InventoryEntity>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Warehouse)
            .Where(i => i.Product != null && i.Warehouse != null)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WarehouseInventoryItemResponse>> GetByWarehouseIdAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        return await _context.Inventories
            .AsNoTracking()
            .Where(i => i.WarehouseId == warehouseId && i.Product != null && !i.Product.IsDeleted)
            .Select(i => new WarehouseInventoryItemResponse(
                i.ProductId,
                i.Product!.Name,
                i.Product!.SKU,
                i.Quantity))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<InventoryAdjustmentResponse>> GetRecentChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.InventoryAdjustments
            .AsNoTracking()
            .OrderByDescending(a => a.AdjustedAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(20)
            .Select(a => new InventoryAdjustmentResponse(
                a.InventoryId,
                a.Inventory!.ProductId,
                a.Inventory!.WarehouseId,
                a.PreviousQuantity,
                a.NewQuantity,
                a.Reason,
                a.AdjustedByUserId,
                a.AdjustedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAdjustStockAtomicAsync(
        Guid inventoryId,
        int previousQuantity,
        int newQuantity,
        InventoryAdjustment adjustment,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE Inventories 
                  SET Quantity = {0} 
                  WHERE Id = {1} AND Quantity = {2}",
                new object[] { newQuantity, inventoryId, previousQuantity },
                cancellationToken);

            if (rowsAffected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await _context.InventoryAdjustments.AddAsync(adjustment, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
