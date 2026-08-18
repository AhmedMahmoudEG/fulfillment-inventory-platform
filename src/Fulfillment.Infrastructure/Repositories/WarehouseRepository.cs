using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Application.Warehouses;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.Infrastructure.Repositories;

public class WarehouseRepository : IWarehouseRepository
{
    private readonly ApplicationDbContext _context;

    public WarehouseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<Warehouse?> GetByIdForDeletionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .Include(w => w.Inventories)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<List<Warehouse>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .IgnoreQueryFilters()
            .AnyAsync(w => w.Name == name, cancellationToken);
    }

    public async Task<bool> HasActiveWarehouseAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AnyAsync(cancellationToken);
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        await _context.Warehouses.AddAsync(warehouse, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConflictException("A warehouse with this name already exists.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            return true;
        }

        return ex.Message.Contains("IX_Warehouses_Name", StringComparison.OrdinalIgnoreCase);
    }
}
