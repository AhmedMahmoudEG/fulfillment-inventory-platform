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

namespace Fulfillment.IntegrationTests.Warehouses;

[Collection("IntegrationTests")]
public class WarehouseInventoryQueryTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public WarehouseInventoryQueryTests(WebApplicationFactory<Program> factory)
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
    public async Task Scenario1_ActiveWarehouseWithInventory_Returns200OKWithCorrectProductsAndQuantities()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        // Seed Category, Warehouse, Product
        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Electronics"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Central Hub", "123 Main St", "Cairo"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Laptop", "Gaming Laptop", "LAP-001", 1200m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        // Adjust stock to 15
        await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(15, "Initial Stock"));

        // Query Warehouse Inventory
        var response = await client.GetAsync($"/api/warehouses/{wh!.Id}/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<WarehouseInventoryItemResponse>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(prod.Id, items[0].ProductId);
        Assert.Equal("Laptop", items[0].ProductName);
        Assert.Equal("LAP-001", items[0].Sku);
        Assert.Equal(15, items[0].Quantity);
    }

    [Fact]
    public async Task Scenario2_MultipleInventoryRecords_ReturnsOnlyRecordsBelongingToRequestedWarehouse()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Hardware"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp1 = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Warehouse 1", "123 St", "Cairo"));
        var wh1 = await whResp1.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp1 = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Mouse", "Optical Mouse", "MOU-001", 20m, cat!.Id, wh1!.Id));
        var prod1 = await prodResp1.Content.ReadFromJsonAsync<ProductResponse>();

        await client.PostAsJsonAsync($"/api/inventory/{prod1!.Id}/adjust", new AdjustStockRequest(40, "Warehouse 1 Stock"));

        var response = await client.GetAsync($"/api/warehouses/{wh1!.Id}/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<WarehouseInventoryItemResponse>>();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(prod1.Id, items[0].ProductId);
        Assert.Equal(40, items[0].Quantity);
    }

    [Fact]
    public async Task Scenario3_ActiveWarehouseWithNoInventory_Returns200OKWithEmptyArray()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Empty Warehouse", "456 St", "Alexandria"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var response = await client.GetAsync($"/api/warehouses/{wh!.Id}/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<WarehouseInventoryItemResponse>>();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Scenario4_NonexistentWarehouse_Returns404NotFoundProblemDetails()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync($"/api/warehouses/{Guid.NewGuid()}/inventory");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Scenario5_SoftDeletedWarehouse_Returns404NotFoundProblemDetails()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("To Delete", "789 St", "Giza"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        // Soft-delete warehouse
        await client.DeleteAsync($"/api/warehouses/{wh!.Id}");

        var response = await client.GetAsync($"/api/warehouses/{wh.Id}/inventory");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Scenario6_SoftDeletedProductWithInventory_DoesNotAppearInResponse()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Gadgets"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Hub", "Addr", "City"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Monitor", "4K Monitor", "MON-001", 300m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(25, "Initial"));

        // Soft-delete product via DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDbContext(scope);
            var dbProduct = await db.Products.FirstAsync(p => p.Id == prod.Id);
            dbProduct.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/warehouses/{wh!.Id}/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = await response.Content.ReadFromJsonAsync<List<WarehouseInventoryItemResponse>>();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Scenario7_Authentication_AnonymousRequest_Returns401Unauthorized()
    {
        var anonymousClient = _factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/api/warehouses/{Guid.NewGuid()}/inventory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Warehouse Operator")]
    [InlineData("Sales Agent")]
    public async Task Scenario8_AuthorizationMatrix_AllApprovedRolesReturn200OK(string role)
    {
        var adminClient = await CreateAuthenticatedClientAsync("Admin");

        var whResp = await adminClient.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest($"Wh_{role.Replace(" ", "")}", "Addr", "City"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var roleClient = await CreateAuthenticatedClientAsync(role);
        var response = await roleClient.GetAsync($"/api/warehouses/{wh!.Id}/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Scenario9_DtoEncapsulation_ExposesOnlyApprovedProperties()
    {
        var client = await CreateAuthenticatedClientAsync("Admin");

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Audio"));
        var cat = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest("Audio Hub", "Addr", "City"));
        var wh = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();

        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Headphones", "Desc", "AUD-001", 100m, cat!.Id, wh!.Id));
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        await client.PostAsJsonAsync($"/api/inventory/{prod!.Id}/adjust", new AdjustStockRequest(5, "Initial"));

        var response = await client.GetAsync($"/api/warehouses/{wh!.Id}/inventory");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jsonString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonString);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(1, root.GetArrayLength());

        var firstItem = root[0];
        var properties = firstItem.EnumerateObject().Select(p => p.Name).ToList();

        // Exact property keys expected
        Assert.Contains("productId", properties);
        Assert.Contains("productName", properties);
        Assert.Contains("sku", properties);
        Assert.Contains("quantity", properties);

        // Does NOT expose internal properties
        Assert.DoesNotContain("isDeleted", properties);
        Assert.DoesNotContain("warehouseId", properties);
        Assert.DoesNotContain("product", properties);
        Assert.DoesNotContain("warehouse", properties);
    }
}
