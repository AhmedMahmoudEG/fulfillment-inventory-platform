using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.IntegrationTests.PersistenceTests;

public class PersistenceTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public PersistenceTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database=FulfillmentDb_Test_{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true")
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    private ApplicationDbContext CreateContext() => new ApplicationDbContext(_options);

    public void Dispose()
    {
        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task Product_SKUUniqueness_IsEnforced_AndCaseSensitive()
    {
        using var context = CreateContext();
        var category = new Category { Name = "Cat_SKU_Test" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product1 = new Product { Name = "P1", SKU = "ABC-001", Price = 10.00m, CategoryId = category.Id };
        var product2 = new Product { Name = "P2", SKU = "abc-001", Price = 15.00m, CategoryId = category.Id };

        context.Products.AddRange(product1, product2);
        await context.SaveChangesAsync(); // Different case should coexist

        var duplicateProduct = new Product { Name = "P3", SKU = "ABC-001", Price = 20.00m, CategoryId = category.Id };
        context.Products.Add(duplicateProduct);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Category_NameUniqueness_IsEnforced_AndCaseSensitive()
    {
        using var context = CreateContext();
        var cat1 = new Category { Name = "Electronics" };
        var cat2 = new Category { Name = "electronics" };

        context.Categories.AddRange(cat1, cat2);
        await context.SaveChangesAsync(); // Different case should coexist

        var duplicateCat = new Category { Name = "Electronics" };
        context.Categories.Add(duplicateCat);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Warehouse_NameUniqueness_IsEnforced_AndCaseSensitive()
    {
        using var context = CreateContext();
        var w1 = new Warehouse { Name = "Hub-A", Address = "123 St" };
        var w2 = new Warehouse { Name = "hub-a", Address = "456 St" };

        context.Warehouses.AddRange(w1, w2);
        await context.SaveChangesAsync(); // Different case should coexist

        var duplicateW = new Warehouse { Name = "Hub-A", Address = "789 St" };
        context.Warehouses.Add(duplicateW);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Inventory_ProductIdAndWarehouseId_Uniqueness_IsEnforced()
    {
        using var context = CreateContext();
        var cat = new Category { Name = "Cat_Inv_Test" };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P1", SKU = "SKU-INV-1", Price = 10.00m, CategoryId = cat.Id };
        var warehouse = new Warehouse { Name = "W1", Address = "Addr 1" };
        context.Products.Add(product);
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var inv1 = new Inventory(product.Id, warehouse.Id, 5);
        context.Inventories.Add(inv1);
        await context.SaveChangesAsync();

        var invDuplicate = new Inventory(product.Id, warehouse.Id, 10);
        context.Inventories.Add(invDuplicate);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Inventory_QuantityCheckConstraint_RejectsNegativeQuantityAtDatabaseLevel()
    {
        using var context = CreateContext();
        var cat = new Category { Name = "Cat_Check_Test" };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P1", SKU = "SKU-CHK-1", Price = 10.00m, CategoryId = cat.Id };
        var warehouse = new Warehouse { Name = "W1", Address = "Addr 1" };
        context.Products.Add(product);
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        // Use raw SQL to bypass domain check and verify DB check constraint
        var invId = Guid.NewGuid();
        var sql = $"INSERT INTO Inventories (Id, ProductId, WarehouseId, Quantity) VALUES ('{invId}', '{product.Id}', '{warehouse.Id}', -5)";

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(async () =>
        {
            await context.Database.ExecuteSqlRawAsync(sql);
        });
    }

    [Fact]
    public async Task SoftDelete_QueryFilters_ExcludeDeletedEntities_FromNormalQueries()
    {
        using var context = CreateContext();
        var category = new Category { Name = "SoftDel_Cat", IsDeleted = true };
        var product = new Product { Name = "SoftDel_Prod", SKU = "SKU-SD-1", Price = 50.00m, CategoryId = category.Id, IsDeleted = true };
        var warehouse = new Warehouse { Name = "SoftDel_Wh", Address = "Addr", IsDeleted = true };

        context.Categories.Add(category);
        context.Products.Add(product);
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        // Normal queries exclude soft deleted entities
        Assert.Empty(await context.Categories.Where(c => c.Id == category.Id).ToListAsync());
        Assert.Empty(await context.Products.Where(p => p.Id == product.Id).ToListAsync());
        Assert.Empty(await context.Warehouses.Where(w => w.Id == warehouse.Id).ToListAsync());

        // IgnoreQueryFilters includes soft deleted entities
        Assert.NotNull(await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == category.Id));
        Assert.NotNull(await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == product.Id));
        Assert.NotNull(await context.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == warehouse.Id));
    }

    [Fact]
    public async Task Inventory_PersistsIndependentlyOfProductSoftDelete()
    {
        using var context = CreateContext();
        var cat = new Category { Name = "Cat_Indep_Test" };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P_Indep", SKU = "SKU-IND-1", Price = 25.00m, CategoryId = cat.Id };
        var warehouse = new Warehouse { Name = "W_Indep", Address = "Addr" };
        context.Products.Add(product);
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var inventory = new Inventory(product.Id, warehouse.Id, 20);
        context.Inventories.Add(inventory);
        await context.SaveChangesAsync();

        // Soft delete the product
        product.IsDeleted = true;
        await context.SaveChangesAsync();

        // Inventory must still exist and be queryable
        var queriedInventory = await context.Inventories.FirstOrDefaultAsync(i => i.Id == inventory.Id);
        Assert.NotNull(queriedInventory);
        Assert.Equal(20, queriedInventory.Quantity);
    }

    [Fact]
    public async Task InventoryAdjustment_DeleteBehaviorRestrict_PreventsCascadeDelete_AndPreservesHistory()
    {
        Guid inventoryId;
        string userId;
        Guid adjustmentId;

        // Arrange & Seed in context 1
        using (var seedContext = CreateContext())
        {
            var user = new ApplicationUser { UserName = "testuser", Email = "test@example.com" };
            seedContext.Users.Add(user);

            var cat = new Category { Name = "Cat_Adj_Test" };
            seedContext.Categories.Add(cat);
            await seedContext.SaveChangesAsync();

            var product = new Product { Name = "P_Adj", SKU = "SKU-ADJ-1", Price = 30.00m, CategoryId = cat.Id };
            var warehouse = new Warehouse { Name = "W_Adj", Address = "Addr" };
            seedContext.Products.Add(product);
            seedContext.Warehouses.Add(warehouse);
            await seedContext.SaveChangesAsync();

            var inventory = new Inventory(product.Id, warehouse.Id, 10);
            seedContext.Inventories.Add(inventory);
            await seedContext.SaveChangesAsync();

            var adjustment = new InventoryAdjustment
            {
                InventoryId = inventory.Id,
                PreviousQuantity = 0,
                NewQuantity = 10,
                Reason = "Initial Receive",
                AdjustedByUserId = user.Id,
                AdjustedAtUtc = DateTime.UtcNow
            };
            seedContext.InventoryAdjustments.Add(adjustment);
            await seedContext.SaveChangesAsync();

            inventoryId = inventory.Id;
            userId = user.Id;
            adjustmentId = adjustment.Id;
        }

        // 1. Attempting to delete Inventory in a fresh context must throw DbUpdateException (SQL Restrict FK constraint violation)
        using (var deleteInvContext = CreateContext())
        {
            var invToDelete = await deleteInvContext.Inventories.FirstAsync(i => i.Id == inventoryId);
            deleteInvContext.Inventories.Remove(invToDelete);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => deleteInvContext.SaveChangesAsync());
        }

        // 2. Attempting to delete ApplicationUser in a fresh context must throw DbUpdateException (SQL Restrict FK constraint violation)
        using (var deleteUserContext = CreateContext())
        {
            var userToDelete = await deleteUserContext.Users.FirstAsync(u => u.Id == userId);
            deleteUserContext.Users.Remove(userToDelete);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => deleteUserContext.SaveChangesAsync());
        }

        // 3. Verify InventoryAdjustment record remains completely intact after rejected deletion attempts
        using (var verifyContext = CreateContext())
        {
            var queriedAdjustment = await verifyContext.InventoryAdjustments.FirstOrDefaultAsync(ia => ia.Id == adjustmentId);
            Assert.NotNull(queriedAdjustment);
            Assert.Equal("Initial Receive", queriedAdjustment.Reason);
            Assert.Equal(userId, queriedAdjustment.AdjustedByUserId);
        }
    }

    [Fact]
    public async Task Product_PriceColumn_UsesDecimal18_2()
    {
        using var context = CreateContext();
        var cat = new Category { Name = "Cat_Price_Test" };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P_Price", SKU = "SKU-PR-1", Price = 12345678901234.56m, CategoryId = cat.Id };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var storedProduct = await context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(12345678901234.56m, storedProduct.Price);
    }

    [Fact]
    public async Task ForeignKeys_RequiredRelationships_AreEnforced()
    {
        using var context = CreateContext();
        // Inserting product without valid CategoryId should fail FK constraint
        var invalidProduct = new Product { Name = "InvalidP", SKU = "SKU-INV-ERR", Price = 10m, CategoryId = Guid.NewGuid() };
        context.Products.Add(invalidProduct);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
