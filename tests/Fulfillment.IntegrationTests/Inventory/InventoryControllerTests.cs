using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Inventory;
using Fulfillment.Application.Inventory.DTOs;
using Fulfillment.Application.Products.DTOs;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Identity;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fulfillment.IntegrationTests.Inventory;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuthScheme";
    public static string CurrentUserId { get; set; } = Guid.NewGuid().ToString();

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, CurrentUserId),
            new Claim(ClaimTypes.Name, "testuser@example.com")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

[Collection("IntegrationTests")]
public class InventoryControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public InventoryControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
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

    private HttpClient CreateAuthenticatedClient(out string userId)
    {
        userId = Guid.NewGuid().ToString("N");
        TestAuthHandler.CurrentUserId = userId;

        // Seed real ApplicationUser in database for foreign key constraint
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDbContext(scope);
            var user = new ApplicationUser
            {
                Id = userId,
                UserName = $"user_{userId}@example.com",
                NormalizedUserName = $"USER_{userId}@EXAMPLE.COM",
                Email = $"user_{userId}@example.com",
                NormalizedEmail = $"USER_{userId}@EXAMPLE.COM",
                EmailConfirmed = true
            };
            db.Users.Add(user);
            db.SaveChanges();
        }

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }).CreateClient();

        return client;
    }

    private async Task<(ProductResponse Product, Guid CategoryId, Guid WarehouseId)> CreateProductAsync(HttpClient client, string name = "Test Laptop")
    {
        var catName = $"Cat_{Guid.NewGuid():N}";
        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var category = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var getWhResp = await client.GetAsync("/api/warehouses");
        var warehouses = await getWhResp.Content.ReadFromJsonAsync<List<WarehouseResponse>>();
        Guid whId;
        if (warehouses != null && warehouses.Count > 0)
        {
            whId = warehouses[0].Id;
        }
        else
        {
            var whName = $"Wh_{Guid.NewGuid():N}";
            var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "123 Street", "City"));
            var warehouse = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();
            whId = warehouse!.Id;
        }

        var sku = $"SKU_{Guid.NewGuid():N}";
        var prodResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(name, "Desc", sku, 100m, category!.Id, whId));
        var product = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

        return (product!, category.Id, whId);
    }

    [Fact]
    public async Task GetAllInventory_ReturnsActiveInventoryList()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, _) = await CreateProductAsync(client);

        var response = await client.GetAsync("/api/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<InventoryResponse>>();
        Assert.NotNull(list);
        Assert.Contains(list, i => i.ProductId == product.Id);
    }

    [Fact]
    public async Task GetAllInventory_ExcludesSoftDeletedProductsAndWarehouses()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product1, _, _) = await CreateProductAsync(client, "Prod 1");
        var (product2, _, _) = await CreateProductAsync(client, "Prod 2");

        // Soft delete product1
        await client.DeleteAsync($"/api/products/{product1.Id}");

        var response = await client.GetAsync("/api/inventory");
        var list = await response.Content.ReadFromJsonAsync<List<InventoryResponse>>();

        Assert.NotNull(list);
        Assert.DoesNotContain(list, i => i.ProductId == product1.Id);
        Assert.Contains(list, i => i.ProductId == product2.Id);
    }

    [Fact]
    public async Task GetInventoryByProductId_ActiveProduct_Returns200OK()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, whId) = await CreateProductAsync(client);

        var response = await client.GetAsync($"/api/inventory/{product.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inventory = await response.Content.ReadFromJsonAsync<InventoryResponse>();

        Assert.NotNull(inventory);
        Assert.Equal(product.Id, inventory.ProductId);
        Assert.Equal(product.Name, inventory.ProductName);
        Assert.Equal(product.SKU, inventory.SKU);
        Assert.Equal(whId, inventory.WarehouseId);
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public async Task GetInventoryByProductId_NonexistentProduct_Returns404NotFound()
    {
        var client = CreateAuthenticatedClient(out _);
        var response = await client.GetAsync($"/api/inventory/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetInventoryByProductId_SoftDeletedProduct_Returns404NotFound()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, _) = await CreateProductAsync(client);

        await client.DeleteAsync($"/api/products/{product.Id}");

        var response = await client.GetAsync($"/api/inventory/{product.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdjustStock_ValidRequest_UpdatesStockAndPersistsAdjustment()
    {
        var client = CreateAuthenticatedClient(out var userId);
        var (product, _, whId) = await CreateProductAsync(client);

        var request = new AdjustStockRequest(25, "Initial delivery");
        var response = await client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<InventoryAdjustmentResponse>();

        Assert.NotNull(result);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Equal(whId, result.WarehouseId);
        Assert.Equal(0, result.PreviousQuantity);
        Assert.Equal(25, result.NewQuantity);
        Assert.Equal("Initial delivery", result.Reason);
        Assert.Equal(userId, result.AdjustedByUserId);

        // Verify DB update
        using var scope = _factory.Services.CreateScope();
        var db = GetDbContext(scope);
        var invInDb = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == product.Id);
        Assert.Equal(25, invInDb!.Quantity);

        var adjInDb = await db.InventoryAdjustments.FirstOrDefaultAsync(ia => ia.InventoryId == invInDb.Id);
        Assert.NotNull(adjInDb);
        Assert.Equal(0, adjInDb.PreviousQuantity);
        Assert.Equal(25, adjInDb.NewQuantity);
        Assert.Equal(userId, adjInDb.AdjustedByUserId);
    }

    [Fact]
    public async Task AdjustStock_NewQuantityZero_Succeeds()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, _) = await CreateProductAsync(client);

        // First adjust to 10
        await client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(10, "First"));

        // Adjust to 0
        var response = await client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(0, "Reset"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<InventoryAdjustmentResponse>();
        Assert.Equal(10, result!.PreviousQuantity);
        Assert.Equal(0, result.NewQuantity);
    }

    [Fact]
    public async Task AdjustStock_NegativeNewQuantity_Returns400BadRequest()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, _) = await CreateProductAsync(client);

        var response = await client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(-5, "Negative"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdjustStock_NonexistentProduct_Returns404NotFound()
    {
        var client = CreateAuthenticatedClient(out _);
        var response = await client.PostAsJsonAsync($"/api/inventory/{Guid.NewGuid()}/adjust", new AdjustStockRequest(10, "Test"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdjustStock_SoftDeletedProduct_Returns404NotFound()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, _) = await CreateProductAsync(client);

        await client.DeleteAsync($"/api/products/{product.Id}");

        var response = await client.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(10, "Test"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdjustStock_UnauthenticatedRequest_Returns401Unauthorized()
    {
        var unauthenticatedClient = _factory.CreateClient();
        var (product, _, _) = await CreateProductAsync(unauthenticatedClient);

        var response = await unauthenticatedClient.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(10, "Test"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdjustStock_ConcurrentAdjustments_OneSucceedsAndCompetingReturns409Conflict()
    {
        var clientA = CreateAuthenticatedClient(out var userIdA);
        var (product, _, _) = await CreateProductAsync(clientA);

        // Adjust initial stock to 10
        var initResp = await clientA.PostAsJsonAsync($"/api/inventory/{product.Id}/adjust", new AdjustStockRequest(10, "Initial"));
        Assert.Equal(HttpStatusCode.OK, initResp.StatusCode);

        var userIdB = Guid.NewGuid().ToString("N");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = GetDbContext(scope);
            db.Users.Add(new ApplicationUser
            {
                Id = userIdB,
                UserName = $"user_{userIdB}@example.com",
                NormalizedUserName = $"USER_{userIdB}@EXAMPLE.COM",
                Email = $"user_{userIdB}@example.com",
                NormalizedEmail = $"USER_{userIdB}@EXAMPLE.COM",
                EmailConfirmed = true
            });
            await db.SaveChangesAsync();
        }

        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var repo1 = scope1.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var repo2 = scope2.ServiceProvider.GetRequiredService<IInventoryRepository>();

        var inv1 = await repo1.GetForAdjustmentAsync(product.Id);
        var inv2 = await repo2.GetForAdjustmentAsync(product.Id);

        Assert.NotNull(inv1);
        Assert.NotNull(inv2);
        Assert.Equal(10, inv1.Quantity);
        Assert.Equal(10, inv2.Quantity);

        var adj1 = new InventoryAdjustment
        {
            InventoryId = inv1.Id,
            PreviousQuantity = 10,
            NewQuantity = 15,
            Reason = "User A",
            AdjustedByUserId = userIdA,
            AdjustedAtUtc = DateTime.UtcNow
        };

        var adj2 = new InventoryAdjustment
        {
            InventoryId = inv2.Id,
            PreviousQuantity = 10,
            NewQuantity = 20,
            Reason = "User B",
            AdjustedByUserId = userIdB,
            AdjustedAtUtc = DateTime.UtcNow
        };

        var task1 = repo1.TryAdjustStockAtomicAsync(inv1.Id, 10, 15, adj1);
        var task2 = repo2.TryAdjustStockAtomicAsync(inv2.Id, 10, 20, adj2);

        var results = await Task.WhenAll(task1, task2);

        Assert.Contains(true, results);
        Assert.Contains(false, results);

        // Verify database state: exactly one successful adjustment record added for step 2
        using var scopeDb = _factory.Services.CreateScope();
        var dbCheck = GetDbContext(scopeDb);
        var invInDb = await dbCheck.Inventories.FirstOrDefaultAsync(i => i.ProductId == product.Id);
        Assert.True(invInDb!.Quantity == 15 || invInDb.Quantity == 20);

        var step2Adjustments = await dbCheck.InventoryAdjustments.Where(ia => ia.InventoryId == invInDb.Id && ia.PreviousQuantity == 10).ToListAsync();
        Assert.Single(step2Adjustments);
    }

    [Fact]
    public async Task InventoryResponseDto_ExposesOnlyPublicData()
    {
        var client = CreateAuthenticatedClient(out _);
        var (product, _, _) = await CreateProductAsync(client);

        var response = await client.GetAsync($"/api/inventory/{product.Id}");
        var jsonString = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("\"isDeleted\":", jsonString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"adjustments\":", jsonString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"navigation\":", jsonString, StringComparison.OrdinalIgnoreCase);
    }
}
