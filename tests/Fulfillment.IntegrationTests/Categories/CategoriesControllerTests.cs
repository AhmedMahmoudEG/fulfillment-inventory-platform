using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Domain.Entities;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fulfillment.IntegrationTests.Categories;

[Collection("IntegrationTests")]
public class CategoriesControllerTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public CategoriesControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private ApplicationDbContext GetDbContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    public async Task InitializeAsync()
    {
        await ClearAllDataAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearAllDataAsync();
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

    [Fact]
    public async Task CreateCategory_WithValidName_Returns201CreatedAndCategoryResponse()
    {
        var client = _factory.CreateClient();
        var catName = $"Electronics_{Guid.NewGuid():N}";
        var request = new CreateCategoryRequest(catName);

        var response = await client.PostAsJsonAsync("/api/categories", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var content = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(content);
        Assert.NotEqual(Guid.Empty, content.Id);
        Assert.Equal(catName, content.Name);
    }

    [Fact]
    public async Task CreateCategory_WithWhitespaceInName_TrimsWhitespaceAndReturns201()
    {
        var client = _factory.CreateClient();
        var catName = $"HomeAppliances_{Guid.NewGuid():N}";
        var request = new CreateCategoryRequest($"   {catName}   ");

        var response = await client.PostAsJsonAsync("/api/categories", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(content);
        Assert.Equal(catName, content.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateCategory_WithInvalidName_Returns400BadRequest(string? invalidName)
    {
        var client = _factory.CreateClient();
        var request = new CreateCategoryRequest(invalidName!);

        var response = await client.PostAsJsonAsync("/api/categories", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateCategory_WithDuplicateActiveName_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var categoryName = $"DupActive_{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(categoryName));

        var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(categoryName));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateCategory_WithDuplicateSoftDeletedName_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var categoryName = $"SoftDelName_{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(categoryName));
        var createdCategory = await createResponse.Content.ReadFromJsonAsync<CategoryResponse>();

        // Soft delete the category
        await client.DeleteAsync($"/api/categories/{createdCategory!.Id}");

        // Attempt to recreate using the same name must return 409 Conflict
        var recreateResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(categoryName));

        Assert.Equal(HttpStatusCode.Conflict, recreateResponse.StatusCode);
        Assert.Equal("application/problem+json", recreateResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateCategory_CaseSensitivity_AllowsDifferentCases()
    {
        var client = _factory.CreateClient();
        var nameUpper = $"CASE_TEST_{Guid.NewGuid():N}";
        var nameLower = nameUpper.ToLower();

        var res1 = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(nameUpper));
        var res2 = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(nameLower));

        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
    }

    [Fact]
    public async Task GetAllCategories_ReturnsActiveCategoriesOnly()
    {
        var client = _factory.CreateClient();
        var catName1 = $"ActiveCat1_{Guid.NewGuid():N}";
        var catName2 = $"ActiveCat2_{Guid.NewGuid():N}";

        var res1 = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName1));
        var cat1 = await res1.Content.ReadFromJsonAsync<CategoryResponse>();

        await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName2));

        // Soft delete cat1
        await client.DeleteAsync($"/api/categories/{cat1!.Id}");

        var getAllResponse = await client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, getAllResponse.StatusCode);

        var list = await getAllResponse.Content.ReadFromJsonAsync<List<CategoryResponse>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, c => c.Id == cat1.Id);
        Assert.Contains(list, c => c.Name == catName2);
    }

    [Fact]
    public async Task GetCategoryById_ActiveCategory_Returns200OK()
    {
        var client = _factory.CreateClient();
        var catName = $"GetByIdTest_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var created = await createRes.Content.ReadFromJsonAsync<CategoryResponse>();

        var getRes = await client.GetAsync($"/api/categories/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var content = await getRes.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(content);
        Assert.Equal(created.Id, content.Id);
        Assert.Equal(catName, content.Name);
    }

    [Fact]
    public async Task GetCategoryById_NonexistentId_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetCategoryById_SoftDeletedCategory_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var catName = $"SoftDelGetById_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var created = await createRes.Content.ReadFromJsonAsync<CategoryResponse>();

        await client.DeleteAsync($"/api/categories/{created!.Id}");

        var getRes = await client.GetAsync($"/api/categories/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_WithActiveProducts_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        var catName = $"DelWithProd_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var category = await createRes.Content.ReadFromJsonAsync<CategoryResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var product = new Product { Name = "Prod1", SKU = $"SKU_{Guid.NewGuid():N}", Price = 10m, CategoryId = category!.Id, IsDeleted = false };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();
        }

        var deleteRes = await client.DeleteAsync($"/api/categories/{category!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
        Assert.Equal("application/problem+json", deleteRes.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DeleteCategory_WithOnlySoftDeletedProducts_Returns204NoContentAndSoftDeletesCategory()
    {
        var client = _factory.CreateClient();
        var catName = $"DelWithSoftDelProd_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var category = await createRes.Content.ReadFromJsonAsync<CategoryResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var softDeletedProduct = new Product { Name = "OldProd", SKU = $"SKU_{Guid.NewGuid():N}", Price = 10m, CategoryId = category!.Id, IsDeleted = true };
            dbContext.Products.Add(softDeletedProduct);
            await dbContext.SaveChangesAsync();
        }

        var deleteRes = await client.DeleteAsync($"/api/categories/{category!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Verify soft deletion preserves the database row
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = GetDbContext(scope);
            var dbCategory = await dbContext.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == category.Id);
            Assert.NotNull(dbCategory);
            Assert.True(dbCategory.IsDeleted);
        }
    }

    [Fact]
    public async Task DeleteCategory_NonexistentId_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"/api/categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_AlreadySoftDeleted_Returns404NotFound()
    {
        var client = _factory.CreateClient();
        var catName = $"AlreadyDeleted_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var category = await createRes.Content.ReadFromJsonAsync<CategoryResponse>();

        await client.DeleteAsync($"/api/categories/{category!.Id}");

        var secondDeleteRes = await client.DeleteAsync($"/api/categories/{category.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondDeleteRes.StatusCode);
    }

    [Fact]
    public async Task CategoryResponse_DTO_ExposesOnlyIdAndName()
    {
        var client = _factory.CreateClient();
        var catName = $"DTOTest_{Guid.NewGuid():N}";
        var createRes = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(catName));
        var json = await createRes.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("id", out _) || root.TryGetProperty("Id", out _));
        Assert.True(root.TryGetProperty("name", out _) || root.TryGetProperty("Name", out _));
        Assert.False(root.TryGetProperty("isDeleted", out _) || root.TryGetProperty("IsDeleted", out _));
        Assert.False(root.TryGetProperty("products", out _) || root.TryGetProperty("Products", out _));
    }
}
