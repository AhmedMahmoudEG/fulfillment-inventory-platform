using Fulfillment.Application.Categories;
using Fulfillment.Application.Categories.DTOs;
using Fulfillment.Application.Common.Exceptions;
using Fulfillment.Domain.Entities;

namespace Fulfillment.UnitTests.Categories;

public class CategoryServiceTests
{
    private class FakeCategoryRepository : ICategoryRepository
    {
        public List<Category> Categories { get; } = new();
        public bool SaveChangesCalled { get; private set; }

        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));
        }

        public Task<Category?> GetByIdForDeletionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.FirstOrDefault(c => c.Id == id && !c.IsDeleted));
        }

        public Task<List<Category>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.Where(c => !c.IsDeleted).ToList());
        }

        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Categories.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)));
        }

        public Task AddAsync(Category category, CancellationToken cancellationToken = default)
        {
            Categories.Add(category);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_InvalidName_ThrowsValidationException(string? invalidName)
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);
        var request = new CreateCategoryRequest(invalidName!);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_ValidNameWithWhitespace_TrimsWhitespaceAndSucceeds()
    {
        var repo = new FakeCategoryRepository();
        var service = new CategoryService(repo);
        var request = new CreateCategoryRequest("  Electronics  ");

        var result = await service.CreateAsync(request);

        Assert.Equal("Electronics", result.Name);
        Assert.Single(repo.Categories);
        Assert.Equal("Electronics", repo.Categories[0].Name);
        Assert.True(repo.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsConflictException()
    {
        var repo = new FakeCategoryRepository();
        repo.Categories.Add(new Category { Name = "Electronics" });
        var service = new CategoryService(repo);
        var request = new CreateCategoryRequest("Electronics");

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithActiveProducts_ThrowsConflictException()
    {
        var categoryId = Guid.NewGuid();
        var category = new Category { Id = categoryId, Name = "Electronics" };
        category.Products.Add(new Product { Name = "Laptop", SKU = "LAP-1", IsDeleted = false });

        var repo = new FakeCategoryRepository();
        repo.Categories.Add(category);
        var service = new CategoryService(repo);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(categoryId));
        Assert.False(category.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithOnlySoftDeletedProducts_SucceedsAndSetsIsDeletedTrue()
    {
        var categoryId = Guid.NewGuid();
        var category = new Category { Id = categoryId, Name = "Electronics" };
        category.Products.Add(new Product { Name = "Old Laptop", SKU = "LAP-OLD", IsDeleted = true });

        var repo = new FakeCategoryRepository();
        repo.Categories.Add(category);
        var service = new CategoryService(repo);

        await service.DeleteAsync(categoryId);

        Assert.True(category.IsDeleted);
        Assert.True(repo.SaveChangesCalled);
    }
}
