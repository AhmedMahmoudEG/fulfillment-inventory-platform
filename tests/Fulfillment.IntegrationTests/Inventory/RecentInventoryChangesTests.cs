using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fulfillment.Application.Auth.DTOs;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Inventory.DTOs;
using Fulfillment.Application.Products.DTOs;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fulfillment.IntegrationTests.Inventory;

[Collection("IntegrationTests")]
public class RecentInventoryChangesTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public RecentInventoryChangesTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", "TestSigningKeyAtLeast256BitsLongForSecurity12345!");
        });
    }

    private ApplicationDbContext GetDbContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    private async Task ClearAllDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = GetDbContext(scope);
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM InventoryAdjustments;
            DELETE FROM Inventories;
            DELETE FROM Products;
            DELETE FROM Categories;
            DELETE FROM Warehouses;
            DELETE FROM AspNetUserRoles;
            DELETE FROM AspNetUserClaims;
            DELETE FROM AspNetUsers;
        ");
    }

    public async Task InitializeAsync()
    {
        await ClearAllDataAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearAllDataAsync();
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string role = "Admin")
    {
        var email = $"user_{Guid.NewGuid():N}@example.com";
        const string password = "Password123!";

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        await userManager.CreateAsync(user, password);
        await userManager.AddToRoleAsync(user, role);

        var client = _factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.Token);
        return client;
    }

    [Fact]
    public async Task Scenario1_NoAdjustments_Returns200OKWithEmptyArray()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Scenario2_OneAdjustment_Returns200OKWithCorrectDtoData()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Electronics"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Hub 1", "Addr 1", "City 1"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Phone", "Desc", "PH-001", 500m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        var adjustResp = await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(25, "Initial Receipt"));
        var adjustment = await adjustResp.Content.ReadFromJsonAsync<InventoryAdjustmentResponse>();

        var response = await client.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(adjustment!.InventoryId, items[0].InventoryId);
        Assert.Equal(prod.Id, items[0].ProductId);
        Assert.Equal(wh.Id, items[0].WarehouseId);
        Assert.Equal(0, items[0].PreviousQuantity);
        Assert.Equal(25, items[0].NewQuantity);
        Assert.Equal("Initial Receipt", items[0].Reason);
    }

    [Fact]
    public async Task Scenario3_MultipleAdjustments_ReturnsOrderedByAdjustedAtUtcDescending()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Software"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Hub 2", "Addr 2", "City 2"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("App", "Desc", "APP-001", 10m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(10, "First Adjustment"));
        await Task.Delay(10);
        await client.PostAsJsonAsync($"/api/inventory/{prod.Id}/adjust", new AdjustStockRequest(20, "Second Adjustment"));
        await Task.Delay(10);
        await client.PostAsJsonAsync($"/api/inventory/{prod.Id}/adjust", new AdjustStockRequest(30, "Third Adjustment"));

        var response = await client.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Equal(3, items.Count);

        Assert.Equal(30, items[0].NewQuantity);
        Assert.Equal("Third Adjustment", items[0].Reason);

        Assert.Equal(20, items[1].NewQuantity);
        Assert.Equal("Second Adjustment", items[1].Reason);

        Assert.Equal(10, items[2].NewQuantity);
        Assert.Equal("First Adjustment", items[2].Reason);
    }

    [Fact]
    public async Task Scenario4_MoreThan20Adjustments_ReturnsExactlyLatest20()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Tools"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Hub 3", "Addr 3", "City 3"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Hammer", "Desc", "HAM-001", 15m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        for (var i = 1; i <= 25; i++)
        {
            await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(i, $"Adjustment #{i}"));
        }

        var response = await client.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Equal(20, items.Count);
    }

    [Fact]
    public async Task Scenario5_Boundary_Verifies20thNewestIsIncludedAnd21stNewestIsExcluded()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Gadgets"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Hub 4", "Addr 4", "City 4"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Widget", "Desc", "WID-001", 5m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        // Create 25 adjustments (Adjustment #1 is oldest, Adjustment #25 is newest)
        for (var i = 1; i <= 25; i++)
        {
            await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(i, $"Adjustment #{i}"));
        }

        var response = await client.GetAsync("/api/inventory/changes");

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Equal(20, items.Count);

        // Newest returned is Adjustment #25 (index 0)
        Assert.Equal("Adjustment #25", items[0].Reason);

        // 20th newest returned is Adjustment #6 (index 19)
        Assert.Equal("Adjustment #6", items[19].Reason);

        // 21st newest (Adjustment #5 and earlier) must be excluded
        Assert.DoesNotContain(items, item => item.Reason == "Adjustment #5");
        Assert.DoesNotContain(items, item => item.Reason == "Adjustment #1");
    }

    [Fact]
    public async Task Scenario6_SameTimestamp_UsesDeterministicSecondaryOrdering()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        using var scope = _factory.Services.CreateScope();
        var db = GetDbContext(scope);
        var user = await db.Users.FirstAsync();

        var warehouse = new Warehouse { Name = "SameTimeWh", Address = "Addr" };
        var category = new Category { Name = "SameTimeCat" };
        var product = new Product { Name = "SameTimeProd", SKU = "ST-01", Category = category };
        var inventory = new Fulfillment.Domain.Entities.Inventory(product.Id, warehouse.Id, 100);

        db.Warehouses.Add(warehouse);
        db.Categories.Add(category);
        db.Products.Add(product);
        db.Inventories.Add(inventory);
        await db.SaveChangesAsync();

        var fixedTimestamp = DateTime.UtcNow;

        var adj1 = new InventoryAdjustment
        {
            InventoryId = inventory.Id,
            PreviousQuantity = 0,
            NewQuantity = 10,
            Reason = "Adj 1",
            AdjustedByUserId = user.Id,
            AdjustedAtUtc = fixedTimestamp
        };

        var adj2 = new InventoryAdjustment
        {
            InventoryId = inventory.Id,
            PreviousQuantity = 10,
            NewQuantity = 20,
            Reason = "Adj 2",
            AdjustedByUserId = user.Id,
            AdjustedAtUtc = fixedTimestamp
        };

        db.InventoryAdjustments.Add(adj1);
        db.InventoryAdjustments.Add(adj2);
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);

        var expectedTopReason = await db.InventoryAdjustments
            .AsNoTracking()
            .OrderByDescending(a => a.AdjustedAtUtc)
            .ThenByDescending(a => a.Id)
            .Select(a => a.Reason)
            .FirstAsync();

        Assert.Equal(expectedTopReason, items[0].Reason);
    }

    [Fact]
    public async Task Scenario7_Authentication_AnonymousRequest_Returns401Unauthorized()
    {
        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Warehouse Operator")]
    [InlineData("Sales Agent")]
    public async Task Scenario8_AuthorizationMatrix_AllApprovedRolesReturn200OK(string role)
    {
        var roleClient = await CreateAuthenticatedClientAsync(role);
        var response = await roleClient.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Scenario9_DtoEncapsulation_ExposesOnlyApprovedProperties()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("EncapsulationTestCat"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("EncapsulationTestWh", "Addr", "City"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("EncapsulationTestProd", "Desc", "ENC-001", 100m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(5, "Initial"));

        var response = await client.GetAsync("/api/inventory/changes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jsonString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());

        var firstItem = root[0];
        var properties = firstItem.EnumerateObject().Select(p => p.Name).ToList();

        // Exact property keys expected
        Assert.Contains("inventoryId", properties);
        Assert.Contains("productId", properties);
        Assert.Contains("warehouseId", properties);
        Assert.Contains("previousQuantity", properties);
        Assert.Contains("newQuantity", properties);
        Assert.Contains("reason", properties);
        Assert.Contains("adjustedByUserId", properties);
        Assert.Contains("adjustedAtUtc", properties);

        // Does NOT expose EF Core navigation properties
        Assert.DoesNotContain("inventory", properties);
        Assert.DoesNotContain("product", properties);
        Assert.DoesNotContain("warehouse", properties);
    }

    [Fact]
    public async Task Scenario10_AuditPreservation_HistoricalAdjustmentsRemainQueryableWhenProductOrWarehouseIsSoftDeleted()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("AuditCat"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("AuditWh", "Addr", "City"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("AuditProd", "Desc", "AUDIT-01", 50m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(15, "Audit Stock"));

        // Soft-delete product directly in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDbContext(scope);
            var dbProd = await db.Products.FirstAsync(p => p.Id == prod.Id);
            dbProd.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/inventory/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryAdjustmentResponse>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("Audit Stock", items[0].Reason);
        Assert.Equal(prod.Id, items[0].ProductId);
        Assert.Equal(wh.Id, items[0].WarehouseId);
    }
}
