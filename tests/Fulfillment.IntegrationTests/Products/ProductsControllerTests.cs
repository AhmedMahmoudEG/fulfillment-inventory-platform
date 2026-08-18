using System.Net;
using System.Net.Http.Json;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Products.DTOs;
using Fulfillment.Application.Warehouses.DTOs;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fulfillment.IntegrationTests.Products;

[Collection("IntegrationTests")]
public class ProductsControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductsControllerTests(WebApplicationFactory<Program> factory)
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
            DELETE FROM Inventories;
            DELETE FROM Products;
            DELETE FROM Categories;
            DELETE FROM Warehouses;
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

    private async Task<(Guid CategoryId, Guid WarehouseId)> CreateActiveCategoryAndWarehouseAsync(HttpClient client)
    {
        var catName = $"Category_{Guid.NewGuid():N}";
        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        Assert.Equal(HttpStatusCode.Created, catResp.StatusCode);
        var category = await catResp.Content.ReadFromJsonAsync<CategoryResponse>();

        var getWhResp = await client.GetAsync("/api/warehouses");
        var warehouses = await getWhResp.Content.ReadFromJsonAsync<List<WarehouseResponse>>();

        Guid warehouseId;
        if (warehouses != null && warehouses.Count > 0)
        {
            warehouseId = warehouses[0].Id;
        }
        else
        {
            var whName = $"Warehouse_{Guid.NewGuid():N}";
            var whResp = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest(whName, "123 Main St", "City"));
            Assert.Equal(HttpStatusCode.Created, whResp.StatusCode);
            var warehouse = await whResp.Content.ReadFromJsonAsync<WarehouseResponse>();
            warehouseId = warehouse!.Id;
        }

        return (category!.Id, warehouseId);
    }

    [Fact]
    public async Task CreateProduct_ValidPayload_Returns201Created()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_{Guid.NewGuid():N}";
        var request = new CreateProductRequest("Gaming Laptop", "High performance", sku, 25000.00m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var content = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(content);
        Assert.NotEqual(Guid.Empty, content.Id);
        Assert.Equal("Gaming Laptop", content.Name);
        Assert.Equal("High performance", content.Description);
        Assert.Equal(sku, content.SKU);
        Assert.Equal(25000.00m, content.Price);
        Assert.Equal(catId, content.CategoryId);
        Assert.Equal(whId, content.WarehouseId);
        Assert.Equal(0, content.InventoryQuantity);
    }

    [Fact]
    public async Task CreateProduct_CreatesInitialInventoryAutomaticallyWithQuantityZero()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_INV_{Guid.NewGuid():N}";
        var request = new CreateProductRequest("Test Product", "Desc", sku, 100m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);
        var content = await response.Content.ReadFromJsonAsync<ProductResponse>();

        using var scope = _factory.Services.CreateScope();
        var dbContext = GetDbContext(scope);
        var inventory = await dbContext.Inventories.FirstOrDefaultAsync(i => i.ProductId == content!.Id && i.WarehouseId == whId);

        Assert.NotNull(inventory);
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public async Task CreateProduct_ProductAndInventoryCreationIsAtomic()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_ATOMIC_{Guid.NewGuid():N}";
        var request = new CreateProductRequest("Atomic Product", "Desc", sku, 100m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<ProductResponse>();

        using var scope = _factory.Services.CreateScope();
        var dbContext = GetDbContext(scope);
        var productInDb = await dbContext.Products.Include(p => p.Inventories).FirstOrDefaultAsync(p => p.Id == content!.Id);

        Assert.NotNull(productInDb);
        Assert.Single(productInDb.Inventories);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateProduct_InvalidName_Returns400BadRequest(string? invalidName)
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest(invalidName!, "Desc", $"SKU_{Guid.NewGuid():N}", 100m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateProduct_InvalidSku_Returns400BadRequest(string? invalidSku)
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", invalidSku!, 100m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateProduct_MissingCategoryId_Returns400BadRequest()
    {
        var client = _factory.CreateClient();
        var (_, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 100m, Guid.Empty, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_NonexistentCategory_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var (_, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 100m, Guid.NewGuid(), whId);

        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_SoftDeletedCategory_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var (_, whId) = await CreateActiveCategoryAndWarehouseAsync(client);

        Guid softDeletedCatId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var softDeletedCat = new Category { Name = $"SoftDelCat_{Guid.NewGuid():N}", IsDeleted = true };
            dbContext.Categories.Add(softDeletedCat);
            await dbContext.SaveChangesAsync();
            softDeletedCatId = softDeletedCat.Id;
        }

        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 100m, softDeletedCatId, whId);
        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_MissingWarehouseId_Returns400BadRequest()
    {
        var client = _factory.CreateClient();
        var (catId, _) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 100m, catId, Guid.Empty);

        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_NonexistentWarehouse_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var (catId, _) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 100m, catId, Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/products", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_SoftDeletedWarehouse_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var (catId, _) = await CreateActiveCategoryAndWarehouseAsync(client);

        Guid softDeletedWhId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var softDeletedWh = new Warehouse { Name = $"SoftDelWh_{Guid.NewGuid():N}", Address = "Street", IsDeleted = true };
            dbContext.Warehouses.Add(softDeletedWh);
            await dbContext.SaveChangesAsync();
            softDeletedWhId = softDeletedWh.Id;
        }

        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 100m, catId, softDeletedWhId);
        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_DuplicateActiveSku_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_DUP_{Guid.NewGuid():N}";

        var request1 = new CreateProductRequest("Product 1", "Desc", sku, 100m, catId, whId);
        var resp1 = await client.PostAsJsonAsync("/api/products", request1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var request2 = new CreateProductRequest("Product 2", "Desc", sku, 200m, catId, whId);
        var resp2 = await client.PostAsJsonAsync("/api/products", request2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_DuplicateSoftDeletedProductSku_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_SOFTP_DUP_{Guid.NewGuid():N}";

        var request1 = new CreateProductRequest("Product 1", "Desc", sku, 100m, catId, whId);
        var resp1 = await client.PostAsJsonAsync("/api/products", request1);
        var created = await resp1.Content.ReadFromJsonAsync<ProductResponse>();

        // Soft delete the product
        await client.DeleteAsync($"/api/products/{created!.Id}");

        // Attempting to recreate using same SKU returns 409
        var request2 = new CreateProductRequest("Product 2", "Desc", sku, 200m, catId, whId);
        var resp2 = await client.PostAsJsonAsync("/api/products", request2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_SkuCaseSensitivity_MaintainsCaseSensitivity()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var baseSku = $"SKU_CASE_{Guid.NewGuid():N}";
        var lowerSku = baseSku.ToLowerInvariant();
        var upperSku = baseSku.ToUpperInvariant();

        var req1 = new CreateProductRequest("Prod Lower", "Desc", lowerSku, 100m, catId, whId);
        var resp1 = await client.PostAsJsonAsync("/api/products", req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        var req2 = new CreateProductRequest("Prod Upper", "Desc", upperSku, 100m, catId, whId);
        var resp2 = await client.PostAsJsonAsync("/api/products", req2);

        // Case-sensitive unique constraint allows different casing
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_SkuSurroundingWhitespace_StoresTrimmedValue()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var rawSku = $"  SKU_WS_{Guid.NewGuid():N}  ";
        var request = new CreateProductRequest("Whitespace SKU Product", "Desc", rawSku, 100m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);
        var content = await response.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.Equal(rawSku.Trim(), content!.SKU);
    }

    [Fact]
    public async Task GetAllProducts_ReturnsActiveProductsOnly_WithInventoryQuantity()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku1 = $"SKU_ALL1_{Guid.NewGuid():N}";
        var sku2 = $"SKU_ALL2_{Guid.NewGuid():N}";

        var resp1 = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Prod 1", "Desc", sku1, 10m, catId, whId));
        var prod1 = await resp1.Content.ReadFromJsonAsync<ProductResponse>();

        var resp2 = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Prod 2", "Desc", sku2, 20m, catId, whId));
        var prod2 = await resp2.Content.ReadFromJsonAsync<ProductResponse>();

        // Delete prod1
        await client.DeleteAsync($"/api/products/{prod1!.Id}");

        var getAllResp = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, getAllResp.StatusCode);

        var products = await getAllResp.Content.ReadFromJsonAsync<List<ProductResponse>>();
        Assert.NotNull(products);
        Assert.DoesNotContain(products, p => p.Id == prod1.Id);
        Assert.Contains(products, p => p.Id == prod2!.Id);
    }

    [Fact]
    public async Task GetProductById_ActiveProduct_Returns200OK()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_GET_{Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Prod Get", "Desc", sku, 50m, catId, whId));
        var created = await createResp.Content.ReadFromJsonAsync<ProductResponse>();

        var getResp = await client.GetAsync($"/api/products/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var product = await getResp.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal(created.Id, product.Id);
        Assert.Equal(sku, product.SKU);
    }

    [Fact]
    public async Task GetProductById_NonexistentProduct_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProductById_SoftDeletedProduct_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_GETDEL_{Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Prod GetDel", "Desc", sku, 50m, catId, whId));
        var created = await createResp.Content.ReadFromJsonAsync<ProductResponse>();

        // Soft delete
        await client.DeleteAsync($"/api/products/{created!.Id}");

        var getResp = await client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task DeleteProduct_ActiveProduct_Returns204NoContent_AndPreservesRowAndInventory()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_DELPRES_{Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Prod DelPres", "Desc", sku, 50m, catId, whId));
        var created = await createResp.Content.ReadFromJsonAsync<ProductResponse>();

        var delResp = await client.DeleteAsync($"/api/products/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // Verify DB row preserved with IsDeleted = true
        using var scope = _factory.Services.CreateScope();
        var dbContext = GetDbContext(scope);
        var productInDb = await dbContext.Products
            .IgnoreQueryFilters()
            .Include(p => p.Inventories)
            .FirstOrDefaultAsync(p => p.Id == created.Id);

        Assert.NotNull(productInDb);
        Assert.True(productInDb.IsDeleted);
        Assert.Single(productInDb.Inventories);
    }

    [Fact]
    public async Task DeleteProduct_NonexistentProduct_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var delResp = await client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, delResp.StatusCode);
    }

    [Fact]
    public async Task DeleteProduct_AlreadySoftDeletedProduct_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_REPDEL_{Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("Prod RepDel", "Desc", sku, 50m, catId, whId));
        var created = await createResp.Content.ReadFromJsonAsync<ProductResponse>();

        await client.DeleteAsync($"/api/products/{created!.Id}");

        var secondDelResp = await client.DeleteAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelResp.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_PriceWithMoreThanTwoDecimalPlaces_Returns400BadRequest()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 19.999m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_PriceWithValidTwoDecimalPlaces_Returns201Created()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var request = new CreateProductRequest("Product Name", "Desc", $"SKU_{Guid.NewGuid():N}", 19.99m, catId, whId);

        var response = await client.PostAsJsonAsync("/api/products", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ProductResponseDto_DoesNotExposeDomainOrPersistenceInternals()
    {
        var client = _factory.CreateClient();
        var (catId, whId) = await CreateActiveCategoryAndWarehouseAsync(client);
        var sku = $"SKU_DTO_{Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync("/api/products", new CreateProductRequest("DTO Test", "Desc", sku, 100m, catId, whId));
        var jsonString = await createResp.Content.ReadAsStringAsync();

        Assert.DoesNotContain("\"isDeleted\":", jsonString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"inventories\":", jsonString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"category\":", jsonString, StringComparison.OrdinalIgnoreCase);
    }
}
