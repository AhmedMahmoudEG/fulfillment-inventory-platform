using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InventoryEntity = Fulfillment.Domain.Entities.Inventory;

namespace Fulfillment.IntegrationTests.Warehouses;

[Collection("IntegrationTests")]
public class WarehousesControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public WarehousesControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private ApplicationDbContext GetDbContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    public async Task InitializeAsync()
    {
        await ClearActiveWarehousesAndProductsAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearActiveWarehousesAndProductsAsync();
    }

    private async Task ClearActiveWarehousesAndProductsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = GetDbContext(scope);
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM Inventories;
            DELETE FROM Products;
            DELETE FROM Categories;
            DELETE FROM Warehouses;
        ");
    }

    [Fact]
    public async Task CreateWarehouse_ValidRequest_Returns201CreatedAndResponse()
    {
        var client = _factory.CreateClient();
        var uniqueName = $"Main_{Guid.NewGuid():N}";
        var request = new CreateWarehouseRequest(uniqueName, "123 Main Street", "Cairo");

        var response = await client.PostAsJsonAsync("/api/warehouses", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var content = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(content);
        Assert.NotEqual(Guid.Empty, content.Id);
        Assert.Equal(uniqueName, content.Name);
        Assert.Equal("123 Main Street", content.Address);
        Assert.Equal("Cairo", content.Location);

        // Cleanup: soft delete warehouse to allow subsequent active warehouse tests to run
        await client.DeleteAsync($"/api/warehouses/{content.Id}");
    }

    [Fact]
    public async Task CreateWarehouse_TrimsLeadingAndTrailingWhitespace()
    {
        var client = _factory.CreateClient();
        var uniqueName = $"TrimTest_{Guid.NewGuid():N}";
        var request = new CreateWarehouseRequest($"   {uniqueName}   ", "   456 North Ave   ", "   Alexandria   ");

        var response = await client.PostAsJsonAsync("/api/warehouses", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(content);
        Assert.Equal(uniqueName, content.Name);
        Assert.Equal("456 North Ave", content.Address);
        Assert.Equal("Alexandria", content.Location);

        // Cleanup
        await client.DeleteAsync($"/api/warehouses/{content.Id}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateWarehouse_InvalidName_Returns400BadRequest(string? invalidName)
    {
        var client = _factory.CreateClient();
        var request = new CreateWarehouseRequest(invalidName!, "123 Street", "Cairo");

        var response = await client.PostAsJsonAsync("/api/warehouses", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateWarehouse_InvalidAddress_Returns400BadRequest(string? invalidAddress)
    {
        var client = _factory.CreateClient();
        var request = new CreateWarehouseRequest("Valid Name", invalidAddress!, "Cairo");

        var response = await client.PostAsJsonAsync("/api/warehouses", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateWarehouse_SecondActiveWarehouse_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var wh1Name = $"WhActive1_{Guid.NewGuid():N}";
        var createRes1 = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(wh1Name, "Address 1", null));
        var wh1 = await createRes1.Content.ReadFromJsonAsync<WarehouseResponse>();

        var wh2Name = $"WhActive2_{Guid.NewGuid():N}";
        var createRes2 = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(wh2Name, "Address 2", null));

        Assert.Equal(HttpStatusCode.Conflict, createRes2.StatusCode);
        Assert.Equal("application/problem+json", createRes2.Content.Headers.ContentType?.MediaType);

        // Cleanup
        await client.DeleteAsync($"/api/warehouses/{wh1!.Id}");
    }

    [Fact]
    public async Task CreateWarehouse_SameNameAsSoftDeletedWarehouse_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var whName = $"SoftDelWh_{Guid.NewGuid():N}";

        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", null));
        var wh = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        // Soft delete the warehouse
        await client.DeleteAsync($"/api/warehouses/{wh!.Id}");

        // Attempting to recreate using the soft-deleted name returns 409 Conflict
        var recreateRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 2", null));

        Assert.Equal(HttpStatusCode.Conflict, recreateRes.StatusCode);
        Assert.Equal("application/problem+json", recreateRes.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateWarehouse_CaseSensitivity_AllowsDifferentCasesIfNoActiveWarehouse()
    {
        var client = _factory.CreateClient();
        var baseName = $"CaseWh_{Guid.NewGuid():N}";
        var upperName = baseName.ToUpper();
        var lowerName = baseName.ToLower();

        // Create upperName
        var res1 = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(upperName, "Address 1", null));
        var wh1 = await res1.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Soft delete wh1 to satisfy single-active-warehouse constraint
        await client.DeleteAsync($"/api/warehouses/{wh1!.Id}");

        // Create lowerName (allowed because case-sensitive name check sees upper != lower)
        var res2 = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(lowerName, "Address 2", null));
        var wh2 = await res2.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);

        // Cleanup
        await client.DeleteAsync($"/api/warehouses/{wh2!.Id}");
    }

    [Fact]
    public async Task GetAllWarehouses_ReturnsActiveWarehousesOnly()
    {
        var client = _factory.CreateClient();
        var whName = $"GetAllWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", null));
        var wh = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        var getAll1 = await client.GetAsync("/api/warehouses");
        var list1 = await getAll1.Content.ReadFromJsonAsync<List<WarehouseResponse>>();
        Assert.NotNull(list1);
        Assert.Contains(list1, w => w.Id == wh!.Id);

        // Soft delete
        await client.DeleteAsync($"/api/warehouses/{wh!.Id}");

        var getAll2 = await client.GetAsync("/api/warehouses");
        var list2 = await getAll2.Content.ReadFromJsonAsync<List<WarehouseResponse>>();
        Assert.NotNull(list2);
        Assert.DoesNotContain(list2, w => w.Id == wh.Id);
    }

    [Fact]
    public async Task GetWarehouseById_ActiveWarehouse_Returns200OK()
    {
        var client = _factory.CreateClient();
        var whName = $"GetByIdWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", "Cairo"));
        var created = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        var response = await client.GetAsync($"/api/warehouses/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<WarehouseResponse>();
        Assert.NotNull(content);
        Assert.Equal(created.Id, content.Id);
        Assert.Equal(whName, content.Name);

        // Cleanup
        await client.DeleteAsync($"/api/warehouses/{created.Id}");
    }

    [Fact]
    public async Task GetWarehouseById_NonexistentId_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/warehouses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetWarehouseById_SoftDeletedId_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var whName = $"SoftDelGetWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", null));
        var created = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        await client.DeleteAsync($"/api/warehouses/{created!.Id}");

        var response = await client.GetAsync($"/api/warehouses/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWarehouse_WithActiveInventory_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var whName = $"DelWithInvWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", null));
        var warehouse = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        // Create category and active product, then attach inventory to warehouse
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var category = new Category { Name = $"CatForWh_{Guid.NewGuid():N}" };
            dbContext.Categories.Add(category);
            await dbContext.SaveChangesAsync();

            var product = new Product { Name = "ProdForWh", SKU = $"SKU_{Guid.NewGuid():N}", Price = 15m, CategoryId = category.Id, IsDeleted = false };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();

            var inventory = new InventoryEntity(product.Id, warehouse!.Id, 5);
            dbContext.Inventories.Add(inventory);
            await dbContext.SaveChangesAsync();
        }

        var deleteRes = await client.DeleteAsync($"/api/warehouses/{warehouse!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
        Assert.Equal("application/problem+json", deleteRes.Content.Headers.ContentType?.MediaType);

        // Cleanup database inventory & warehouse
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var inv = await dbContext.Inventories.FirstOrDefaultAsync(i => i.WarehouseId == warehouse.Id);
            if (inv != null) dbContext.Inventories.Remove(inv);
            await dbContext.SaveChangesAsync();
        }
        await client.DeleteAsync($"/api/warehouses/{warehouse.Id}");
    }

    [Fact]
    public async Task DeleteWarehouse_WithoutInventory_Returns204NoContentAndSoftDeletesRow()
    {
        var client = _factory.CreateClient();
        var whName = $"DelNoInvWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", null));
        var warehouse = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        var deleteRes = await client.DeleteAsync($"/api/warehouses/{warehouse!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Verify soft deletion preserves the database row
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var dbWarehouse = await dbContext.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == warehouse.Id);
            Assert.NotNull(dbWarehouse);
            Assert.True(dbWarehouse.IsDeleted);
        }
    }

    [Fact]
    public async Task DeleteWarehouse_NonexistentId_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"/api/warehouses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteWarehouse_AlreadySoftDeleted_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var whName = $"AlreadyDelWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", null));
        var warehouse = await createRes.Content.ReadFromJsonAsync<WarehouseResponse>();

        await client.DeleteAsync($"/api/warehouses/{warehouse!.Id}");

        var secondDelete = await client.DeleteAsync($"/api/warehouses/{warehouse.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task WarehouseResponse_DTO_ExposesOnlyPublicData()
    {
        var client = _factory.CreateClient();
        var whName = $"DtoTestWh_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "Address 1", "Location 1"));
        var json = await createRes.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("name", out _));
        Assert.True(root.TryGetProperty("address", out _));
        Assert.True(root.TryGetProperty("location", out _));
        Assert.False(root.TryGetProperty("isDeleted", out _));
        Assert.False(root.TryGetProperty("inventories", out _));

        // Cleanup
        var created = JsonSerializer.Deserialize<WarehouseResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        await client.DeleteAsync($"/api/warehouses/{created!.Id}");
    }
}
